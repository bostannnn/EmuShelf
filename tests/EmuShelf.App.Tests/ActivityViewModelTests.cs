using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Library;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.App.Tests;

public sealed class ActivityViewModelTests : IDisposable
{
    // Thursday 12 November 2026, 18:00 in UTC+3.
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T18:00:00+03:00");
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");

    private readonly string _baseDirectory = Path.Combine(Path.GetTempPath(), "EmuShelfActivityTests", Guid.NewGuid().ToString("N"));
    private readonly GameLibrary _library;

    public ActivityViewModelTests()
    {
        var paths = new AppPaths(_baseDirectory);
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        _library = new GameLibrary(database, new RelativePathResolver(paths));
    }

    public void Dispose()
    {
        try { Directory.Delete(_baseDirectory, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }

    private ActivityViewModel CreateViewModel() => new(_library, new FixedClock(Now), Zone, DayOfWeek.Monday);

    private long AddGame(string title)
    {
        _library.AddGames([new Game { SystemId = "nes", Path = $"/g/{title}.nes", Title = title, DateAdded = Now }]);
        return _library.GetGames().Single(game => game.Title == title).Id;
    }

    private void Play(long gameId, DateTimeOffset startedAt, int minutes)
    {
        var id = _library.RecordLaunchStarted(gameId, startedAt)!.Value;
        _library.CompletePlaySession(id, TimeSpan.FromMinutes(minutes), PlaySessionMeasurement.TrackedProcess);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task Timeline_ShowsThisWeeksDays_AndLeftGoesBackAWeek()
    {
        var zelda = AddGame("Zelda");
        var metroid = AddGame("Metroid");
        Play(zelda, Now.AddHours(-2), 45); // today
        Play(metroid, Now.AddDays(-2), 20); // Tuesday
        Play(zelda, Now.AddDays(-7), 10); // last week
        var vm = CreateViewModel();

        await vm.LoadAsync();

        Assert.Equal("This week", vm.WeekLabel);
        Assert.Equal("1h 5m played", vm.WeekTotalText);
        Assert.Equal(["Today", "Tuesday 10 November"], vm.Days.Select(day => day.Label));
        Assert.False(vm.CanShowNewerWeek);
        Assert.True(vm.Days[0].Sessions[0].IsFocused);

        Assert.True(vm.Dispatch(GamepadAction.NavigateLeft));
        await WaitUntil(() => vm.WeekLabel == "Last week");
        Assert.Equal("10m played", vm.WeekTotalText);
        Assert.True(vm.CanShowNewerWeek);

        Assert.True(vm.Dispatch(GamepadAction.NavigateRight));
        await WaitUntil(() => vm.WeekLabel == "This week");
    }

    [Fact]
    public async Task Ranking_OpensAGamesPage_AndBBacksOutToTheRanking()
    {
        var zelda = AddGame("Zelda");
        var metroid = AddGame("Metroid");
        Play(zelda, Now.AddDays(-3), 30);
        Play(metroid, Now.AddDays(-1), 90);
        Play(metroid, Now.AddHours(-1), 30);
        var vm = CreateViewModel();
        await vm.LoadAsync();

        vm.Dispatch(GamepadAction.NextPlatform);
        await WaitUntil(() => vm.IsRanking);
        Assert.Equal(["Metroid", "Zelda"], vm.RankItems.Select(item => item.Title));
        Assert.Equal("2h", vm.RankItems[0].ValueText);

        vm.Dispatch(GamepadAction.Confirm);
        await WaitUntil(() => vm.IsGamePage);
        Assert.Equal("Metroid", vm.Title);
        Assert.Equal("2h", vm.GameTotalText);
        Assert.Equal("2 times", vm.GamePlaysText);
        Assert.Equal("1h", vm.GameAverageText);
        Assert.Equal("11 Nov 2026", vm.GameFirstPlayedText);
        Assert.Equal(2, vm.GameSessions.Count);

        Assert.True(vm.Dispatch(GamepadAction.Cancel));
        await WaitUntil(() => vm.IsRanking);
        Assert.False(vm.Dispatch(GamepadAction.Cancel));
    }

    [Fact]
    public async Task AGamePageOpenedDirectly_ClosesOnB_AndOneReachedFromTheTimelineGoesBack()
    {
        var zelda = AddGame("Zelda");
        Play(zelda, Now.AddHours(-1), 30);
        var direct = CreateViewModel();
        await direct.LoadAsync(zelda);
        Assert.True(direct.IsGamePage);
        Assert.False(direct.Dispatch(GamepadAction.Cancel));

        var browsed = CreateViewModel();
        await browsed.LoadAsync();
        browsed.Dispatch(GamepadAction.Confirm);
        await WaitUntil(() => browsed.IsGamePage);
        Assert.True(browsed.Dispatch(GamepadAction.Cancel));
        await WaitUntil(() => browsed.IsTimeline);
    }

    [Fact]
    public async Task YEditsTheFocusedSession_AndSavingUpdatesTheTotals()
    {
        var zelda = AddGame("Zelda");
        Play(zelda, Now.AddHours(-3), 40);
        var vm = CreateViewModel();
        await vm.LoadAsync(zelda);

        vm.Dispatch(GamepadAction.Actions);
        Assert.True(vm.IsEditing);
        Assert.Equal(40, vm.EditMinutes);
        vm.Dispatch(GamepadAction.NavigateRight);
        vm.Dispatch(GamepadAction.NextPlatform);
        vm.Dispatch(GamepadAction.NavigateLeft);
        Assert.Equal(50, vm.EditMinutes);
        vm.Dispatch(GamepadAction.Confirm); // Save is focused first

        await WaitUntil(() => !vm.IsEditing && vm.GameTotalText == "50m");
        Assert.True(vm.HasChanges);
        Assert.Equal("edited", vm.GameSessions.Single().NoteText);
        Assert.Equal(TimeSpan.FromMinutes(50), _library.GetGames().Single().Playtime);
    }

    [Fact]
    public async Task DeletingASession_AsksFirst_ThenTakesItOff()
    {
        var zelda = AddGame("Zelda");
        Play(zelda, Now.AddDays(-1), 25);
        Play(zelda, Now.AddHours(-1), 15);
        var vm = CreateViewModel();
        await vm.LoadAsync(zelda);

        vm.Dispatch(GamepadAction.Actions);
        vm.Dispatch(GamepadAction.NavigateDown); // Delete
        vm.Dispatch(GamepadAction.Confirm);
        Assert.True(vm.IsConfirmingDelete);
        vm.Dispatch(GamepadAction.Cancel); // keep it
        Assert.False(vm.IsConfirmingDelete);
        Assert.True(vm.IsEditing);
        vm.Dispatch(GamepadAction.Confirm);
        vm.Dispatch(GamepadAction.Confirm);

        await WaitUntil(() => vm.GameSessions.Count == 1);
        Assert.Equal("25m", vm.GameTotalText);
        Assert.Equal(1, _library.GetGames().Single().PlayCount);
    }

    [Fact]
    public async Task AGameWithPlayFromBeforeTheLog_SaysSo()
    {
        var zelda = AddGame("Zelda");
        _library.RecordLaunchStarted(zelda, Now.AddDays(-1));
        // A play recorded before v21 lives only in the totals.
        var database = new LibraryDatabase(new AppPaths(_baseDirectory));
        using (var connection = database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE Games SET PlaytimeSeconds = 7200, PlayCount = PlayCount + 3;";
            command.ExecuteNonQuery();
        }
        var vm = CreateViewModel();

        await vm.LoadAsync(zelda);

        Assert.Equal("Before the play log", vm.GameFirstPlayedText);
        Assert.True(vm.HasGameEarlierPlay);
        Assert.Equal("Includes 2h over 3 plays from before the play log", vm.GameEarlierText);
        Assert.Equal("?", vm.GameSessions.Single().LengthText);
        Assert.Equal("length unknown", vm.GameSessions.Single().NoteText);
    }
}
