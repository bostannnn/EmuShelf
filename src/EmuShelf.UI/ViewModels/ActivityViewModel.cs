using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;

namespace EmuShelf.App.ViewModels;

/// <summary>The Activity screen's three pages (M46 P3).</summary>
public enum ActivityPage
{
    Timeline,
    Ranking,
    Game,
}

/// <summary>
/// The play log as a screen, shared by the Desktop window and the Gamepad overlay: a week-by-week timeline of
/// sessions, a ranking of played games, and one game's page. Sessions can be corrected or deleted. Reads run
/// off the UI thread and the timeline loads one week at a time.
/// </summary>
public sealed partial class ActivityViewModel : ViewModelBase
{
    private readonly IGameLibrary _library;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _zone;
    private readonly DayOfWeek _firstDayOfWeek;
    private readonly IAppLogger _logger;
    private DateTimeOffset _weekStart;
    // Where B returns from a game page; null when the screen opened on that game (B then closes it).
    private ActivityPage? _pageBeforeGame;
    private bool _hasShownPage;
    private long? _gameId;
    private int _loadGeneration;

    public ActivityViewModel(
        IGameLibrary library,
        TimeProvider? clock = null,
        TimeZoneInfo? zone = null,
        DayOfWeek? firstDayOfWeek = null,
        IAppLogger? logger = null)
    {
        _library = library;
        _clock = clock ?? TimeProvider.System;
        _zone = zone ?? TimeZoneInfo.Local;
        _firstDayOfWeek = firstDayOfWeek ?? CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        _logger = logger ?? NullAppLogger.Instance;
        _weekStart = PlayActivity.WeekStart(_clock.GetUtcNow(), _zone, _firstDayOfWeek);
    }

    /// <summary>Raised when the Desktop window should close.</summary>
    public event Action? CloseRequested;

    /// <summary>A session was edited or deleted, so the library's play totals changed.</summary>
    public bool HasChanges { get; private set; }

    public ObservableCollection<ActivityDayViewModel> Days { get; } = [];
    public ObservableCollection<ActivityRankItemViewModel> RankItems { get; } = [];
    public ObservableCollection<ActivitySessionViewModel> GameSessions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimeline), nameof(IsRanking), nameof(IsGamePage), nameof(Title))]
    public partial ActivityPage Page { get; private set; }

    public bool IsTimeline => Page == ActivityPage.Timeline;
    public bool IsRanking => Page == ActivityPage.Ranking;
    public bool IsGamePage => Page == ActivityPage.Game;
    public string Title => Page == ActivityPage.Game ? GameTitle : "Activity";

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    // Timeline
    [ObservableProperty]
    public partial string WeekLabel { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string WeekTotalText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanShowNewerWeek { get; private set; }

    [ObservableProperty]
    public partial bool IsWeekEmpty { get; private set; }

    // Ranking
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RankingMetricLabel))]
    public partial PlayRankingMetric RankingMetric { get; private set; }

    public string RankingMetricLabel => RankingMetric switch
    {
        PlayRankingMetric.TimesPlayed => "Times played",
        PlayRankingMetric.AverageSession => "Average session",
        _ => "Total time",
    };

    [ObservableProperty]
    public partial bool IsRankingEmpty { get; private set; }

    // Game page
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial string GameTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GameTotalText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GamePlaysText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GameAverageText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GameFirstPlayedText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GameLastPlayedText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string GameEarlierText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasGameEarlierPlay { get; private set; }

    // Focus (Gamepad): one ring over the current page's rows.
    [ObservableProperty]
    public partial int FocusedIndex { get; private set; } = -1;

    /// <summary>Bumped whenever the ring moves, so the view can bring the focused row into view.</summary>
    [ObservableProperty]
    public partial int FocusRevision { get; private set; }

    public IActivityRow? FocusedRow => FocusedIndex >= 0 && FocusedIndex < FocusRows.Count ? FocusRows[FocusedIndex] : null;

    private IReadOnlyList<IActivityRow> FocusRows => Page switch
    {
        ActivityPage.Ranking => RankItems,
        ActivityPage.Game => GameSessions,
        _ => Days.SelectMany(day => day.Sessions).ToList(),
    };

    // Editing one session
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    public partial ActivitySessionViewModel? EditingSession { get; private set; }

    public bool IsEditing => EditingSession is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditLengthText))]
    public partial int EditMinutes { get; private set; }

    public string EditLengthText => ActivityText.Duration(TimeSpan.FromMinutes(EditMinutes), zeroText: "0m");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaveFocused), nameof(IsDeleteFocused), nameof(IsCancelFocused))]
    public partial int EditFocus { get; private set; }

    public bool IsSaveFocused => EditFocus == 0;
    public bool IsDeleteFocused => EditFocus == 1;
    public bool IsCancelFocused => EditFocus == 2;

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; private set; }

    /// <summary>Loads the timeline's current week, or opens straight on one game's page.</summary>
    public async Task LoadAsync(long? gameId = null)
    {
        if (gameId is { } id)
            await OpenGameAsync(id);
        else
            await LoadTimelineAsync();
    }

    [RelayCommand]
    private Task ShowTimelineAsync()
    {
        CancelEditCore();
        return LoadTimelineAsync();
    }

    [RelayCommand]
    private Task ShowRankingAsync()
    {
        CancelEditCore();
        return LoadRankingAsync();
    }

    [RelayCommand]
    private Task OlderWeekAsync()
    {
        _weekStart = PlayActivity.WeekStart(_weekStart.AddDays(-1), _zone, _firstDayOfWeek);
        return LoadTimelineAsync();
    }

    [RelayCommand]
    private Task NewerWeekAsync()
    {
        if (!CanShowNewerWeek)
            return Task.CompletedTask;
        _weekStart = PlayActivity.WeekStart(_weekStart.AddDays(8), _zone, _firstDayOfWeek);
        return LoadTimelineAsync();
    }

    [RelayCommand]
    private Task CycleMetricAsync(int step = 1)
    {
        var count = Enum.GetValues<PlayRankingMetric>().Length;
        RankingMetric = (PlayRankingMetric)(((int)RankingMetric + step % count + count) % count);
        return LoadRankingAsync();
    }

    [RelayCommand]
    private Task OpenGameAsync(long gameId)
    {
        if (_hasShownPage && Page != ActivityPage.Game)
            _pageBeforeGame = Page;
        _gameId = gameId;
        return LoadGameAsync();
    }

    /// <summary>A on a row: a ranked game or a session opens that game's page.</summary>
    [RelayCommand]
    private Task OpenRowAsync(IActivityRow? row) => row switch
    {
        ActivityRankItemViewModel rank => OpenGameAsync(rank.GameId),
        ActivitySessionViewModel { IsGameRemoved: false } session when Page != ActivityPage.Game =>
            OpenGameAsync(session.Session.GameId),
        _ => Task.CompletedTask,
    };

    /// <summary>B: closes the editor, then the game page; false when there is nothing left to back out of.</summary>
    public bool Back()
    {
        if (IsEditing)
        {
            if (IsConfirmingDelete)
                IsConfirmingDelete = false;
            else
                CancelEditCore();
            return true;
        }
        if (Page == ActivityPage.Game && _pageBeforeGame is { } previous)
        {
            _ = previous == ActivityPage.Ranking ? LoadRankingAsync() : LoadTimelineAsync();
            return true;
        }
        return false;
    }

    [RelayCommand]
    private void GoBack()
    {
        if (!Back())
            CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    [RelayCommand]
    private void EditSession(ActivitySessionViewModel? session)
    {
        if (session is null)
            return;
        EditingSession = session;
        EditMinutes = (int)Math.Round((session.Session.Duration ?? TimeSpan.Zero).TotalMinutes);
        EditFocus = 0;
        IsConfirmingDelete = false;
    }

    [RelayCommand]
    private void AdjustEdit(int minutes)
    {
        if (IsEditing)
            EditMinutes = Math.Clamp(EditMinutes + minutes, 0, 24 * 60);
    }

    [RelayCommand]
    private async Task SaveEditAsync()
    {
        if (EditingSession is not { } session)
            return;
        var minutes = EditMinutes;
        await MutateAsync(() => _library.EditPlaySession(session.Session.Id, TimeSpan.FromMinutes(minutes)));
    }

    /// <summary>Asks before deleting; the second press (or A on the confirmation) deletes.</summary>
    [RelayCommand]
    private async Task DeleteEditAsync()
    {
        if (EditingSession is not { } session)
            return;
        if (!IsConfirmingDelete)
        {
            IsConfirmingDelete = true;
            return;
        }
        await MutateAsync(() => _library.DeletePlaySession(session.Session.Id));
    }

    [RelayCommand]
    private void CancelEdit() => CancelEditCore();

    /// <summary>The Gamepad input for the screen. False only for B with nothing to back out of.</summary>
    public bool Dispatch(GamepadAction action)
    {
        if (IsEditing)
            return DispatchEdit(action);

        switch (action)
        {
            case GamepadAction.NavigateUp:
                MoveFocus(-1);
                return true;
            case GamepadAction.NavigateDown:
                MoveFocus(1);
                return true;
            case GamepadAction.NavigateLeft:
                if (IsTimeline)
                    _ = OlderWeekAsync();
                else if (IsRanking)
                    _ = CycleMetricAsync(-1);
                return true;
            case GamepadAction.NavigateRight:
                if (IsTimeline)
                    _ = NewerWeekAsync();
                else if (IsRanking)
                    _ = CycleMetricAsync(1);
                return true;
            case GamepadAction.PreviousPlatform:
            case GamepadAction.NextPlatform:
                _ = IsRanking || (IsGamePage && _pageBeforeGame is ActivityPage.Ranking)
                    ? ShowTimelineAsync()
                    : ShowRankingAsync();
                return true;
            case GamepadAction.Confirm:
                _ = OpenRowAsync(FocusedRow);
                return true;
            case GamepadAction.Actions:
                if (FocusedRow is ActivitySessionViewModel session)
                    EditSession(session);
                return true;
            case GamepadAction.Cancel:
                return Back();
            default:
                return true;
        }
    }

    private bool DispatchEdit(GamepadAction action)
    {
        if (IsConfirmingDelete)
        {
            if (action == GamepadAction.Confirm)
                _ = DeleteEditAsync();
            else if (action == GamepadAction.Cancel)
                IsConfirmingDelete = false;
            return true;
        }

        switch (action)
        {
            case GamepadAction.NavigateLeft:
                AdjustEdit(-1);
                break;
            case GamepadAction.NavigateRight:
                AdjustEdit(1);
                break;
            case GamepadAction.PreviousPlatform:
                AdjustEdit(-10);
                break;
            case GamepadAction.NextPlatform:
                AdjustEdit(10);
                break;
            case GamepadAction.NavigateUp:
                EditFocus = (EditFocus + 2) % 3;
                break;
            case GamepadAction.NavigateDown:
                EditFocus = (EditFocus + 1) % 3;
                break;
            case GamepadAction.Confirm:
                if (EditFocus == 0)
                    _ = SaveEditAsync();
                else if (EditFocus == 1)
                    _ = DeleteEditAsync();
                else
                    CancelEditCore();
                break;
            case GamepadAction.Cancel:
                CancelEditCore();
                break;
        }
        return true;
    }

    private void CancelEditCore()
    {
        EditingSession = null;
        IsConfirmingDelete = false;
    }

    private async Task MutateAsync(Func<bool> change)
    {
        try
        {
            var changed = await Task.Run(change);
            if (changed)
                HasChanges = true;
        }
        catch (Exception ex)
        {
            _logger.Error("Could not change a play session.", ex);
        }
        CancelEditCore();
        await ReloadCurrentPageAsync();
    }

    private Task ReloadCurrentPageAsync() => Page switch
    {
        ActivityPage.Ranking => LoadRankingAsync(),
        ActivityPage.Game => LoadGameAsync(),
        _ => LoadTimelineAsync(),
    };

    private void MoveFocus(int step)
    {
        var rows = FocusRows;
        if (rows.Count == 0)
            return;
        SetFocus(Math.Clamp(FocusedIndex + step, 0, rows.Count - 1));
    }

    private void SetFocus(int index)
    {
        var rows = FocusRows;
        for (var i = 0; i < rows.Count; i++)
            rows[i].IsFocused = i == index;
        FocusedIndex = rows.Count == 0 ? -1 : index;
        OnPropertyChanged(nameof(FocusedRow));
        FocusRevision++;
    }

    // Each load first takes a generation; a slower read that a newer one overtook is dropped.
    private async Task LoadTimelineAsync()
    {
        var generation = ++_loadGeneration;
        IsLoading = true;
        var from = _weekStart;
        var to = PlayActivity.WeekStart(_weekStart.AddDays(8), _zone, _firstDayOfWeek);
        IReadOnlyList<PlayDay> days;
        try
        {
            var sessions = await Task.Run(() => _library.GetPlaySessionsBetween(from, to));
            days = PlayActivity.ByDay(sessions, _zone);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not read the play log.", ex);
            days = [];
        }
        if (generation != _loadGeneration)
            return;

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _zone).DateTime);
        Days.Clear();
        foreach (var day in days)
            Days.Add(new ActivityDayViewModel(day, today, _zone, EditSession));
        var thisWeek = PlayActivity.WeekStart(_clock.GetUtcNow(), _zone, _firstDayOfWeek);
        WeekLabel = ActivityText.Week(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(from, _zone).DateTime), from == thisWeek,
            from == PlayActivity.WeekStart(thisWeek.AddDays(-1), _zone, _firstDayOfWeek));
        var total = TimeSpan.FromSeconds(days.Sum(day => day.Total.TotalSeconds));
        WeekTotalText = days.Count == 0 ? "Nothing played" : $"{ActivityText.Duration(total)} played";
        IsWeekEmpty = days.Count == 0;
        CanShowNewerWeek = from < thisWeek;
        Page = ActivityPage.Timeline;
        SetFocus(0);
        _hasShownPage = true;
        IsLoading = false;
    }

    private async Task LoadRankingAsync()
    {
        var generation = ++_loadGeneration;
        IsLoading = true;
        var metric = RankingMetric;
        IReadOnlyList<PlayRanking> ranking;
        try
        {
            ranking = await Task.Run(() => PlayActivity.Rank(_library.GetGames(), metric));
        }
        catch (Exception ex)
        {
            _logger.Error("Could not read the play totals.", ex);
            ranking = [];
        }
        if (generation != _loadGeneration)
            return;

        RankItems.Clear();
        for (var i = 0; i < ranking.Count; i++)
            RankItems.Add(new ActivityRankItemViewModel(i + 1, ranking[i], metric));
        IsRankingEmpty = RankItems.Count == 0;
        Page = ActivityPage.Ranking;
        SetFocus(0);
        _hasShownPage = true;
        IsLoading = false;
    }

    private async Task LoadGameAsync()
    {
        if (_gameId is not { } gameId)
            return;
        var generation = ++_loadGeneration;
        IsLoading = true;
        Game? game;
        IReadOnlyList<PlaySession> sessions;
        try
        {
            (game, sessions) = await Task.Run(() =>
                (_library.GetGames().FirstOrDefault(candidate => candidate.Id == gameId), _library.GetPlaySessions(gameId)));
        }
        catch (Exception ex)
        {
            _logger.Error("Could not read a game's play history.", ex);
            (game, sessions) = (null, []);
        }
        if (generation != _loadGeneration)
            return;
        if (game is null)
        {
            IsLoading = false;
            return;
        }

        var history = GamePlayHistory.From(game, sessions);
        GameTitle = game.Title;
        GameTotalText = ActivityText.Duration(game.Playtime, zeroText: "None yet");
        GamePlaysText = game.PlayCount == 1 ? "1 time" : $"{game.PlayCount} times";
        GameAverageText = game.PlayCount == 0
            ? RetroAchievementsDisplay.Dash
            : ActivityText.Duration(TimeSpan.FromSeconds(game.Playtime.TotalSeconds / game.PlayCount));
        GameFirstPlayedText = history.FirstPlayedAt is { } first
            ? ActivityText.Date(first, _zone)
            : game.PlayCount > 0 ? "Before the play log" : RetroAchievementsDisplay.Dash;
        GameLastPlayedText = game.LastPlayedAt is { } last ? ActivityText.Date(last, _zone) : RetroAchievementsDisplay.Dash;
        HasGameEarlierPlay = history.EarlierPlayCount > 0 || history.EarlierPlaytime > TimeSpan.Zero;
        GameEarlierText = HasGameEarlierPlay
            ? $"Includes {ActivityText.Duration(history.EarlierPlaytime, zeroText: "no time")} over " +
              $"{(history.EarlierPlayCount == 1 ? "1 play" : $"{history.EarlierPlayCount} plays")} from before the play log"
            : string.Empty;
        GameSessions.Clear();
        foreach (var session in sessions)
            GameSessions.Add(new ActivitySessionViewModel(session, _zone, showDate: true, EditSession));
        Page = ActivityPage.Game;
        SetFocus(0);
        _hasShownPage = true;
        IsLoading = false;
    }
}

/// <summary>A row the Gamepad ring can land on.</summary>
public interface IActivityRow
{
    bool IsFocused { get; set; }
}

/// <summary>One day on the timeline.</summary>
public sealed class ActivityDayViewModel
{
    public ActivityDayViewModel(PlayDay day, DateOnly today, TimeZoneInfo zone, Action<ActivitySessionViewModel>? edit = null)
    {
        Label = ActivityText.Day(day.Date, today);
        TotalText = ActivityText.Duration(day.Total, zeroText: RetroAchievementsDisplay.Dash);
        Sessions = day.Sessions.Select(session => new ActivitySessionViewModel(session, zone, showDate: false, edit)).ToList();
    }

    public string Label { get; }
    public string TotalText { get; }
    public IReadOnlyList<ActivitySessionViewModel> Sessions { get; }
}

/// <summary>One session: when, which game, and how long, with how the length was measured.</summary>
public sealed partial class ActivitySessionViewModel : ObservableObject, IActivityRow
{
    public ActivitySessionViewModel(
        PlaySession session,
        TimeZoneInfo zone,
        bool showDate,
        Action<ActivitySessionViewModel>? edit = null)
    {
        Session = session;
        // The row carries its own command so a context menu, which lives in a popup outside the view's
        // name scope, can reach the editor without a reference back to the screen.
        EditCommand = new RelayCommand(() => edit?.Invoke(this));
        var local = TimeZoneInfo.ConvertTime(session.StartedAt, zone);
        FullWhenText = $"{ActivityText.Date(session.StartedAt, zone)} · {local.ToString("t", CultureInfo.CurrentCulture)}";
        WhenText = showDate ? FullWhenText : local.ToString("t", CultureInfo.CurrentCulture);
        GameTitle = session.IsGameRemoved ? $"{session.GameTitle} (removed)" : session.GameTitle;
        LengthText = ActivityText.SessionLength(session);
        // An estimate already reads "about …" in its length, so only an edit and an unknown length get a note.
        NoteText = session.Measurement switch
        {
            PlaySessionMeasurement.Edited => "edited",
            _ => session.Duration is null ? "length unknown" : string.Empty,
        };
    }

    public PlaySession Session { get; }
    public IRelayCommand EditCommand { get; }
    public bool IsGameRemoved => Session.IsGameRemoved;
    public string WhenText { get; }

    /// <summary>Date and time, for the editor, which can be opened from a timeline row that shows only the time.</summary>
    public string FullWhenText { get; }

    public string GameTitle { get; }
    public string LengthText { get; }
    public string NoteText { get; }
    public bool HasNote => NoteText.Length > 0;

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

/// <summary>One played game in the ranking.</summary>
public sealed partial class ActivityRankItemViewModel : ObservableObject, IActivityRow
{
    public ActivityRankItemViewModel(int place, PlayRanking ranking, PlayRankingMetric metric)
    {
        Place = place;
        GameId = ranking.Game.Id;
        Title = ranking.Game.Title;
        var plays = ranking.Plays == 1 ? "1 play" : $"{ranking.Plays} plays";
        ValueText = metric switch
        {
            PlayRankingMetric.TimesPlayed => plays,
            PlayRankingMetric.AverageSession => ActivityText.Duration(ranking.Average),
            _ => ActivityText.Duration(ranking.Total),
        };
        DetailText = metric switch
        {
            PlayRankingMetric.TimesPlayed => ActivityText.Duration(ranking.Total),
            PlayRankingMetric.AverageSession => $"{ActivityText.Duration(ranking.Total)} · {plays}",
            _ => plays,
        };
    }

    public int Place { get; }
    public long GameId { get; }
    public string Title { get; }
    public string ValueText { get; }
    public string DetailText { get; }

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

/// <summary>The Activity screen's wording for times and lengths.</summary>
public static class ActivityText
{
    /// <summary>"1h 5m", "25m", "&lt; 1m", or <paramref name="zeroText"/> for nothing.</summary>
    public static string Duration(TimeSpan duration, string zeroText = "< 1m")
    {
        var totalSeconds = (long)duration.TotalSeconds;
        if (totalSeconds <= 0)
            return zeroText;
        var totalMinutes = totalSeconds / 60;
        if (totalMinutes < 1)
            return "< 1m";
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        if (hours == 0)
            return $"{minutes}m";
        return minutes == 0 ? $"{hours}h" : $"{hours}h {minutes}m";
    }

    /// <summary>A session's length, "about" for an estimate, "?" when unknown.</summary>
    public static string SessionLength(PlaySession session) => session.Duration switch
    {
        null => "?",
        { } length when session.Measurement == PlaySessionMeasurement.WallClockEstimate => $"about {Duration(length)}",
        { } length => Duration(length),
    };

    public static string Day(DateOnly date, DateOnly today) =>
        date == today ? "Today"
        : date == today.AddDays(-1) ? "Yesterday"
        : date.ToString("dddd d MMMM", CultureInfo.CurrentCulture);

    public static string Date(DateTimeOffset moment, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(moment, zone).ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    public static string Week(DateOnly start, bool isThisWeek, bool isLastWeek) =>
        isThisWeek ? "This week"
        : isLastWeek ? "Last week"
        : $"{start.ToString("d MMM", CultureInfo.CurrentCulture)} – {start.AddDays(6).ToString("d MMM", CultureInfo.CurrentCulture)}";
}
