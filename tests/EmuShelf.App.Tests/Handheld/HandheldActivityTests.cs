using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M46 P4: the play log in Handheld mode. Activity is a tile on home beside Settings (and a Start menu row, and a game's
// Play history row); open, it is a panel: the sessions, the ranking or a game's sessions on the Launcher, the focused day's
// or game's stats on the Stage. The panel wraps the Activity view model over a real play log.
public partial class HandheldShellViewModelTests
{
    private sealed partial class FakeLibrary
    {
        public Func<ActivityViewModel?>? ActivityFactory { get; set; }
        public List<ActivityViewModel> ActivitiesClosed { get; } = [];

        public ActivityViewModel? CreateActivity() => ActivityFactory?.Invoke();

        public Task CloseActivityAsync(ActivityViewModel activity)
        {
            ActivitiesClosed.Add(activity);
            return Task.CompletedTask;
        }
    }

    /// <summary>A play log on disk: two games, three sessions this week and one last week.</summary>
    private sealed class PlayLog : IDisposable
    {
        // Thursday 12 November 2026, 18:00 in UTC+3.
        public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T18:00:00+03:00");
        public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "EmuShelfHandheldActivity", Guid.NewGuid().ToString("N"));

        public PlayLog()
        {
            var paths = new AppPaths(_directory);
            paths.EnsureDirectoriesExist();
            var database = new LibraryDatabase(paths);
            database.Initialize();
            Library = new GameLibrary(database, new RelativePathResolver(paths));
            Zelda = Add("Zelda");
            Metroid = Add("Metroid");
            // In the order they were played, as the app records them.
            Play(Metroid, Now.AddDays(-8), 10); // last week
            Play(Zelda, Now.AddDays(-1), 30); // yesterday
            Play(Zelda, Now.AddHours(-5), 45); // today, 13:00
            Play(Metroid, Now.AddHours(-2), 20); // today, 16:00
        }

        public GameLibrary Library { get; }
        public long Zelda { get; }
        public long Metroid { get; }

        public ActivityViewModel Create() => new(Library, new FixedClock(Now), Zone, DayOfWeek.Monday);

        private long Add(string title)
        {
            Library.AddGames([new Game { SystemId = "nes", Path = $"/g/{title}.nes", Title = title, DateAdded = Now.AddDays(-30) }]);
            return Library.GetGames().Single(game => game.Title == title).Id;
        }

        private void Play(long gameId, DateTimeOffset startedAt, int minutes)
        {
            var id = Library.RecordLaunchStarted(gameId, startedAt)!.Value;
            Library.CompletePlaySession(id, TimeSpan.FromMinutes(minutes), PlaySessionMeasurement.TrackedProcess);
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        }

        private sealed class FixedClock(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    private static async Task<(FakeLibrary Library, HandheldShellViewModel Shell, HandheldActivityPanel Panel)> OpenActivityAsync(PlayLog log)
    {
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        library.ActivityFactory = log.Create;
        var shell = await CreateAsync(library);
        // ps2, Recent, Settings, then Activity beside it.
        FocusHome(shell, 3);
        Assert.Equal(HandheldStageKind.ActivityTile, shell.Stage.Kind);
        Assert.Equal("Activity", shell.Stage.Title);

        Press(shell, GamepadAction.Confirm);

        var panel = Assert.IsType<HandheldActivityPanel>(shell.Panel);
        await WaitUntil(() => !panel.Activity.IsLoading && panel.Rows.Count > 0);
        return (library, shell, panel);
    }

    [Fact]
    public async Task TheActivityTile_OpensTheWeek_WithTheFocusedDayOnTheStage()
    {
        using var log = new PlayLog();
        var (library, shell, panel) = await OpenActivityAsync(log);

        // The Launcher: this week's sessions, newest first, each with its day; the chips say which list this is.
        Assert.Equal("Activity", panel.Heading);
        Assert.Equal("This week", panel.Subheading);
        Assert.Equal(["Metroid", "Zelda", "Zelda"], panel.Rows.Select(row => row.Label));
        Assert.Equal(["20m", "45m", "30m"], panel.Rows.Select(row => row.Value));
        Assert.StartsWith("Today · ", panel.Rows[0].Detail, StringComparison.Ordinal);
        Assert.StartsWith("Yesterday · ", panel.Rows[2].Detail, StringComparison.Ordinal);
        Assert.Equal(["Timeline", "Ranking"], panel.Chips.Select(chip => chip.Label));
        Assert.True(panel.Chips[0].IsSelected);
        Assert.StartsWith("1h 35m played", panel.Status, StringComparison.Ordinal);

        // The Stage: the focused session's day, its total and what was played.
        Assert.Equal(HandheldStageKind.Panel, shell.Stage.Kind);
        var card = shell.Stage.PanelCard!;
        Assert.Equal("Today", card.Title);
        Assert.Equal("1h 5m · 2 sessions", card.Value);
        Assert.Equal("Zelda 45m · Metroid 20m", card.Description);
        Assert.StartsWith("Metroid at ", card.Status, StringComparison.Ordinal);
        Assert.Equal(HandheldGlyph.Activity, card.Glyph);
        Assert.Equal(["A", "Y", "RB", "B"], shell.Hints.Select(hint => hint.Button));

        // Down to yesterday: the card follows the day.
        Press(shell, GamepadAction.NavigateDown, 2);
        Assert.Equal(2, panel.FocusIndex);
        Assert.Equal("Yesterday", shell.Stage.PanelCard!.Title);
        Assert.Equal("30m · 1 session", shell.Stage.PanelCard.Value);

        // Left goes back a week.
        Press(shell, GamepadAction.NavigateLeft);
        await WaitUntil(() => panel.Subheading == "Last week" && panel.Rows.Count == 1);
        Assert.Equal("Metroid", panel.Rows[0].Label);

        // B closes it back onto home, and the library lets go of it.
        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.Panel);
        Assert.Equal(HandheldStageKind.ActivityTile, shell.Stage.Kind);
        Assert.Same(panel.Activity, Assert.Single(library.ActivitiesClosed));
    }

    [Fact]
    public async Task TheRanking_ShowsAGamesStats_AndAOpensItsPage()
    {
        using var log = new PlayLog();
        var (_, shell, panel) = await OpenActivityAsync(log);

        Press(shell, GamepadAction.NextPlatform);
        await WaitUntil(() => panel.Activity.IsRanking && !panel.Activity.IsLoading);

        Assert.True(panel.Chips[1].IsSelected);
        Assert.Equal(["1. Zelda", "2. Metroid"], panel.Rows.Select(row => row.Label));
        var card = shell.Stage.PanelCard!;
        Assert.Equal("#1 by total time", card.Eyebrow);
        Assert.Equal("Zelda", card.Title);
        Assert.Equal("1h 15m", card.Value);
        Assert.Equal("1h 15m in all · played 2 times · 37m a session", card.Description);
        Assert.StartsWith("Last played ", card.Status, StringComparison.Ordinal);

        // A opens the game's page: its sessions on the Launcher, its stats (first played among them) on the Stage.
        Press(shell, GamepadAction.Confirm);
        await WaitUntil(() => panel.Activity.IsGamePage && !panel.Activity.IsLoading);
        Assert.Equal("Zelda", panel.Heading);
        Assert.Equal(2, panel.Rows.Count);
        Assert.True(panel.Chips[1].IsSelected);
        card = shell.Stage.PanelCard!;
        Assert.Equal("Zelda", card.Title);
        Assert.Equal("1h 15m", card.Value);
        Assert.Contains("Played 2 times · 37m a session", card.Description, StringComparison.Ordinal);
        Assert.Contains("First played 11 Nov 2026", card.Description, StringComparison.Ordinal);

        // B goes back to the ranking, then closes.
        Press(shell, GamepadAction.Cancel);
        await WaitUntil(() => panel.Activity.IsRanking && !panel.Activity.IsLoading);
        Assert.NotNull(shell.Panel);
        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.Panel);
    }

    [Fact]
    public async Task YEditsASession_SaveAndDeleteAreRows_AndTheChangeReachesTheLibraryOnClose()
    {
        using var log = new PlayLog();
        var (library, shell, panel) = await OpenActivityAsync(log);

        Press(shell, GamepadAction.Actions);
        Assert.True(panel.Activity.IsEditing);
        Assert.Equal(["Save", "Delete session", "Cancel"], panel.Rows.Select(row => row.Label));
        Assert.Equal("20m", panel.Rows[0].Value);
        Assert.Equal("Edit session", shell.Stage.PanelCard!.Eyebrow);

        // Right adds a minute, RB ten; the row and the card follow.
        Press(shell, GamepadAction.NavigateRight);
        Press(shell, GamepadAction.NextPlatform);
        Assert.Equal("31m", panel.Rows[0].Value);
        Assert.Equal("31m", shell.Stage.PanelCard!.Value);

        // Delete asks first; the card warns.
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.Confirm);
        Assert.Equal("Press A again to delete", panel.Rows[1].Label);
        Assert.True(shell.Stage.PanelCard!.IsWarning);
        Press(shell, GamepadAction.Cancel);
        Assert.Equal("Delete session", panel.Rows[1].Label);

        // Save writes it; the list comes back with the new length.
        Press(shell, GamepadAction.NavigateUp);
        Press(shell, GamepadAction.Confirm);
        await WaitUntil(() => !panel.Activity.IsEditing && !panel.Activity.IsLoading && panel.Rows.Count == 3 && panel.Rows[0].Value == "31m");
        Assert.Equal("edited", panel.Rows[0].Detail!.Split(" · ")[^1]);
        Assert.True(panel.Activity.HasChanges);

        Press(shell, GamepadAction.Cancel);
        Assert.True(Assert.Single(library.ActivitiesClosed).HasChanges);
    }

    [Fact]
    public async Task ATap_SelectsARow_AndASecondTapOpensIt()
    {
        using var log = new PlayLog();
        var (_, shell, panel) = await OpenActivityAsync(log);

        panel.TapRow(panel.Rows[1]);
        Assert.Equal(1, panel.FocusIndex);
        Assert.False(panel.Activity.IsGamePage);

        panel.TapRow(panel.Rows[1]);
        await WaitUntil(() => panel.Activity.IsGamePage && !panel.Activity.IsLoading);
        Assert.Equal("Zelda", panel.Heading);

        panel.TapChip(panel.Chips[0]);
        await WaitUntil(() => panel.Activity.IsTimeline && !panel.Activity.IsLoading);
        Assert.Equal(3, panel.Rows.Count);
        _ = shell;
    }

    [Fact]
    public async Task TheStartMenu_AndAGamesPlayHistory_OpenTheLogToo()
    {
        using var log = new PlayLog();
        var library = new FakeLibrary().WithPlatform("ps2", 2);
        // The folder's first game is the log's Zelda.
        library.Games["ps2"][0] = library.Games["ps2"][0] with { Id = log.Zelda, PlayCount = 2, Playtime = TimeSpan.FromMinutes(75) };
        library.ActivityFactory = log.Create;
        var shell = await CreateAsync(library);

        Press(shell, GamepadAction.Menu);
        var activity = shell.MenuItems.Single(item => item.Label == "Activity");
        Press(shell, GamepadAction.NavigateDown, shell.MenuItems.IndexOf(activity));
        Press(shell, GamepadAction.Confirm);
        await Task.Yield();
        Assert.IsType<HandheldActivityPanel>(shell.Panel);
        Press(shell, GamepadAction.Cancel);

        // Y on a game → Play history: the log opens on that game's page, and B there closes it.
        Press(shell, GamepadAction.Confirm); // open the ps2 folder
        Press(shell, GamepadAction.Actions);
        Assert.Equal("1 h 15 min played", shell.Sheet!.Items.Single(item => item.Label == "Play history").Note);
        RunSheetRow(shell, "Play history");
        var panel = Assert.IsType<HandheldActivityPanel>(shell.Panel);
        await WaitUntil(() => panel.Activity.IsGamePage && !panel.Activity.IsLoading);
        Assert.Equal("Zelda", panel.Heading);
        Assert.Equal("Play history", panel.Subheading);
        Assert.Equal(["Y", "B"], shell.Hints.Select(hint => hint.Button));
        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.Panel);
        Assert.True(shell.IsFolderOpen);
    }

    [Fact]
    public async Task WithoutAPlayLog_TheTileDoesNothing()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2));
        FocusHome(shell, 3);

        Press(shell, GamepadAction.Confirm);

        Assert.Null(shell.Panel);
        Assert.Equal(HandheldStageKind.ActivityTile, shell.Stage.Kind);
    }

    // ---- The Title page (M46 P4) ----

    [Fact]
    public async Task TitlePage_AddsFirstPlayed_AndTheAveragePerSession()
    {
        var first = DateTimeOffset.Parse("2026-03-04T12:00:00Z");
        var details = Details() with { FirstPlayedAt = first };
        var library = new FakeLibrary().WithPlatform("gc", 1);
        library.Games["gc"][0] = library.Games["gc"][0] with
        {
            PlayCount = 4, Playtime = TimeSpan.FromMinutes(130), LastPlayedAt = DateTimeOffset.Now.AddDays(-1),
        };
        library.Details[library.Games["gc"][0].Id] = details;
        var shell = await CreateAsync(library);
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.Confirm);
        await WaitUntil(() => shell.TitlePage?.Details is not null);
        var page = shell.TitlePage!;

        Assert.True(page.HasPlayed);
        Assert.Equal(HandheldText.Date(first), page.FirstPlayedText);
        Assert.Equal("32 min", page.AverageSessionText);

        // Earlier play than the log: said so, not invented.
        page.Details = details with { FirstPlayedAt = null, PlayedBeforeLog = true };
        Assert.Equal("Before the play log", page.FirstPlayedText);
    }

    [Fact]
    public async Task TitlePage_ForAGameNeverPlayed_HidesThePlayLogFacts()
    {
        var (_, shell, _) = await OpenTitleAsync(Details());

        Assert.False(shell.TitlePage!.HasPlayed);
        Assert.Equal(string.Empty, shell.TitlePage.AverageSessionText);
    }
}
