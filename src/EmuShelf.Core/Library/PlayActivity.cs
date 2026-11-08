namespace EmuShelf.Core.Library;

/// <summary>How the Activity ranking orders games (M46 P3).</summary>
public enum PlayRankingMetric
{
    TotalTime,
    TimesPlayed,
    AverageSession,
}

/// <summary>One local calendar day of the timeline and the sessions that started on it, newest first.</summary>
public sealed record PlayDay(DateOnly Date, IReadOnlyList<PlaySession> Sessions)
{
    /// <summary>The day's known play time; sessions of unknown length add nothing.</summary>
    public TimeSpan Total => TimeSpan.FromSeconds(Sessions.Sum(session => (long)(session.Duration?.TotalSeconds ?? 0)));
}

/// <summary>A game's place in the ranking, from its running totals.</summary>
public sealed record PlayRanking(Game Game, TimeSpan Total, int Plays, TimeSpan Average);

/// <summary>The calendar arithmetic and orderings behind the Activity screen, kept pure for tests.</summary>
public static class PlayActivity
{
    /// <summary>The local midnight that starts the week holding <paramref name="moment"/>.</summary>
    public static DateTimeOffset WeekStart(DateTimeOffset moment, TimeZoneInfo zone, DayOfWeek firstDayOfWeek)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(moment, zone);
        var back = ((int)local.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        return LocalMidnight(DateOnly.FromDateTime(local.DateTime).AddDays(-back), zone);
    }

    /// <summary>The instant a local calendar day starts in <paramref name="zone"/>.</summary>
    public static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo zone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // A midnight skipped by a clock change does not exist; the day then starts an hour later.
        if (zone.IsInvalidTime(midnight))
            midnight = midnight.AddHours(1);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }

    /// <summary>
    /// Groups sessions by the local day they started on, newest day first; days without play are left out.
    /// </summary>
    public static IReadOnlyList<PlayDay> ByDay(IEnumerable<PlaySession> sessions, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(zone);
        return sessions
            .GroupBy(session => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(session.StartedAt, zone).DateTime))
            .OrderByDescending(group => group.Key)
            .Select(group => new PlayDay(
                group.Key,
                group.OrderByDescending(session => session.StartedAt).ThenByDescending(session => session.Id).ToList()))
            .ToList();
    }

    /// <summary>
    /// Played games ordered by <paramref name="metric"/>, highest first; ties fall back to total time, then
    /// title. The average is the play time over the plays, the same totals the library shows.
    /// </summary>
    public static IReadOnlyList<PlayRanking> Rank(IEnumerable<Game> games, PlayRankingMetric metric)
    {
        ArgumentNullException.ThrowIfNull(games);
        var played = games
            .Where(game => game.PlayCount > 0)
            .Select(game => new PlayRanking(
                game,
                game.Playtime,
                game.PlayCount,
                TimeSpan.FromSeconds(game.Playtime.TotalSeconds / game.PlayCount)));
        var ordered = metric switch
        {
            PlayRankingMetric.TimesPlayed => played.OrderByDescending(rank => rank.Plays),
            PlayRankingMetric.AverageSession => played.OrderByDescending(rank => rank.Average),
            _ => played.OrderByDescending(rank => rank.Total),
        };
        return ordered
            .ThenByDescending(rank => rank.Total)
            .ThenBy(rank => rank.Game.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
