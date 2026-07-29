using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.ViewModels;

/// <summary>Presentation state for a single display-ordered RetroAchievements achievement.</summary>
public partial class AchievementRowViewModel : ObservableObject, IDisposable
{
    private readonly IRetroAchievementsBadgeCache? _badges;

    public int AchievementId { get; }
    public string Title { get; }
    public string Description { get; }
    public string PointsText { get; }
    public string EarnedText { get; }
    public string UnlockStateText { get; }
    public bool IsUnlocked { get; }
    public bool IsHardcore { get; }
    public string BadgeName { get; }

    [ObservableProperty]
    public partial Bitmap? Badge { get; set; }

    /// <summary>Gamepad-only logical row focus; independent from Avalonia keyboard focus.</summary>
    [ObservableProperty]
    public partial bool IsFocused { get; set; }

    public bool HasBadge => Badge is not null;

    public AchievementRowViewModel(
        RetroAchievementsAchievement achievement,
        IRetroAchievementsBadgeCache? badges,
        bool loadBadge = true)
    {
        _badges = badges;
        AchievementId = achievement.AchievementId;
        Title = achievement.Title;
        Description = achievement.Description;
        PointsText = achievement.Points == 1 ? "1 point" : $"{achievement.Points} points";
        IsUnlocked = achievement.IsEarned;
        IsHardcore = achievement.IsHardcore;
        BadgeName = achievement.BadgeName;
        UnlockStateText = achievement.IsHardcore
            ? "Hardcore"
            : achievement.IsEarned
                ? "Softcore"
                : "Locked";
        EarnedText = (achievement.DateEarned ?? achievement.DateEarnedHardcore) is { } earned
            ? $"Earned {earned.ToLocalTime():d MMM yyyy}"
            : "Not earned";

        if (loadBadge && _badges is not null && !string.IsNullOrWhiteSpace(BadgeName))
            _ = LoadBadgeAsync(BadgeName);
    }

    public async Task LoadBadgeAsync(string badgeName, CancellationToken cancellationToken = default)
    {
        if (_badges is null || Badge is not null || string.IsNullOrWhiteSpace(badgeName) ||
            Interlocked.CompareExchange(ref _badgeLoadStarted, 1, 0) != 0)
            return;

        try
        {
            // Badge cache lookup/download and file I/O stay on a worker. Only assigning the
            // decoded Bitmap returns to the UI context.
            var path = await Task.Run(
                () => _badges.GetBadgePathAsync(badgeName, cancellationToken),
                cancellationToken);
            if (path is null)
                return;

            var image = await Task.Run(() => new Bitmap(path), cancellationToken);
            if (!cancellationToken.IsCancellationRequested && Volatile.Read(ref _disposed) == 0)
                Badge = image;
            else
                image.Dispose();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing a popup only stops this row from updating; the shared cache request may
            // still finish for another open view.
        }
        catch (Exception)
        {
            // The XAML placeholder remains visible for an unreadable/missing local badge.
        }
    }

    partial void OnBadgeChanging(Bitmap? value)
    {
        if (!ReferenceEquals(Badge, value))
            Badge?.Dispose();
    }

    partial void OnBadgeChanged(Bitmap? value) => OnPropertyChanged(nameof(HasBadge));

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposed, 1);
        Badge = null;
    }

    private int _badgeLoadStarted;
    private int _disposed;
}

/// <summary>
/// Compact, cache-first achievement details presentation. It never talks to Avalonia controls:
/// the dialog host supplies services and requests the optional stale refresh after the cached
/// state has already been bound.
/// </summary>
public partial class AchievementDetailsViewModel : ViewModelBase, IDisposable
{
    public static readonly TimeSpan DetailRefreshAge = TimeSpan.FromMinutes(5);

    private readonly int _retroAchievementsGameId;
    private readonly IRetroAchievementsDetailsService _details;
    private readonly IRetroAchievementsAccountService _account;
    private readonly IRetroAchievementsBadgeCache? _badges;
    private readonly IAppLogger _logger;
    private readonly bool _deferBadgeLoading;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _lifetime = new();

    public ObservableCollection<AchievementRowViewModel> Achievements { get; } = [];

    [ObservableProperty]
    public partial string GameTitle { get; set; }

    [ObservableProperty]
    public partial int UnlockedCount { get; set; }

    [ObservableProperty]
    public partial int TotalCount { get; set; }

    [ObservableProperty]
    public partial int EarnedPoints { get; set; }

    [ObservableProperty]
    public partial int TotalPoints { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? LastRefreshedAt { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial bool HasLoadedSnapshot { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    public int ProgressMaximum => Math.Max(TotalCount, 1);
    public string ProgressText => $"{UnlockedCount} / {TotalCount} unlocked";
    public string PointsText => $"{EarnedPoints} / {TotalPoints} points";
    public string ProgressAndPointsText => $"{ProgressText} · {PointsText}";
    public string LastRefreshText => LastRefreshedAt is { } refreshed
        ? $"Last refreshed {refreshed.ToLocalTime():g}"
        : "Not refreshed yet";
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public bool HasAchievements => Achievements.Count > 0;
    public string EmptyStateTitle => HasLoadedSnapshot
        ? "No achievements available"
        : IsRefreshing
            ? "Loading achievements…"
            : "No achievement details cached";
    public string EmptyStateDescription => IsRefreshing
        ? "Contacting RetroAchievements and updating this game’s details."
        : HasLoadedSnapshot
            ? "RetroAchievements did not return any achievements for this game."
            : _account.IsConnected
                ? "Press Refresh to download this game's achievement list."
                : "Reconnect to load this game's achievement list. Once loaded, it will remain available offline.";

    public event Action? CloseRequested;

    public AchievementDetailsViewModel(
        string gameTitle,
        int retroAchievementsGameId,
        IRetroAchievementsDetailsService details,
        IRetroAchievementsAccountService account,
        IRetroAchievementsBadgeCache? badges = null,
        RetroAchievementsDetailsSnapshot? cached = null,
        TimeProvider? timeProvider = null,
        IAppLogger? logger = null,
        bool deferBadgeLoading = false)
    {
        GameTitle = gameTitle;
        _retroAchievementsGameId = retroAchievementsGameId;
        _details = details;
        _account = account;
        _badges = badges;
        _logger = logger ?? NullAppLogger.Instance;
        _deferBadgeLoading = deferBadgeLoading;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _details.DetailsRefreshed += HandleDetailsRefreshed;

        if (cached is not null)
            ApplySnapshot(cached);
        else
            StatusText = "Loading achievement details…";
    }

    /// <summary>Starts a background refresh only for missing or older-than-five-minute details.</summary>
    public Task RefreshIfStaleAsync()
    {
        var needsRefresh = LastRefreshedAt is null ||
            _timeProvider.GetUtcNow() - LastRefreshedAt.Value > DetailRefreshAge;
        return needsRefresh ? RefreshCoreAsync(manual: false) : Task.CompletedTask;
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync(manual: true);

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    private async Task RefreshCoreAsync(bool manual)
    {
        if (IsRefreshing)
            return;

        var credentials = _account.CurrentCredentials;
        if (credentials is null)
        {
            StatusText = HasAchievements
                ? "Reconnect RetroAchievements to refresh cached details."
                : "Connect RetroAchievements to load achievement details.";
            return;
        }

        IsRefreshing = true;
        if (manual)
            StatusText = "Refreshing achievement details…";
        try
        {
            // Detail requests and SQLite cache writes run away from the UI thread. The result is
            // applied below on the captured UI context so the popup remains responsive.
            var response = await Task.Run(
                () => _details.RefreshAsync(
                    credentials,
                    _retroAchievementsGameId,
                    _lifetime.Token,
                    manual),
                _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
                return;

            if (response.IsSuccess)
            {
                ApplySnapshot(response.Value!);
                StatusText = string.Empty;
                return;
            }

            StatusText = response.Status switch
            {
                RetroAchievementsRequestStatus.AuthenticationFailed =>
                    "RetroAchievements needs to be reconnected before details can refresh.",
                RetroAchievementsRequestStatus.Offline =>
                    HasLoadedSnapshot
                        ? "Offline — showing cached achievement details."
                        : "Offline — achievement details have not been cached yet.",
                RetroAchievementsRequestStatus.RateLimited =>
                    "RetroAchievements is rate limiting detail refreshes. Try again shortly.",
                _ => HasLoadedSnapshot
                    ? "Achievement details could not be refreshed; cached data is still available."
                    : "Achievement details could not be loaded and no cached copy is available.",
            };
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window closed before its background refresh returned.
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"RetroAchievements detail refresh failed for game id {_retroAchievementsGameId}.",
                ex);
            StatusText = HasLoadedSnapshot
                ? "Achievement details could not be refreshed; cached data is still available."
                : "Achievement details could not be loaded and no cached copy is available.";
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
                IsRefreshing = false;
        }
    }

    private void ApplySnapshot(RetroAchievementsDetailsSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(GameTitle))
            GameTitle = snapshot.Details.Title;

        foreach (var row in Achievements)
            row.Dispose();
        Achievements.Clear();
        foreach (var achievement in snapshot.Details.Achievements
                     .OrderBy(achievement => achievement.DisplayOrder)
                     .ThenBy(achievement => achievement.AchievementId))
        {
            Achievements.Add(new AchievementRowViewModel(
                achievement,
                _badges,
                loadBadge: !_deferBadgeLoading));
        }

        UnlockedCount = snapshot.Details.UnlockedAchievements;
        TotalCount = snapshot.Details.TotalAchievements;
        EarnedPoints = snapshot.Details.EarnedPoints;
        TotalPoints = snapshot.Details.TotalPoints;
        LastRefreshedAt = snapshot.LastRefreshedAt;
        HasLoadedSnapshot = true;
        OnPropertyChanged(nameof(ProgressMaximum));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(PointsText));
        OnPropertyChanged(nameof(LastRefreshText));
        OnPropertyChanged(nameof(HasAchievements));
    }

    private void HandleDetailsRefreshed(RetroAchievementsDetailsSnapshot snapshot)
    {
        if (snapshot.Details.GameId != _retroAchievementsGameId || _lifetime.IsCancellationRequested)
            return;

        // A post-session refresh can complete independently of this window. Updating through
        // the dispatcher keeps the active popup and its bound collection in sync safely.
        Dispatcher.UIThread.Post(() =>
        {
            if (!_lifetime.IsCancellationRequested)
                ApplySnapshot(snapshot);
        }, DispatcherPriority.Send);
    }

    partial void OnUnlockedCountChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressAndPointsText));
    }
    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressMaximum));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ProgressAndPointsText));
    }
    partial void OnEarnedPointsChanged(int value)
    {
        OnPropertyChanged(nameof(PointsText));
        OnPropertyChanged(nameof(ProgressAndPointsText));
    }
    partial void OnTotalPointsChanged(int value)
    {
        OnPropertyChanged(nameof(PointsText));
        OnPropertyChanged(nameof(ProgressAndPointsText));
    }
    partial void OnLastRefreshedAtChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastRefreshText));
    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnIsRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
    }
    partial void OnHasLoadedSnapshotChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _details.DetailsRefreshed -= HandleDetailsRefreshed;
        _lifetime.Cancel();
        foreach (var row in Achievements)
            row.Dispose();
        _lifetime.Dispose();
    }

    private int _disposed;
}
