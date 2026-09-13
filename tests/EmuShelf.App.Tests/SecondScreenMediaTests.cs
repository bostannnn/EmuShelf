using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.SecondScreen;

namespace EmuShelf.App.Tests;

public class SecondScreenMediaTests
{
    private static CompanionMediaSet Media(long id = 1) => new(id, "Game", [
        new("Screenshot", GameMediaKind.Screenshot, null), new("Video", GameMediaKind.Video, null)]);

    [AvaloniaFact]
    public void FanartWithoutLogoKeepsTheGameTitleVisible()
    {
        var vm = new SecondScreenViewModel();
        var art = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(8, 8),
            new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888);
        vm.SetSpotlight(art, null);
        Assert.True(vm.HasFanart);
        Assert.True(vm.ShowRestingBranding);
        vm.SetSpotlight(null, null);
    }

    [AvaloniaFact]
    public async Task GalleryFitsThorLogicalViewportAndKeepsVideoHostHiddenUntilPlay()
    {
        var vm = new SecondScreenViewModel { CanFetchMedia = true };
        vm.SetMedia(new(1, "Assassin's Creed Liberation", [new("Video", GameMediaKind.Video, null)]));
        vm.OpenMediaCommand.Execute(null);
        var view = new EmuShelf.App.Views.SecondScreenView { DataContext = vm };
        var window = new Avalonia.Controls.Window { Width = 538, Height = 468, Content = view };
        window.Show();
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render);
            var viewport = Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.Panel>(view, "MediaViewport")!;
            Assert.Equal(538, viewport.Bounds.Width, 1);
            Assert.Equal(468, viewport.Bounds.Height, 1);
            var dock = Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.Border>(view, "CompanionDock")!;
            Assert.False(dock.IsVisible);
            Assert.DoesNotContain(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<EmuShelf.App.Controls.CompanionVideoHost>(), host => host.IsEffectivelyVisible);
            using var frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
            var output = Environment.GetEnvironmentVariable("EMUSHELF_MEDIA_PREVIEW");
            if (!string.IsNullOrEmpty(output)) frame?.Save(output, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        }
        finally { window.Close(); vm.ClearMedia(); }
    }

    [AvaloniaFact]
    public void HorizontalSwipeOpensAndCyclesButVerticalAndSmallGesturesDoNot()
    {
        var vm = new SecondScreenViewModel();
        vm.SetMedia(Media());
        vm.SwipeMedia(10, 0);
        vm.SwipeMedia(50, 100);
        Assert.False(vm.IsMediaOpen);
        vm.SwipeMedia(-100, 0);
        Assert.True(vm.IsMediaOpen);
        Assert.StartsWith("1 / 2", vm.MediaCaption);
        vm.SwipeMedia(-100, 0);
        Assert.True(vm.IsSelectedVideo);
        vm.SwipeMedia(-100, 0);
        Assert.False(vm.IsSelectedVideo);
        vm.ClearMedia();
    }

    [AvaloniaFact]
    public void ViewerOwnsDisplayAndDownSwipeRestoresDock()
    {
        var vm = new SecondScreenViewModel();
        vm.SetMedia(Media());
        Assert.True(vm.IsDockVisible);
        vm.OpenMediaCommand.Execute(null);
        Assert.False(vm.IsDockVisible);
        vm.TapMedia();
        Assert.False(vm.IsMediaChromeVisible);
        vm.SwipeMedia(4, 100);
        Assert.False(vm.IsMediaOpen);
        Assert.True(vm.IsDockVisible);
        vm.OpenMediaCommand.Execute(null);
        Assert.True(vm.IsMediaChromeVisible);
        vm.ClearMedia();
    }

    [AvaloniaFact]
    public void BrowsingVideoDoesNotDownloadAndClosingStopsPlayback()
    {
        var loads = 0;
        var vm = new SecondScreenViewModel { ResolveMediaPath = (_, _) => { loads++; return Task.FromResult<string?>(null); } };
        vm.SetMedia(new(1, "Video game", [new("Video", GameMediaKind.Video, null)]));
        vm.OpenMediaCommand.Execute(null);
        Assert.Equal(0, loads);
        vm.PlayingVideoPath = "/cached.mp4";
        vm.CloseOverlayCommand.Execute(null);
        Assert.Null(vm.PlayingVideoPath);
        Assert.False(vm.IsMediaOpen);
    }

    [AvaloniaFact]
    public void VideoPosterUsesScreenshotWithoutRequestingVideo()
    {
        var requested = new List<CompanionMediaItem>();
        var vm = new SecondScreenViewModel { ResolveMediaPath = (item, _) => { requested.Add(item); return Task.FromResult<string?>(null); } };
        vm.SetMedia(Media());
        vm.OpenMediaCommand.Execute(null);
        vm.NextMediaCommand.Execute(null);
        Assert.True(vm.IsSelectedVideo);
        Assert.True(vm.ShowMediaPlay);
        Assert.Equal(2, requested.Count);
        Assert.All(requested, item => Assert.False(item.IsVideo));
        vm.ClearMedia();
    }

    [AvaloniaFact]
    public async Task ClosingViewerRejectsLateVideoCompletion()
    {
        var pending = new TaskCompletionSource<string?>();
        var vm = new SecondScreenViewModel { ResolveMediaPath = (_, _) => pending.Task };
        vm.SetMedia(new(1, "Video game", [new("Video", GameMediaKind.Video, null)]));
        vm.OpenMediaCommand.Execute(null);
        var play = vm.PlayMediaCommand.ExecuteAsync(null);
        vm.CloseOverlayCommand.Execute(null);
        pending.SetResult("/late.mp4");
        await play;
        Assert.Null(vm.PlayingVideoPath);
        Assert.Null(vm.MediaStatus);
    }

    [AvaloniaFact]
    public void GameChangesResetPageAndStopVideoWhileStandbyRespectsViewer()
    {
        var vm = new SecondScreenViewModel { IsGameRunning = true };
        vm.SetMedia(Media());
        vm.OpenMediaCommand.Execute(null);
        Assert.False(vm.IsStandby);
        vm.DispatchGamepadAction(GamepadAction.NavigateRight);
        vm.PlayingVideoPath = "/video.mp4";
        vm.SetMedia(Media(2));
        Assert.Null(vm.PlayingVideoPath);
        Assert.StartsWith("1 / 2", vm.MediaCaption);
        vm.CloseOverlayCommand.Execute(null);
        Assert.True(vm.IsStandby);
    }

    [AvaloniaFact]
    public void SwipingAchievementsDoesNotReplaceItsOverlay()
    {
        var vm = new SecondScreenViewModel { Overlay = SecondScreenOverlayKind.Achievements };
        vm.SetMedia(Media());
        vm.SwipeMedia(-120, 0);
        Assert.Equal(SecondScreenOverlayKind.Achievements, vm.Overlay);
    }
}
