using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M45 H6c: the real Launcher and Stage hosted headlessly with the Y sheet, the panels (Edit title, Search, the scraper, the
// batch scraper, achievements) and a failure's toast, on both screens' canvases (the 1240-wide one and the 16:9 one), since
// both Thor displays are FLAG_SECURE. Set EMUSHELF_SNAPSHOT_DIR to get the frames as PNGs.
public partial class HandheldShellViewModelTests
{
    // A cover in one of a few colours, so the fan and the cards read as covers in the frames.
    private static Bitmap MakeCover(HandheldGameEntry game)
    {
        var colours = new[] { unchecked((int)0xFFD96F3B), unchecked((int)0xFF3B57D9), unchecked((int)0xFF79BD3B),
            unchecked((int)0xFFD93BB4), unchecked((int)0xFF3BB2D9) };
        var colour = colours[(int)(game.Id % colours.Length)];
        var bitmap = new WriteableBitmap(new PixelSize(60, 84), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var row = new int[buffer.RowBytes / 4];
            for (var y = 0; y < 84; y++)
            {
                Array.Fill(row, y < 22 ? unchecked((int)0xFFF4F4F4) : colour);
                System.Runtime.InteropServices.Marshal.Copy(row, 0, buffer.Address + (y * buffer.RowBytes), row.Length);
            }
        }
        return bitmap;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheSheetsPanelsAndToasts_DrawOnBothScreensCanvases(bool wideLauncher)
    {
        var library = new FakeLibrary().WithPlatform("ps2", 14).WithPlatform("gc", 3);
        library.Games["ps2"][0] = library.Games["ps2"][0] with { Title = "Okami", AchievementText = "1/3" };
        library.CoverMaker = MakeCover;
        library.AchievementsFactory = _ => Achievements(new FakeAchievements());
        library.ScraperFactory = _ => Scraper(new StubScreenScraperPreviewService(ScraperFixtures.ReadyPreview()));
        library.BatchFactory = (ids, name) => new GamepadBatchScraperViewModel(new GameBatchScraperViewModel(ids, name, new FakeBatch(),
            new ScreenScraperSettings { Enabled = true }));
        var shell = await CreateAsync(library);
        shell.SearchDebounce = TimeSpan.Zero;
        var launcher = new HandheldLauncherView(null, pinnedWide: wideLauncher) { DataContext = shell };
        var stage = new HandheldStageView(null, pinnedNarrow: wideLauncher) { DataContext = shell };
        var (launcherWidth, stageWidth) = wideLauncher ? (1920, 1240) : (1240, 1920);
        var launcherWindow = new Window { Content = launcher, Width = launcherWidth, Height = 1080 };
        var stageWindow = new Window { Content = stage, Width = stageWidth, Height = 1080 };
        launcherWindow.Show();
        stageWindow.Show();
        var name = wideLauncher ? "wide" : "narrow";
        void Frame(string what)
        {
            Dump(launcherWindow, $"h6c-{name}-{what}-launcher");
            Dump(stageWindow, $"h6c-{name}-{what}-stage");
        }
        try
        {
            // Y on a game in its folder: every action, scrolled to its last row.
            Press(shell, GamepadAction.Confirm);
            await SettleAsync();
            Press(shell, GamepadAction.Actions);
            await SettleAsync();
            Frame("0-sheet");
            Press(shell, GamepadAction.NavigateDown, shell.Sheet!.Items.Count - 1);
            await SettleAsync();
            Assert.True(shell.Sheet.Items[^1].IsFocused);
            Frame("1-sheet-end");
            Press(shell, GamepadAction.Confirm);
            await SettleAsync();
            Frame("2-remove-confirm");
            RunSheetRow(shell, "Keep it");

            // Edit title.
            Press(shell, GamepadAction.Actions);
            RunSheetRow(shell, "Edit title");
            await SettleAsync();
            Assert.True(launcher.FindControl<Border>("PanelLayer")!.IsVisible);
            Assert.Equal(launcherWidth, launcher.FindControl<Grid>("PanelCanvas")!.Width);
            Frame("3-edit-title");
            shell.TapPanelBack();
            Assert.Null(shell.Panel);
            await SettleAsync();

            // The scraper, its field rows, a field focused.
            Press(shell, GamepadAction.Actions);
            RunSheetRow(shell, "Scrape with ScreenScraper");
            await SettleAsync();
            Frame("4-scraper-apply");
            var scraper = Assert.IsType<HandheldScraperPanel>(shell.Panel);
            while (scraper.FocusedRow!.Source is not ScraperFieldRowViewModel)
                Press(shell, GamepadAction.NavigateUp);
            await SettleAsync();
            Frame("5-scraper-field");
            Press(shell, GamepadAction.Cancel);

            // Achievements, from the Title page.
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.NavigateRight);
            Press(shell, GamepadAction.Confirm);
            await SettleAsync();
            Assert.IsType<HandheldAchievementsPanel>(shell.Panel);
            Frame("6-achievements");
            Press(shell, GamepadAction.NextPlatform);
            Press(shell, GamepadAction.NavigateDown);
            await SettleAsync();
            Frame("7-achievements-locked");
            Press(shell, GamepadAction.Cancel);
            Press(shell, GamepadAction.Cancel);
            Press(shell, GamepadAction.Cancel); // close the folder
            await SettleAsync();

            // Scrape all in a folder, before and after.
            Press(shell, GamepadAction.Actions);
            RunSheetRow(shell, "Scrape all in this folder");
            await SettleAsync();
            Frame("8-batch");
            var batch = Assert.IsType<HandheldBatchScraperPanel>(shell.Panel);
            Press(shell, GamepadAction.Confirm);
            for (var i = 0; i < 100 && !batch.Details.Batch.IsDone; i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            await SettleAsync();
            Frame("9-batch-done");
            Press(shell, GamepadAction.Confirm);

            // Search: the keyboard and the fan, then the matches as a folder.
            RunMenuRow(shell, "Search");
            var search = Assert.IsType<HandheldSearchPanel>(shell.Panel);
            search.Keyboard!.Text = "ps2 game 00";
            await search.Pending;
            await SettleAsync();
            Frame("10-search");
            Press(shell, GamepadAction.Search);
            await search.Pending;
            await SettleAsync();
            Assert.True(shell.OpenFolder!.Source.IsSearch);
            Frame("11-search-folder");

            // A failure, as the launch core reports it.
            shell.ShowStatus("Could not start PCSX2: the emulator is not installed. Choose one in Settings → Emulators.",
                StatusSeverity.Error);
            await SettleAsync();
            Frame("12-error-toast");
        }
        finally
        {
            launcherWindow.Close();
            stageWindow.Close();
        }
    }
}
