using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Achievements;
using Xunit;

namespace EmuShelf.App.Tests;

/// <summary>
/// Renders the Thor companion's achievements panel at the real Screen-2 size (1240×1080 px at density 369 →
/// 538×468 dip) and dumps PNGs when <c>EMUSHELF_SNAPSHOT_DIR</c> is set, so the panel can be eyeballed
/// without the device. Point <c>EMUSHELF_BADGE_DIR</c> at a folder of real RetroAchievements badge PNGs to
/// render with them; otherwise generated colour swatches stand in.
/// </summary>
public class SecondScreenAchievementsSnapshotTests
{
    private const double ThorWidth = 538;
    private const double ThorHeight = 468;

    private sealed class FolderBadgeCache(IReadOnlyList<string> files) : IRetroAchievementsBadgeCache
    {
        public Task<string?> GetBadgePathAsync(string badgeName, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(files[int.Parse(badgeName) % files.Count]);
    }

    private static IReadOnlyList<string> BadgeFiles()
    {
        var folder = Environment.GetEnvironmentVariable("EMUSHELF_BADGE_DIR");
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            var real = Directory.GetFiles(folder, "*.png").OrderBy(path => path, StringComparer.Ordinal).ToList();
            if (real.Count > 0)
                return real;
        }

        var generated = Path.Combine(Path.GetTempPath(), "emushelf-badge-swatches");
        Directory.CreateDirectory(generated);
        var files = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var path = Path.Combine(generated, $"{i:D2}.png");
            if (!File.Exists(path))
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(64, 64));
                using (var context = bitmap.CreateDrawingContext())
                {
                    context.FillRectangle(new SolidColorBrush(Color.FromRgb((byte)(40 + i * 17), 90, (byte)(200 - i * 13))), new Rect(0, 0, 64, 64));
                    context.FillRectangle(Brushes.Goldenrod, new Rect(8, 46, 48, 10));
                }
                bitmap.Save(path);
            }
            files.Add(path);
        }
        return files;
    }

    private static Bitmap Backdrop()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(620, 540));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#3B5B8C"), 0), new GradientStop(Color.Parse("#C9A645"), 1) },
                },
                new Rect(0, 0, 620, 540));
        }
        return bitmap;
    }

    private static Bitmap Logo()
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(360, 120));
        using (var context = bitmap.CreateDrawingContext())
            context.FillRectangle(Brushes.Crimson, new Rect(0, 0, 360, 120));
        return bitmap;
    }

    internal static SecondScreenViewModel BuildModel(int count, IRetroAchievementsBadgeCache? badges)
    {
        var model = new SecondScreenViewModel
        {
            AchievementsTitle = "007 : Everything or Nothing",
            AchievementsSummary = "2 / 142 · 10 pts",
            CanRefresh = true,
        };
        model.SetSpotlight(Backdrop(), Logo());
        model.FanartOpacity = 1;
        model.LogoOpacity = 1;
        model.SetAchievements(Enumerable.Range(1, count).Select(i => new AchievementRowViewModel(
            new RetroAchievementsAchievement(
                i,
                i == 1 ? "What a Mess We Made When it All Went Wrong" : $"Achievement {i}",
                i == 1 ? "Ground Zero: Retrieve the nuclear device and escape!" : "Complete the level on Agent.",
                Points: 5,
                BadgeName: i.ToString(),
                DisplayOrder: i,
                DateEarned: i is 1 or 34 ? DateTimeOffset.UtcNow : null,
                DateEarnedHardcore: i is 1 or 34 ? DateTimeOffset.UtcNow : null),
            badges,
            loadBadge: false)).ToList());
        return model;
    }

    private static async Task PumpAsync(int frames = 6)
    {
        for (var i = 0; i < frames; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    // Lets transitions, async badge decodes, and the column re-slice settle before a capture.
    private static async Task SettleAsync()
    {
        for (var i = 0; i < 12; i++)
        {
            await PumpAsync(2);
            await Task.Delay(40);
        }
        await PumpAsync();
    }

    private static void Dump(Window window, string name)
    {
        var output = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
        if (string.IsNullOrEmpty(output))
            return;
        Directory.CreateDirectory(output);
        using (window.CaptureRenderedFrame()) { }
        using var frame = window.CaptureRenderedFrame();
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        frame!.Save(stream, PngBitmapEncoderOptions.Default);
    }

    [AvaloniaFact]
    public async Task ThorPanel_DumpsRestingOpenAndScrolledStates()
    {
        var previousTheme = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
        var model = BuildModel(142, new FolderBadgeCache(BadgeFiles()));
        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = ThorWidth, Height = ThorHeight };
        window.Show();
        try
        {
            await SettleAsync();
            Dump(window, "ss-ach-0-resting");

            model.Overlay = SecondScreenOverlayKind.Achievements;
            await SettleAsync();
            Dump(window, "ss-ach-1-open");

            var list = view.FindControl<ListBox>("AchievementsBadgeList")!;
            Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(list).OfType<ScrollViewer>().First().Offset = new Vector(0, 900);
            await SettleAsync();
            Dump(window, "ss-ach-2-scrolled");

            model.AchievementsStatus = "Offline — showing cached achievement details.";
            await SettleAsync();
            Dump(window, "ss-ach-3-status");
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = previousTheme;
        }
    }
}
