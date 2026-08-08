using EmuShelf.Core.Hotkeys;

namespace EmuShelf.Integrations.Emulators;

/// <summary>One config file a plan will rewrite: its full path and the complete new text.</summary>
internal sealed record HotkeyFilePlan(string Path, string NewText);

/// <summary>
/// The computed effect of applying the profile to one emulator, before anything is written. The
/// configurator produces this from the current files; the base then either writes the file plans
/// (Apply) or discards them (Preview).
/// </summary>
internal sealed record HotkeyPlan(
    HotkeyApplyStatus Status,
    IReadOnlyList<HotkeyBindingResult> Bindings,
    IReadOnlyList<HotkeyFilePlan> Files,
    IReadOnlyList<HotkeyChange> Changes,
    string? Diagnostic)
{
    public static HotkeyPlan NotFound(string diagnostic) =>
        new(HotkeyApplyStatus.ConfigurationNotFound, [], [], [], diagnostic);

    public static HotkeyPlan UnsupportedFormat(string diagnostic) =>
        new(HotkeyApplyStatus.UnsupportedFormat, [], [], [], diagnostic);

    public static HotkeyPlan Failed(string diagnostic) =>
        new(HotkeyApplyStatus.Failed, [], [], [], diagnostic);

    /// <summary>An edit that carries binding outcomes; Changed only when a file actually differs.</summary>
    public static HotkeyPlan Edited(
        IReadOnlyList<HotkeyBindingResult> bindings,
        IReadOnlyList<HotkeyFilePlan> files,
        IReadOnlyList<HotkeyChange> changes,
        string? diagnostic = null) =>
        new(files.Count > 0 ? HotkeyApplyStatus.Changed : HotkeyApplyStatus.Unchanged, bindings, files, changes, diagnostic);

    public HotkeyApplyResult ToResult() => new(Status, Bindings, Changes, Diagnostic);
}

/// <summary>
/// Shared scaffolding for the per-emulator hotkey configurators: it drives Preview/Apply/Revert,
/// backs each file up before its first modification, writes atomically through an injected writer,
/// and offers a "set these key bindings in this section, clearing conflicts" helper the four
/// section-based emulators reuse. Subclasses supply only the emulator-specific plan.
/// </summary>
public abstract class HotkeyConfiguratorBase : IEmulatorHotkeyConfigurator
{
    private readonly HotkeyConfigBackup _backup;
    private readonly Action<string, string> _writeFile;

    protected HotkeyConfiguratorBase(
        string emulatorId,
        string displayName,
        string backupRoot,
        Action<string, string>? writeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emulatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        EmulatorId = emulatorId;
        DisplayName = displayName;
        _backup = new HotkeyConfigBackup(backupRoot, emulatorId);
        // Integrations cannot reference Infrastructure's AtomicFile, so the durable writer is injected
        // by the App; tests and a bare default fall back to a plain write.
        _writeFile = writeFile ?? File.WriteAllText;
    }

    public string EmulatorId { get; }

    public string DisplayName { get; }

    public abstract IReadOnlyList<HotkeyActionSupport> DescribeSupport(HotkeyProfile profile);

    /// <summary>Builds the plan from the current config files. May throw IO/parse errors; the base wraps them.</summary>
    private protected abstract HotkeyPlan BuildPlan(HotkeyProfile profile, CancellationToken cancellationToken);

    /// <summary>The files Revert may restore — the ones this configurator writes.</summary>
    protected abstract IReadOnlyList<string> ManagedFiles { get; }

    public HotkeyApplyResult Preview(HotkeyProfile profile, CancellationToken cancellationToken = default) =>
        SafeBuild(profile, cancellationToken).ToResult();

    public HotkeyApplyResult Apply(HotkeyProfile profile, CancellationToken cancellationToken = default)
    {
        var plan = SafeBuild(profile, cancellationToken);
        if (plan.Status != HotkeyApplyStatus.Changed)
            return plan.ToResult();

        try
        {
            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _backup.Capture(file.Path);
                _writeFile(file.Path, file.NewText);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HotkeyApplyResult.Failed($"Writing the configuration failed: {ex.Message}");
        }

        return plan.ToResult();
    }

    public HotkeyApplyResult Revert(CancellationToken cancellationToken = default)
    {
        if (!_backup.HasAny())
            return new HotkeyApplyResult(HotkeyApplyStatus.Unchanged, [], [], "There is no EmuShelf backup to restore.");

        var restored = new List<HotkeyChange>();
        try
        {
            foreach (var path in ManagedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var backup = _backup.NewestBackup(Path.GetFileName(path));
                if (backup is null)
                    continue;
                File.Copy(backup, path, overwrite: true);
                restored.Add(new HotkeyChange(Path.GetFileName(path), null, "(whole file)", null, "restored from backup"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HotkeyApplyResult.Failed($"Restoring the backup failed: {ex.Message}");
        }

        return restored.Count > 0
            ? new HotkeyApplyResult(HotkeyApplyStatus.Changed, [], restored, $"Restored {restored.Count} file(s) from backup.")
            : new HotkeyApplyResult(HotkeyApplyStatus.Unchanged, [], [], "There is no EmuShelf backup to restore.");
    }

    private HotkeyPlan SafeBuild(HotkeyProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            return BuildPlan(profile, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidDataException)
        {
            return HotkeyPlan.Failed($"The configuration could not be processed: {ex.Message}");
        }
    }

    /// <summary>Reads a file's text, or null when it does not exist.</summary>
    protected static string? ReadTextOrNull(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;

    /// <summary>
    /// Writes each action's key into <paramref name="section"/> of <paramref name="document"/>: it
    /// records an <c>Unsupported</c> binding when the emulator lacks the action, and otherwise clears
    /// any other key already holding the same value (so two actions can't fight for one binding) and
    /// sets the key. Returns the binding outcomes and the concrete changes made to the document.
    /// </summary>
    private protected static (List<HotkeyBindingResult> Bindings, List<HotkeyChange> Changes) ApplyKeySection(
        EmulatorConfigDocument document,
        string fileName,
        string section,
        HotkeyProfile profile,
        Func<HotkeyAction, string?> keyFor,
        Func<HotkeyAction, string> unsupportedReasonFor,
        Func<HotkeyAction, string> valueFor)
    {
        var bindings = new List<HotkeyBindingResult>();
        var changes = new List<HotkeyChange>();
        var targets = new List<(string Key, string Value)>();

        foreach (var action in profile.Actions)
        {
            var label = profile[action].Label();
            var key = keyFor(action);
            if (key is null)
            {
                bindings.Add(new HotkeyBindingResult(action, HotkeyBindingStatus.Unsupported, label, unsupportedReasonFor(action)));
                continue;
            }

            targets.Add((key, valueFor(action)));
            bindings.Add(new HotkeyBindingResult(action, HotkeyBindingStatus.Bound, label));
        }

        var targetKeys = targets.Select(target => target.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var value in targets.Select(target => target.Value).Distinct(StringComparer.Ordinal))
        {
            foreach (var conflicting in document.KeysWithValue(section, value))
            {
                if (targetKeys.Contains(conflicting))
                    continue;
                if (document.RemoveKey(section, conflicting))
                    changes.Add(new HotkeyChange(fileName, section, conflicting, value, "(unbound — conflicted with EmuShelf's scheme)"));
            }
        }

        foreach (var (key, value) in targets)
        {
            var previous = document.GetValue(section, key);
            if (document.SetValue(section, key, value))
                changes.Add(new HotkeyChange(fileName, section, key, previous, value));
        }

        return (bindings, changes);
    }
}
