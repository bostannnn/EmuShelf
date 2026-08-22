using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Systems;
using EmuShelf.Core.TexturePacks;

namespace EmuShelf.App.ViewModels;

public enum SettingsSection
{
    General,
    Emulators,
    Hotkeys,
    RetroAchievements,
    ArtworkMetadata,
    Saves,
    TexturePacks,
    Themes,
    About,
}

public partial class EmulatorSettingsViewModel : ViewModelBase
{
    private readonly IEmulatorConfigurationStore _configurations;
    private readonly IDialogService _dialogs;
    private readonly Func<bool, Task>? _setAmbientThemeFromArtwork;
    private readonly Func<bool, Task>? _setCrtScreenEffect;
    private bool _suppressAmbientCallback;
    private readonly LibraryMaintenanceActions? _maintenance;
    private readonly IMetadataPreferencesService? _metadataPreferences;
    private readonly RetroAchievementsSettingsContext? _retroAchievements;
    private readonly ScreenScraperSettingsContext? _screenScraper;
    private readonly CloudSaveSyncSettingsContext? _cloudSaves;
    private readonly TexturePackSettingsContext? _texturePacks;
    private readonly HotkeySettingsContext? _hotkeys;
    private readonly SteamInputTemplateInstaller _steamTemplateInstaller;
    private readonly AppUpdateCoordinator? _updates;
    private readonly IAppLogger _logger;
    // How the built-in Google Drive sign-in page is opened. Injected so tests can drive the failure
    // path without launching a real browser, and so the host can substitute a platform launcher.
    private readonly Action<Uri> _openSignInUri;
    // A human has to complete an OAuth consent in a browser; without a bound this would wait on the
    // loopback redirect forever, wedging the whole cloud UI (IsCloudBusy) and the coordinator's gate.
    private static readonly TimeSpan ManagedConnectTimeout = TimeSpan.FromMinutes(5);
    // Held only for the duration of one cloud operation so the Stop button can reach it.
    private CancellationTokenSource? _cloudCancellation;
    private bool _synchronizingSharedInstallation;

    public ObservableCollection<EmulatorSettingsRowViewModel> Rows { get; }
    public IReadOnlyList<SettingsSection> Sections { get; }
    public bool HasRetroAchievements => _retroAchievements is not null;
    public bool HasScreenScraper => _screenScraper is not null;
    public bool HasCloudSaves => _cloudSaves is not null;
    public bool HasTexturePacks => _texturePacks is not null;
    public bool HasHotkeys => _hotkeys is { Emulators.Count: > 0 };
    public event Action<bool>? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralSection))]
    [NotifyPropertyChangedFor(nameof(IsEmulatorsSection))]
    [NotifyPropertyChangedFor(nameof(IsHotkeysSection))]
    [NotifyPropertyChangedFor(nameof(IsRetroAchievementsSection))]
    [NotifyPropertyChangedFor(nameof(IsArtworkMetadataSection))]
    [NotifyPropertyChangedFor(nameof(IsSavesSection))]
    [NotifyPropertyChangedFor(nameof(IsTexturePacksSection))]
    [NotifyPropertyChangedFor(nameof(IsThemesSection))]
    [NotifyPropertyChangedFor(nameof(IsAboutSection))]
    [NotifyPropertyChangedFor(nameof(SectionSubtitle))]
    public partial SettingsSection SelectedSection { get; set; } = SettingsSection.General;

    /// <summary>Header subtitle that follows the selected section, instead of one static line that
    /// only described a couple of sections.</summary>
    public string SectionSubtitle => SelectedSection switch
    {
        SettingsSection.General => "Library visibility, maintenance, and where EmuShelf keeps its data.",
        SettingsSection.Emulators => "Point each system at its emulator and set how it launches.",
        SettingsSection.Hotkeys => "Write one keyboard-hotkey scheme into each emulator's own settings.",
        SettingsSection.RetroAchievements => "Connect your RetroAchievements account to track progress.",
        SettingsSection.ArtworkMetadata => "Fetch titles, covers, and artwork from the built-in catalogue, ScreenScraper, or web image search.",
        SettingsSection.Saves => "Sync in-game saves between machines through your own Google Drive.",
        SettingsSection.TexturePacks => "See the replacement-texture packs your emulators already have.",
        SettingsSection.Themes => "Choose how EmuShelf looks.",
        SettingsSection.About => "Version, updates, and build details.",
        _ => string.Empty,
    };

    public bool IsGeneralSection => SelectedSection == SettingsSection.General;
    public bool IsEmulatorsSection => SelectedSection == SettingsSection.Emulators;
    public bool IsHotkeysSection => SelectedSection == SettingsSection.Hotkeys;
    public bool IsRetroAchievementsSection => SelectedSection == SettingsSection.RetroAchievements;
    public bool IsArtworkMetadataSection => SelectedSection == SettingsSection.ArtworkMetadata;
    public bool IsSavesSection => SelectedSection == SettingsSection.Saves;
    public bool IsTexturePacksSection => SelectedSection == SettingsSection.TexturePacks;
    public bool IsThemesSection => SelectedSection == SettingsSection.Themes;
    public bool IsAboutSection => SelectedSection == SettingsSection.About;

    /// <summary>App version stamped from the newest git tag at build time, e.g. "1.0.8".</summary>
    public string AppVersionDisplay => AppBuildInfo.Version;

    /// <summary>Short hash of the last commit compiled into this build, or a fallback note.</summary>
    public string AppCommitDisplay => string.IsNullOrEmpty(AppBuildInfo.CommitHash)
        ? "unavailable (built without git)"
        : AppBuildInfo.CommitHash;

    /// <summary>Local date/time of that commit; empty when it was not stamped.</summary>
    public string AppCommitDateDisplay => AppBuildInfo.CommitDate is { } date
        ? date.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : string.Empty;

    public bool HasCommitDate => AppBuildInfo.CommitDate is not null;

    /// <summary>Whether in-app update checking is wired up (false in tests/design-time).</summary>
    public bool HasUpdateChecker => _updates is not null;

    /// <summary>The shared update coordinator, exposed so the About card and the Gamepad settings
    /// projection can bind the same live download progress the main-window banner shows, instead of
    /// only a static status line; null in tests/design-time.</summary>
    public AppUpdateCoordinator? Updates => _updates;

    /// <summary>Result of the most recent manual check, shown under the button.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateStatus))]
    public partial string UpdateStatusText { get; set; } = string.Empty;

    public bool HasUpdateStatus => !string.IsNullOrWhiteSpace(UpdateStatusText);

    /// <summary>True once a check has found a newer version, so the install action can appear.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    public partial bool IsUpdateAvailable { get; set; }

    /// <summary>True while a check or an install/download is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    public partial bool IsUpdateBusy { get; set; }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        if (_updates is null || IsUpdateBusy)
            return;

        IsUpdateBusy = true;
        UpdateStatusText = "Checking for updates…";
        try
        {
            UpdateStatusText = await _updates.CheckManuallyAsync();
            IsUpdateAvailable = _updates.HasAvailableUpdate;
        }
        catch (Exception ex)
        {
            _logger.Error("Update check failed from Settings.", ex);
            UpdateStatusText = "Couldn't check for updates.";
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    private bool CanCheckForUpdates() => _updates is not null && !IsUpdateBusy;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (_updates is null || !_updates.HasAvailableUpdate || IsUpdateBusy)
            return;

        IsUpdateBusy = true;
        UpdateStatusText = "Downloading the update…";
        try
        {
            // Returns on Windows/macOS just before the app exits to relaunch; on the AppImage build it
            // re-execs and never returns. The coordinator surfaces any failure through its own status.
            await _updates.InstallAsync();
            UpdateStatusText = _updates.HasError ? _updates.StatusText : "Restarting to finish the update…";
        }
        catch (Exception ex)
        {
            _logger.Error("Installing the update failed from Settings.", ex);
            UpdateStatusText = "Couldn't install the update.";
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    private bool CanInstallUpdate() => _updates is not null && IsUpdateAvailable && !IsUpdateBusy;

    /// <summary>Appearance choices shown as a Themes section so Desktop and Gamepad settings both
    /// expose theme selection; empty when the host did not provide them.</summary>
    public IReadOnlyList<ThemeChoiceViewModel> ThemeChoices { get; }

    public bool HasThemes => ThemeChoices.Count > 0;

    /// <summary>Sits with the theme gallery: recolours the couch UI from the focused game's artwork,
    /// falling back to the chosen theme. Applied live through the host callback.</summary>
    [ObservableProperty]
    public partial bool AmbientThemeFromArtwork { get; set; }

    /// <summary>Whether the couch shelf is presented through a simulated CRT tube.</summary>
    [ObservableProperty]
    public partial bool CrtScreenEffect { get; set; }

    [ObservableProperty]
    public partial string RetroAchievementsUsername { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RetroAchievementsApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRetroAchievementsConnected))]
    [NotifyPropertyChangedFor(nameof(IsRetroAchievementsDisconnected))]
    [NotifyPropertyChangedFor(nameof(CanRefreshRetroAchievementsMatches))]
    [NotifyCanExecuteChangedFor(nameof(RefreshRetroAchievementsMatchesCommand))]
    public partial string? ConnectedAccountName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRetroAchievementsStatus))]
    public partial string RetroAchievementsStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRetroAchievementsProgress))]
    [NotifyPropertyChangedFor(nameof(CanRefreshRetroAchievementsMatches))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(RefreshRetroAchievementsMatchesCommand))]
    public partial bool IsRetroAchievementsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRetroAchievementsProgress))]
    public partial int RetroAchievementsProgressCompleted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRetroAchievementsProgress))]
    public partial int RetroAchievementsProgressTotal { get; set; }

    [ObservableProperty]
    public partial string RetroAchievementsProgressText { get; set; } = string.Empty;

    public bool IsRetroAchievementsConnected => !string.IsNullOrEmpty(ConnectedAccountName);
    public bool IsRetroAchievementsDisconnected => !IsRetroAchievementsConnected;
    public bool HasRetroAchievementsMatchRefresh => _retroAchievements?.RefreshMatchesAsync is not null;
    public bool CanRefreshRetroAchievementsMatches =>
        IsRetroAchievementsConnected && !IsRetroAchievementsBusy && HasRetroAchievementsMatchRefresh;
    public bool HasRetroAchievementsStatus => !string.IsNullOrWhiteSpace(RetroAchievementsStatusText);
    public bool HasRetroAchievementsProgress =>
        IsRetroAchievementsBusy && RetroAchievementsProgressTotal > 0;

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMaintainingLibrary { get; set; }

    // Distinct from IsMaintainingLibrary (which the metadata fetch also raises): true only while a
    // General-section scan action runs (Rescan all consoles, or the Gamepad RPCS3 library sync), so
    // that card's indeterminate bar shows without lighting up during a metadata fetch, a per-system
    // rescan, or a folder edit.
    [ObservableProperty]
    public partial bool IsRescanningAllConsoles { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMaintenanceStatus))]
    public partial string MaintenanceStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadataStatus))]
    public partial string MetadataStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadataProgress))]
    public partial int MetadataProgressCompleted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadataProgress))]
    public partial int MetadataProgressTotal { get; set; }

    [ObservableProperty]
    public partial string MetadataProgressText { get; set; } = string.Empty;

    public bool CanRescanAll => !IsWorking && _maintenance is not null;
    public bool CanFetchAllMetadata =>
        !IsWorking && _maintenance?.FetchAllMetadata is not null;

    public bool IsWorking => IsSaving || IsMaintainingLibrary;

    /// <summary>True while ANY async settings operation is running — save, library maintenance,
    /// cloud sync/connect, account connects, or texture rescan. The global Save/Cancel buttons gate on
    /// this so the window can't be committed or torn down mid-operation (which would race concurrent
    /// writes and orphan the in-flight task's progress callbacks).</summary>
    public bool IsBusy => IsWorking || IsCloudBusy || IsRetroAchievementsBusy
        || IsScreenScraperBusy || IsTexturePackBusy;

    public bool HasMaintenanceStatus => !string.IsNullOrWhiteSpace(MaintenanceStatusText);
    public bool HasMetadataStatus => !string.IsNullOrWhiteSpace(MetadataStatusText);
    public bool HasMetadataProgress => IsMaintainingLibrary && MetadataProgressTotal > 0;

    [ObservableProperty]
    public partial bool AutomaticallyFetchMetadataAfterImport { get; set; }

    /// <summary>Whether the manual "Set cover" picker offers unverified web image search (DuckDuckGo).
    /// Unverified results are never applied automatically; this only toggles the manual picker.</summary>
    [ObservableProperty]
    public partial bool WebImageSearchEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowEmptyPlatforms { get; set; }

    /// <summary>EmuShelf's data folder (database, covers, settings, saves), shown in General so the
    /// user can open it. Empty when the host did not supply it (design-time and tests).</summary>
    public string DataDirectory => _maintenance?.DataDirectory ?? string.Empty;
    public bool HasDataDirectory => !string.IsNullOrWhiteSpace(_maintenance?.DataDirectory);

    /// <summary>
    /// Whether revealing a folder or file in an OS file manager is possible here. The reveal commands
    /// shell out through <see cref="System.Diagnostics.Process"/> with <c>UseShellExecute</c>, which
    /// every desktop OS honours but Android does not — there is no file manager to hand a path to, so
    /// the call throws. Surfaces gate their "open folder / open log" affordances on this so a controller
    /// user is not offered a button that can only fail.
    /// </summary>
    public bool CanRevealFiles => !OperatingSystem.IsAndroid();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDataFolderStatus))]
    public partial string DataFolderStatusText { get; set; } = string.Empty;

    public bool HasDataFolderStatus => !string.IsNullOrWhiteSpace(DataFolderStatusText);

    [ObservableProperty]
    public partial string CloudFolder { get; set; } = "EmuShelf/Saves";

    /// <summary>One row per registered save platform, rendered by a single view template.</summary>
    public ObservableCollection<CloudSavePlatformRowViewModel> CloudPlatforms { get; } = new();
    public ObservableCollection<TexturePackRowViewModel> TexturePlatforms { get; } = new();

    /// <summary>One row per emulator EmuShelf can write the keyboard-hotkey scheme for.</summary>
    public ObservableCollection<HotkeyEmulatorRowViewModel> HotkeyEmulators { get; } = new();

    /// <summary>A human summary of the hotkey scheme, shown at the top of the Hotkeys section.</summary>
    public string HotkeySchemeSummary { get; private set; } = string.Empty;

    /// <summary>True while an apply-to-all pass runs, so its button can show it is working.</summary>
    [ObservableProperty]
    public partial bool IsHotkeyBusy { get; set; }

    /// <summary>Applies the recommended scheme to every configured emulator in turn.</summary>
    [RelayCommand]
    private async Task ApplyAllHotkeys()
    {
        if (_hotkeys is null || IsHotkeyBusy)
            return;

        IsHotkeyBusy = true;
        try
        {
            foreach (var row in HotkeyEmulators)
                await row.RunAsync(_hotkeys.ApplyAsync);
        }
        finally
        {
            IsHotkeyBusy = false;
        }
    }

    /// <summary>The last Steam-template install result message; empty until the button is used.</summary>
    [ObservableProperty]
    public partial string SteamTemplateStatus { get; set; } = string.Empty;

    /// <summary>Installs the bundled Steam Input layout into Steam's templates folder.</summary>
    [RelayCommand]
    private void InstallSteamTemplate()
    {
        var result = _steamTemplateInstaller.Install();
        SteamTemplateStatus = result.Status switch
        {
            SteamTemplateInstallStatus.Installed =>
                "Installed. In Steam, open the emulator's controller settings and pick the \"EmuShelf\" template.",
            SteamTemplateInstallStatus.SteamNotFound =>
                "Couldn't find Steam. Launch Steam once, then try again.",
            _ => result.Detail ?? "The template couldn't be installed.",
        };
    }

    /// <summary>The packs matching the current emulator and status filters.</summary>
    public ObservableCollection<TexturePackEntryViewModel> TexturePackEntries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudDisconnected))]
    [NotifyCanExecuteChangedFor(nameof(ExportDeviceAndCloudSavesCommand))]
    public partial bool IsCloudConnected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudStatus))]
    public partial string CloudStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudSyncProgress))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(ExportDeviceSavesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportDeviceAndCloudSavesCommand))]
    public partial bool IsCloudBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudSyncProgress))]
    public partial int CloudSyncProgressCompleted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudSyncProgress))]
    public partial int CloudSyncProgressTotal { get; set; }

    /// <summary>Whether the transfer is running but has not reported a percentage yet.</summary>
    [ObservableProperty]
    public partial bool IsCloudTransferIndeterminate { get; set; }

    [ObservableProperty]
    public partial string CloudSyncProgressText { get; set; } = string.Empty;

    /// <summary>
    /// Whether this build ships an OAuth client, so the built-in Google Drive transport — the only
    /// transport — can be offered at all. Set once at construction; a build without a client cannot
    /// connect. See <see cref="CloudSaveSyncCoordinator.IsManagedTransportAvailable"/>.
    /// </summary>
    public bool IsManagedTransportAvailable { get; private set; }

    /// <summary>A one-line description of the live connection (the connected Google Drive account).</summary>
    [ObservableProperty]
    public partial string CloudConnectionSummary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSyncLog))]
    [NotifyPropertyChangedFor(nameof(SyncLogUri))]
    public partial string SyncLogPath { get; set; } = string.Empty;

    /// <summary>True once at least one sync has been recorded in the activity log.</summary>
    public bool HasSyncLog => !string.IsNullOrWhiteSpace(SyncLogPath) && File.Exists(SyncLogPath);

    /// <summary>The activity log as a file URI so the view can offer to open it.</summary>
    public Uri? SyncLogUri =>
        string.IsNullOrWhiteSpace(SyncLogPath) ? null : new Uri(SyncLogPath);

    public bool IsCloudDisconnected => !IsCloudConnected;
    public bool HasCloudStatus => !string.IsNullOrWhiteSpace(CloudStatusText);
    public bool HasCloudSyncProgress => IsCloudBusy && CloudSyncProgressTotal > 0;

    [ObservableProperty]
    public partial string ScreenScraperUsername { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ScreenScraperPassword { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScreenScraperConnected))]
    [NotifyPropertyChangedFor(nameof(IsScreenScraperDisconnected))]
    public partial string? ScreenScraperConnectedName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScreenScraperStatus))]
    public partial string ScreenScraperStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool IsScreenScraperBusy { get; set; }

    public bool IsScreenScraperConnected => !string.IsNullOrEmpty(ScreenScraperConnectedName);
    public bool IsScreenScraperDisconnected => !IsScreenScraperConnected;
    public bool HasScreenScraperStatus => !string.IsNullOrWhiteSpace(ScreenScraperStatusText);

    public EmulatorSettingsViewModel(
        IReadOnlyList<GameSystem> systems,
        IReadOnlyList<EmulatorDefinition> emulators,
        IReadOnlyDictionary<string, EmulatorConfiguration?> configured,
        IEmulatorConfigurationStore configurations,
        IDialogService dialogs,
        LibraryMaintenanceActions? maintenance = null,
        IMetadataPreferencesService? metadataPreferences = null,
        IAppLogger? logger = null,
        RetroAchievementsSettingsContext? retroAchievements = null,
        CloudSaveSyncSettingsContext? cloudSaves = null,
        TexturePackSettingsContext? texturePacks = null,
        ScreenScraperSettingsContext? screenScraper = null,
        HotkeySettingsContext? hotkeys = null,
        IReadOnlyList<ThemeChoiceViewModel>? themeChoices = null,
        bool ambientThemeFromArtwork = false,
        Func<bool, Task>? setAmbientThemeFromArtwork = null,
        IReadOnlyDictionary<string, SystemEmulatorProfiles>? profiles = null,
        AppUpdateCoordinator? updates = null,
        IReadOnlyDictionary<string, IReadOnlyList<LibraryFolder>>? libraryFolders = null,
        SteamInputTemplateInstaller? steamTemplateInstaller = null,
        Func<bool, Task>? setCrtScreenEffect = null,
        bool crtShelfEffect = true,
        Action<Uri>? openSignInUri = null,
        IReadOnlyDictionary<string, IReadOnlyList<EmulatorChoice>>? fixedEmulatorChoices = null)
    {
        _configurations = configurations;
        _dialogs = dialogs;
        _maintenance = maintenance;
        _metadataPreferences = metadataPreferences;
        _retroAchievements = retroAchievements;
        _screenScraper = screenScraper;
        _cloudSaves = cloudSaves;
        _texturePacks = texturePacks;
        _hotkeys = hotkeys;
        _steamTemplateInstaller = steamTemplateInstaller ?? new SteamInputTemplateInstaller();
        // Prefer an explicit injection (tests), then the platform hook the head sets (Android fires an
        // ACTION_VIEW intent — Process.Start throws there), then the desktop shell-open default.
        _openSignInUri = openSignInUri ?? App.ExternalUriOpener ?? DefaultOpenSignInUri;
        HotkeySchemeSummary = hotkeys?.SchemeSummary ?? string.Empty;
        _updates = updates;
        IsUpdateAvailable = updates?.HasAvailableUpdate ?? false;
        _logger = logger ?? NullAppLogger.Instance;
        ThemeChoices = themeChoices ?? [];
        _setAmbientThemeFromArtwork = setAmbientThemeFromArtwork;
        _setCrtScreenEffect = setCrtScreenEffect;
        CrtScreenEffect = crtShelfEffect;
        // Seed the toggle without firing the host callback (which would re-apply on open).
        _suppressAmbientCallback = true;
        AmbientThemeFromArtwork = ambientThemeFromArtwork;
        _suppressAmbientCallback = false;

        var sections = new List<SettingsSection> { SettingsSection.General, SettingsSection.Emulators };
        if (hotkeys is { Emulators.Count: > 0 })
            sections.Add(SettingsSection.Hotkeys);
        if (retroAchievements is not null)
            sections.Add(SettingsSection.RetroAchievements);
        if (screenScraper is not null)
            sections.Add(SettingsSection.ArtworkMetadata);
        if (cloudSaves is not null)
            sections.Add(SettingsSection.Saves);
        if (texturePacks is not null)
            sections.Add(SettingsSection.TexturePacks);
        if (HasThemes)
            sections.Add(SettingsSection.Themes);
        // About is always present — it just reads build metadata and needs no host context.
        sections.Add(SettingsSection.About);
        Sections = sections;
        if (hotkeys is not null)
        {
            foreach (var snapshot in hotkeys.Emulators)
                HotkeyEmulators.Add(new HotkeyEmulatorRowViewModel(snapshot, hotkeys));
        }
        if (texturePacks is not null)
            ApplyTexturePackInventory();
        if (cloudSaves is not null)
        {
            var saves = cloudSaves.Current;
            // The built-in Google Drive client is the only transport. It can be offered when the build
            // ships an OAuth client *and* the coordinator handed us a delegate to drive it — either
            // missing means this build cannot connect at all.
            IsManagedTransportAvailable =
                cloudSaves.IsManagedTransportAvailable && cloudSaves.ConnectGoogleDriveManagedAsync is not null;
            CloudFolder = string.IsNullOrWhiteSpace(saves.CloudFolder) ? "EmuShelf/Saves" : saves.CloudFolder!;
            // One row per registered platform. The row owns its own override, detected path, and
            // per-platform actions, so this view model never names an emulator.
            foreach (var platform in cloudSaves.GetPlatforms())
            {
                CloudPlatforms.Add(new CloudSavePlatformRowViewModel(
                    platform,
                    cloudSaves,
                    dialogs,
                    _logger,
                    (systemId, direction) => ForceCloudAsync(systemId, direction)));
            }
            // Mirror the coordinator's own IsConfigured so the two never disagree: the built-in client
            // authenticates as the account and needs only the folder, and a connection left over from
            // the retired rclone transport counts as not connected (the user reconnects).
            IsCloudConnected = saves switch
            {
                { Enabled: false } => false,
                { TransportKind: CloudTransportKind.GoogleDrive } => saves.CloudFolder is { Length: > 0 },
                _ => false,
            };
            CloudConnectionSummary = DescribeConnection(saves);
            SyncLogPath = cloudSaves.SyncLogPath;
        }
        if (retroAchievements?.CurrentAccount is { } account)
        {
            RetroAchievementsUsername = account.Username;
            if (retroAchievements.IsConnected)
            {
                ConnectedAccountName = account.Username;
            }
            else
            {
                RetroAchievementsStatusText =
                    "Reconnect required: this platform keeps your Web API key only for the current session.";
            }
        }
        if (screenScraper is { IsConnected: true })
        {
            ScreenScraperConnectedName = screenScraper.Account?.Username ?? "Connected";
            ScreenScraperStatusText = screenScraper.Account?.Quota is
                { RequestsToday: { } used, MaxRequestsPerDay: { } max }
                ? $"Connected. {used} / {max} requests used today."
                : "Connected.";
        }
        var rows = systems.Select(system =>
        {
            var emulator = emulators.First(candidate => candidate.Supports(system.Id));
            var supportedEmulators = emulators.Where(candidate => candidate.Supports(system.Id)).ToList();
            configured.TryGetValue(system.Id, out var configuration);
            // The full profile set (all stored emulators for this system) drives the picker; when the
            // caller only supplied the active configuration, fall back to a single-profile view of it.
            var systemProfiles = profiles?.GetValueOrDefault(system.Id)
                ?? new SystemEmulatorProfiles(
                    system.Id,
                    configuration?.EmulatorId,
                    configuration is null ? [] : [configuration]);
            var installationId = systemProfiles.Active?.EmulatorInstallationId
                ?? configuration?.EmulatorInstallationId
                ?? emulator.GetDefaultInstallationId(system.Id);
            return new EmulatorSettingsRowViewModel(
                system,
                emulator,
                configuration,
                dialogs,
                maintenance is null || system.Id == "playstation3" ? null : RescanSystemAsync,
                system.Id == "playstation3" && maintenance?.SyncRpcs3Library is not null
                    ? SyncRpcs3LibraryAsync
                    : null,
                isExpanded: false,
                emulatorInstallationId: installationId,
                isExecutableShared: false,
                logger: _logger,
                folderActions: system.Id == "playstation3" ? null : maintenance?.Folders,
                // Folder edits (add/change/forget) are immediate and single-step, so they ignore the
                // per-console progress reporter the scan actions use.
                runFolderMaintenance: (action, report) => RunMaintenanceAsync(
                    _ => action(),
                    "Updating remembered folders…",
                    report),
                supportedEmulators: supportedEmulators,
                profiles: systemProfiles,
                // When the caller pre-fetched every system's folders (one connection, off the UI
                // thread), seed the row from that — including an empty list — so a system with no
                // remembered folders still never reopens the database on the UI thread. A null map
                // (tests) leaves the row to read its own folders as before.
                initialLibraryFolders: libraryFolders is null
                    ? null
                    : libraryFolders.GetValueOrDefault(system.Id) ?? [],
                fixedChoices: fixedEmulatorChoices?.GetValueOrDefault(system.Id));
        }).ToArray();
        Rows = new ObservableCollection<EmulatorSettingsRowViewModel>(rows);
        foreach (var row in Rows)
        {
            row.ExecutablePathEdited += SynchronizeSharedExecutable;
            row.TargetKindEdited += SynchronizeSharedTargetKind;
            row.FlatpakAppIdEdited += SynchronizeSharedFlatpakAppId;
            row.ProfileChanged += OnRowProfileChanged;
        }
        // The active installation of a row can change when its profile changes, so "shared" is derived
        // from the rows themselves rather than the seed configuration.
        RecomputeSharedInstallations();
        AutomaticallyFetchMetadataAfterImport =
            metadataPreferences?.AutomaticallyFetchAfterImport ?? false;
        WebImageSearchEnabled = metadataPreferences?.WebImageSearchEnabled ?? true;
        ShowEmptyPlatforms = maintenance?.GetShowEmptyPlatforms?.Invoke() ?? false;
    }

    /// <summary>
    /// Groups every system's remembered folders by system id so the constructor can seed each row
    /// from a single off-the-UI-thread read. A null input (the batched read was unavailable) returns
    /// null, which leaves each row to read its own folders; an empty list returns an empty map, so a
    /// library with no remembered folders still never reopens the database while Settings builds.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<LibraryFolder>>? GroupLibraryFolders(
        IReadOnlyList<LibraryFolder>? folders) =>
        folders is null
            ? null
            : folders
                .GroupBy(folder => folder.SystemId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<LibraryFolder>)group.ToArray(),
                    StringComparer.Ordinal);

    partial void OnCrtScreenEffectChanged(bool value)
    {
        _ = _setCrtScreenEffect?.Invoke(value);
    }

    partial void OnAmbientThemeFromArtworkChanged(bool value)
    {
        if (_suppressAmbientCallback)
            return;
        _ = _setAmbientThemeFromArtwork?.Invoke(value);
    }

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanRescanAll));
        OnPropertyChanged(nameof(CanFetchAllMetadata));
        UpdateRowMaintenanceState();
    }

    partial void OnIsMaintainingLibraryChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRescanAll));
        OnPropertyChanged(nameof(CanFetchAllMetadata));
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(IsBusy));
        UpdateRowMaintenanceState();
    }

    [RelayCommand]
    private async Task RescanAllAsync()
    {
        // Gamepad collapses the whole General section into one status pill, so a prior metadata
        // fetch's completion line (MetadataStatusText, which outranks MaintenanceStatusText in the
        // pill's FirstNonEmpty) would otherwise mask this rescan's status. Clear it up front.
        MetadataStatusText = string.Empty;
        IsRescanningAllConsoles = true;
        try
        {
            await RunMaintenanceAsync(
                _maintenance?.RescanAll,
                "Rescanning remembered folders…",
                message => MaintenanceStatusText = message);
        }
        finally
        {
            IsRescanningAllConsoles = false;
        }
    }

    /// <summary>Reveals EmuShelf's data folder in the desktop file manager. Nothing there is modified.</summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        var path = _maintenance?.DataDirectory;
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not open the data folder '{path}': {ex.Message}");
            DataFolderStatusText = "Couldn't open the data folder.";
        }
    }

    /// <summary>Opens the portable save-sync activity log in the OS default viewer. Read-only.</summary>
    [RelayCommand]
    private void OpenSyncLog()
    {
        if (!HasSyncLog)
            return;

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SyncLogPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not open the sync log '{SyncLogPath}': {ex.Message}");
            CloudStatusText = "Couldn't open the sync log.";
        }
    }

    [RelayCommand]
    private async Task FetchAllMetadataAsync()
    {
        if (_maintenance?.FetchAllMetadata is null || IsWorking)
            return;
        IsMaintainingLibrary = true;
        MetadataStatusText = "Fetching missing titles and covers…";
        MetadataProgressCompleted = 0;
        MetadataProgressTotal = 0;
        MetadataProgressText = string.Empty;
        try
        {
            MetadataStatusText = await _maintenance.FetchAllMetadata(
                new Progress<MetadataEnrichmentProgress>(progress =>
                {
                    MetadataProgressCompleted = progress.Completed;
                    MetadataProgressTotal = progress.Total;
                    MetadataProgressText = progress.CurrentGameTitle is null
                        ? $"Fetching {progress.Completed} of {progress.Total}"
                        : $"Fetching {progress.Completed} of {progress.Total}: {progress.CurrentGameTitle}";
                }));
        }
        catch (Exception ex)
        {
            _logger.Error("Metadata fetch failed from Settings.", ex);
            MetadataStatusText = $"Metadata fetch failed: {ex.Message}";
        }
        finally
        {
            // The final count lives in MetadataStatusText; the live progress line and its bar total
            // have served their purpose. Clear them so a later maintenance run cannot re-show the
            // stale bar (desktop) or mask its own status behind "Fetching N of N" (the Gamepad pill).
            ResetMetadataProgress();
            IsMaintainingLibrary = false;
        }
    }

    private Task RescanSystemAsync(EmulatorSettingsRowViewModel row) => RunMaintenanceAsync(
        _maintenance is null ? null : progress => _maintenance.RescanSystem(row.SystemId, progress),
        "Rescanning remembered folders…",
        message => row.MaintenanceStatusText = message);

    private Task SyncRpcs3LibraryAsync(EmulatorSettingsRowViewModel row) => RunMaintenanceAsync(
        _maintenance?.SyncRpcs3Library is null ? null : _ => _maintenance.SyncRpcs3Library!(),
        "Reading the RPCS3 game list…",
        message => row.MaintenanceStatusText = message);

    private async Task RunMaintenanceAsync(
        Func<IProgress<string>, Task<string>>? action,
        string startingMessage,
        Action<string> report)
    {
        if (action is null || IsWorking)
            return;

        IsMaintainingLibrary = true;
        // Maintenance shares IsMaintainingLibrary with the metadata fetch, and the desktop metadata
        // bar is gated on that flag AND a non-zero total. Zero the stale metadata progress a prior
        // fetch left set so this run does not transiently re-show that bar.
        ResetMetadataProgress();
        report(startingMessage);
        try
        {
            // Live per-console counts flow back through this reporter so the modal (and the Gamepad
            // pill) update as the scan walks each console, instead of sitting on the static start
            // line until the whole run finishes. The scan already marshals its counts to the UI
            // thread before reporting, so a synchronous reporter is thread-safe here and, unlike
            // Progress<T>, guarantees the final result below is the last line written.
            var progress = new SynchronousProgress<string>(report);
            report(await action(progress));
        }
        catch (Exception ex)
        {
            _logger.Error("Library maintenance failed from Settings.", ex);
            report($"Maintenance failed: {ex.Message}");
        }
        finally
        {
            IsMaintainingLibrary = false;
        }
    }

    private void UpdateRowMaintenanceState()
    {
        foreach (var row in Rows)
            row.IsMaintenanceBlocked = IsWorking;
    }

    // A row's active installation can change when its profile changes, so recompute which rows share
    // an installation and seed a switched-to shared installation from a sibling that already has one.
    private void OnRowProfileChanged(EmulatorSettingsRowViewModel source)
    {
        RecomputeSharedInstallations();
        RefreshCloudOverrideForRow(source);

        if (!source.IsExecutableShared || !string.IsNullOrWhiteSpace(source.ExecutablePath))
            return;

        var sibling = Rows.FirstOrDefault(row =>
            row != source &&
            string.Equals(row.EmulatorInstallationId, source.EmulatorInstallationId, StringComparison.Ordinal) &&
            (!string.IsNullOrWhiteSpace(row.ExecutablePath) || row.TargetKind == "Flatpak"));
        if (sibling is null)
            return;

        source.TargetKind = sibling.TargetKind;
        source.ExecutablePath = sibling.ExecutablePath;
        source.FlatpakAppId = sibling.FlatpakAppId;
    }

    // Each emulator keeps its own save override, so switching the picker must show the newly-selected
    // emulator's stored folder rather than the one active when Settings opened. The switch is not
    // persisted until Save, so read the override for the selected emulator directly instead of through
    // the coordinator's persisted-active-emulator lookup.
    private void RefreshCloudOverrideForRow(EmulatorSettingsRowViewModel source)
    {
        if (_cloudSaves?.DescribePlatformForEmulator is not { } describe)
            return;
        if (describe(source.SystemId, source.EmulatorId) is not { } platform)
            return;
        CloudPlatforms
            .FirstOrDefault(row => string.Equals(row.SystemId, source.SystemId, StringComparison.Ordinal))
            ?.ApplyEmulatorSwitch(platform);
    }

    private void RecomputeSharedInstallations()
    {
        foreach (var row in Rows)
        {
            var sharedCount = Rows.Count(other => string.Equals(
                other.EmulatorInstallationId,
                row.EmulatorInstallationId,
                StringComparison.Ordinal));
            row.SetExecutableShared(sharedCount > 1);
        }
    }

    private void SynchronizeSharedExecutable(EmulatorSettingsRowViewModel source, string path) =>
        SynchronizeSharedInstallation(source, row => row.ExecutablePath = path);

    private void SynchronizeSharedTargetKind(EmulatorSettingsRowViewModel source, string targetKind) =>
        SynchronizeSharedInstallation(source, row => row.TargetKind = targetKind);

    private void SynchronizeSharedFlatpakAppId(EmulatorSettingsRowViewModel source, string appId) =>
        SynchronizeSharedInstallation(source, row => row.FlatpakAppId = appId);

    private void SynchronizeSharedInstallation(
        EmulatorSettingsRowViewModel source,
        Action<EmulatorSettingsRowViewModel> update)
    {
        if (_synchronizingSharedInstallation || !source.IsExecutableShared)
            return;

        _synchronizingSharedInstallation = true;
        try
        {
            foreach (var row in Rows.Where(row =>
                         row != source &&
                         string.Equals(
                             row.EmulatorInstallationId,
                             source.EmulatorInstallationId,
                             StringComparison.Ordinal)))
            {
                update(row);
            }
        }
        finally
        {
            _synchronizingSharedInstallation = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
            return;

        IsSaving = true;
        StatusText = string.Empty;
        try
        {
            // Persist every configured profile (not just the active one) so a system's alternative
            // emulator setup survives, then pin the active profile the user has selected per system.
            var configurations = Rows.SelectMany(row => row.ToConfigurations()).ToArray();
            var activeBySystem = Rows.ToDictionary(row => row.SystemId, row => row.EmulatorId, StringComparer.Ordinal);
            await Task.Run(() =>
            {
                _configurations.SaveAll(configurations);
                foreach (var (systemId, emulatorId) in activeBySystem)
                    _configurations.SetActiveEmulator(systemId, emulatorId);
            });
            if (_metadataPreferences is not null)
            {
                await _metadataPreferences.SaveAutomaticFetchAsync(
                    AutomaticallyFetchMetadataAfterImport);
                await _metadataPreferences.SaveWebImageSearchAsync(WebImageSearchEnabled);
            }
            if (_maintenance?.SetShowEmptyPlatforms is not null)
                await _maintenance.SetShowEmptyPlatforms(ShowEmptyPlatforms);
            PersistCloudSaveLocations();
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not save settings.", ex);
            StatusText = $"Could not save settings: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private async Task ConnectRetroAchievementsAsync()
    {
        if (_retroAchievements is null || IsRetroAchievementsBusy)
            return;

        var username = RetroAchievementsUsername.Trim();
        var apiKey = RetroAchievementsApiKey.Trim();
        if (username.Length == 0 || apiKey.Length == 0)
        {
            RetroAchievementsStatusText = "Enter your username and Web API key.";
            return;
        }

        IsRetroAchievementsBusy = true;
        RetroAchievementsStatusText = "Connecting…";
        RetroAchievementsProgressCompleted = 0;
        RetroAchievementsProgressTotal = 0;
        RetroAchievementsProgressText = string.Empty;
        try
        {
            var outcome = await _retroAchievements.ConnectAsync(
                username,
                apiKey,
                new Progress<RetroAchievementsLibrarySyncProgress>(ApplyRetroAchievementsProgress),
                CancellationToken.None);
            var result = outcome.Result;
            RetroAchievementsStatusText = result switch
            {
                RetroAchievementsConnectionResult.Connected =>
                    BuildConnectedStatus(outcome.Sync),
                RetroAchievementsConnectionResult.AuthenticationFailed =>
                    "That username or Web API key wasn't accepted.",
                RetroAchievementsConnectionResult.Offline =>
                    "Couldn't reach RetroAchievements. Check your connection.",
                RetroAchievementsConnectionResult.RateLimited =>
                    "RetroAchievements is busy right now. Try again shortly.",
                RetroAchievementsConnectionResult.LocalStorageFailed =>
                    "Connected, but the key couldn't be saved on this machine.",
                _ => "Couldn't connect. Try again.",
            };
            if (result == RetroAchievementsConnectionResult.Connected)
            {
                ConnectedAccountName = username;
                RetroAchievementsApiKey = string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("RetroAchievements connection failed from Settings.", ex);
            RetroAchievementsStatusText = $"Couldn't connect: {ex.Message}";
        }
        finally
        {
            // Keep the summary in RetroAchievementsStatusText; drop the live progress line so it
            // cannot mask that summary in the Gamepad pill or linger as a stale bar on desktop.
            ResetRetroAchievementsProgress();
            IsRetroAchievementsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectRetroAchievementsAsync()
    {
        if (_retroAchievements is null || IsRetroAchievementsBusy)
            return;

        IsRetroAchievementsBusy = true;
        try
        {
            await _retroAchievements.DisconnectAsync(CancellationToken.None);
            ConnectedAccountName = null;
            RetroAchievementsUsername = string.Empty;
            RetroAchievementsStatusText = "Disconnected.";
        }
        catch (Exception ex)
        {
            _logger.Error("RetroAchievements disconnect failed from Settings.", ex);
            RetroAchievementsStatusText = $"Couldn't disconnect: {ex.Message}";
        }
        finally
        {
            IsRetroAchievementsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConnectScreenScraperAsync()
    {
        if (_screenScraper is null || IsScreenScraperBusy)
            return;

        var username = ScreenScraperUsername.Trim();
        if (username.Length == 0 || string.IsNullOrEmpty(ScreenScraperPassword))
        {
            ScreenScraperStatusText = "Enter your ScreenScraper username and password.";
            return;
        }

        IsScreenScraperBusy = true;
        ScreenScraperStatusText = "Connecting…";
        try
        {
            var outcome = await _screenScraper.ConnectAsync(username, ScreenScraperPassword, CancellationToken.None);
            ScreenScraperStatusText = outcome.Result switch
            {
                ScreenScraperConnectionResult.Connected => outcome.Account?.Quota is
                    { RequestsToday: { } used, MaxRequestsPerDay: { } max }
                    ? $"Connected. {used} / {max} requests used today."
                    : "Connected.",
                ScreenScraperConnectionResult.AuthenticationFailed => "That username or password wasn't accepted.",
                ScreenScraperConnectionResult.Offline => "Couldn't reach ScreenScraper. Check your connection.",
                ScreenScraperConnectionResult.RateLimited => "ScreenScraper is busy right now. Try again shortly.",
                ScreenScraperConnectionResult.QuotaExceeded => "Your ScreenScraper quota is used up. Try again later.",
                ScreenScraperConnectionResult.ProviderUnavailable =>
                    "ScreenScraper isn't configured in this build.",
                ScreenScraperConnectionResult.LocalStorageFailed =>
                    "Connected, but the login couldn't be saved on this machine.",
                _ => "Couldn't connect. Try again.",
            };
            if (outcome.Result == ScreenScraperConnectionResult.Connected)
            {
                ScreenScraperConnectedName = username;
                ScreenScraperPassword = string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("ScreenScraper connection failed from Settings.", ex);
            ScreenScraperStatusText = $"Couldn't connect: {ex.Message}";
        }
        finally
        {
            IsScreenScraperBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectScreenScraperAsync()
    {
        if (_screenScraper is null || IsScreenScraperBusy)
            return;

        IsScreenScraperBusy = true;
        try
        {
            await _screenScraper.DisconnectAsync(CancellationToken.None);
            ScreenScraperConnectedName = null;
            ScreenScraperUsername = string.Empty;
            ScreenScraperStatusText = "Disconnected.";
        }
        catch (Exception ex)
        {
            _logger.Error("ScreenScraper disconnect failed from Settings.", ex);
            ScreenScraperStatusText = $"Couldn't disconnect: {ex.Message}";
        }
        finally
        {
            IsScreenScraperBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshRetroAchievementsMatches))]
    private async Task RefreshRetroAchievementsMatchesAsync()
    {
        if (_retroAchievements?.RefreshMatchesAsync is null || !CanRefreshRetroAchievementsMatches)
            return;

        IsRetroAchievementsBusy = true;
        RetroAchievementsStatusText = "Refreshing achievement matches…";
        RetroAchievementsProgressCompleted = 0;
        RetroAchievementsProgressTotal = 0;
        RetroAchievementsProgressText = string.Empty;
        try
        {
            var sync = await _retroAchievements.RefreshMatchesAsync(
                new Progress<RetroAchievementsLibrarySyncProgress>(ApplyRetroAchievementsProgress),
                CancellationToken.None);
            RetroAchievementsStatusText = sync is null
                ? "Reconnect to RetroAchievements to refresh game matches."
                : BuildRefreshStatus(sync);
        }
        catch (Exception ex)
        {
            _logger.Error("RetroAchievements match refresh failed from Settings.", ex);
            RetroAchievementsStatusText = "Couldn't refresh achievement matches. Try again.";
        }
        finally
        {
            ResetRetroAchievementsProgress();
            IsRetroAchievementsBusy = false;
        }
    }

    /// <summary>Emulator filter for the pack list. The first entry shows every emulator.</summary>
    public ObservableCollection<string> TextureEmulatorFilters { get; } = new() { AllFilter };

    /// <summary>Status filter for the pack list. The first entry shows every status.</summary>
    public IReadOnlyList<string> TextureStatusFilters { get; } =
        [AllFilter, "Matched", "No game in your library", "Needs attention"];

    private const string AllFilter = "All";

    [ObservableProperty]
    public partial string TextureEmulatorFilter { get; set; } = AllFilter;

    [ObservableProperty]
    public partial string TextureStatusFilter { get; set; } = AllFilter;

    [ObservableProperty]
    public partial string TexturePackSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TexturePackLastScanText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool IsTexturePackBusy { get; set; }

    [ObservableProperty]
    public partial string TexturePackStatusText { get; set; } = string.Empty;

    public bool HasNoTexturePacks => TexturePackEntries.Count == 0;

    partial void OnTextureEmulatorFilterChanged(string value) => ApplyTexturePackFilter();

    partial void OnTextureStatusFilterChanged(string value) => ApplyTexturePackFilter();

    /// <summary>Rescans every configured texture root. Read-only: nothing on disk is changed.</summary>
    [RelayCommand]
    private async Task RescanTexturePacksAsync()
    {
        if (_texturePacks is null || IsTexturePackBusy)
            return;

        IsTexturePackBusy = true;
        TexturePackStatusText = "Scanning installed texture packs…";
        try
        {
            await _texturePacks.RescanAsync(CancellationToken.None);
            ApplyTexturePackInventory();
            TexturePackStatusText = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.Error("Texture-pack rescan failed from Settings.", ex);
            TexturePackStatusText = $"Couldn't finish the scan: {ex.Message}";
        }
        finally
        {
            IsTexturePackBusy = false;
        }
    }

    /// <summary>Reveals a texture folder in the desktop file manager. It is never modified.</summary>
    [RelayCommand]
    private void OpenTextureFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not open the texture folder '{path}': {ex.Message}");
            TexturePackStatusText = "Couldn't open that folder.";
        }
    }

    /// <summary>Points one platform at an explicit texture folder instead of the detected one.</summary>
    [RelayCommand]
    private async Task BrowseTextureOverrideAsync(TexturePackRowViewModel? row)
    {
        if (_texturePacks is null || row is null)
            return;

        var directory = await _dialogs.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(directory))
            return;

        row.DirectoryOverride = directory;
        _texturePacks.UpdateOverride(row.SystemId, directory);
        await RescanTexturePacksAsync();
    }

    /// <summary>Clears an override so the platform's detected folder is used again.</summary>
    [RelayCommand]
    private async Task ClearTextureOverrideAsync(TexturePackRowViewModel? row)
    {
        if (_texturePacks is null || row is null)
            return;

        row.DirectoryOverride = string.Empty;
        _texturePacks.UpdateOverride(row.SystemId, null);
        await RescanTexturePacksAsync();
    }

    // Rebuilds the platform rows, the totals, and the filter choices from the latest pass. The
    // totals come from the same classification the library marks use, so Settings and the library
    // can never report a different number of matches.
    private void ApplyTexturePackInventory()
    {
        if (_texturePacks is null)
            return;

        var inventory = _texturePacks.GetInventory();

        TexturePlatforms.Clear();
        foreach (var platform in inventory.Platforms)
        {
            _texturePacks.OverridePlaceholders.TryGetValue(platform.SystemId, out var placeholder);
            TexturePlatforms.Add(new TexturePackRowViewModel(
                platform,
                placeholder ?? string.Empty,
                row => BrowseTextureOverrideAsync(row),
                row => ClearTextureOverrideAsync(row),
                OpenTextureFolder));
        }

        var emulators = inventory.Map.Classifications
            .Select(classification => TexturePackProviderRegistry.DescribeEmulator(classification.EmulatorId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        TextureEmulatorFilters.Clear();
        TextureEmulatorFilters.Add(AllFilter);
        foreach (var emulator in emulators)
            TextureEmulatorFilters.Add(emulator);
        if (!TextureEmulatorFilters.Contains(TextureEmulatorFilter, StringComparer.Ordinal))
            TextureEmulatorFilter = AllFilter;

        var map = inventory.Map;
        TexturePackSummary = _texturePacks.HasScanned()
            ? $"{map.MatchedCount} matched · {map.NoMatchCount} with no game in your library · {map.AttentionCount} needing attention"
            : "Not scanned yet.";
        TexturePackLastScanText = map.LastScannedAt is { } scannedAt
            ? $"Last scan {scannedAt.ToLocalTime():g}"
            : string.Empty;

        ApplyTexturePackFilter();
    }

    private void ApplyTexturePackFilter()
    {
        if (_texturePacks is null)
            return;

        var titles = _texturePacks.GetGameTitles();
        TexturePackEntries.Clear();
        foreach (var classification in _texturePacks.GetInventory().Map.Classifications)
        {
            var entry = new TexturePackEntryViewModel(classification, titles);
            if (TextureEmulatorFilter != AllFilter &&
                !string.Equals(entry.EmulatorName, TextureEmulatorFilter, StringComparison.Ordinal))
            {
                continue;
            }

            if (!MatchesStatusFilter(entry.Status))
                continue;

            TexturePackEntries.Add(entry);
        }

        OnPropertyChanged(nameof(HasNoTexturePacks));
    }

    private bool MatchesStatusFilter(TexturePackEntryStatus status) => TextureStatusFilter switch
    {
        "Matched" => status == TexturePackEntryStatus.Matched,
        "No game in your library" => status == TexturePackEntryStatus.NoLibraryMatch,
        // "Needs attention" deliberately excludes "no library match": a pack for a game you have
        // not imported is a normal state, not something the user has to act on.
        "Needs attention" => status
            is TexturePackEntryStatus.EmptyOrDumpsOnly
            or TexturePackEntryStatus.UnrecognizedLayout
            or TexturePackEntryStatus.FolderUnavailable
            or TexturePackEntryStatus.IdentifierPending,
        _ => true,
    };

    partial void OnSelectedSectionChanged(SettingsSection value)
    {
        if (value == SettingsSection.TexturePacks && _texturePacks is not null)
            ApplyTexturePackInventory();

        if (value != SettingsSection.Saves || _cloudSaves is null)
            return;

        foreach (var platform in CloudPlatforms.Where(row => row.DetectedDirectory is null))
            _ = platform.RefreshDetectedDirectoryAsync();
    }

    // Signs into Google Drive with EmuShelf's own built-in client — the only transport. Needs only a
    // cloud folder; the account itself is the connection. The browser launcher is handed to the
    // coordinator so the loopback redirect it starts receives the code — and so Android can substitute
    // a custom tab.
    //
    // Bounded by a timeout, and cancelled immediately if the browser cannot be opened, because the
    // coordinator otherwise waits on the loopback redirect indefinitely — which would leave IsCloudBusy
    // stuck (locking every cloud control) and hold the coordinator's sync gate for the whole time.
    [RelayCommand]
    private async Task ConnectCloudAsync()
    {
        if (_cloudSaves?.ConnectGoogleDriveManagedAsync is not { } connect || IsCloudBusy)
            return;

        IsCloudBusy = true;
        CloudStatusText = "Connecting… complete the Google sign-in in your browser.";
        using var flow = new CancellationTokenSource(ManagedConnectTimeout);
        try
        {
            var result = await connect(
                CloudFolder.Trim(),
                CollectOverrides(),
                uri => LaunchSignIn(uri, flow),
                flow.Token);
            CloudStatusText = result switch
            {
                CloudSaveSyncConnectResult.Connected => "Connected to Google Drive. Use Sync now to reconcile enabled saves.",
                CloudSaveSyncConnectResult.InvalidInput => "Enter a cloud folder, then configure at least one save platform.",
                CloudSaveSyncConnectResult.ManagedTransportUnavailable =>
                    "This build can't sign in to Google Drive, so cloud sync isn't available in it.",
                CloudSaveSyncConnectResult.SignInDeclined =>
                    "The Google sign-in didn't finish. Try again and complete the consent in your browser.",
                _ => "Couldn't connect to Google Drive.",
            };
            if (result == CloudSaveSyncConnectResult.Connected)
            {
                IsCloudConnected = true;
                CloudConnectionSummary = "Google Drive — signed in directly.";
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out, or cancelled because the browser could not be opened. Either way the fix is to
            // try again, not to check anything — and IsCloudBusy is released below.
            CloudStatusText =
                "The Google sign-in didn't finish (the browser may not have opened). Try again.";
        }
        catch (Exception ex)
        {
            _logger.Error("Google Drive connect failed from Settings.", ex);
            CloudStatusText = $"Couldn't connect: {ex.Message}";
        }
        finally
        {
            IsCloudBusy = false;
        }
    }

    // Opens the consent page and, if that fails, cancels the connect so the coordinator stops waiting
    // on a redirect that can never arrive rather than hanging until the timeout.
    private void LaunchSignIn(Uri uri, CancellationTokenSource flow)
    {
        try
        {
            _openSignInUri(uri);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not open the Google sign-in page automatically: {ex.Message}");
            flow.Cancel();
        }
    }

    // Opens a URI in the system browser. The default launcher for the built-in sign-in; injectable so
    // tests exercise the failure path without launching a real browser.
    private static void DefaultOpenSignInUri(Uri uri)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            // AbsoluteUri keeps the OAuth query's percent-encoding intact; ToString() can unescape it.
            FileName = uri.AbsoluteUri,
            UseShellExecute = true,
        });
    }

    // A one-line description of a live connection for the connected panel. Empty for a connection that
    // is not actually established (including a stale rclone-era connection, which is treated as not
    // connected). Kept in lockstep with the IsCloudConnected seeding above (and
    // CloudSaveSyncCoordinator.IsConfigured): every state those treat as connected produces a non-empty
    // summary, and no other state does.
    private static string DescribeConnection(CloudSaveSyncSettings saves) => saves switch
    {
        { Enabled: false } => string.Empty,
        { TransportKind: CloudTransportKind.GoogleDrive, CloudFolder.Length: > 0 } =>
            "Google Drive — signed in directly.",
        _ => string.Empty,
    };

    [RelayCommand]
    private async Task DisconnectCloudAsync()
    {
        if (_cloudSaves is null || IsCloudBusy)
            return;

        IsCloudBusy = true;
        try
        {
            await _cloudSaves.DisconnectAsync(CancellationToken.None);
            IsCloudConnected = false;
            CloudConnectionSummary = string.Empty;
            CloudStatusText = "Disconnected. Your cloud saves were left untouched.";
        }
        catch (Exception ex)
        {
            _logger.Error("Cloud save disconnect failed from Settings.", ex);
            CloudStatusText = $"Couldn't disconnect: {ex.Message}";
        }
        finally
        {
            IsCloudBusy = false;
        }
    }

    [RelayCommand]
    private Task SyncCloudNowAsync() =>
        RunCloudOperationAsync((progress, token) => _cloudSaves!.SyncNowAsync(progress, token), "Syncing saves…");

    private Task ForceCloudAsync(string? systemId, SaveSyncDirection direction)
    {
        if (string.IsNullOrWhiteSpace(systemId))
            return Task.CompletedTask;

        var platformName = CloudPlatforms
            .FirstOrDefault(row => string.Equals(row.SystemId, systemId, StringComparison.Ordinal))
            ?.DisplayName ?? systemId;
        var startingMessage = direction == SaveSyncDirection.Upload
            ? $"Uploading {platformName} saves…"
            : $"Downloading {platformName} saves…";
        return RunCloudOperationAsync(
            (progress, token) => _cloudSaves!.ForceAsync(systemId, direction, progress, token),
            startingMessage);
    }

    /// <summary>
    /// Stops the running sync. The transfer commits in batches, so the batches already on the
    /// remote stay there and the next pass resumes from them rather than starting over.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelCloudSync))]
    private void CancelCloudSync()
    {
        if (_cloudCancellation is not { } cancellation)
            return;

        CloudStatusText = "Stopping the sync…";
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation finished between the button press and this call.
        }
    }

    private bool CanCancelCloudSync() => IsCloudBusy && _cloudCancellation is not null;

    private async Task RunCloudOperationAsync(
        Func<IProgress<SaveSyncProgress>, CancellationToken, Task<CloudSaveSyncOutcome>> operation,
        string startingMessage)
    {
        if (_cloudSaves is null || IsCloudBusy)
            return;

        using var cancellation = new CancellationTokenSource();
        _cloudCancellation = cancellation;
        IsCloudBusy = true;
        CancelCloudSyncCommand.NotifyCanExecuteChanged();
        CloudStatusText = startingMessage;
        CloudSyncProgressCompleted = 0;
        CloudSyncProgressTotal = 0;
        CloudSyncProgressText = string.Empty;
        var progress = new Progress<SaveSyncProgress>(ApplyCloudProgress);
        try
        {
            PersistCloudSaveLocations();
            var outcome = await operation(progress, cancellation.Token);
            CloudStatusText = outcome.Status switch
            {
                CloudSaveSyncStatus.Completed => DescribeCloudReport(outcome.Report!),
                CloudSaveSyncStatus.NotConfigured => "Connect Google Drive and configure at least one save platform first.",
                CloudSaveSyncStatus.AlreadyRunning => "Another cloud sync is already running.",
                _ => outcome.Message is null ? "Sync failed." : $"Sync failed: {outcome.Message}",
            };
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Stopping is a normal outcome, not a failure: whatever committed before the stop is
            // on the remote, and saying so is what stops the user re-running it from scratch.
            CloudStatusText = "Sync stopped. Saves already transferred are in the cloud; " +
                "the next sync continues from there.";
        }
        catch (Exception ex)
        {
            _logger.Error("Cloud save sync failed from Settings.", ex);
            CloudStatusText = $"Sync failed: {ex.Message}";
        }
        finally
        {
            _cloudCancellation = null;
            IsCloudBusy = false;
            // The outcome is in CloudStatusText now; clear the transfer progress line so the Gamepad
            // Saves pill shows that outcome instead of a stale "Transferring… 100%".
            ResetCloudProgress();
            CancelCloudSyncCommand.NotifyCanExecuteChanged();
            // A successful sync creates the log after SyncLogPath was first assigned, so notify
            // the view that the previously hidden activity-log link is now available.
            OnPropertyChanged(nameof(HasSyncLog));
            RefreshPlatformResults();
        }
    }

    /// <summary>Exports the saves present on this machine into a portable <c>.zip</c>.</summary>
    [RelayCommand(CanExecute = nameof(CanExportDeviceSaves))]
    private Task ExportDeviceSavesAsync() => RunExportAsync(SaveExportScope.Device);

    /// <summary>
    /// Exports this machine's saves plus any that live only in the connected cloud remote. Gated on a
    /// live connection, so a build that cannot reach the cloud only offers the device export.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExportDeviceAndCloudSaves))]
    private Task ExportDeviceAndCloudSavesAsync() => RunExportAsync(SaveExportScope.DeviceAndCloud);

    private bool CanExportDeviceSaves() => !IsCloudBusy && _cloudSaves?.ExportSavesAsync is not null;

    private bool CanExportDeviceAndCloudSaves() =>
        !IsCloudBusy && IsCloudConnected && _cloudSaves?.ExportSavesAsync is not null;

    private async Task RunExportAsync(SaveExportScope scope)
    {
        if (_cloudSaves?.ExportSavesAsync is not { } export || IsCloudBusy)
            return;

        var suggestedName = $"EmuShelf-saves-{DateTime.Now:yyyyMMdd-HHmm}.zip";
        var destination = await _dialogs.PickSaveArchiveAsync(suggestedName);
        if (string.IsNullOrWhiteSpace(destination))
            return; // The user cancelled the save dialog; leave the last status alone.

        using var cancellation = new CancellationTokenSource();
        _cloudCancellation = cancellation;
        IsCloudBusy = true;
        CancelCloudSyncCommand.NotifyCanExecuteChanged();
        CloudStatusText = scope == SaveExportScope.DeviceAndCloud
            ? "Exporting device and cloud saves…"
            : "Exporting saves on this device…";
        CloudSyncProgressText = string.Empty;
        var progress = new Progress<SaveTransferProgress>(reported =>
            CloudSyncProgressText = $"Gathered {reported.CompletedUnits} save(s)…");
        try
        {
            var result = await export(destination, scope, progress, cancellation.Token);
            CloudStatusText = DescribeExportResult(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            CloudStatusText = "Export stopped. No archive was written.";
        }
        catch (Exception ex)
        {
            _logger.Error("Save export failed from Settings.", ex);
            CloudStatusText = $"Export failed: {ex.Message}";
        }
        finally
        {
            _cloudCancellation = null;
            IsCloudBusy = false;
            ResetCloudProgress();
            CancelCloudSyncCommand.NotifyCanExecuteChanged();
        }
    }

    private static string DescribeExportResult(SaveExportResult result) => result.Status switch
    {
        SaveExportStatus.Completed => DescribeCompletedExport(result),
        SaveExportStatus.NothingToExport => "No saves were found to export.",
        SaveExportStatus.NotConfigured => "Connect Google Drive first to include cloud saves.",
        _ => result.Message is null ? "Export failed." : $"Export failed: {result.Message}",
    };

    private static string DescribeCompletedExport(SaveExportResult result)
    {
        var cloudPart = result.FromCloud > 0 ? $" ({result.FromCloud} from the cloud)" : string.Empty;
        var message = $"Exported {result.SavesExported} save(s){cloudPart} to {result.DestinationPath}.";
        if (result.Skipped.Count > 0)
            message += $" {result.Skipped.Count} cloud item(s) could not be included.";
        return message;
    }

    // Each row shows its own last result, which the coordinator has just rewritten. Re-read it so
    // a row that previously said "last attempt failed" does not keep saying so after a success.
    private void RefreshPlatformResults()
    {
        if (_cloudSaves is null)
            return;

        foreach (var platform in _cloudSaves.GetPlatforms())
        {
            CloudPlatforms
                .FirstOrDefault(row => string.Equals(row.SystemId, platform.SystemId, StringComparison.Ordinal))
                ?.ApplyResult(platform);
        }
    }

    private void ApplyCloudProgress(SaveSyncProgress progress)
    {
        // Comparing units and transferring them are different measures. The unit counter reaches
        // its total before the upload starts — everything until then is staged locally — so the
        // transfer reports its own percentage, and an indeterminate bar until the provider has
        // moved enough bytes to report one.
        if (progress.Phase == SaveSyncPhase.Transferring)
        {
            IsCloudTransferIndeterminate = progress.TransferPercent is null;
            CloudSyncProgressTotal = 100;
            CloudSyncProgressCompleted = progress.TransferPercent ?? 0;
            // The count is named as well as the percentage. The percentage alone reads as stalled
            // whenever the remaining saves are small ones, because they take provider round trips
            // rather than bandwidth; "142 of 180 saves" keeps moving when the bar does not.
            CloudSyncProgressText = progress.TransferPercent is { } percent
                ? $"Transferring saves to the cloud — {percent}% ({progress.Completed} of {progress.Total} saves)"
                : $"Transferring saves to the cloud — {progress.Total} save(s)…";
            return;
        }

        IsCloudTransferIndeterminate = false;
        CloudSyncProgressCompleted = progress.Completed;
        CloudSyncProgressTotal = progress.Total;
        var position = Math.Min(progress.Completed + 1, progress.Total);
        CloudSyncProgressText = $"{DescribeAction(progress.Action)} {position} of {progress.Total}: {progress.CurrentUnit}";
    }

    // Progress fields are write-once-per-operation: they are seeded at the start of the run that
    // owns them, but the completion summary lives in the matching *StatusText. Clearing the progress
    // line and its bar total on completion keeps both surfaces honest — the desktop bar hides and the
    // single Gamepad status pill (which ranks progress text ahead of status text) shows the summary
    // rather than a frozen "N of N" from the finished run.
    private void ResetMetadataProgress()
    {
        MetadataProgressText = string.Empty;
        MetadataProgressCompleted = 0;
        MetadataProgressTotal = 0;
    }

    private void ResetRetroAchievementsProgress()
    {
        RetroAchievementsProgressText = string.Empty;
        RetroAchievementsProgressCompleted = 0;
        RetroAchievementsProgressTotal = 0;
    }

    private void ResetCloudProgress()
    {
        CloudSyncProgressText = string.Empty;
        CloudSyncProgressCompleted = 0;
        CloudSyncProgressTotal = 0;
        IsCloudTransferIndeterminate = false;
    }

    // Reports on the caller's thread rather than posting to the synchronization context the way
    // Progress<T> does. Maintenance progress is already produced on the UI thread, so inline delivery
    // is safe and keeps the run's final result — reported immediately after the action returns — from
    // being overwritten by a progress update that Progress<T> would have queued behind it.
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private void PersistCloudSaveLocations()
    {
        if (_cloudSaves is null)
            return;

        // Typed paths and folder-picker paths follow the same persistence rule. A configured
        // emulator still supplies the default when a platform's box is left empty.
        if (_cloudSaves.UpdateOverrides is { } updateOverrides)
            updateOverrides(CollectOverrides());
        else
            foreach (var platform in CloudPlatforms)
                _cloudSaves.UpdateOverride(platform.SystemId, platform.NormalizedOverride);

        // Save-state folders persist the same way as save folders, so a typed state path is not
        // lost when the picker was not used.
        if (_cloudSaves.UpdateStateOverride is { } updateStateOverride)
            foreach (var platform in CloudPlatforms)
                updateStateOverride(platform.SystemId, platform.NormalizedStateOverride);
    }

    /// <summary>The per-platform overrides as typed, keyed by system id for the connect call.</summary>
    private IReadOnlyDictionary<string, string?> CollectOverrides() =>
        CloudPlatforms.ToDictionary(
            platform => platform.SystemId,
            platform => platform.NormalizedOverride,
            StringComparer.Ordinal);

    private static string DescribeAction(SaveSyncAction action) => action switch
    {
        SaveSyncAction.Upload => "Uploading",
        SaveSyncAction.Download => "Downloading",
        SaveSyncAction.ConflictLocalWins or SaveSyncAction.ConflictRemoteWins => "Resolving",
        _ => "Checking",
    };

    // The rows render their own enabled/visible state, so mirror the shared cloud state onto them
    // rather than binding each row back up to the parent view model through the item template.
    partial void OnIsCloudBusyChanged(bool value)
    {
        foreach (var platform in CloudPlatforms)
            platform.IsCloudBusy = value;
    }

    partial void OnIsCloudConnectedChanged(bool value)
    {
        foreach (var platform in CloudPlatforms)
            platform.IsCloudConnected = value;
    }

    private static string DescribeCloudReport(SaveSyncReport report)
    {
        var parts = new List<string>();
        if (report.Uploaded > 0)
            parts.Add($"{report.Uploaded} uploaded");
        if (report.Downloaded > 0)
            parts.Add($"{report.Downloaded} downloaded");
        if (report.Conflicts > 0)
            parts.Add($"{report.Conflicts} conflict{(report.Conflicts == 1 ? "" : "s")} resolved (older copy backed up)");
        if (report.Unchanged > 0)
            parts.Add($"{report.Unchanged} already in sync");
        // Saves that were deliberately left behind (card-type or state-version mismatch, etc.) are a
        // real outcome, not "nothing found" — say so, or an all-skipped pass reads as a clean success
        // while the per-platform rows say the opposite.
        if (report.Skipped.Count > 0)
            parts.Add($"{report.Skipped.Count} skipped");
        return parts.Count == 0
            ? "No enabled saves were found to sync."
            : "Sync complete: " + string.Join(", ", parts) + ".";
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    internal void ApplyRetroAchievementsProgress(RetroAchievementsLibrarySyncProgress progress)
    {
        RetroAchievementsProgressCompleted = progress.Completed;
        RetroAchievementsProgressTotal = progress.Total;
        RetroAchievementsProgressText = progress.Phase switch
        {
            RetroAchievementsLibrarySyncPhase.Identifying when progress.CurrentGameTitle is not null =>
                $"Identifying {Math.Min(progress.Completed + 1, progress.Total)} of {progress.Total}: " +
                progress.CurrentGameTitle,
            RetroAchievementsLibrarySyncPhase.Identifying =>
                $"Identifying {progress.Completed} of {progress.Total}",
            RetroAchievementsLibrarySyncPhase.Matching when progress.CurrentGameTitle is not null =>
                $"Matching {Math.Min(progress.Completed + 1, progress.Total)} of {progress.Total}: " +
                progress.CurrentGameTitle,
            RetroAchievementsLibrarySyncPhase.Matching =>
                $"Matching {progress.Completed} of {progress.Total}",
            RetroAchievementsLibrarySyncPhase.RefreshingProgress =>
                $"Refreshing progress for {progress.Completed} of {progress.Total} matched games",
            _ => string.Empty,
        };
    }

    private static string BuildConnectedStatus(RetroAchievementsLibrarySyncSummary? sync)
    {
        if (sync is null)
            return "Connected. RetroAchievements will check games as they are added.";

        var identification = sync.Identification;
        var identificationParts = new List<string>();
        if (identification.Hashed > 0)
        {
            var noun = identification.Hashed == 1 ? "hash" : "hashes";
            identificationParts.Add($"{identification.Hashed} {noun} calculated this sync");
        }
        if (identification.Reused > 0)
        {
            var noun = identification.Reused == 1 ? "result" : "results";
            identificationParts.Add($"{identification.Reused} prior {noun} reused");
        }
        var identificationText = identificationParts.Count == 0
            ? "No games could be identified"
            : string.Join(", ", identificationParts);
        if (identification.Unsupported > 0)
            identificationText += $", {identification.Unsupported} unsupported";
        if (identification.Failed > 0)
            identificationText += $", {identification.Failed} unreadable or invalid";

        var matchingText = sync.Matching is null
            ? "matching unavailable"
            : $"{sync.Matching.Matched} matched, {sync.Matching.NoAchievements} without achievements, " +
              $"{sync.Matching.Unresolved} unresolved";
        var progressText = sync.Progress is null
            ? "progress refresh unavailable"
            : sync.Progress.Status == RetroAchievementsRequestStatus.Success
                ? $"{sync.Progress.UpdatedGames} progress summaries refreshed"
                : $"progress refresh {sync.Progress.Status.ToString().ToLowerInvariant()}";
        return $"Connected. {identificationText}; {matchingText}; {progressText}.";
    }

    private static string BuildRefreshStatus(RetroAchievementsLibrarySyncSummary sync)
    {
        var identification = sync.Identification;
        var identificationText = identification.Hashed > 0
            ? $"{identification.Hashed} new {Pluralize(identification.Hashed, "hash", "hashes")} calculated"
            : identification.Reused > 0
                ? $"{identification.Reused} cached {Pluralize(identification.Reused, "result", "results")} reused"
                : "no games required identification";
        var matchingText = sync.Matching is null
            ? "matching unavailable"
            : $"{sync.Matching.Matched} matched, {sync.Matching.NoAchievements} without achievements, " +
              $"{sync.Matching.Unresolved} unresolved";
        var progressText = sync.Progress is { Status: RetroAchievementsRequestStatus.Success } progress
            ? $"{progress.UpdatedGames} progress summaries refreshed"
            : sync.Progress is null
                ? "progress refresh unavailable"
                : $"progress refresh {sync.Progress.Status.ToString().ToLowerInvariant()}";
        return $"Achievement matches refreshed. {identificationText}; {matchingText}; {progressText}.";
    }

    private static string Pluralize(int count, string singular, string plural) =>
        count == 1 ? singular : plural;
}
