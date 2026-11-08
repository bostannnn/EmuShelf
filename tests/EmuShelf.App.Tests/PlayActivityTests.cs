using EmuShelf.Core.Library;

namespace EmuShelf.App.Tests;

public class PlayActivityTests
{
    private static readonly TimeZoneInfo Moscow = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");

    private static PlaySession Session(long id, string title, string startedAt, int minutes) => new(
        id, id, title, "nes", DateTimeOffset.Parse(startedAt), TimeSpan.FromMinutes(minutes),
        PlaySessionMeasurement.TrackedProcess, "device");

    [Fact]
    public void WeekStart_IsTheLocalMidnight_OfTheCulturesFirstDay()
    {
        // Sunday 8 November 2026, 01:30 in UTC+3 (still Saturday 22:30 in UTC).
        var moment = DateTimeOffset.Parse("2026-11-07T22:30:00Z");

        Assert.Equal(DateTimeOffset.Parse("2026-11-02T00:00:00+03:00"), PlayActivity.WeekStart(moment, Moscow, DayOfWeek.Monday));
        Assert.Equal(DateTimeOffset.Parse("2026-11-08T00:00:00+03:00"), PlayActivity.WeekStart(moment, Moscow, DayOfWeek.Sunday));
    }

    [Fact]
    public void ByDay_GroupsByTheLocalDay_NewestFirst()
    {
        var sessions = new[]
        {
            Session(1, "A", "2026-11-06T20:00:00Z", 30), // 23:00 local, Friday
            Session(2, "B", "2026-11-06T21:30:00Z", 45), // 00:30 local, Saturday
            Session(3, "C", "2026-11-07T09:00:00Z", 15), // Saturday
        };

        var days = PlayActivity.ByDay(sessions, Moscow);

        Assert.Equal([new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 6)], days.Select(day => day.Date));
        Assert.Equal(["C", "B"], days[0].Sessions.Select(session => session.GameTitle));
        Assert.Equal(TimeSpan.FromMinutes(60), days[0].Total);
    }

    [Fact]
    public void Rank_OrdersByTheMetric_LeavesOutUnplayed_AndBreaksTiesByTime()
    {
        Game Game(long id, string title, int minutes, int plays) => new()
        {
            Id = id, SystemId = "nes", Path = $"/g/{id}", Title = title, Playtime = TimeSpan.FromMinutes(minutes),
            PlayCount = plays,
        };
        var games = new[]
        {
            Game(1, "Long sessions", 300, 2), // average 150
            Game(2, "Many plays", 200, 10), // average 20
            Game(3, "Never", 0, 0),
            Game(4, "Also ten", 100, 10),
        };

        Assert.Equal(["Long sessions", "Many plays", "Also ten"],
            PlayActivity.Rank(games, PlayRankingMetric.TotalTime).Select(rank => rank.Game.Title));
        Assert.Equal(["Many plays", "Also ten", "Long sessions"],
            PlayActivity.Rank(games, PlayRankingMetric.TimesPlayed).Select(rank => rank.Game.Title));
        Assert.Equal(["Long sessions", "Many plays", "Also ten"],
            PlayActivity.Rank(games, PlayRankingMetric.AverageSession).Select(rank => rank.Game.Title));
        Assert.Equal(TimeSpan.FromMinutes(150), PlayActivity.Rank(games, PlayRankingMetric.TotalTime)[0].Average);
    }
}
