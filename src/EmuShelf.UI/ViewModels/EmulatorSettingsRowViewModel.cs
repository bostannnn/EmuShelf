using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Systems;
using EmuShelf.Infrastructure.Launching;
using EmuShelf.Integrations.Emulators;

namespace EmuShelf.App.ViewModels;

public partial class EmulatorSettingsRowViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    private readonly IAppLogger _logger;
    private readonly Func<EmulatorSettingsRowViewModel, Task>? _rescanLibrary;
    private readonly Func<EmulatorSettingsRowViewModel, Task>? _syncLibrary;
    private readonly LibraryFolderManagementActions? _folderActions;
    private readonly Func<Func<Task<string>>, Action<string>, Task>? _runFolderMaintenance;
    private readonly string _homeDirectory;
    private readonly IReadOnlyList<EmulatorChoice>? _fixedChoices;
    // The emulator definitions that can serve this system, keyed by id, and one saved draft per
    // emulator so switching the profile picker keeps each profile's edits.
    private readonly Dictionary<string, EmulatorDefinition> _emulatorsById;
    private readonly Dictionary<string, ProfileDraft> _drafts = new(StringComparer.Ordinal);
    private bool _switchingProfile;
    private bool _refreshingChoices;

    public string SystemId { get; }
    public string SystemName { get; }
    public string SystemShortName { get; }
    public string AccentColor { get; }

    /// <summary>
    /// True for a dual-screen console (DS/3DS). Its games always launch on the built-in display, so the
    /// launch-screen preference row is hidden for it — see <see cref="GameSystem.IsDualScreen"/>.
    /// </summary>
    public bool IsDualScreen { get; }
    public string EmulatorName { get; private set; }
    public string DefaultLaunchArguments { get; private set; }
    public string EmulatorId { get; private set; }
    public string EmulatorInstallationId { get; private set; }
    public bool RequiresCorePath { get; private set; }
    public bool IsExecutableShared { get; private set; }

    // Stable AutomationIds so the Emulators section is scriptable/accessible like the others.
    // Keyed by system id, e.g. "emulators.playstation.executable". (Emulators isn't part of the
    // Desktop/Gamepad parity set, so these are Desktop-only.)
    public string EmulatorChoiceFieldId => $"emulators.{SystemId}.emulator";
    public string ExecutableFieldId => $"emulators.{SystemId}.executable";
    public string BrowseFieldId => $"emulators.{SystemId}.browse";
    public string LaunchArgumentsFieldId => $"emulators.{SystemId}.launch-args";
    public string ResetArgumentsFieldId => $"emulators.{SystemId}.reset-args";
    public string CoreFilterFieldId => $"emulators.{SystemId}.core-filter";
    public string AddFolderFieldId => $"emulators.{SystemId}.add-folder";
    public string RescanFieldId => $"emulators.{SystemId}.rescan";
    public string SyncFieldId => $"emulators.{SystemId}.sync";

    /// <summary>
    /// The flat picker: standalone emulators plus one RetroArch item per discovered/fixed core.
    /// Android injects a fixed catalog; desktop rebuilds this collection after every core scan.
    /// </summary>
    public ObservableCollection<EmulatorChoice> AvailableChoices { get; } = [];

    public bool HasEmulatorChoices => AvailableChoices.Count > 0;
    /// <summary>Flatpak targets are meaningful only on Linux.</summary>
    public bool CanSelectFlatpakTarget => OperatingSystem.IsLinux();
    public bool IsLaunchTargetPickerVisible => CanSelectFlatpakTarget;
    public bool IsFlatpakTarget => TargetKind == "Flatpak";
    public bool IsEditableFlatpakTarget => CanSelectFlatpakTarget && IsFlatpakTarget;
    public bool IsDirectTarget => !IsFlatpakTarget;
    /// <summary>A persisted Flatpak target is shown read-only when the current platform cannot run it.</summary>
    public bool IsUnsupportedFlatpakTarget => IsFlatpakTarget && !CanSelectFlatpakTarget;
    public string DirectTargetLabel => OperatingSystem.IsLinux()
        ? "DIRECT EXECUTABLE OR APPIMAGE"
        : "EXECUTABLE";
    public string UnsupportedFlatpakTargetMessage => OperatingSystem.IsWindows()
        ? "This saved Flatpak target cannot run on Windows. Choose a direct executable to use this emulator."
        : "Flatpak targets cannot run on Windows or macOS. Choose a direct executable to use this emulator.";
    public ObservableCollection<LibretroCoreOption> AvailableCores { get; } = [];
    public ObservableCollection<LibretroCoreOption> FilteredCores { get; } = [];
    public ObservableCollection<string> AvailableFlatpakApplicationIds { get; } = [];
    public ObservableCollection<LibraryFolderRowViewModel> LibraryFolders { get; } = [];
    public string ExecutableDescription => IsExecutableShared
        ? "Shared executable"
        : "Executable";
    public bool HasCorePath => !string.IsNullOrWhiteSpace(CorePath);
    public string CoreFileName => HasCorePath
        ? Path.GetFileName(CorePath.Trim())
        : "No core selected";

    internal event Action<EmulatorSettingsRowViewModel, string>? ExecutablePathEdited;
    internal event Action<EmulatorSettingsRowViewModel, string>? TargetKindEdited;
    internal event Action<EmulatorSettingsRowViewModel, string>? FlatpakAppIdEdited;
    /// <summary>Raised after the active emulator profile changes, so the parent can recompute which
    /// executables are shared and seed a switched-to shared installation.</summary>
    internal event Action<EmulatorSettingsRowViewModel>? ProfileChanged;

    [ObservableProperty]
    public partial string ExecutablePath { get; set; }

    /// <summary>Either Direct (binary/AppImage) or Flatpak. A Flatpak id is never inferred.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlatpakTarget))]
    [NotifyPropertyChangedFor(nameof(IsDirectTarget))]
    public partial string TargetKind { get; set; } = "Direct";

    [ObservableProperty]
    public partial string FlatpakAppId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LaunchArguments { get; set; }

    /// <summary>
    /// Which screen this platform launches on when a second screen is present (the Thor). Per system, not
    /// per emulator, so switching the emulator picker keeps it; applied to every saved profile in
    /// <see cref="ConfigurationFrom"/>. Only surfaced by the Android couch settings; ignored elsewhere.
    /// </summary>
    [ObservableProperty]
    public partial GameLaunchScreen LaunchScreen { get; set; }

    /// <summary>The launch-screen options and their labels, in picker order (couch settings, Android).</summary>
    public static IReadOnlyList<GameLaunchScreen> LaunchScreenOrder { get; } =
        [GameLaunchScreen.Ask, GameLaunchScreen.BuiltIn, GameLaunchScreen.External];

    /// <summary>
    /// The only hardware this reaches is a handheld with two of its own panels (the Thor), never a
    /// monitor plugged into a PC — the whole feature is gated behind <see cref="IExternalDisplayProbe"/>,
    /// which only the Android head implements. So the labels name the device's two screens rather than
    /// saying "external", which reads as a TV that is not there. The enum member stays
    /// <see cref="GameLaunchScreen.External"/>: it is a stored value.
    /// </summary>
    public static string LaunchScreenLabel(GameLaunchScreen screen) => screen switch
    {
        GameLaunchScreen.BuiltIn => "Main screen",
        GameLaunchScreen.External => "Second screen",
        _ => "Ask each time",
    };

    /// <summary>
    /// The complete emulator/core selection. Changing the underlying emulator swaps editable drafts;
    /// changing only a RetroArch core keeps the same executable/argument draft.
    /// </summary>
    [ObservableProperty]
    public partial EmulatorChoice? SelectedChoice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCorePath))]
    [NotifyPropertyChangedFor(nameof(CoreFileName))]
    [NotifyCanExecuteChangedFor(nameof(ClearCoreCommand))]
    public partial string CorePath { get; set; }

    [ObservableProperty]
    public partial LibretroCoreOption? SelectedCore { get; set; }

    [ObservableProperty]
    public partial string CoreSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRescan))]
    [NotifyPropertyChangedFor(nameof(CanSyncLibrary))]
    [NotifyPropertyChangedFor(nameof(CanManageLibraryFolders))]
    [NotifyCanExecuteChangedFor(nameof(RescanLibraryCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncLibraryCommand))]
    public partial bool IsMaintenanceBlocked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaintenanceStatus))]
    public partial string MaintenanceStatusText { get; set; } = string.Empty;

    public bool HasRescanLibrary => _rescanLibrary is not null;
    public bool CanRescan => HasRescanLibrary && !IsMaintenanceBlocked;
    public bool HasSyncLibrary => _syncLibrary is not null;
    public bool CanSyncLibrary => HasSyncLibrary && !IsMaintenanceBlocked;
    public bool HasMaintenanceStatus => !string.IsNullOrWhiteSpace(MaintenanceStatusText);
    public bool HasFolderManagement => _folderActions is not null;
    public bool HasRememberedFolders => LibraryFolders.Count > 0;
    public bool CanManageLibraryFolders => HasFolderManagement && !IsMaintenanceBlocked;

    public EmulatorSettingsRowViewModel(
        GameSystem system,
        EmulatorDefinition emulator,
        EmulatorConfiguration? configuration,
        IDialogService dialogs,
        Func<EmulatorSettingsRowViewModel, Task>? rescanLibrary = null,
        Func<EmulatorSettingsRowViewModel, Task>? syncLibrary = null,
        bool isExpanded = false,
        string? emulatorInstallationId = null,
        bool isExecutableShared = false,
        IAppLogger? logger = null,
        string? homeDirectory = null,
        LibraryFolderManagementActions? folderActions = null,
        Func<Func<Task<string>>, Action<string>, Task>? runFolderMaintenance = null,
        IReadOnlyList<EmulatorDefinition>? supportedEmulators = null,
        SystemEmulatorProfiles? profiles = null,
        IReadOnlyList<LibraryFolder>? initialLibraryFolders = null,
        IReadOnlyList<EmulatorChoice>? fixedChoices = null)
    {
        _dialogs = dialogs;
        _logger = logger ?? NullAppLogger.Instance;
        _rescanLibrary = rescanLibrary;
        _syncLibrary = syncLibrary;
        _folderActions = folderActions;
        _runFolderMaintenance = runFolderMaintenance;
        _homeDirectory = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _fixedChoices = fixedChoices;
        SystemId = system.Id;
        SystemName = system.Name;
        SystemShortName = system.ShortName;
        AccentColor = system.AccentColor;
        IsDualScreen = system.IsDualScreen;

        // Registered definitions own editable drafts. A fixed platform catalog may add selection-only
        // Android emulators that have no desktop executable definition.
        var available = (supportedEmulators is { Count: > 0 } ? supportedEmulators : [emulator])
            .Where(candidate => candidate.Supports(system.Id))
            .ToList();
        if (available.Count == 0)
            available = [emulator];
        if (fixedChoices is { Count: > 0 })
        {
            foreach (var choiceGroup in fixedChoices.GroupBy(choice => choice.EmulatorId, StringComparer.Ordinal))
            {
                if (available.Any(candidate => string.Equals(
                        candidate.Id,
                        choiceGroup.Key,
                        StringComparison.Ordinal)))
                {
                    continue;
                }

                var firstChoice = choiceGroup.First();
                var separator = firstChoice.DisplayName.IndexOf(" · ", StringComparison.Ordinal);
                var displayName = separator >= 0
                    ? firstChoice.DisplayName[..separator]
                    : firstChoice.DisplayName;
                available.Add(new EmulatorDefinition(
                    choiceGroup.Key,
                    displayName,
                    [system.Id],
                    string.Empty,
                    RequiresCorePath: choiceGroup.Any(choice => !string.IsNullOrWhiteSpace(choice.CorePath)),
                    SharesDefaultInstallation: string.Equals(
                        choiceGroup.Key,
                        "retroarch",
                        StringComparison.Ordinal)));
            }
        }

        _emulatorsById = available
            .DistinctBy(candidate => candidate.Id, StringComparer.Ordinal)
            .ToDictionary(candidate => candidate.Id, StringComparer.Ordinal);

        // One editable draft per emulator so switching the picker keeps each profile's own edits.
        foreach (var candidate in available)
        {
            var candidateConfig = profiles is not null
                ? profiles.ForEmulator(candidate.Id)
                : string.Equals(candidate.Id, emulator.Id, StringComparison.Ordinal)
                    ? configuration
                    : null;
            var fallbackInstallationId = string.Equals(candidate.Id, emulator.Id, StringComparison.Ordinal)
                ? emulatorInstallationId
                : null;
            _drafts[candidate.Id] = DraftFor(system.Id, candidate, candidateConfig, fallbackInstallationId);
        }

        var configuredEmulatorId = profiles?.ActiveEmulatorId ?? configuration?.EmulatorId;
        var configuredCorePath = configuredEmulatorId is null
            ? null
            : profiles?.ForEmulator(configuredEmulatorId)?.CorePath ?? configuration?.CorePath;
        var initialChoice = fixedChoices?.FirstOrDefault(choice =>
            choice.Matches(configuredEmulatorId, configuredCorePath));
        // A fixed catalog is authoritative. In particular, legacy Android rows may say only
        // "retroarch" with no core even though that pair cannot launch; use the maintained-first
        // choice that the launch service would actually fall back to instead of inventing a fifth
        // picker item such as "RetroArch (current)".
        if (initialChoice is null && fixedChoices is { Count: > 0 })
            initialChoice = fixedChoices[0];

        var activeEmulatorId =
            initialChoice?.EmulatorId is { } selected && _emulatorsById.ContainsKey(selected) ? selected
            : profiles?.ActiveEmulatorId is { } chosen && _emulatorsById.ContainsKey(chosen) ? chosen
            : configuration?.EmulatorId is { } configured && _emulatorsById.ContainsKey(configured) ? configured
            : _emulatorsById.ContainsKey(emulator.Id) ? emulator.Id
            : available[0].Id;

        // Identity and editable fields are set by LoadProfile under the switch guard so seeding never
        // fires the shared-installation propagation before the parent has wired it up.
        EmulatorName = string.Empty;
        EmulatorId = string.Empty;
        EmulatorInstallationId = string.Empty;
        DefaultLaunchArguments = string.Empty;
        ExecutablePath = string.Empty;
        LaunchArguments = string.Empty;
        CorePath = string.Empty;
        IsExecutableShared = isExecutableShared;
        _switchingProfile = true;
        LoadProfile(activeEmulatorId);
        if (initialChoice is not null)
            CorePath = initialChoice.CorePath ?? string.Empty;
        _switchingProfile = false;

        RefreshAvailableCores();
        IsExpanded = isExpanded;
        RefreshLibraryFolders(initialLibraryFolders);

        // Per-system, so it is seeded from the active profile (whichever emulator was current) and kept
        // across picker changes. A brand-new system with no stored config reads as Ask.
        LaunchScreen = profiles?.Active?.LaunchScreen ?? configuration?.LaunchScreen ?? GameLaunchScreen.Ask;
    }

    private static ProfileDraft DraftFor(
        string systemId,
        EmulatorDefinition emulator,
        EmulatorConfiguration? configuration,
        string? fallbackInstallationId)
    {
        var installationId = configuration?.EmulatorInstallationId
            ?? fallbackInstallationId
            ?? emulator.GetDefaultInstallationId(systemId);
        return new ProfileDraft(
            installationId,
            configuration?.ExecutablePath ?? string.Empty,
            configuration?.LaunchTarget is FlatpakApplicationTarget ? "Flatpak" : "Direct",
            // The ref includes any pinned branch (appId//branch) so it matches a dropdown entry.
            (configuration?.LaunchTarget as FlatpakApplicationTarget)?.Ref ?? string.Empty,
            configuration?.LaunchArguments ?? emulator.DefaultLaunchArguments,
            configuration?.CorePath ?? string.Empty);
    }

    // Applies one profile's emulator identity and its editable draft to the live fields. Runs under
    // the switch guard so the field setters do not re-broadcast a shared-installation edit.
    private void LoadProfile(string emulatorId)
    {
        var emulator = _emulatorsById[emulatorId];
        var draft = _drafts[emulatorId];
        EmulatorName = emulator.Name;
        EmulatorId = emulator.Id;
        DefaultLaunchArguments = emulator.DefaultLaunchArguments;
        RequiresCorePath = emulator.RequiresCorePath;
        EmulatorInstallationId = draft.InstallationId;
        ExecutablePath = draft.ExecutablePath;
        TargetKind = draft.TargetKind;
        FlatpakAppId = draft.FlatpakAppId;
        LaunchArguments = draft.LaunchArguments;
        CorePath = draft.CorePath;
        OnPropertyChanged(nameof(EmulatorName));
        OnPropertyChanged(nameof(EmulatorId));
        OnPropertyChanged(nameof(DefaultLaunchArguments));
        OnPropertyChanged(nameof(RequiresCorePath));
        OnPropertyChanged(nameof(EmulatorInstallationId));
        OnPropertyChanged(nameof(ExecutableDescription));
        RefreshFlatpakApplicationIds();
    }

    private void RefreshFlatpakApplicationIds()
    {
        AvailableFlatpakApplicationIds.Clear();
        if (!CanSelectFlatpakTarget)
            return;
        // One entry per installed branch: a bare app id when only one branch exists, or branch-qualified
        // refs (e.g. net.pcsx2.PCSX2//beta) when both a stable and a nightly build are installed.
        foreach (var reference in new FlatpakApplicationDiscovery().FindInstalledForEmulator(EmulatorId))
            AvailableFlatpakApplicationIds.Add(reference);
    }

    partial void OnSelectedChoiceChanged(EmulatorChoice? value)
    {
        if (_switchingProfile ||
            _refreshingChoices ||
            value is null ||
            !_emulatorsById.ContainsKey(value.EmulatorId))
        {
            return;
        }

        var emulatorChanged = !string.Equals(value.EmulatorId, EmulatorId, StringComparison.Ordinal);
        _switchingProfile = true;
        try
        {
            if (emulatorChanged)
            {
                // Snapshot the profile we are leaving so its edits return when it is picked again.
                CaptureActiveDraft();
                LoadProfile(value.EmulatorId);
            }

            CorePath = value.CorePath ?? string.Empty;
            CaptureActiveDraft();
        }
        finally
        {
            _switchingProfile = false;
        }

        if (emulatorChanged)
        {
            RefreshAvailableCores();
            ProfileChanged?.Invoke(this);
        }
        else
        {
            RefreshAvailableChoices();
        }
    }

    /// <summary>Recomputes whether this row's executable is shared with other systems. Parent-driven,
    /// because sharing depends on how many rows point at the same installation after a profile switch.</summary>
    public void SetExecutableShared(bool shared)
    {
        if (IsExecutableShared == shared)
            return;
        IsExecutableShared = shared;
        OnPropertyChanged(nameof(IsExecutableShared));
        OnPropertyChanged(nameof(ExecutableDescription));
    }

    partial void OnExecutablePathChanged(string value)
    {
        RefreshAvailableCores();
        if (!_switchingProfile)
            ExecutablePathEdited?.Invoke(this, value);
    }

    partial void OnTargetKindChanged(string value)
    {
        OnPropertyChanged(nameof(IsFlatpakTarget));
        OnPropertyChanged(nameof(IsEditableFlatpakTarget));
        OnPropertyChanged(nameof(IsDirectTarget));
        OnPropertyChanged(nameof(IsUnsupportedFlatpakTarget));
        RefreshAvailableCores();
        if (!_switchingProfile)
            TargetKindEdited?.Invoke(this, value);
    }

    partial void OnFlatpakAppIdChanged(string value)
    {
        RefreshAvailableCores();
        if (!_switchingProfile)
            FlatpakAppIdEdited?.Invoke(this, value);
    }

    partial void OnCorePathChanged(string value)
    {
        SelectedCore = AvailableCores.FirstOrDefault(option =>
            string.Equals(option.Path, value, StringComparison.OrdinalIgnoreCase));
        if (!_switchingProfile && !_refreshingChoices)
            RefreshAvailableChoices();
    }

    partial void OnSelectedCoreChanged(LibretroCoreOption? value)
    {
        if (value is not null && !string.Equals(CorePath, value.Path, StringComparison.OrdinalIgnoreCase))
            CorePath = value.Path;
    }

    partial void OnCoreSearchTextChanged(string value) => RefreshFilteredCores();

    private void RefreshAvailableCores()
    {
        AvailableCores.Clear();
        FilteredCores.Clear();
        if (_fixedChoices is not null)
        {
            RefreshAvailableChoices();
            return;
        }

        var coreEmulator = _emulatorsById.Values.FirstOrDefault(candidate => candidate.RequiresCorePath);
        if (coreEmulator is null)
        {
            RefreshAvailableChoices();
            return;
        }

        var coreDraft = CurrentDraftFor(coreEmulator.Id);

        try
        {
            foreach (var core in CoreSearchDirectories(coreDraft)
                         .Where(Directory.Exists)
                         .SelectMany(Directory.EnumerateFiles)
                         .Where(path => Path.GetExtension(path) is ".dll" or ".dylib" or ".so")
                         .DistinctBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
            {
                AvailableCores.Add(new LibretroCoreOption(Path.GetFileName(core), core));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not list RetroArch cores for {SystemName}.", ex);
        }

        SelectedCore = AvailableCores.FirstOrDefault(option =>
            string.Equals(option.Path, CorePath, StringComparison.OrdinalIgnoreCase));
        RefreshFilteredCores();
        RefreshAvailableChoices();
    }

    private void RefreshAvailableChoices()
    {
        var coreEmulator = _emulatorsById.Values.FirstOrDefault(candidate => candidate.RequiresCorePath);
        var rememberedCorePath = coreEmulator is null ? null : CurrentDraftFor(coreEmulator.Id).CorePath;
        var choices = _fixedChoices ?? DesktopEmulatorChoiceCatalog.ForSystem(
            SystemId,
            _emulatorsById.Values.ToList(),
            AvailableCores.Select(core => core.Path),
            IsCoreTargetConfigured(),
            rememberedCorePath);

        _refreshingChoices = true;
        try
        {
            AvailableChoices.Clear();
            foreach (var choice in choices)
                AvailableChoices.Add(choice);

            var selected = AvailableChoices.FirstOrDefault(choice =>
                choice.Matches(EmulatorId, CorePath));
            if (selected is null && _fixedChoices is null && !string.IsNullOrWhiteSpace(EmulatorId))
            {
                // Keep a choice written by an older build or another compatible frontend visible.
                // Desktop should not silently replace a temporarily missing core merely because its
                // drive is disconnected. Fixed platform catalogs remain exact and authoritative.
                var emulatorName = _emulatorsById.GetValueOrDefault(EmulatorId)?.Name ?? EmulatorId;
                var corePath = string.IsNullOrWhiteSpace(CorePath) ? null : CorePath.Trim();
                var coreId = corePath is null ? null : Path.GetFileNameWithoutExtension(corePath);
                selected = new EmulatorChoice(
                    coreId is null ? $"{EmulatorId}:current" : $"{EmulatorId}:{coreId}:current",
                    coreId is null
                        ? $"{emulatorName} (current)"
                        : $"{emulatorName} · {coreId} (current)",
                    EmulatorId,
                    coreId,
                    corePath);
                AvailableChoices.Add(selected);
            }

            SelectedChoice = selected;
            OnPropertyChanged(nameof(HasEmulatorChoices));
        }
        finally
        {
            _refreshingChoices = false;
        }
    }

    private bool IsCoreTargetConfigured()
    {
        var coreEmulator = _emulatorsById.Values.FirstOrDefault(candidate => candidate.RequiresCorePath);
        if (coreEmulator is null)
            return false;
        var draft = CurrentDraftFor(coreEmulator.Id);
        return draft.TargetKind == "Flatpak"
            ? !string.IsNullOrWhiteSpace(draft.FlatpakAppId)
            : !string.IsNullOrWhiteSpace(draft.ExecutablePath);
    }

    private ProfileDraft CurrentDraftFor(string emulatorId) =>
        string.Equals(EmulatorId, emulatorId, StringComparison.Ordinal)
            ? new ProfileDraft(
                EmulatorInstallationId,
                ExecutablePath,
                TargetKind,
                FlatpakAppId,
                LaunchArguments,
                CorePath)
            : _drafts[emulatorId];

    // Direct RetroArch targets keep cores beside the executable in portable and AppImage-extracted
    // layouts, or under the user's RetroArch config directory on Linux/macOS. Flatpak targets have
    // no executable path; their per-app directory is mounted at the identical host path, so derive
    // the installed-core directory from the user-selected app id without editing RetroArch config.
    private IEnumerable<string> CoreSearchDirectories(ProfileDraft draft)
    {
        if (draft.TargetKind == "Flatpak")
        {
            if (string.IsNullOrWhiteSpace(_homeDirectory) || string.IsNullOrWhiteSpace(draft.FlatpakAppId))
                yield break;

            // Every branch of an app shares one per-app data directory (.var/app/<appId>), so strip any
            // pinned branch from the ref before building the path.
            yield return Path.Combine(
                _homeDirectory,
                ".var",
                "app",
                FlatpakApplicationTarget.Parse(draft.FlatpakAppId).AppId,
                "config",
                "retroarch",
                "cores");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(draft.ExecutablePath))
            yield break;

        var emulatorDirectory = Path.GetDirectoryName(draft.ExecutablePath);
        if (string.IsNullOrWhiteSpace(emulatorDirectory))
            yield break;

        yield return Path.Combine(emulatorDirectory, "cores");
        if (OperatingSystem.IsWindows())
            yield break;

        if (OperatingSystem.IsMacOS())
        {
            // macOS RetroArch keeps downloaded cores under Application Support — the same user root its
            // config and saves use — not beside the `.app` and not under XDG. Without this the core
            // dropdown was always empty on macOS even after cores were installed.
            if (!string.IsNullOrWhiteSpace(_homeDirectory))
            {
                yield return Path.Combine(
                    _homeDirectory, "Library", "Application Support", "RetroArch", "cores");
            }

            yield break;
        }

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
        {
            if (string.IsNullOrWhiteSpace(_homeDirectory))
                yield break;
            configHome = Path.Combine(_homeDirectory, ".config");
        }

        yield return Path.Combine(configHome, "retroarch", "cores");
    }

    private void RefreshFilteredCores()
    {
        FilteredCores.Clear();
        var filter = CoreSearchText.Trim();
        foreach (var core in AvailableCores.Where(core =>
                     string.IsNullOrWhiteSpace(filter) ||
                     core.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            FilteredCores.Add(core);
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        try
        {
            var path = await _dialogs.PickEmulatorExecutableAsync(EmulatorName);
            if (path is not null)
                ExecutablePath = path;
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not choose the {EmulatorName} executable.", ex);
            MaintenanceStatusText = $"Could not open the executable picker: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task BrowseCoreAsync()
    {
        if (!RequiresCorePath)
            return;

        try
        {
            var path = await _dialogs.PickLibretroCoreAsync(SystemName);
            if (path is not null)
                CorePath = path;
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not choose the {SystemName} RetroArch core.", ex);
            MaintenanceStatusText = $"Could not open the core picker: {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(HasCorePath))]
    private void ClearCore() => CorePath = string.Empty;

    [RelayCommand]
    private void ResetArguments() => LaunchArguments = DefaultLaunchArguments;

    /// <summary>Explicit migration from a legacy/unavailable Flatpak target to a direct executable.</summary>
    [RelayCommand]
    private void UseDirectTarget() => TargetKind = "Direct";

    [RelayCommand(CanExecute = nameof(CanRescan))]
    private Task RescanLibraryAsync() =>
        _rescanLibrary?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanSyncLibrary))]
    private Task SyncLibraryAsync() =>
        _syncLibrary?.Invoke(this) ?? Task.CompletedTask;

    [RelayCommand]
    private async Task AddLibraryFolderAsync()
    {
        if (!CanManageLibraryFolders || _folderActions is null)
            return;
        var path = await _dialogs.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;
        await RunFolderActionAsync(() => _folderActions.Add(SystemId, path));
    }

    private async Task ChangeLibraryFolderAsync(LibraryFolderRowViewModel? folder)
    {
        if (!CanManageLibraryFolders || _folderActions is null || folder is null)
            return;
        var path = await _dialogs.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;
        await RunFolderActionAsync(() => _folderActions.Change(SystemId, folder.Id, path));
    }

    private Task ForgetLibraryFolderAsync(LibraryFolderRowViewModel? folder) =>
        !CanManageLibraryFolders || _folderActions is null || folder is null
            ? Task.CompletedTask
            : RunFolderActionAsync(() => _folderActions.Forget(SystemId, folder.Id));

    private async Task RunFolderActionAsync(Func<Task<string>> action)
    {
        async Task<string> RunAndRefreshAsync()
        {
            var result = await action();
            RefreshLibraryFolders();
            return result;
        }

        if (_runFolderMaintenance is not null)
        {
            await _runFolderMaintenance(
                RunAndRefreshAsync,
                message => MaintenanceStatusText = message);
            return;
        }

        IsMaintenanceBlocked = true;
        try
        {
            MaintenanceStatusText = await RunAndRefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not manage remembered folders for {SystemName}.", ex);
            MaintenanceStatusText = $"Folder change failed: {ex.Message}";
        }
        finally
        {
            IsMaintenanceBlocked = false;
        }
    }

    // <paramref name="seeded"/> is the row's folders pre-read off the UI thread when Settings opened;
    // an on-demand refresh after a folder edit passes null and reads the current rows directly.
    private void RefreshLibraryFolders(IReadOnlyList<LibraryFolder>? seeded = null)
    {
        LibraryFolders.Clear();
        if (_folderActions is not null)
        {
            foreach (var folder in seeded ?? _folderActions.Get(SystemId))
            {
                var row = new LibraryFolderRowViewModel(
                    folder,
                    ChangeLibraryFolderAsync,
                    ForgetLibraryFolderAsync);
                LibraryFolders.Add(row);
                _ = row.RefreshAvailabilityAsync();
            }
        }
        OnPropertyChanged(nameof(HasRememberedFolders));
    }

    /// <summary>The active profile's configuration. Kept for callers that only need the current one.</summary>
    public EmulatorConfiguration ToConfiguration()
    {
        CaptureActiveDraft();
        return ConfigurationFrom(EmulatorId, _drafts[EmulatorId]);
    }

    /// <summary>
    /// Every profile worth persisting for this system: the active one always, plus any alternative
    /// the user has actually configured, so a second emulator's setup is not lost and an untouched
    /// alternative does not create an empty row.
    /// </summary>
    public IReadOnlyList<EmulatorConfiguration> ToConfigurations()
    {
        CaptureActiveDraft();
        var configurations = new List<EmulatorConfiguration>();
        foreach (var emulator in _emulatorsById.Values)
        {
            var isActive = string.Equals(emulator.Id, EmulatorId, StringComparison.Ordinal);
            var draft = _drafts[emulator.Id];
            if (isActive || IsConfigured(emulator.Id, draft))
                configurations.Add(ConfigurationFrom(emulator.Id, draft));
        }

        return configurations;
    }

    private void CaptureActiveDraft() =>
        _drafts[EmulatorId] = new ProfileDraft(
            EmulatorInstallationId, ExecutablePath, TargetKind, FlatpakAppId, LaunchArguments, CorePath);

    private bool IsConfigured(string emulatorId, ProfileDraft draft) =>
        !string.IsNullOrWhiteSpace(draft.ExecutablePath) ||
        !string.IsNullOrWhiteSpace(draft.CorePath) ||
        (draft.TargetKind == "Flatpak" && !string.IsNullOrWhiteSpace(draft.FlatpakAppId)) ||
        !string.Equals(
            draft.LaunchArguments,
            _emulatorsById[emulatorId].DefaultLaunchArguments,
            StringComparison.Ordinal);

    private EmulatorConfiguration ConfigurationFrom(string emulatorId, ProfileDraft draft)
    {
        var isFlatpak = draft.TargetKind == "Flatpak";
        var executablePath = string.IsNullOrWhiteSpace(draft.ExecutablePath) ? null : draft.ExecutablePath.Trim();
        return new EmulatorConfiguration(
            SystemId,
            isFlatpak ? null : executablePath,
            draft.LaunchArguments)
        {
            LaunchTarget = isFlatpak
                ? (string.IsNullOrWhiteSpace(draft.FlatpakAppId) ? null : FlatpakApplicationTarget.Parse(draft.FlatpakAppId.Trim()))
                : (executablePath is null ? null : new DirectExecutableTarget(executablePath)),
            EmulatorId = emulatorId,
            EmulatorInstallationId = draft.InstallationId,
            CorePath = string.IsNullOrWhiteSpace(draft.CorePath) ? null : draft.CorePath.Trim(),
            // Per-system: stamped onto every profile so the preference is the same whichever emulator is
            // active, and reading it back through the active-selection Get() always agrees.
            LaunchScreen = LaunchScreen,
        };
    }

    public sealed record LibretroCoreOption(string Name, string Path);

    // The editable state of one emulator profile, cached per emulator so switching the picker keeps
    // each profile's own edits until Save.
    private sealed record ProfileDraft(
        string InstallationId,
        string ExecutablePath,
        string TargetKind,
        string FlatpakAppId,
        string LaunchArguments,
        string CorePath);
}
