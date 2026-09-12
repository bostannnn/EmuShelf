using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.App.ViewModels;

public enum AchievementDisplayFilter
{
    All,
    Locked,
    Unlocked,
}

public enum AchievementDisplaySort
{
    Default,
    Points,
    UnlockedFirst,
    RecentlyUnlocked,
}

/// <summary>Presentation state for a single display-ordered RetroAchievements achievement.</summary>
public partial class AchievementRowViewModel : ObservableObject, IDisposable
{
    private readonly Func<string, CancellationToken, Task<string?>>? _loadIcon;

    public string AchievementId { get; }
    public bool HasPoints { get; }
    public bool ProgressKnown { get; }
    private readonly string _title;
    private readonly string _description;
    [ObservableProperty]
    public partial bool IsRevealed { get; set; }
    public bool IsHidden { get; }
    public bool CanReveal => IsHidden && !IsUnlocked && !IsRevealed;
    [RelayCommand]
    private void Reveal() => IsRevealed = true;
    partial void OnIsRevealedChanged(bool value) { OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Description)); OnPropertyChanged(nameof(CanReveal)); if (!CanReveal) _ = LoadBadgeAsync(BadgeName); }
    public string Title => CanReveal ? "Hidden achievement" : _title;
    public string Description => CanReveal ? "Reveal to see this achievement’s details." : _description;
    public string PointsText { get; }
    public string EarnedText { get; }
    public string UnlockStateText { get; }
    public bool IsUnlocked { get; }
    public bool IsLocked => ProgressKnown && !IsUnlocked;

    /// <summary>
    /// Whether this achievement was unlocked in hardcore. A hardcore unlock always implies the
    /// softcore unlock, so the list stays the RA-standard softcore view and the hardcore ones are
    /// distinguished by a gold border rather than a separate mode. See DECISIONS 2026-08-16.
    /// </summary>
    public bool IsHardcore { get; }
    public string BadgeName { get; }
    public int Points { get; }
    public int DisplayOrder { get; }
    public DateTimeOffset? EarnedAt { get; }

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
        _loadIcon = badges is null ? null : badges.GetBadgePathAsync;
        AchievementId = achievement.AchievementId.ToString();
        _title = achievement.Title; _description = achievement.Description;
        Points = achievement.Points; HasPoints = true; ProgressKnown = true;
        DisplayOrder = achievement.DisplayOrder;
        PointsText = achievement.Points == 1 ? "1 point" : $"{achievement.Points} points";
        IsUnlocked = achievement.IsEarned; IsHardcore = achievement.IsHardcore;
        BadgeName = achievement.BadgeName;
        EarnedAt = achievement.DateEarnedHardcore ?? achievement.DateEarned;
        UnlockStateText = IsHardcore ? "Hardcore" : IsUnlocked ? "Softcore" : "Locked";
        EarnedText = EarnedAt is { } earned ? $"Earned {earned.ToLocalTime():d MMM yyyy}" : "Not earned";
        if (loadBadge && _loadIcon is not null) _ = LoadBadgeAsync(BadgeName);
    }

    public AchievementRowViewModel(AchievementEntry entry, IAchievementProvider provider, bool loadBadge = true)
    {
        _loadIcon = provider.GetIconPathAsync;
        AchievementId = entry.Id; _title = entry.Title; _description = entry.Description;
        Points = entry.Points ?? 0; HasPoints = entry.Points is not null;
        PointsText = entry.Points is { } points ? $"{points} point{(points == 1 ? "" : "s")}" : "";
        ProgressKnown = entry.IsUnlocked is not null; IsUnlocked = entry.IsUnlocked == true;
        IsHardcore = entry.IsHardcore; IsHidden = entry.IsHidden;
        BadgeName = entry.Icon; DisplayOrder = entry.DisplayOrder; EarnedAt = entry.EarnedAt;
        UnlockStateText = !ProgressKnown ? "Unknown" : IsHardcore ? "Hardcore" : IsUnlocked ?
            (provider.SupportsHardcore ? "Softcore" : "Unlocked") : "Locked";
        EarnedText = !ProgressKnown ? "Progress unavailable" : EarnedAt is { } earned ?
            $"Earned {earned.ToLocalTime():d MMM yyyy}" : IsUnlocked ? "Unlocked" : "Not earned";
        if (loadBadge && !CanReveal) _ = LoadBadgeAsync(BadgeName);
    }

    public async Task LoadBadgeAsync(string badgeName, CancellationToken cancellationToken = default)
    {
        if (_loadIcon is null || CanReveal || Badge is not null || string.IsNullOrWhiteSpace(badgeName) ||
            Interlocked.CompareExchange(ref _badgeLoadStarted, 1, 0) != 0)
            return;

        try
        {
            // Badge cache lookup/download and file I/O stay on a worker. Only assigning the
            // decoded Bitmap returns to the UI context.
            var path = await Task.Run(
                () => _loadIcon(badgeName, cancellationToken),
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

    private readonly AchievementGameRef _game;
    private readonly IAchievementProvider _provider;
    private readonly bool _ownsProvider;
    public bool SupportsPoints => _provider.SupportsPoints;
    public bool SupportsHardcore => _provider.SupportsHardcore;
    public string ProviderText => _provider.DisplayName + (_provider.AccountId is { } account ? " · " + account : "");
    [ObservableProperty]
    public partial bool ProgressKnown { get; set; } = true;
    private readonly IAppLogger _logger;
    private readonly bool _deferBadgeLoading;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _lifetime = new();
    private IList<AchievementRowViewModel> _visibleAchievements = Array.Empty<AchievementRowViewModel>();

    public ObservableCollection<AchievementRowViewModel> Achievements { get; } = [];
    public IList<AchievementRowViewModel> VisibleAchievements
    {
        get => _visibleAchievements;
        private set => SetProperty(ref _visibleAchievements, value);
    }

    [ObservableProperty]
    public partial AchievementDisplayFilter SelectedFilter { get; set; } = AchievementDisplayFilter.All;

    [ObservableProperty]
    public partial AchievementDisplaySort SelectedSort { get; set; } = AchievementDisplaySort.Default;

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

    /// <summary>Unlocks earned in hardcore (the gold subset of <see cref="UnlockedCount"/>).</summary>
    [ObservableProperty]
    public partial int HardcoreUnlockedCount { get; set; }

    /// <summary>Points from hardcore unlocks (the gold subset of <see cref="EarnedPoints"/>).</summary>
    [ObservableProperty]
    public partial int HardcoreEarnedPoints { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset? LastRefreshedAt { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    [ObservableProperty]
    public partial bool HasLoadedSnapshot { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    public int ProgressMaximum => Math.Max(TotalCount, 1);
    // Softcore (silver) and hardcore (gold) read in parallel — same shape, one word apart — so no
    // surface labels one of them "unlocked" and the other by mode. Both are always shown once a set
    // has loaded, so they never disagree.
    public string ProgressText => !ProgressKnown ? "Progress unavailable" : $"{UnlockedCount} / {TotalCount} {(SupportsHardcore ? "softcore" : "unlocked")}";
    public string PointsText => $"{EarnedPoints} / {TotalPoints} points";
    public string HardcoreProgressText => $"{HardcoreUnlockedCount} / {TotalCount} hardcore";
    public string HardcorePointsText => $"{HardcoreEarnedPoints} / {TotalPoints} points";
    public string LastRefreshText => LastRefreshedAt is { } refreshed
        ? $"Last refreshed {refreshed.ToLocalTime():g}"
        : "Not refreshed yet";
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public bool HasAchievements => Achievements.Count > 0;
    public bool HasVisibleAchievements => VisibleAchievements.Count > 0;
    public bool HasFilteredEmptyState => HasAchievements && !HasVisibleAchievements;
    public int LockedCount => Achievements.Count(row => row.IsLocked);
    public string AllFilterText => $"All  {TotalCount}";
    public string LockedFilterText => $"Locked  {LockedCount}";
    public string UnlockedFilterText => $"Unlocked  {UnlockedCount}";
    public bool IsAllFilterSelected => SelectedFilter == AchievementDisplayFilter.All;
    public bool IsLockedFilterSelected => SelectedFilter == AchievementDisplayFilter.Locked;
    public bool IsUnlockedFilterSelected => SelectedFilter == AchievementDisplayFilter.Unlocked;
    public string SortText => SelectedSort switch
    {
        AchievementDisplaySort.Default => "Default",
        AchievementDisplaySort.Points => "Points",
        AchievementDisplaySort.UnlockedFirst => "Unlocked first",
        AchievementDisplaySort.RecentlyUnlocked => "Recently unlocked",
        _ => "Default",
    };
    public string FilterEmptyStateText => SelectedFilter switch
    {
        AchievementDisplayFilter.Locked => "No locked achievements",
        AchievementDisplayFilter.Unlocked => "No unlocked achievements yet",
        _ => "No achievements available",
    };
    public string EmptyStateTitle => HasLoadedSnapshot
        ? "No achievements available"
        : IsRefreshing
            ? "Loading achievements…"
            : "No achievement details cached";
    public string EmptyStateDescription => IsRefreshing
        ? $"Contacting {_provider.DisplayName} and updating this game’s details."
        : HasLoadedSnapshot
            ? $"{_provider.DisplayName} did not return any achievements for this game."
            : _provider.IsConnected
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
        : this(gameTitle, new("retroachievements", retroAchievementsGameId.ToString()),
            new RetroAchievementProvider(details, account, badges),
            cached is null ? null : RetroAchievementProvider.Convert(cached, account.Account?.UserUlid ?? ""),
            timeProvider, logger, deferBadgeLoading, ownsProvider: true)
    { }

    public AchievementDetailsViewModel(string gameTitle, AchievementGameRef game, IAchievementProvider provider,
        AchievementSnapshot? cached = null, TimeProvider? timeProvider = null, IAppLogger? logger = null,
        bool deferBadgeLoading = false, bool ownsProvider = false)
    {
        GameTitle = gameTitle; _game = game; _provider = provider; _ownsProvider = ownsProvider;
        _logger = logger ?? NullAppLogger.Instance;
        _deferBadgeLoading = deferBadgeLoading;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _provider.Changed += HandleProviderChanged;
        if (cached is not null) ApplySnapshot(cached);
        else StatusText = "Loading achievement details…";
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
    private void ShowAllAchievements() => SelectedFilter = AchievementDisplayFilter.All;

    [RelayCommand]
    private void ShowLockedAchievements() => SelectedFilter = AchievementDisplayFilter.Locked;

    [RelayCommand]
    private void ShowUnlockedAchievements() => SelectedFilter = AchievementDisplayFilter.Unlocked;

    [RelayCommand]
    private void CycleFilter(int delta)
    {
        var filters = Enum.GetValues<AchievementDisplayFilter>();
        var next = ((int)SelectedFilter + delta) % filters.Length;
        if (next < 0)
            next += filters.Length;
        SelectedFilter = filters[next];
    }

    [RelayCommand]
    private void CycleSort()
    {
        var sorts = Enum.GetValues<AchievementDisplaySort>().Where(sort => SupportsPoints || sort != AchievementDisplaySort.Points).ToArray();
        SelectedSort = sorts[(Array.IndexOf(sorts, SelectedSort) + 1) % sorts.Length];
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    private async Task RefreshCoreAsync(bool manual)
    {
        if (IsRefreshing)
            return;

        if (!_provider.IsConnected)
        {
            StatusText = HasAchievements
                ? $"Reconnect {_provider.DisplayName} to refresh cached details."
                : $"Connect {_provider.DisplayName} in Settings to load achievement details.";
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
                () => _provider.RefreshAsync(_game, _lifetime.Token, manual),
                _lifetime.Token);
            if (_lifetime.IsCancellationRequested)
                return;

            if (response.Status == AchievementStatus.AccountChanged) return;
            if (response.Snapshot is { } snapshot) ApplySnapshot(snapshot);
            if (response.IsSuccess)
            {
                StatusText = string.Empty;
                return;
            }

            StatusText = response.Status switch
            {
                AchievementStatus.AuthenticationFailed =>
                    $"{_provider.DisplayName} needs to be reconnected before details can refresh.",
                AchievementStatus.Offline =>
                    HasLoadedSnapshot
                        ? "Offline — showing cached achievement details."
                        : "Offline — achievement details have not been cached yet.",
                AchievementStatus.RateLimited =>
                    $"{_provider.DisplayName} is rate limiting detail refreshes. Try again shortly.",
                AchievementStatus.Unavailable => "Progress is private or unavailable. Showing the achievement list.",
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
                $"Achievement detail refresh failed for {_game.Provider} game id {_game.GameId}.",
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

    private void ApplySnapshot(AchievementSnapshot snapshot)
    {
        if (snapshot.Game != _game || (_provider.Id == "steam" && snapshot.AccountId != _provider.AccountId)) return;
        if (string.IsNullOrWhiteSpace(GameTitle)) GameTitle = snapshot.Title;
        foreach (var row in Achievements) row.Dispose();
        Achievements.Clear();
        foreach (var entry in snapshot.Achievements.OrderBy(a => a.DisplayOrder).ThenBy(a => a.Id))
            Achievements.Add(new AchievementRowViewModel(entry, _provider, !_deferBadgeLoading));
        ProgressKnown = snapshot.ProgressKnown;
        UnlockedCount = Achievements.Count(a => a.IsUnlocked); TotalCount = Achievements.Count;
        EarnedPoints = Achievements.Where(a => a.IsUnlocked).Sum(a => a.Points);
        TotalPoints = Achievements.Sum(a => a.Points);
        HardcoreUnlockedCount = Achievements.Count(a => a.IsHardcore);
        HardcoreEarnedPoints = Achievements.Where(a => a.IsHardcore).Sum(a => a.Points);
        LastRefreshedAt = snapshot.RefreshedAt; HasLoadedSnapshot = true;
        RebuildVisibleAchievements();
        foreach (var name in new[] { nameof(ProgressMaximum), nameof(ProgressText), nameof(PointsText),
            nameof(LastRefreshText), nameof(HasAchievements), nameof(HasFilteredEmptyState), nameof(ProviderText),
            nameof(LockedCount), nameof(LockedFilterText) }) OnPropertyChanged(name);
    }

    private void RebuildVisibleAchievements()
    {
        IEnumerable<AchievementRowViewModel> rows = SelectedFilter switch
        {
            AchievementDisplayFilter.Locked => Achievements.Where(row => row.IsLocked),
            AchievementDisplayFilter.Unlocked => Achievements.Where(row => row.IsUnlocked),
            _ => Achievements,
        };

        rows = SelectedSort switch
        {
            AchievementDisplaySort.Points => rows
                .OrderByDescending(row => row.Points)
                .ThenBy(row => row.DisplayOrder)
                .ThenBy(row => row.AchievementId),
            AchievementDisplaySort.UnlockedFirst => rows
                .OrderByDescending(row => row.IsUnlocked)
                .ThenBy(row => row.DisplayOrder)
                .ThenBy(row => row.AchievementId),
            AchievementDisplaySort.RecentlyUnlocked => rows
                .OrderBy(row => row.IsUnlocked ? 0 : 1)
                .ThenByDescending(row => row.EarnedAt)
                .ThenBy(row => row.DisplayOrder)
                .ThenBy(row => row.AchievementId),
            _ => rows
                .OrderBy(row => row.DisplayOrder)
                .ThenBy(row => row.AchievementId),
        };

        // Publish a new immutable snapshot instead of mutating the active ItemsRepeater source.
        // A collection Reset can retain a stale virtualization anchor in a real compositor and
        // leave the first cell reserved but unrealized after repeated sorting.
        VisibleAchievements = rows.ToArray();

        OnPropertyChanged(nameof(HasVisibleAchievements));
        OnPropertyChanged(nameof(HasFilteredEmptyState));
        OnPropertyChanged(nameof(FilterEmptyStateText));
    }

    private int _providerChangeRevision;

    private async void HandleProviderChanged()
    {
        var revision = Interlocked.Increment(ref _providerChangeRevision);
        try
        {
            var snapshot = await Task.Run(() => _provider.GetCached(_game)).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Volatile.Read(ref _disposed) != 0 || revision != Volatile.Read(ref _providerChangeRevision)) return;
                if (snapshot is not null && (_provider.Id != "steam" || snapshot.AccountId == _provider.AccountId))
                    ApplySnapshot(snapshot);
                else
                {
                    foreach (var row in Achievements) row.Dispose();
                    Achievements.Clear(); RebuildVisibleAchievements();
                    UnlockedCount = TotalCount = EarnedPoints = TotalPoints = HardcoreUnlockedCount = HardcoreEarnedPoints = 0;
                    LastRefreshedAt = null; HasLoadedSnapshot = false; ProgressKnown = false;
                    StatusText = $"Connect {_provider.DisplayName} in Settings to load achievement details.";
                    OnPropertyChanged(nameof(HasAchievements)); OnPropertyChanged(nameof(ProviderText));
                }
            }, DispatcherPriority.Send);
        }
        catch (Exception) { /* An optional cache update must not interrupt the active viewer. */ }
    }

    partial void OnProgressKnownChanged(bool value) => OnPropertyChanged(nameof(ProgressText));

    partial void OnUnlockedCountChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(LockedCount));
        OnPropertyChanged(nameof(LockedFilterText));
        OnPropertyChanged(nameof(UnlockedFilterText));
    }
    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressMaximum));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(HardcoreProgressText));
        OnPropertyChanged(nameof(LockedCount));
        OnPropertyChanged(nameof(AllFilterText));
        OnPropertyChanged(nameof(LockedFilterText));
    }
    partial void OnEarnedPointsChanged(int value)
    {
        OnPropertyChanged(nameof(PointsText));
    }
    partial void OnTotalPointsChanged(int value)
    {
        OnPropertyChanged(nameof(PointsText));
        OnPropertyChanged(nameof(HardcorePointsText));
    }
    partial void OnHardcoreUnlockedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HardcoreProgressText));
    }
    partial void OnHardcoreEarnedPointsChanged(int value)
    {
        OnPropertyChanged(nameof(HardcorePointsText));
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
    partial void OnSelectedFilterChanged(AchievementDisplayFilter value)
    {
        OnPropertyChanged(nameof(IsAllFilterSelected));
        OnPropertyChanged(nameof(IsLockedFilterSelected));
        OnPropertyChanged(nameof(IsUnlockedFilterSelected));
        RebuildVisibleAchievements();
    }
    partial void OnSelectedSortChanged(AchievementDisplaySort value)
    {
        OnPropertyChanged(nameof(SortText));
        RebuildVisibleAchievements();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _provider.Changed -= HandleProviderChanged;
        if (_ownsProvider && _provider is IDisposable owned) owned.Dispose();
        _lifetime.Cancel();
        VisibleAchievements = Array.Empty<AchievementRowViewModel>();
        foreach (var row in Achievements)
            row.Dispose();
        _lifetime.Dispose();
    }

    private int _disposed;
}
