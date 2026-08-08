using System.Diagnostics;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Hotkeys;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Storage;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.Services;

/// <summary>How a status line should read.</summary>
public enum HotkeyRowTone
{
    Info,
    Success,
    Muted,
    Warning,
    Error,
}

/// <summary>One action's line in an emulator's hotkey card.</summary>
public sealed record HotkeyActionLine(string Label, string Detail, bool IsAvailable)
{
    public string Display => $"{Label}  —  {Detail}";
}

/// <summary>An emulator's hotkey state as Settings renders it: the per-action grid plus a status line.</summary>
public sealed record HotkeyEmulatorSnapshot(
    string EmulatorId,
    string DisplayName,
    IReadOnlyList<HotkeyActionLine> Actions,
    string StatusText,
    HotkeyRowTone StatusTone,
    bool CanOperate);

/// <summary>The delegate surface the Settings view model drives, so it never names an emulator.</summary>
public sealed record HotkeySettingsContext(
    IReadOnlyList<HotkeyEmulatorSnapshot> Emulators,
    Func<string, CancellationToken, Task<HotkeyEmulatorSnapshot>> ApplyAsync,
    Func<string, CancellationToken, Task<HotkeyEmulatorSnapshot>> PreviewAsync,
    Func<string, CancellationToken, Task<HotkeyEmulatorSnapshot>> RevertAsync,
    string SchemeSummary);

/// <summary>
/// Composes the hotkey configurators from EmuShelf's own emulator configuration and turns their
/// results into the snapshots Settings shows. Platform knowledge lives in
/// <see cref="HotkeyProviderRegistry"/>; this type only resolves which emulators are configured,
/// refuses to write while an emulator is running, and formats the outcomes.
/// </summary>
public sealed class HotkeyCoordinator
{
    private static readonly IReadOnlyDictionary<HotkeyAction, string> ActionLabels = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.CloseGame] = "Close game",
        [HotkeyAction.Rewind] = "Rewind",
        [HotkeyAction.FastForward] = "Fast-forward",
        [HotkeyAction.SaveState] = "Save state",
        [HotkeyAction.LoadState] = "Load state",
    };

    private readonly HotkeyProfile _profile = HotkeyProfile.Default;
    private readonly string _backupRoot;
    private readonly IReadOnlyList<string> _systemIds;
    private readonly Func<string, SaveEmulatorInstallation?> _resolveInstallation;
    private readonly Action<string, string> _writeFile;
    private readonly Func<SaveEmulatorInstallation, bool> _isRunning;
    private readonly IAppLogger _logger;

    private enum Operation
    {
        Initial,
        Preview,
        Apply,
        Revert,
    }

    public HotkeyCoordinator(
        IAppPaths paths,
        IReadOnlyList<GameSystem> systems,
        IAppLogger logger,
        Func<string, SaveEmulatorInstallation?> resolveInstallation,
        Action<string, string>? writeFile = null,
        Func<SaveEmulatorInstallation, bool>? isEmulatorRunning = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(resolveInstallation);
        _backupRoot = Path.Combine(paths.SettingsDirectory, "hotkey-backups");
        _systemIds = systems.Select(system => system.Id).ToArray();
        _resolveInstallation = resolveInstallation;
        _writeFile = writeFile ?? File.WriteAllText;
        _isRunning = isEmulatorRunning ?? IsProcessRunning;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <summary>A human summary of the scheme, for the Settings header.</summary>
    public string SchemeSummary =>
        "EmuShelf writes a uniform keyboard scheme into each emulator's own settings: R rewinds, L " +
        "fast-forwards, F2 saves state, F4 loads state, and F8 closes the game. To drive these from a " +
        "controller, set up a Steam Input layout once (there is no file to import — Steam Input layouts " +
        "can't be dropped in) using the controller mapping below. Changes take effect the next time you " +
        "open the emulator.";

    /// <summary>The delegate context for the Settings view model. Reads the config files, so call it off the UI thread.</summary>
    public HotkeySettingsContext CreateSettingsContext() =>
        new(Describe(), ApplyAsync, PreviewAsync, RevertAsync, SchemeSummary);

    /// <summary>A snapshot per configured, supported emulator, built from a dry-run preview.</summary>
    public IReadOnlyList<HotkeyEmulatorSnapshot> Describe()
    {
        var installations = ConfiguredInstallations();
        var snapshots = new List<HotkeyEmulatorSnapshot>();
        foreach (var descriptor in HotkeyProviderRegistry.All)
        {
            if (!installations.TryGetValue(descriptor.EmulatorId, out var installation))
                continue;

            var configurator = CreateConfigurator(descriptor, installation);
            snapshots.Add(configurator is null
                ? UnavailableSnapshot(descriptor)
                : BuildSnapshot(descriptor, configurator, SafePreview(configurator), Operation.Initial));
        }

        return snapshots;
    }

    public Task<HotkeyEmulatorSnapshot> PreviewAsync(string emulatorId, CancellationToken cancellationToken) =>
        OperateAsync(emulatorId, Operation.Preview, cancellationToken);

    public Task<HotkeyEmulatorSnapshot> ApplyAsync(string emulatorId, CancellationToken cancellationToken) =>
        OperateAsync(emulatorId, Operation.Apply, cancellationToken);

    public Task<HotkeyEmulatorSnapshot> RevertAsync(string emulatorId, CancellationToken cancellationToken) =>
        OperateAsync(emulatorId, Operation.Revert, cancellationToken);

    private async Task<HotkeyEmulatorSnapshot> OperateAsync(string emulatorId, Operation operation, CancellationToken cancellationToken)
    {
        var descriptor = HotkeyProviderRegistry.Find(emulatorId);
        if (descriptor is null || !ConfiguredInstallations().TryGetValue(emulatorId, out var installation))
            return FailedSnapshot(emulatorId, "This emulator is no longer configured.");

        var configurator = CreateConfigurator(descriptor, installation);
        if (configurator is null)
            return UnavailableSnapshot(descriptor);

        // A running emulator rewrites its config on exit and would clobber the edit, so refuse.
        if (operation == Operation.Apply && _isRunning(installation))
        {
            return BuildSnapshot(
                descriptor,
                configurator,
                HotkeyApplyResult.EmulatorRunning($"{descriptor.DisplayName} is running — close it first, then apply."),
                operation);
        }

        var result = await Task.Run(() => operation switch
        {
            Operation.Apply => configurator.Apply(_profile, cancellationToken),
            Operation.Revert => configurator.Revert(cancellationToken),
            _ => configurator.Preview(_profile, cancellationToken),
        }, cancellationToken);

        if (operation == Operation.Apply)
            _logger.Information($"Hotkeys applied to {descriptor.DisplayName}: {result.Status}.");
        return BuildSnapshot(descriptor, configurator, result, operation);
    }

    private HotkeyApplyResult SafePreview(IEmulatorHotkeyConfigurator configurator)
    {
        try
        {
            return configurator.Preview(_profile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HotkeyApplyResult.Failed(ex.Message);
        }
    }

    private IReadOnlyDictionary<string, SaveEmulatorInstallation> ConfiguredInstallations()
    {
        var map = new Dictionary<string, SaveEmulatorInstallation>(StringComparer.Ordinal);
        foreach (var systemId in _systemIds)
        {
            if (_resolveInstallation(systemId) is { EmulatorId: { } emulatorId } installation)
                map.TryAdd(emulatorId, installation);
        }

        return map;
    }

    private IEmulatorHotkeyConfigurator? CreateConfigurator(HotkeyEmulatorDescriptor descriptor, SaveEmulatorInstallation installation) =>
        descriptor.Create(new HotkeyInstallationContext(installation.Directory, installation.IsFlatpak, _backupRoot, _writeFile));

    private HotkeyEmulatorSnapshot BuildSnapshot(
        HotkeyEmulatorDescriptor descriptor,
        IEmulatorHotkeyConfigurator configurator,
        HotkeyApplyResult result,
        Operation operation)
    {
        var actions = result.Bindings.Count > 0
            ? result.Bindings.Select(ToLine).ToArray()
            : configurator.DescribeSupport(_profile).Select(ToLine).ToArray();
        var (status, tone) = DescribeStatus(descriptor.DisplayName, result, operation);
        return new HotkeyEmulatorSnapshot(descriptor.EmulatorId, descriptor.DisplayName, actions, status, tone, CanOperate: true);
    }

    private HotkeyActionLine ToLine(HotkeyBindingResult binding) => binding.Status switch
    {
        HotkeyBindingStatus.Bound => new HotkeyActionLine(ActionLabels[binding.Action], binding.Key, IsAvailable: true),
        _ => new HotkeyActionLine(ActionLabels[binding.Action], $"Not available — {binding.Detail}", IsAvailable: false),
    };

    private HotkeyActionLine ToLine(HotkeyActionSupport support) => support.IsSupported
        ? new HotkeyActionLine(ActionLabels[support.Action], _profile[support.Action].Label(), IsAvailable: true)
        : new HotkeyActionLine(ActionLabels[support.Action], $"Not available — {support.Reason}", IsAvailable: false);

    private static (string Text, HotkeyRowTone Tone) DescribeStatus(string name, HotkeyApplyResult result, Operation operation)
    {
        var note = string.IsNullOrWhiteSpace(result.Diagnostic) ? string.Empty : $" {result.Diagnostic}";
        switch (result.Status)
        {
            case HotkeyApplyStatus.Failed:
                return ($"Couldn't update {name}: {result.Diagnostic}", HotkeyRowTone.Error);
            case HotkeyApplyStatus.ConfigurationNotFound:
                return (result.Diagnostic ?? $"{name}'s settings file was not found.", HotkeyRowTone.Muted);
            case HotkeyApplyStatus.UnsupportedFormat:
                return (result.Diagnostic ?? $"{name}'s settings format is not supported.", HotkeyRowTone.Warning);
            case HotkeyApplyStatus.EmulatorRunning:
                return (result.Diagnostic ?? $"{name} is running — close it first.", HotkeyRowTone.Warning);
            case HotkeyApplyStatus.Changed:
                return operation switch
                {
                    Operation.Apply => ($"Applied. Takes effect next time you open {name}.{note}", HotkeyRowTone.Success),
                    Operation.Revert => ("Reverted to your previous configuration.", HotkeyRowTone.Success),
                    Operation.Preview => ($"Preview: {result.Changes.Count} setting(s) would change.{note}", HotkeyRowTone.Info),
                    _ => ($"Recommended hotkeys aren't applied yet.{note}", HotkeyRowTone.Info),
                };
        }

        // Unchanged: nothing was written because the configuration already matches the scheme.
        if (operation == Operation.Revert)
            return ("Nothing to revert.", HotkeyRowTone.Muted);

        return ("Already applied.", HotkeyRowTone.Success);
    }

    private static HotkeyEmulatorSnapshot UnavailableSnapshot(HotkeyEmulatorDescriptor descriptor) =>
        new(descriptor.EmulatorId, descriptor.DisplayName, [],
            $"EmuShelf couldn't find {descriptor.DisplayName}'s configuration directory on this machine.",
            HotkeyRowTone.Muted, CanOperate: false);

    private static HotkeyEmulatorSnapshot FailedSnapshot(string emulatorId, string message) =>
        new(emulatorId, emulatorId, [], message, HotkeyRowTone.Error, CanOperate: false);

    private static bool IsProcessRunning(SaveEmulatorInstallation installation)
    {
        if (string.IsNullOrWhiteSpace(installation.ExecutablePath))
            return false;

        var name = Path.GetFileNameWithoutExtension(installation.ExecutablePath);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        try
        {
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
            return false;
        }
    }
}
