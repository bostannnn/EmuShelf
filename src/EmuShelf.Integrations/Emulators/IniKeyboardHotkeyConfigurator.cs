using EmuShelf.Core.Hotkeys;

namespace EmuShelf.Integrations.Emulators;

/// <summary>
/// Base for the emulators that store hotkeys as <c>Action = Keyboard/&lt;Key&gt;</c> in an INI section —
/// DuckStation and PCSX2, which share Stenzek's input engine. A subclass supplies the file, the
/// section, the settings-version gate, and the action→key map (a null key means the emulator has no
/// such feature). Everything else — reading, the version check, building the <c>Keyboard/&lt;Key&gt;</c>
/// token, conflict clearing, and the plan — lives here.
/// </summary>
public abstract class IniKeyboardHotkeyConfigurator : HotkeyConfiguratorBase
{
    private readonly string _configDirectory;
    private readonly IReadOnlyList<string> _relativeConfigPaths;
    private readonly string _section;
    private readonly string _versionSection;
    private readonly string _versionKey;
    private readonly string _supportedVersion;
    private readonly IReadOnlyDictionary<HotkeyAction, string?> _actionKeys;
    private readonly IReadOnlyDictionary<HotkeyAction, string> _unsupportedReasons;

    private protected IniKeyboardHotkeyConfigurator(
        string emulatorId,
        string displayName,
        string configDirectory,
        IReadOnlyList<string> relativeConfigPaths,
        string section,
        string versionSection,
        string versionKey,
        string supportedVersion,
        IReadOnlyDictionary<HotkeyAction, string?> actionKeys,
        IReadOnlyDictionary<HotkeyAction, string> unsupportedReasons,
        string backupRoot,
        Action<string, string>? writeFile)
        : base(emulatorId, displayName, backupRoot, writeFile)
    {
        _configDirectory = Path.GetFullPath(configDirectory);
        _relativeConfigPaths = relativeConfigPaths;
        _section = section;
        _versionSection = versionSection;
        _versionKey = versionKey;
        _supportedVersion = supportedVersion;
        _actionKeys = actionKeys;
        _unsupportedReasons = unsupportedReasons;
    }

    public override IReadOnlyList<HotkeyActionSupport> DescribeSupport(HotkeyProfile profile) =>
        profile.Actions
            .Select(action => _actionKeys[action] is null
                ? HotkeyActionSupport.Unsupported(action, ReasonFor(action))
                : HotkeyActionSupport.Supported(action))
            .ToArray();

    protected override IReadOnlyList<string> ManagedFiles =>
        ResolveExistingPath() is { } path ? [path] : [];

    private protected override HotkeyPlan BuildPlan(HotkeyProfile profile, CancellationToken cancellationToken)
    {
        var path = ResolveExistingPath();
        if (path is null)
            return HotkeyPlan.NotFound($"{DisplayName}'s settings file was not found.");

        var text = ReadTextOrNull(path);
        if (text is null)
            return HotkeyPlan.NotFound($"{DisplayName}'s settings file was not found.");

        var document = new EmulatorConfigDocument(text);
        var version = document.GetValue(_versionSection, _versionKey);
        if (!string.Equals(version, _supportedVersion, StringComparison.Ordinal))
        {
            return HotkeyPlan.UnsupportedFormat(
                $"{DisplayName}'s settings file is {_versionKey} '{version ?? "unknown"}', not the supported {_supportedVersion}, so EmuShelf will not edit it.");
        }

        var (bindings, changes) = ApplyKeySection(
            document,
            Path.GetFileName(path),
            _section,
            profile,
            action => _actionKeys[action],
            ReasonFor,
            action => $"Keyboard/{profile[action].Label()}");

        var boundActions = bindings
            .Where(binding => binding.Status == HotkeyBindingStatus.Bound)
            .Select(binding => binding.Action)
            .ToHashSet();
        ApplyExtraSettings(document, Path.GetFileName(path), boundActions, changes);

        IReadOnlyList<HotkeyFilePlan> files = document.Changed
            ? [new HotkeyFilePlan(path, document.ToText())]
            : [];
        return HotkeyPlan.Edited(bindings, files, changes);
    }

    /// <summary>
    /// Hook for a feature-enable flag a bound action depends on (DuckStation flips
    /// <c>[Main] RewindEnable</c> when it binds rewind). Runs against the same document, so the change
    /// rides the same file write.
    /// </summary>
    protected virtual void ApplyExtraSettings(
        EmulatorConfigDocument document,
        string fileName,
        IReadOnlySet<HotkeyAction> boundActions,
        List<HotkeyChange> changes)
    {
    }

    protected void SetFlag(
        EmulatorConfigDocument document,
        string fileName,
        string section,
        string key,
        string value,
        List<HotkeyChange> changes)
    {
        var previous = document.GetValue(section, key);
        if (document.SetValue(section, key, value))
            changes.Add(new HotkeyChange(fileName, section, key, previous, value));
    }

    private string ReasonFor(HotkeyAction action) =>
        _unsupportedReasons.GetValueOrDefault(action, "This emulator has no such feature.");

    private string? ResolveExistingPath() =>
        _relativeConfigPaths
            .Select(relative => Path.Combine(_configDirectory, relative))
            .FirstOrDefault(File.Exists);
}
