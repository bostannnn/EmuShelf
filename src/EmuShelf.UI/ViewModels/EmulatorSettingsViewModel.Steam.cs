using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.Achievements;

namespace EmuShelf.App.ViewModels;

public partial class EmulatorSettingsViewModel
{
    [ObservableProperty]
    public partial bool IsSteamAchievementsExpanded { get; set; }
    [ObservableProperty]
    public partial bool IsRetroAchievementsExpanded { get; set; }
    partial void OnIsSteamAchievementsExpandedChanged(bool value)
    { if (value) IsRetroAchievementsExpanded = false; }
    partial void OnIsRetroAchievementsExpandedChanged(bool value)
    { if (value) IsSteamAchievementsExpanded = false; }

    public bool HasSteamAchievements => _retroAchievements?.Steam is not null;
    public bool IsSteamConnected => _retroAchievements?.Steam?.IsConnected == true;
    public string SteamAccountText => _retroAchievements?.Steam?.ProfileName ?? "Not linked";
    public string SteamKeyStorageText => _retroAchievements?.Steam?.IsPersistent == true
        ? "API key protected on this device. Game details must be visible to the Steam API."
        : "API key kept for this session only; re-enter after restarting. Game details must be visible to the Steam API.";

    [ObservableProperty]
    public partial string SteamProfileInput { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SteamApiKey { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SteamStatusText { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial bool IsSteamBusy { get; set; }

    [RelayCommand]
    private async Task ConnectSteamAsync()
    {
        if (_retroAchievements?.Steam is not { } steam || IsSteamBusy) return;
        IsSteamBusy = true; SteamStatusText = "Checking Steam profile…";
        try
        {
            var result = await Task.Run(() => steam.ConnectAsync(SteamProfileInput, SteamApiKey));
            SteamStatusText = result switch
            {
                AchievementStatus.Success => "Linked. Use Sync all Steam achievements to update your library.",
                AchievementStatus.AuthenticationFailed => "Enter a valid Steam Web API key.",
                AchievementStatus.MalformedResponse => "Enter a SteamID64 or an https://steamcommunity.com profile URL.",
                AchievementStatus.Offline => "Couldn't reach Steam. Check your connection.",
                AchievementStatus.RateLimited => "Steam is busy. Try again later.",
                AchievementStatus.Unavailable => "Steam profile could not be found.",
                _ => "Couldn't link Steam. Try again.",
            };
            if (result == AchievementStatus.Success) SteamApiKey = string.Empty;
        }
        catch (Exception) { SteamStatusText = "Couldn't save the Steam connection on this device."; }
        finally
        {
            IsSteamBusy = false;
            OnPropertyChanged(nameof(IsSteamConnected)); OnPropertyChanged(nameof(SteamAccountText));
        }
    }

    [RelayCommand]
    private async Task DisconnectSteamAsync()
    {
        if (_retroAchievements?.Steam is not { } steam || IsSteamBusy) return;
        IsSteamBusy = true;
        try
        {
            await Task.Run(steam.Disconnect);
            SteamApiKey = string.Empty; SteamProfileInput = string.Empty;
            SteamStatusText = "Disconnected. Your Steam achievements are unchanged.";
        }
        catch (Exception) { SteamStatusText = "Couldn't remove the saved Steam connection. Try again."; }
        finally
        {
            IsSteamBusy = false;
            OnPropertyChanged(nameof(IsSteamConnected)); OnPropertyChanged(nameof(SteamAccountText));
        }
    }

    public bool CanSyncSteamAchievements => IsSteamConnected && !IsSteamBusy && _retroAchievements?.GetSteamGamesAsync is not null;

    [ObservableProperty]
    public partial bool IsSteamSyncing { get; set; }

    partial void OnIsSteamBusyChanged(bool value) => SyncSteamAchievementsCommand.NotifyCanExecuteChanged();

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanSyncSteamAchievements))]
    private async Task SyncSteamAchievementsAsync(CancellationToken cancellationToken)
    {
        if (_retroAchievements?.Steam is not { } steam || _retroAchievements.GetSteamGamesAsync is not { } getGames) return;
        IsSteamBusy = true; IsSteamSyncing = true;
        SteamStatusText = "Reading Steam games…";
        try
        {
            var games = await getGames(cancellationToken);
            var progress = new Progress<SteamAchievementSyncProgress>(p =>
                SteamStatusText = $"Syncing Steam achievements: {p.Completed} / {p.Total} games · {p.Updated} updated · {p.Unavailable} unavailable");
            var result = await Task.Run(() => steam.SyncLibraryAsync(games, progress, cancellationToken), cancellationToken);
            var reason = result.Status switch
            {
                AchievementStatus.RateLimited => "Steam rate limit reached; try again later.",
                AchievementStatus.AuthenticationFailed or AchievementStatus.NotConnected => "Reconnect Steam to continue.",
                AchievementStatus.AccountChanged => "Steam account changed; sync stopped.",
                AchievementStatus.Offline => "Offline; cached achievements preserved.",
                AchievementStatus.ServerError => "Steam is unavailable; try again later.",
                _ => "Sync complete.",
            };
            SteamStatusText = result.Total == 0 ? "No Steam games added yet. Add your GameNative export folder first." :
                $"{reason} {result.Completed} / {result.Total} games checked · {result.Updated} updated · {result.Unavailable} unavailable.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { SteamStatusText = "Steam sync cancelled. Completed updates are saved."; }
        catch (Exception) { SteamStatusText = "Steam sync failed. Cached achievements are preserved."; }
        finally { IsSteamSyncing = false; IsSteamBusy = false; }
    }

    [RelayCommand]
    private void OpenSteamApiKeyPage() => _openSignInUri(new Uri("https://steamcommunity.com/dev/apikey"));
}
