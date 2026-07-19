using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.ViewModels;

public enum SettingsSection
{
    General,
    Emulators,
    RetroAchievements,
}

public partial class EmulatorSettingsViewModel : ViewModelBase
{
    private readonly IEmulatorConfigurationStore _configurations;
    private readonly LibraryMaintenanceActions? _maintenance;
    private readonly IMetadataPreferencesService? _metadataPreferences;
    private readonly RetroAchievementsSettingsContext? _retroAchievements;
    private readonly IAppLogger _logger;
    private bool _synchronizingSharedExecutable;

    public ObservableCollection<EmulatorSettingsRowViewModel> Rows { get; }
    public IReadOnlyList<SettingsSection> Sections { get; }
    public bool HasRetroAchievements => _retroAchievements is not null;
    public event Action<bool>? CloseRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralSection))]
    [NotifyPropertyChangedFor(nameof(IsEmulatorsSection))]
    [NotifyPropertyChangedFor(nameof(IsRetroAchievementsSection))]
    public partial SettingsSection SelectedSection { get; set; } = SettingsSection.General;

    public bool IsGeneralSection => SelectedSection == SettingsSection.General;
    public bool IsEmulatorsSection => SelectedSection == SettingsSection.Emulators;
    public bool IsRetroAchievementsSection => SelectedSection == SettingsSection.RetroAchievements;

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

    public bool CanRescanAll => !IsWorking && _maintenance is not null;
    public bool CanFetchAllMetadata =>
        !IsWorking && _maintenance?.FetchAllMetadata is not null;
    public bool IsWorking => IsSaving || IsMaintainingLibrary;
    public bool HasMaintenanceStatus => !string.IsNullOrWhiteSpace(MaintenanceStatusText);
    public bool HasMetadataStatus => !string.IsNullOrWhiteSpace(MetadataStatusText);

    [ObservableProperty]
    public partial bool AutomaticallyFetchMetadataAfterImport { get; set; }

    public EmulatorSettingsViewModel(
        IReadOnlyList<GameSystem> systems,
        IReadOnlyList<EmulatorDefinition> emulators,
        IReadOnlyDictionary<string, EmulatorConfiguration?> configured,
        IEmulatorConfigurationStore configurations,
        IDialogService dialogs,
        LibraryMaintenanceActions? maintenance = null,
        IMetadataPreferencesService? metadataPreferences = null,
        IAppLogger? logger = null,
        RetroAchievementsSettingsContext? retroAchievements = null)
    {
        _configurations = configurations;
        _maintenance = maintenance;
        _metadataPreferences = metadataPreferences;
        _retroAchievements = retroAchievements;
        _logger = logger ?? NullAppLogger.Instance;

        Sections = retroAchievements is null
            ? [SettingsSection.General, SettingsSection.Emulators]
            : [SettingsSection.General, SettingsSection.Emulators, SettingsSection.RetroAchievements];
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
        var rows = systems.Select((system, index) =>
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
                maintenance?.FetchMetadataForSystem is null ? null : FetchSystemMetadataAsync,
                system.Id == "playstation3" && maintenance?.SyncRpcs3Library is not null
                    ? SyncRpcs3LibraryAsync
                    : null,
                isExpanded: index == 0,
                emulatorInstallationId: installationId,
                isExecutableShared: isShared,
                logger: _logger);
        }).ToArray();
        Rows = new ObservableCollection<EmulatorSettingsRowViewModel>(rows);
        foreach (var row in Rows)
            row.ExecutablePathEdited += SynchronizeSharedExecutable;
        AutomaticallyFetchMetadataAfterImport =
            metadataPreferences?.AutomaticallyFetchAfterImport ?? false;
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
    private Task FetchAllMetadataAsync() => RunMaintenanceAsync(
        _maintenance?.FetchAllMetadata,
        "Fetching missing titles and covers…",
        message => MetadataStatusText = message);

    private Task RescanSystemAsync(EmulatorSettingsRowViewModel row) => RunMaintenanceAsync(
        _maintenance is null ? null : () => _maintenance.RescanSystem(row.SystemId),
        "Rescanning remembered folders…",
        message => row.MaintenanceStatusText = message);

    private Task FetchSystemMetadataAsync(EmulatorSettingsRowViewModel row) => RunMaintenanceAsync(
        _maintenance?.FetchMetadataForSystem is null
            ? null
            : () => _maintenance.FetchMetadataForSystem(row.SystemId),
        "Fetching missing titles and covers…",
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

    private void SynchronizeSharedExecutable(EmulatorSettingsRowViewModel source, string path)
    {
        if (_synchronizingSharedExecutable || !source.IsExecutableShared)
            return;

        _synchronizingSharedExecutable = true;
        try
        {
            foreach (var row in Rows.Where(row =>
                         row != source &&
                         string.Equals(
                             row.EmulatorInstallationId,
                             source.EmulatorInstallationId,
                             StringComparison.Ordinal)))
            {
                row.ExecutablePath = path;
            }
        }
        finally
        {
            _synchronizingSharedExecutable = false;
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
