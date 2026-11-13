using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M46 P4: the Activity tile, the play log across both screens, and the Title page's new facts, hosted headlessly on both
// canvases. Set EMUSHELF_SNAPSHOT_DIR to get the frames as PNGs.
public partial class HandheldShellViewModelTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivityAndItsTile_DrawOnBothScreensCanvases(bool wideLauncher)
    {
        using var log = new PlayLog();
        var library = new FakeLibrary().WithPlatform("ps2", 4).WithPlatform("gc", 2);
        library.ActivityFactory = log.Create;
        var shell = new HandheldShellViewModel(library);
        await shell.InitializeAsync(TestContext.Current.CancellationToken);
        var launcher = new HandheldLauncherView(null, pinnedWide: wideLauncher) { DataContext = shell };
        var stage = new HandheldStageView(null, pinnedNarrow: wideLauncher) { DataContext = shell };
        var (launcherWidth, stageWidth) = wideLauncher ? (1920, 1240) : (1240, 1920);
        var launcherWindow = new Window { Content = launcher, Width = launcherWidth, Height = 1080 };
        var stageWindow = new Window { Content = stage, Width = stageWidth, Height = 1080 };
        launcherWindow.Show();
        stageWindow.Show();
        var name = wideLauncher ? "wide" : "narrow";
        try
        {
            // ps2, gc, Recent, Settings, then Activity beside it.
            FocusHome(shell, 4);
            await SettleAsync();
            Assert.True(shell.CurrentPage!.Slots[4].IsActivity);
            Dump(launcherWindow, $"p4-{name}-0-tile-launcher");
            Dump(stageWindow, $"p4-{name}-0-tile-stage");

            Press(shell, GamepadAction.Confirm);
            var panel = Assert.IsType<HandheldActivityPanel>(shell.Panel);
            await WaitUntil(() => !panel.Activity.IsLoading && panel.Rows.Count > 0);
            await SettleAsync();
            Assert.True(launcher.FindControl<Border>("PanelLayer")!.IsVisible);
            Dump(launcherWindow, $"p4-{name}-1-week-launcher");
            Dump(stageWindow, $"p4-{name}-1-week-stage");

            Press(shell, GamepadAction.NextPlatform);
            await WaitUntil(() => panel.Activity.IsRanking && !panel.Activity.IsLoading);
            await SettleAsync();
            Dump(launcherWindow, $"p4-{name}-2-ranking-launcher");
            Dump(stageWindow, $"p4-{name}-2-ranking-stage");

            Press(shell, GamepadAction.Confirm);
            await WaitUntil(() => panel.Activity.IsGamePage && !panel.Activity.IsLoading);
            await SettleAsync();
            Dump(launcherWindow, $"p4-{name}-3-game-launcher");
            Dump(stageWindow, $"p4-{name}-3-game-stage");

            Press(shell, GamepadAction.Actions);
            await SettleAsync();
            Assert.True(panel.Activity.IsEditing);
            Dump(launcherWindow, $"p4-{name}-4-edit-launcher");
            Dump(stageWindow, $"p4-{name}-4-edit-stage");
            Press(shell, GamepadAction.Cancel);
            Press(shell, GamepadAction.Cancel);
            await WaitUntil(() => panel.Activity.IsRanking && !panel.Activity.IsLoading);
            Press(shell, GamepadAction.Cancel);
            await SettleAsync();
            Assert.False(launcher.FindControl<Border>("PanelLayer")!.IsVisible);
        }
        finally
        {
            launcherWindow.Close();
            stageWindow.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheTitlePage_DrawsFirstPlayedAndPerSession()
    {
        var library = new FakeLibrary().WithPlatform("gc", 2);
        library.Games["gc"][0] = library.Games["gc"][0] with
        {
            Title = "The Legend of Zelda: The Wind Waker", PlayCount = 7, Playtime = TimeSpan.FromMinutes(745),
            LastPlayedAt = DateTimeOffset.Now.AddDays(-2),
        };
        library.Details[library.Games["gc"][0].Id] = Details(discs: 2) with { FirstPlayedAt = DateTimeOffset.Parse("2026-09-14T19:00:00Z") };
        var shell = await CreateAsync(library);
        var view = new HandheldLauncherView { DataContext = shell };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            await WaitUntil(() => shell.TitlePage?.Details is not null);
            await SettleAsync();
            Assert.Equal("1 h 46 min", shell.TitlePage!.AverageSessionText);
            Dump(window, "p4-title-facts");
        }
        finally
        {
            window.Close();
        }
    }
}
