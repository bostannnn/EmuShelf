using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Library;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.App.Tests;

/// <summary>
/// Renders the play log (M46 P3) in the Desktop window and the Gamepad overlay. With EMUSHELF_SNAPSHOT_DIR
/// set, every page is saved as a PNG for a look by eye.
/// </summary>
public sealed class ActivitySnapshotTests : IDisposable
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("UTC+3", TimeSpan.FromHours(3), "UTC+3", "UTC+3");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T18:00:00+03:00");
    private readonly string _baseDirectory = Path.Combine(Path.GetTempPath(), "EmuShelfActivitySnapshots", Guid.NewGuid().ToString("N"));
    private readonly GameLibrary _library;
    private readonly string? _outputDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");

    public ActivitySnapshotTests()
    {
        var paths = new AppPaths(_baseDirectory);
        paths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(paths);
        database.Initialize();
        _library = new GameLibrary(database, new RelativePathResolver(paths));
        Seed();
    }

    public void Dispose()
    {
        try { Directory.Delete(_baseDirectory, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }

    private void Seed()
    {
        var titles = new[] { "The Legend of Zelda: Ocarina of Time", "Metroid Prime", "Castlevania", "Luigi's Mansion", "Gran Turismo 4" };
        _library.AddGames(titles.Select((title, i) => new Game
        {
            SystemId = "nes", Path = $"/g/{i}.bin", Title = title, DateAdded = Now,
        }));
        var ids = _library.GetGames().ToDictionary(game => game.Title, game => game.Id);
        void Play(string title, double daysAgo, double hourOfDay, int minutes, PlaySessionMeasurement how = PlaySessionMeasurement.TrackedProcess)
        {
            var start = new DateTimeOffset(Now.Date.AddDays(-daysAgo).AddHours(hourOfDay), Now.Offset);
            var id = _library.RecordLaunchStarted(ids[title], start)!.Value;
            _library.CompletePlaySession(id, TimeSpan.FromMinutes(minutes), how);
        }
        Play("The Legend of Zelda: Ocarina of Time", 0, 16.5, 72);
        Play("Castlevania", 0, 13.2, 3, PlaySessionMeasurement.WallClockEstimate);
        Play("Metroid Prime", 1, 21, 95);
        Play("Luigi's Mansion", 2, 20.4, 29, PlaySessionMeasurement.UsageAccess);
        Play("The Legend of Zelda: Ocarina of Time", 2, 11, 40);
        Play("Gran Turismo 4", 3, 22, 55);
        _library.RecordLaunchStarted(ids["Metroid Prime"], new DateTimeOffset(Now.Date.AddDays(-3).AddHours(9), Now.Offset));
        Play("Metroid Prime", 9, 19, 120);
        var edited = _library.GetPlaySessions(ids["Gran Turismo 4"]).Single().Id;
        _library.EditPlaySession(edited, TimeSpan.FromMinutes(50));
    }

    private ActivityViewModel CreateViewModel() => new(_library, new FixedClock(Now), Zone, DayOfWeek.Monday);

    private static async Task PumpAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private async Task SaveAsync(Window window, string name)
    {
        await PumpAsync();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (_outputDirectory is null)
            return;
        Directory.CreateDirectory(_outputDirectory);
        using var output = File.Create(Path.Combine(_outputDirectory, name));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public async Task DesktopWindow_RendersEveryPage()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        var window = new ActivityWindow { DataContext = vm, Width = 760, Height = 640 };
        window.Show();
        try
        {
            await SaveAsync(window, "activity-desktop-timeline.png");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "This week");

            await vm.ShowRankingCommand.ExecuteAsync(null);
            await SaveAsync(window, "activity-desktop-ranking.png");

            await vm.OpenRowCommand.ExecuteAsync(vm.RankItems[0]);
            await SaveAsync(window, "activity-desktop-game.png");
            Assert.True(vm.IsGamePage);

            vm.EditSessionCommand.Execute(vm.GameSessions[0]);
            await SaveAsync(window, "activity-desktop-edit.png");
            Assert.True(vm.IsEditing);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Click(Window window, string content)
    {
        var button = window.GetVisualDescendants().OfType<Button>()
            .First(candidate => candidate.IsEffectivelyVisible && Equals(candidate.Content, content));
        Assert.True(button.Command!.CanExecute(button.CommandParameter));
        button.Command.Execute(button.CommandParameter);
    }

    [AvaloniaFact]
    public async Task DesktopButtons_PassRealNumbers_AndTheRowMenuOpensTheEditor()
    {
        var vm = CreateViewModel();
        await vm.LoadAsync();
        var window = new ActivityWindow { DataContext = vm, Width = 760, Height = 640 };
        window.Show();
        try
        {
            await PumpAsync();
            // The timeline row's context menu lives in a popup; its item must still reach the editor.
            var row = window.GetVisualDescendants().OfType<Button>()
                .First(candidate => candidate.DataContext is ActivitySessionViewModel);
            row.ContextMenu!.Open(row);
            await PumpAsync();
            var item = Assert.IsType<MenuItem>(row.ContextMenu.Items[0]);
            item.Command!.Execute(item.CommandParameter);
            row.ContextMenu.Close();
            await PumpAsync();
            Assert.True(vm.IsEditing);
            var before = vm.EditMinutes;

            Click(window, "+10");
            Click(window, "−1");
            Assert.Equal(before + 9, vm.EditMinutes);

            vm.CancelEditCommand.Execute(null);
            await vm.ShowRankingCommand.ExecuteAsync(null);
            await PumpAsync();
            Click(window, "›");
            await PumpAsync();
            for (var i = 0; i < 100 && vm.RankingMetric != PlayRankingMetric.TimesPlayed; i++)
                await Task.Delay(10);
            Assert.Equal(PlayRankingMetric.TimesPlayed, vm.RankingMetric);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task GamepadOverlay_RendersTheTimelineAndTheEditor()
    {
        var viewModel = new MainViewModel();
        await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
        viewModel.IsGamepadMode = true;
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            var activity = CreateViewModel();
            await activity.LoadAsync();
            viewModel.GamepadActivity = activity;
            viewModel.GamepadOverlay = GamepadOverlayKind.Activity;
            activity.Dispatch(GamepadAction.NavigateDown);
            await SaveAsync(window, "activity-gamepad-timeline.png");
            var view = window.GetVisualDescendants().OfType<ActivityView>().Single();
            Assert.True(view.IsEffectivelyVisible);

            activity.Dispatch(GamepadAction.Actions);
            await SaveAsync(window, "activity-gamepad-edit.png");
            Assert.True(activity.IsEditing);
        }
        finally
        {
            window.Close();
        }
    }
}
