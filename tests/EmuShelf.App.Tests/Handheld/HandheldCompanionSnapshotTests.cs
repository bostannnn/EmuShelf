using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.SecondScreen;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M45 H6b: the companion view hosted headlessly on both canvases (the bottom screen's and the 16:9 one, for a game on the
// bottom screen), since both Thor displays are FLAG_SECURE. Set EMUSHELF_SNAPSHOT_DIR to get the frames as PNGs.
public partial class HandheldShellViewModelTests
{
    // A flat picture with a band across it, standing in for a cover, a badge, an icon or a screenshot.
    private static Bitmap Picture(int width, int height, string colour, string band)
    {
        var target = new RenderTargetBitmap(new PixelSize(width, height));
        var face = new Grid
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(Color.Parse(colour)),
            Children =
            {
                new Border
                {
                    Height = height / 4.0,
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Color.Parse(band)),
                },
            },
        };
        face.Measure(new Size(width, height));
        face.Arrange(new Rect(0, 0, width, height));
        target.Render(face);
        return target;
    }

    private static string SavePicture(string directory, string name, int width, int height, string colour, string band)
    {
        var path = Path.Combine(directory, name + ".png");
        using var picture = Picture(width, height, colour, band);
        using var stream = File.Create(path);
        picture.Save(stream, PngBitmapEncoderOptions.Default);
        return path;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheCompanion_DrawsItsBodyAndLayers_OnBothScreensCanvases(bool wide)
    {
        var media = Directory.CreateTempSubdirectory("emushelf-companion-");
        try
        {
            var model = CompanionModel();
            var colours = new[] { "#D9573B", "#3B8ED9", "#4CB36B", "#B34CA8", "#E0B03A" };
            foreach (var (row, index) in model.Achievements.Select((row, index) => (row, index)))
                row.Badge = Picture(64, 64, colours[index % colours.Length], "#33FFFFFF");
            model.SetMedia(new CompanionMediaSet(1, "Ico", [
                new CompanionMediaItem("Screenshot", GameMediaKind.Screenshot,
                    SavePicture(media.FullName, "shot-0", 960, 540, "#2E5E8C", "#7FB2D9")),
                new CompanionMediaItem("Screenshot", GameMediaKind.Screenshot,
                    SavePicture(media.FullName, "shot-1", 960, 540, "#8C5E2E", "#D9B27F")),
                new CompanionMediaItem("Gameplay video", GameMediaKind.Video, null),
            ]));
            model.Dock[0].Icon = Picture(96, 96, "#3B8ED9", "#FFFFFF");
            model.Dock[2].Icon = Picture(96, 96, "#D9573B", "#FFFFFF");
            model.CanRefresh = true;
            model.CanFetchMedia = true;
            using var cover = Picture(200, 284, "#4A3B7A", "#E0B03A");
            var companion = new HandheldCompanionViewModel(model, (_, _) => Task.FromResult<Bitmap?>(Picture(200, 284, "#4A3B7A", "#E0B03A")))
            {
                IsOnWideScreen = wide,
            };
            companion.SetNowPlaying(new HandheldGameEntry(1, "Ico", "ps2", "ps2", null, 3, TimeSpan.FromMinutes(95), true,
                AchievementText: "8 / 20"), "PlayStation 2");
            var view = new HandheldCompanionView(wide) { DataContext = companion };
            var width = wide ? 1920 : 1240;
            var window = new Window { Content = view, Width = width, Height = 1080 };
            window.Show();
            var name = wide ? "wide" : "narrow";
            try
            {
                model.IsGameRunning = true;
                model.Overlay = SecondScreenOverlayKind.Achievements;
                await SettleAsync();
                Assert.True(view.FindControl<Grid>("Body")!.IsVisible);
                Assert.Equal(width, view.FindControl<Grid>("Canvas")!.Width);
                Dump(window, $"h6b-{name}-0-achievements");

                Press(companion, GamepadAction.NavigateDown);
                Press(companion, GamepadAction.NavigateRight, 2);
                await SettleAsync();
                Dump(window, $"h6b-{name}-1-dock");

                Press(companion, GamepadAction.NavigateUp);
                Press(companion, GamepadAction.NextPlatform);
                await SettleAsync();
                await Task.Delay(100, TestContext.Current.CancellationToken);
                await SettleAsync();
                Assert.True(companion.HasPanelPhoto);
                Dump(window, $"h6b-{name}-2-photos");

                Press(companion, GamepadAction.PreviousPlatform);
                Press(companion, GamepadAction.Confirm);
                Press(companion, GamepadAction.NavigateRight, 2);
                Press(companion, GamepadAction.NavigateDown);
                await SettleAsync();
                Assert.True(view.FindControl<Grid>("GridLayer")!.IsVisible);
                Dump(window, $"h6b-{name}-3-badges");
                Press(companion, GamepadAction.NextPlatform);
                await SettleAsync();
                Dump(window, $"h6b-{name}-4-badges-locked");
                Press(companion, GamepadAction.Cancel);

                model.DrawerTitle = "All apps";
                model.Overlay = SecondScreenOverlayKind.Drawer;
                Press(companion, GamepadAction.NavigateRight);
                await SettleAsync();
                Assert.True(view.FindControl<Grid>("DrawerLayer")!.IsVisible);
                Dump(window, $"h6b-{name}-5-drawer");
                Press(companion, GamepadAction.Cancel);
                model.Overlay = SecondScreenOverlayKind.Achievements;

                companion.Rest();
                await SettleAsync();
                await Task.Delay(700, TestContext.Current.CancellationToken);
                await SettleAsync();
                Dump(window, $"h6b-{name}-6-resting");
                Press(companion, GamepadAction.Confirm);

                // Over the Launcher: All apps in the Launcher's light look.
                model.IsGameRunning = false;
                model.Overlay = SecondScreenOverlayKind.Drawer;
                await SettleAsync();
                Assert.False(view.FindControl<Grid>("Body")!.IsVisible);
                Dump(window, $"h6b-{name}-7-drawer-light");
            }
            finally
            {
                window.Close();
                companion.Dispose();
            }
        }
        finally
        {
            media.Delete(recursive: true);
        }
    }
}
