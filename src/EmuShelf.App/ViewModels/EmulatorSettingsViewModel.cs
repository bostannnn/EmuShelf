using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
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
    public partial string? ConnectedAccountName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRetroAchievementsStatus))]
    public partial string RetroAchievementsStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRetroAchievementsBusy { get; set; }

    public bool IsRetroAchievementsConnected => !string.IsNullOrEmpty(ConnectedAccountName);
    public bool IsRetroAchievementsDisconnected => !IsRetroAchievementsConnected;
    public bool HasRetroAchievementsStatus => !string.IsNullOrWhiteSpace(RetroAchievementsStatusText);

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
            ConnectedAccountName = account.Username;
            RetroAchievementsUsername = account.Username;
        }
        Rows = new ObservableCollection<EmulatorSettingsRowViewModel>(systems.Select((system, index) =>
        {
            var emulator = emulators.First(candidate => candidate.Supports(system.Id));
            configured.TryGetValue(system.Id, out var configuration);
            return new EmulatorSettingsRowViewModel(
                system,
                emulator,
                configuration,
                dialogs,
                maintenance is null ? null : RescanSystemAsync,
                maintenance?.FetchMetadataForSystem is null ? null : FetchSystemMetadataAsync,
                isExpanded: index == 0,
                logger: _logger);
        }));
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
        try
        {
            var result = await _retroAchievements.ConnectAsync(username, apiKey, CancellationToken.None);
            RetroAchievementsStatusText = result switch
            {
                RetroAchievementsConnectionResult.Connected =>
                    "Connected. Your library is checking for achievements.",
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

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
