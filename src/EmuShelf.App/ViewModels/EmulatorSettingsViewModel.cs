using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Systems;
using EmuShelf.Core.TexturePacks;

namespace EmuShelf.App.ViewModels;

public enum SettingsSection
{
    General,
    Emulators,
    RetroAchievements,
    Saves,
    TexturePacks,
    Themes,
}

public partial class EmulatorSettingsViewModel : ViewModelBase
{
    private readonly IEmulatorConfigurationStore _configurations;
    private readonly IDialogService _dialogs;
    private readonly LibraryMaintenanceActions? _maintenance;
    private readonly IMetadataPreferencesService? _metadataPreferences;
    private readonly RetroAchievementsSettingsContext? _retroAchievements;
    private readonly CloudSaveSyncSettingsContext? _cloudSaves;
    private readonly TexturePackSettingsContext? _texturePacks;
    private readonly IAppLogger _logger;
    // Held only for the duration of one cloud operation so the Stop button can reach it.
    private CancellationTokenSource? _cloudCancellation;
    private bool _synchronizingSharedInstallation;

    public ObservableCollection<EmulatorSettingsRowViewModel> Rows { get; }
    public IReadOnlyList<SettingsSection> Sections { get; }
    public bool HasRetroAchievements => _retroAchievements is not null;
    public bool HasCloudSaves => _cloudSaves is not null;
    public bool HasTexturePacks => _texturePacks is not null;
    public event Action<bool>? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralSection))]
    [NotifyPropertyChangedFor(nameof(IsEmulatorsSection))]
    [NotifyPropertyChangedFor(nameof(IsRetroAchievementsSection))]
    [NotifyPropertyChangedFor(nameof(IsSavesSection))]
    [NotifyPropertyChangedFor(nameof(IsTexturePacksSection))]
    [NotifyPropertyChangedFor(nameof(IsThemesSection))]
    public partial SettingsSection SelectedSection { get; set; } = SettingsSection.General;

    public bool IsGeneralSection => SelectedSection == SettingsSection.General;
    public bool IsEmulatorsSection => SelectedSection == SettingsSection.Emulators;
    public bool IsRetroAchievementsSection => SelectedSection == SettingsSection.RetroAchievements;
    public bool IsSavesSection => SelectedSection == SettingsSection.Saves;
    public bool IsTexturePacksSection => SelectedSection == SettingsSection.TexturePacks;
    public bool IsThemesSection => SelectedSection == SettingsSection.Themes;

    /// <summary>Appearance choices shown as a Themes section so Desktop and Gamepad settings both
    /// expose theme selection; empty when the host did not provide them.</summary>
    public IReadOnlyList<ThemeChoiceViewModel> ThemeChoices { get; }

    public bool HasThemes => ThemeChoices.Count > 0;

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
    public bool HasMaintenanceStatus => !string.IsNullOrWhiteSpace(MaintenanceStatusText);
    public bool HasMetadataStatus => !string.IsNullOrWhiteSpace(MetadataStatusText);
    public bool HasMetadataProgress => IsMaintainingLibrary && MetadataProgressTotal > 0;

    [ObservableProperty]
    public partial bool AutomaticallyFetchMetadataAfterImport { get; set; }

    [ObservableProperty]
    public partial bool ShowEmptyPlatforms { get; set; }

    [ObservableProperty]
    public partial string CloudRemoteName { get; set; } = "emushelf-gdrive";

    [ObservableProperty]
    public partial string CloudFolder { get; set; } = "EmuShelf/Saves";

    /// <summary>
    /// The user's own Google OAuth client id, or empty to use rclone's shared client. Its own
    /// client avoids the shared one's rate limiting — the multi-second wait before a launch — and
    /// the shared client's retirement during 2026.
    /// </summary>
    [ObservableProperty]
    public partial string CloudClientId { get; set; } = string.Empty;

    /// <summary>
    /// The matching client secret, read from the imported JSON. Held only long enough to hand to
    /// rclone, which stores it in its own config next to the OAuth token; it is never written to
    /// EmuShelf's settings, never shown, and dropped as soon as the connection is made.
    /// </summary>
    private string? _cloudClientSecret;

    /// <summary>What was imported, for the row under the button. Never the secret itself.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudClientStatus))]
    public partial string CloudClientStatusText { get; set; } = string.Empty;

    public bool HasCloudClientStatus => !string.IsNullOrWhiteSpace(CloudClientStatusText);

    /// <summary>One row per registered save platform, rendered by a single view template.</summary>
    public ObservableCollection<CloudSavePlatformRowViewModel> CloudPlatforms { get; } = new();
    public ObservableCollection<TexturePackRowViewModel> TexturePlatforms { get; } = new();

    /// <summary>The packs matching the current emulator and status filters.</summary>
    public ObservableCollection<TexturePackEntryViewModel> TexturePackEntries { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCloudDisconnected))]
    public partial bool IsCloudConnected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudStatus))]
    public partial string CloudStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCloudSyncProgress))]
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

    [ObservableProperty]
    public partial bool IsRcloneMissing { get; set; }

    [ObservableProperty]
    public partial string RcloneExpectedPath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSyncLog))]
    [NotifyPropertyChangedFor(nameof(SyncLogUri))]
    public partial string SyncLogPath { get; set; } = string.Empty;

    /// <summary>True once at least one sync has been recorded in the activity log.</summary>
    public bool HasSyncLog => !string.IsNullOrWhiteSpace(SyncLogPath) && File.Exists(SyncLogPath);

    /// <summary>The activity log as a file URI so the view can offer to open it.</summary>
    public Uri? SyncLogUri =>
        string.IsNullOrWhiteSpace(SyncLogPath) ? null : new Uri(SyncLogPath);

    [ObservableProperty]
    public partial bool IsDownloadingRclone { get; set; }

    public bool IsCloudDisconnected => !IsCloudConnected;
    public bool HasCloudStatus => !string.IsNullOrWhiteSpace(CloudStatusText);
    public bool HasCloudSyncProgress => IsCloudBusy && CloudSyncProgressTotal > 0;

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
        IReadOnlyList<ThemeChoiceViewModel>? themeChoices = null)
    {
        _configurations = configurations;
        _dialogs = dialogs;
        _maintenance = maintenance;
        _metadataPreferences = metadataPreferences;
        _retroAchievements = retroAchievements;
        _cloudSaves = cloudSaves;
        _texturePacks = texturePacks;
        _logger = logger ?? NullAppLogger.Instance;
        ThemeChoices = themeChoices ?? [];

        var sections = new List<SettingsSection> { SettingsSection.General, SettingsSection.Emulators };
        if (retroAchievements is not null)
            sections.Add(SettingsSection.RetroAchievements);
        if (cloudSaves is not null)
            sections.Add(SettingsSection.Saves);
        if (texturePacks is not null)
            sections.Add(SettingsSection.TexturePacks);
        if (HasThemes)
            sections.Add(SettingsSection.Themes);
        Sections = sections;
        if (texturePacks is not null)
            ApplyTexturePackInventory();
        if (cloudSaves is not null)
        {
            var saves = cloudSaves.Current;
            CloudRemoteName = string.IsNullOrWhiteSpace(saves.RemoteName) ? "emushelf-gdrive" : saves.RemoteName!;
            CloudFolder = string.IsNullOrWhiteSpace(saves.CloudFolder) ? "EmuShelf/Saves" : saves.CloudFolder!;
            CloudClientId = saves.GoogleClientId ?? string.Empty;
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
            IsCloudConnected = saves is { Enabled: true, RemoteName.Length: > 0 };
            IsRcloneMissing = !cloudSaves.IsRcloneAvailable;
            RcloneExpectedPath = cloudSaves.RcloneExpectedPath;
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
        var rows = systems.Select(system =>
        {
            var emulator = emulators.First(candidate => candidate.Supports(system.Id));
            configured.TryGetValue(system.Id, out var configuration);
            var installationId = configuration?.EmulatorInstallationId
                ?? emulator.GetDefaultInstallationId(system.Id);
            var isShared = systems.Count(otherSystem =>
            {
                var otherEmulator = emulators.First(candidate => candidate.Supports(otherSystem.Id));
                configured.TryGetValue(otherSystem.Id, out var otherConfiguration);
                return string.Equals(
                    otherConfiguration?.EmulatorInstallationId
                        ?? otherEmulator.GetDefaultInstallationId(otherSystem.Id),
                    installationId,
                    StringComparison.Ordinal);
            }) > 1;
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
                isExecutableShared: isShared,
                logger: _logger,
                folderActions: system.Id == "playstation3" ? null : maintenance?.Folders,
                runFolderMaintenance: (action, report) => RunMaintenanceAsync(
                    action,
                    "Updating remembered folders…",
                    report));
        }).ToArray();
        Rows = new ObservableCollection<EmulatorSettingsRowViewModel>(rows);
        foreach (var row in Rows)
        {
            row.ExecutablePathEdited += SynchronizeSharedExecutable;
            row.TargetKindEdited += SynchronizeSharedTargetKind;
            row.FlatpakAppIdEdited += SynchronizeSharedFlatpakAppId;
        }
        AutomaticallyFetchMetadataAfterImport =
            metadataPreferences?.AutomaticallyFetchAfterImport ?? false;
        ShowEmptyPlatforms = maintenance?.GetShowEmptyPlatforms?.Invoke() ?? false;
    }

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(CanRescanAll));
        OnPropertyChanged(nameof(CanFetchAllMetadata));
        UpdateRowMaintenanceState();
    }

    partial void OnIsMaintainingLibraryChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRescanAll));
        OnPropertyChanged(nameof(CanFetchAllMetadata));
        OnPropertyChanged(nameof(IsWorking));
        UpdateRowMaintenanceState();
    }

    [RelayCommand]
    private Task RescanAllAsync() => RunMaintenanceAsync(
        _maintenance?.RescanAll,
        "Rescanning remembered folders…",
        message => MaintenanceStatusText = message);

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
        finally { IsMaintainingLibrary = false; }
    }

    private Task RescanSystemAsync(EmulatorSettingsRowViewModel row) => RunMaintenanceAsync(
        _maintenance is null ? null : () => _maintenance.RescanSystem(row.SystemId),
        "Rescanning remembered folders…",
        message => row.MaintenanceStatusText = message);

    private Task SyncRpcs3LibraryAsync(EmulatorSettingsRowViewModel row) => RunMaintenanceAsync(
        _maintenance?.SyncRpcs3Library,
        "Reading the RPCS3 game list…",
        message => row.MaintenanceStatusText = message);

    private async Task RunMaintenanceAsync(
        Func<Task<string>>? action,
        string startingMessage,
        Action<string> report)
    {
        if (action is null || IsWorking)
            return;

        IsMaintainingLibrary = true;
        report(startingMessage);
        try
        {
            report(await action());
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
        if (IsWorking)
            return;

        IsSaving = true;
        StatusText = string.Empty;
        try
        {
            var configurations = Rows.Select(row => row.ToConfiguration()).ToArray();
            await Task.Run(() => _configurations.SaveAll(configurations));
            if (_metadataPreferences is not null)
            {
                await _metadataPreferences.SaveAutomaticFetchAsync(
                    AutomaticallyFetchMetadataAfterImport);
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
            TexturePlatforms.Add(new TexturePackRowViewModel(platform, placeholder ?? string.Empty));
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

    /// <summary>
    /// Imports the OAuth client JSON downloaded from the Google Cloud console. Using a personal
    /// client instead of rclone's shared one is what removes the rate limiting that shows up as a
    /// slow sync before a launch — and the shared client stops working during 2026.
    /// </summary>
    [RelayCommand]
    private async Task ImportGoogleClientAsync()
    {
        try
        {
            var path = await _dialogs.PickGoogleClientJsonAsync();
            if (path is null)
                return;

            var client = await GoogleOAuthClientFile.ReadAsync(path);
            CloudClientId = client.ClientId;
            _cloudClientSecret = client.ClientSecret;
            CloudClientStatusText = client.ProjectId is null
                ? "Google client loaded. Press Connect Google Drive to sign in."
                : $"Google client loaded from project {client.ProjectId}. Press Connect Google Drive to sign in.";
        }
        catch (InvalidDataException ex)
        {
            // The message names what to download and from where; it never contains file contents.
            CloudClientId = string.Empty;
            _cloudClientSecret = null;
            CloudClientStatusText = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ConnectCloudAsync()
    {
        if (_cloudSaves is null || IsCloudBusy)
            return;

        IsCloudBusy = true;
        CloudStatusText = "Connecting… complete the Google sign-in in your browser.";
        try
        {
            var result = await _cloudSaves.ConnectGoogleDriveAsync(
                CloudRemoteName.Trim(),
                CloudFolder.Trim(),
                CollectOverrides(),
                CancellationToken.None,
                string.IsNullOrWhiteSpace(CloudClientId) ? null : CloudClientId.Trim(),
                _cloudClientSecret);
            // Whatever the outcome, the secret has been handed to rclone and has no further use
            // here; holding it any longer only widens where it can be read from.
            _cloudClientSecret = null;
            CloudStatusText = result switch
            {
                CloudSaveSyncConnectResult.Connected => "Connected. Use Sync now to reconcile enabled saves.",
                CloudSaveSyncConnectResult.InvalidInput => "Enter a remote name and cloud folder, then configure at least one save platform.",
                CloudSaveSyncConnectResult.RcloneMissing => "rclone isn't installed — put rclone.exe beside EmuShelf (use “Get rclone” above), then reconnect.",
                _ => "Couldn't connect. The Google sign-in may have been declined.",
            };
            if (result == CloudSaveSyncConnectResult.Connected)
                IsCloudConnected = true;
        }
        catch (Exception ex)
        {
            _logger.Error("Cloud save connect failed from Settings.", ex);
            CloudStatusText = $"Couldn't connect: {ex.Message}";
        }
        finally
        {
            IsCloudBusy = false;
        }
    }

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
    private async Task DownloadRcloneAsync()
    {
        if (_cloudSaves is null || IsDownloadingRclone)
            return;

        IsDownloadingRclone = true;
        CloudStatusText = "Downloading rclone…";
        try
        {
            if (await _cloudSaves.DownloadRcloneAsync(CancellationToken.None))
            {
                IsRcloneMissing = false;
                CloudStatusText = "rclone installed. You can connect Google Drive now.";
            }
            else
            {
                CloudStatusText = "Couldn't download rclone. Check your connection, or add it manually.";
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Cloud save rclone download failed from Settings.", ex);
            CloudStatusText = $"Couldn't download rclone: {ex.Message}";
        }
        finally
        {
            IsDownloadingRclone = false;
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
            CancelCloudSyncCommand.NotifyCanExecuteChanged();
            // A successful sync creates the log after SyncLogPath was first assigned, so notify
            // the view that the previously hidden activity-log link is now available.
            OnPropertyChanged(nameof(HasSyncLog));
            RefreshPlatformResults();
        }
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

    private void PersistCloudSaveLocations()
    {
        if (_cloudSaves is null)
            return;

        // Typed paths and folder-picker paths follow the same persistence rule. A configured
        // emulator still supplies the default when a platform's box is left empty.
        if (_cloudSaves.UpdateOverrides is { } updateOverrides)
        {
            updateOverrides(CollectOverrides());
            return;
        }

        foreach (var platform in CloudPlatforms)
            _cloudSaves.UpdateOverride(platform.SystemId, platform.NormalizedOverride);
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
