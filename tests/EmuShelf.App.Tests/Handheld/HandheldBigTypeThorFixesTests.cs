using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public partial class HandheldShellViewModelTests
{
    /// <summary>
    /// M47 B10a, two faults the first Thor pass found that no desktop frame could show. (1) The native Launcher pictured a folder's
    /// name at the fit before its count was laid out, keyed by the name alone, so "Game Boy Advance" sat 10 px from 106 there while
    /// the Avalonia header kept <c>HeaderGap</c>. (2) The Avalonia ring's breath scaled about the Launcher's top left, because the
    /// compositor applies a visual's Scale about its CenterPoint after the control's RenderTransform (the ring's place), so the ring
    /// rested up to 30 px down and right of its key whenever the Avalonia Launcher had the screen. Desktop frames never include the
    /// compositor's animations, so these tests render through the compositor (or read what it is told).
    /// </summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeThorFixes
    {
        private static async Task BigType(Func<Task> test)
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                await test();
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        private static async Task Rest(int frames = 40)
        {
            for (var i = 0; i < frames; i++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(25);
            }
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public Task RevisitingAConsole_ReusesBothTileFaces() => BigType(async () =>
        {
            var previous = HandheldNativeStage.Launcher;
            var screen = new RecordingNativeScreen();
            using var shell = await CreateAsync(new FakeLibrary().WithPlatform("nes", 1).WithPlatform("snes", 1));
            HandheldNativeStage.Launcher = screen;
            var window = new Window { Width = 1240, Height = 1080 };
            try
            {
                window.Content = new HandheldLauncherView { DataContext = shell };
                window.Show();
                await SettleAsync();
                StagePixels Face() => screen.Sprites("h:0:0").Single().Image.Pixels;
                var focused = Face();
                Press(shell, GamepadAction.NavigateRight);
                await SettleAsync();
                var resting = Face();
                Assert.NotSame(focused, resting);
                Press(shell, GamepadAction.NavigateLeft);
                await SettleAsync();
                Assert.Same(focused, Face());
                Press(shell, GamepadAction.NavigateRight);
                await SettleAsync();
                Assert.Same(resting, Face());
            }
            finally
            {
                window.Close();
                HandheldNativeStage.Launcher = previous;
            }
        });

        [AvaloniaFact]
        public Task RapidMoves_HoldTheStageUntilTheSelectorLands() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("nes", 1).WithPlatform("snes", 1);
            var (shell, clock) = await CreateWithClockAsync(library);
            using (shell)
            {
                Assert.True(shell.DispatchGamepadAction(GamepadAction.NavigateRight));
                Assert.False(shell.IsBrowsingRapidly);
                var first = shell.Stage.Title;
                clock.Advance(TimeSpan.FromMilliseconds(250));
                Assert.True(shell.DispatchGamepadAction(GamepadAction.NavigateLeft));
                Assert.True(shell.IsBrowsingRapidly);
                Assert.True(shell.CurrentPage!.Slots[0].IsFocused);
                Assert.Equal(first, shell.Stage.Title);
                shell.SettleStage();
                Assert.False(shell.IsBrowsingRapidly);
                Assert.Equal("NES System", shell.Stage.Title);
                Assert.Empty(library.PeekRequests);
            }
        });

        // ---- (1) The native feed's folder name follows the fit ----

        private static async Task<(HandheldShellViewModel Shell, RecordingNativeScreen Launcher, HandheldLauncherView View, Window Window)>
            OpenNativeFolderAsync(string name, int games)
        {
            var library = new FakeLibrary().WithPlatform("gba", games);
            library.Platforms[0] = library.Platforms[0] with { Name = name };
            var shell = await CreateAsync(library);
            var launcher = new RecordingNativeScreen();
            HandheldNativeStage.Launcher = launcher;
            var view = new HandheldLauncherView { DataContext = shell };
            var window = new Window { Content = view, Width = 1240, Height = 1080 };
            window.Show();
            await SettleAsync();
            Press(shell, GamepadAction.Confirm); // open the folder
            for (var i = 0; i < 100 && shell.OpenFolder is not { IsLoaded: true }; i++)
                await Task.Delay(5);
            await Rest();
            return (shell, launcher, view, window);
        }

        [AvaloniaTheory]
        [InlineData("Game Boy Advance", 106, true)]
        [InlineData("PlayStation 2", 14, false)]
        public Task TheNativeFeed_PicturesTheFolderName_AtItsFit(string name, int games, bool shrinks) => BigType(async () =>
        {
            var (previousStage, previousLauncher) = (HandheldNativeStage.Host, HandheldNativeStage.Launcher);
            var (shell, launcher, view, window) = await OpenNativeFolderAsync(name, games);
            try
            {
                var heading = view.FindControl<HandheldBigTypeHeading>("BtFolderName")!;
                var count = view.FindControl<Control>("BtFolderCount")!;
                var header = view.FindControl<Control>("BtFolderHeader")!;
                var letters = Assert.IsType<LauncherLettersCommand>(launcher.Retained["letters:f-back"]).Letters!;
                // The native side draws the name at the size the header fits it to, which keeps HeaderGap from the count.
                Assert.Equal(heading.Layout!.Size, letters.Size, 2);
                Assert.Equal(shrinks, letters.Size < HandheldTokens.FolderHeading);
                var nameRight = heading.TranslatePoint(default, header)!.Value.X + heading.Layout.Width;
                Assert.True(count.TranslatePoint(default, header)!.Value.X - nameRight >= HandheldTokens.HeaderGap - 0.5);
                if (!shrinks)
                    Assert.Equal(HandheldTokens.FolderHeading, letters.Size);

                // A new fit after the first picture (here a narrower room) is pictured again: the feed follows it.
                heading.MaxTextWidth = 400;
                await Rest(10);
                var refitted = Assert.IsType<LauncherLettersCommand>(launcher.Retained["letters:f-back"]).Letters!;
                Assert.Equal(heading.Layout!.Size, refitted.Size, 2);
                Assert.True(refitted.Size < letters.Size);
            }
            finally
            {
                window.Close();
                shell.Dispose();
                HandheldNativeStage.Host = previousStage;
                HandheldNativeStage.Launcher = previousLauncher;
            }
        });

        // ---- (2) The ring rests on the focused key, through the compositor ----

        /// <summary>
        /// At rest, the ring (the Avalonia Launcher's selector) is centred on the focused anchor, its breath scales about that middle in
        /// the compositor's frame, and a frame drawn by the compositor with the breath pushed to ×1.5 still has it centred there.
        /// </summary>
        private static async Task AssertRingOnFocus(Window window, HandheldLauncherView view, string where)
        {
            await Rest();
            var selector = view.FindControl<Panel>("Selector")!;
            var layer = view.FindControl<Control>("SelectorLayer")!;
            var shift = Assert.IsType<TranslateTransform>(selector.RenderTransform);
            var anchor = view.GetVisualDescendants().OfType<Control>()
                .Last(control => HandheldSelectorAnchor.GetIsFocused(control) && control.IsEffectivelyVisible &&
                    control.GetVisualAncestors().Any(a => a is Control { Name: "Keyboard" }));
            var expected = anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2), layer)!.Value;
            var middle = new Point(shift.X + (selector.Width / 2), shift.Y + (selector.Height / 2));
            Assert.True(Math.Abs(middle.X - expected.X) < 1 && Math.Abs(middle.Y - expected.Y) < 1,
                $"{where}: the ring's middle {middle} is not the focused key's {expected}");

            var visual = ElementComposition.GetElementVisual(selector)!;
            Assert.True(Math.Abs(visual.CenterPoint.X - middle.X) < 0.6 && Math.Abs(visual.CenterPoint.Y - middle.Y) < 0.6,
                $"{where}: the breath scales about {visual.CenterPoint}, not the ring's middle {middle}");

            // Through the compositor, the breath held at ×1.5 (the ring still inside the window): the ring stays centred on the key.
            var background = selector.Background;
            selector.Background = Brushes.Red;
            var hold = visual.Compositor.CreateVector3DKeyFrameAnimation();
            hold.InsertKeyFrame(0f, new Vector3D(1.5, 1.5, 1));
            hold.InsertKeyFrame(1f, new Vector3D(1.5, 1.5, 1));
            hold.Duration = TimeSpan.FromSeconds(100);
            visual.StartAnimation("Scale", hold);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var (min, max) = RedBox(window);
            var drawn = new Point((min.X + max.X + 1) / 2.0, (min.Y + max.Y + 1) / 2.0);
            var onScreen = anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2), window)!.Value;
            Assert.True(Math.Abs(drawn.X - onScreen.X) < 3 && Math.Abs(drawn.Y - onScreen.Y) < 3,
                $"{where}: the compositor draws the breathing ring about {drawn}, the key is at {onScreen}");
            var back = visual.Compositor.CreateVector3DKeyFrameAnimation();
            back.InsertKeyFrame(0f, new Vector3D(1, 1, 1));
            back.InsertKeyFrame(1f, new Vector3D(1, 1, 1));
            back.Duration = TimeSpan.FromMilliseconds(1);
            visual.StartAnimation("Scale", back);
            selector.Background = background;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        private static (PixelPoint Min, PixelPoint Max) RedBox(Window window)
        {
            using var frame = window.CaptureRenderedFrame()!;
            using var locked = frame.Lock();
            var stride = locked.RowBytes / 4;
            var pixels = new int[stride * locked.Size.Height];
            System.Runtime.InteropServices.Marshal.Copy(locked.Address, pixels, 0, pixels.Length);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (var y = 0; y < locked.Size.Height; y++)
            {
                for (var x = 0; x < locked.Size.Width; x++)
                {
                    var c = (uint)pixels[(y * stride) + x];
                    // Rgba8888 in memory: red is the low byte.
                    if ((c & 0xFF) > 230 && ((c >> 8) & 0xFF) < 30 && ((c >> 16) & 0xFF) < 30)
                    {
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }
            }
            Assert.True(maxX >= 0, "the ring was not drawn");
            return (new PixelPoint(minX, minY), new PixelPoint(maxX, maxY));
        }

        [AvaloniaFact]
        public Task TheRing_RestsOnTheFocusedKey_WhileNaming_OnEveryPage_AndAfterTheColours() => BigType(async () =>
        {
            var previous = HandheldNativeStage.Launcher;
            HandheldNativeStage.Launcher = null;
            var library = new FakeLibrary().WithPlatform("ps2", 3);
            var store = new InMemoryHandheldHomeLayoutStore();
            store.Save(HandheldHomeLayout.FromSlots([HandheldSlot.PlatformFolder("ps2")]));
            var shell = await CreateAsync(library, store);
            var view = new HandheldLauncherView { DataContext = shell };
            var window = new Window { Content = view, Width = 1240, Height = 1080 };
            window.Show();
            try
            {
                await SettleAsync();
                FocusHome(shell, 7);
                Press(shell, GamepadAction.Confirm); // name a folder on an empty slot: the colours row is above the keys
                Assert.NotNull(shell.Naming);
                await AssertRingOnFocus(window, view, "naming, the first key");
                Press(shell, GamepadAction.NavigateRight, 2);
                await AssertRingOnFocus(window, view, "naming, E");
                Press(shell, GamepadAction.NavigateUp);
                Assert.Contains(shell.Naming!.Colors, chip => chip.IsFocused);
                await AssertRingOnFocus(window, view, "naming, a colour");
                Press(shell, GamepadAction.NavigateDown);
                await AssertRingOnFocus(window, view, "naming, back from the colours");
                Press(shell, GamepadAction.NextPlatform);
                await AssertRingOnFocus(window, view, "naming, the second page");
                Press(shell, GamepadAction.NextPlatform);
                await AssertRingOnFocus(window, view, "naming, the third page");
                Press(shell, GamepadAction.NextPlatform);
                Press(shell, GamepadAction.NavigateUp);
                await AssertRingOnFocus(window, view, "naming, the letters again, L");
            }
            finally
            {
                window.Close();
                shell.Dispose();
                HandheldNativeStage.Launcher = previous;
            }
        });

        [AvaloniaFact]
        public Task TheRing_RestsOnTheFocusedKey_InSearch_BeforeHits_WithHits_AndBackFromAHit() => BigType(async () =>
        {
            var previous = HandheldNativeStage.Launcher;
            HandheldNativeStage.Launcher = null;
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 8).WithPlatform("gc", 2));
            shell.SearchDebounce = TimeSpan.Zero;
            var view = new HandheldLauncherView { DataContext = shell };
            var window = new Window { Content = view, Width = 1240, Height = 1080 };
            window.Show();
            try
            {
                await SettleAsync();
                RunMenuRow(shell, "Search");
                var search = Assert.IsType<HandheldSearchPanel>(shell.Panel);
                var keyboard = search.Keyboard!;
                await AssertRingOnFocus(window, view, "Search, no hits");
                foreach (var ch in "GAME")
                    shell.TapPanelKey(keyboard.KeyRows.SelectMany(row => row.Keys).First(key => key.Value == ch.ToString()));
                await search.Pending;
                Assert.NotEmpty(search.Hits);
                keyboard.FocusAt(0, 4);
                await AssertRingOnFocus(window, view, "Search, with hits, T");
                Press(shell, GamepadAction.NavigateUp);
                Assert.NotNull(search.FocusedHit);
                await Rest();
                Press(shell, GamepadAction.Cancel);
                Assert.Null(search.FocusedHit);
                await AssertRingOnFocus(window, view, "Search, back from a hit");
            }
            finally
            {
                window.Close();
                shell.Dispose();
                HandheldNativeStage.Launcher = previous;
            }
        });
    }
}
