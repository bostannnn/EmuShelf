using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using Xunit;

namespace EmuShelf.App.Tests;

/// <summary>
/// The companion's "a game is playing on the other screen" dim standby. IsStandby is what the view binds to
/// wash the idle surface near-black with a faint logo, and it must lift the moment anything is shown over it
/// (the achievements/app-drawer overlay) and drop back when they close — the requested behaviour for the
/// achievement viewer.
/// </summary>
public class SecondScreenStandbyTests
{
    [Fact]
    public void NotStandby_WhileBrowsing()
    {
        var vm = new SecondScreenViewModel();

        Assert.False(vm.IsStandby);
    }

    [Fact]
    public void Standby_WhenGameRunning_AndNothingOver()
    {
        var vm = new SecondScreenViewModel { IsGameRunning = true };

        Assert.True(vm.IsStandby);
    }

    [Fact]
    public void OpeningAchievements_LiftsTheDim_ClosingRestoresIt()
    {
        var vm = new SecondScreenViewModel { IsGameRunning = true };
        Assert.True(vm.IsStandby);

        vm.Overlay = SecondScreenOverlayKind.Achievements;
        Assert.False(vm.IsStandby);

        vm.Overlay = SecondScreenOverlayKind.None;
        Assert.True(vm.IsStandby);
    }

    [Fact]
    public void OpeningTheDrawer_LiftsTheDim()
    {
        var vm = new SecondScreenViewModel { IsGameRunning = true, Overlay = SecondScreenOverlayKind.Drawer };

        Assert.False(vm.IsStandby);
    }

    // The standby wash paints its OWN faint copy of the running game's logo, centred over the whole
    // surface while the resting one is centred in the spotlight row above the dock bar. With both
    // painted, the two copies sat half a dock bar apart and the logo appeared doubled on the companion
    // screen, so the resting one is dark for as long as the wash is up.
    [Fact]
    public void RestingLogo_IsDark_WhileStandbyPaintsItsOwnCopy()
    {
        var vm = new SecondScreenViewModel { LogoOpacity = 1 };
        Assert.Equal(1, vm.SpotlightLogoOpacity);

        vm.IsGameRunning = true;
        Assert.Equal(0, vm.SpotlightLogoOpacity);

        // An overlay lifts the standby wash, so the resting spotlight — and its logo — is back.
        vm.Overlay = SecondScreenOverlayKind.Achievements;
        Assert.Equal(1, vm.SpotlightLogoOpacity);
    }

    [Fact]
    public void RestingBranding_IsHidden_WhileStandbyPaintsItsOwnWordmark()
    {
        var vm = new SecondScreenViewModel();
        Assert.True(vm.ShowRestingBranding);

        vm.IsGameRunning = true;
        Assert.False(vm.ShowRestingBranding);

        // No artwork at all is the only case the wordmark shows; standby draws its own.
        vm.SetSpotlight(null, null);
        Assert.True(vm.ShowBranding);
        Assert.False(vm.ShowRestingBranding);
    }

    [Fact]
    public void SpotlightLogoOpacity_RaisesChange_WhenTheStandbyStateFlips()
    {
        var vm = new SecondScreenViewModel { LogoOpacity = 1 };
        var changes = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SecondScreenViewModel.SpotlightLogoOpacity))
                changes++;
        };

        vm.IsGameRunning = true;
        vm.Overlay = SecondScreenOverlayKind.Drawer;

        Assert.Equal(2, changes);
    }

    // ── Tap to wake ────────────────────────────────────────────────────────────────────────────────
    // Dimmed to near-black you cannot see what you are about to press, so a touch has to buy a look at
    // the panel before it activates anything on it.

    [AvaloniaFact]
    public void Touch_LiftsTheDim_WhileAGameIsRunning()
    {
        var vm = new SecondScreenViewModel { IsGameRunning = true };
        Assert.True(vm.IsStandby);

        vm.NoteInteraction();

        Assert.True(vm.IsAwake);
        Assert.False(vm.IsStandby);
    }

    [AvaloniaFact]
    public async Task WakeExpires_AndTheDimComesBack()
    {
        var vm = new SecondScreenViewModel { IsGameRunning = true, WakeDuration = TimeSpan.FromMilliseconds(40) };
        vm.NoteInteraction();
        Assert.False(vm.IsStandby);

        await WaitForStandbyAsync(vm);

        Assert.False(vm.IsAwake);
        Assert.True(vm.IsStandby);
    }

    [AvaloniaFact]
    public async Task EachTouchBuysTheFullWindowAgain()
    {
        var window = TimeSpan.FromMilliseconds(400);
        var vm = new SecondScreenViewModel { IsGameRunning = true, WakeDuration = window };

        // t=0: first touch. Its window would close at 400ms on its own.
        vm.NoteInteraction();
        await SettleForAsync(window / 2);        // t≈200: still inside the first window
        Assert.False(vm.IsStandby);

        // t≈200: second touch. If the countdown restarts, the window now closes at ≈600ms; if it does
        // not, it still closes at 400ms — which is what the wait below distinguishes.
        vm.NoteInteraction();
        await SettleForAsync(window / 2 + TimeSpan.FromMilliseconds(60));

        // t≈460: past the FIRST window's expiry, and no touch since t≈200. Awake here only because the
        // second touch restarted the timer — the assertion the old shape could not make, because it
        // touched immediately before reading.
        Assert.True(vm.IsAwake);
        Assert.False(vm.IsStandby);

        await WaitForStandbyAsync(vm);
        Assert.True(vm.IsStandby);
    }

    [AvaloniaFact]
    public void TouchWhileBrowsing_DoesNothing()
    {
        // No game running means no dim to lift; waking would only start a timer for nothing.
        var vm = new SecondScreenViewModel();

        vm.NoteInteraction();

        Assert.False(vm.IsAwake);
        Assert.False(vm.IsStandby);
    }

    [AvaloniaFact]
    public void GameStartingOrEnding_ClearsAnyWakeWindow()
    {
        var vm = new SecondScreenViewModel { IsGameRunning = true };
        vm.NoteInteraction();
        Assert.True(vm.IsAwake);

        // Ending the session: nothing to keep awake, and no timer left running behind the library.
        vm.IsGameRunning = false;
        Assert.False(vm.IsAwake);

        // Starting the next one: a leftover wake window must not swallow the new game's dim.
        vm.IsGameRunning = true;
        Assert.False(vm.IsAwake);
        Assert.True(vm.IsStandby);
    }

    [AvaloniaFact]
    public void OverlaysStillOwnTheDim_Independently()
    {
        // The achievements behaviour is untouched by waking: opening lifts the dim, closing restores it,
        // even though no touch was ever recorded (a gamepad can drive it).
        var vm = new SecondScreenViewModel { IsGameRunning = true };

        vm.Overlay = SecondScreenOverlayKind.Achievements;
        Assert.False(vm.IsStandby);
        Assert.False(vm.IsAwake);

        vm.Overlay = SecondScreenOverlayKind.None;
        Assert.True(vm.IsStandby);
    }

    [AvaloniaFact]
    public void ATouchWhileAnOverlayIsOpen_DoesNotOutliveTheOverlay()
    {
        // The touch that closes achievements or the drawer also reaches the root wake handler. It must
        // not buy a wake window, or the wash would stay off for five seconds after the sheet is gone —
        // the overlay owns the dim while it is up and hands it straight back on close.
        var vm = new SecondScreenViewModel { IsGameRunning = true };
        vm.Overlay = SecondScreenOverlayKind.Achievements;

        vm.NoteInteraction();
        Assert.False(vm.IsAwake);

        vm.Overlay = SecondScreenOverlayKind.None;
        Assert.False(vm.IsAwake);
        Assert.True(vm.IsStandby);
    }

    [AvaloniaFact]
    public void AnOverlayOpening_EndsAWakeWindowInFlight()
    {
        // Tap (wake), then open the drawer from that lit panel, then close it: the dim comes back on
        // close, not whenever the original tap's window happens to run out.
        var vm = new SecondScreenViewModel { IsGameRunning = true };
        vm.NoteInteraction();
        Assert.True(vm.IsAwake);

        vm.Overlay = SecondScreenOverlayKind.Drawer;
        Assert.False(vm.IsAwake);

        vm.Overlay = SecondScreenOverlayKind.None;
        Assert.True(vm.IsStandby);
    }

    [AvaloniaFact]
    public async Task ARealTouchWhileTheOverlayIsUp_LeavesTheDimToTheOverlay()
    {
        // Same as above, through the real tunnel handler with the achievements sheet mounted over the
        // press point — the on-device path a view-model call cannot stand in for.
        var model = new SecondScreenViewModel { IsGameRunning = true, Overlay = SecondScreenOverlayKind.Achievements };
        var window = new Window { Content = new SecondScreenView { DataContext = model }, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            await PumpAsync();
            window.MouseDown(new Point(620, 400), MouseButton.Left, RawInputModifiers.None);
            await PumpAsync();
            Assert.False(model.IsAwake);

            model.Overlay = SecondScreenOverlayKind.None;
            Assert.True(model.IsStandby);
        }
        finally
        {
            window.Close();
        }
    }

    // The whole wake path hangs off one root handler registered for the TUNNEL pass of PointerPressed.
    // Nothing else in the app routes that way, the companion screen cannot be screenshotted (Screen-2 is
    // capture-blocked), and a wrong routing strategy would fail silently — so drive a real press.
    [AvaloniaFact]
    public async Task ARealTouchAnywhereOnTheSurface_WakesIt()
    {
        var model = new SecondScreenViewModel { IsGameRunning = true };
        var window = new Window { Content = new SecondScreenView { DataContext = model }, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            await PumpAsync();
            Assert.True(model.IsStandby);

            // Middle of the surface: the dim wash is what the press lands on while it is up.
            window.MouseDown(new Point(620, 400), MouseButton.Left, RawInputModifiers.None);
            await PumpAsync();

            Assert.True(model.IsAwake);
            Assert.False(model.IsStandby);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DimWash_SwallowsTheWakingTouch_ThenGetsOutOfTheWay()
    {
        var model = new SecondScreenViewModel { IsGameRunning = true };
        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            await PumpAsync();
            var wash = view.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("ss-standby"));

            // Dimmed: the wash takes the touch, so the dock slot under the finger is not launched blind.
            Assert.True(wash.IsHitTestVisible);

            model.NoteInteraction();
            await PumpAsync();

            // Lit: the wash is out of the way and the dock/trophy are tappable again.
            Assert.False(wash.IsHitTestVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task PumpAsync()
    {
        for (var i = 0; i < 4; i++)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    /// <summary>Waits real time while keeping the dispatcher turning, so the wake timer's tick can land.</summary>
    private static async Task SettleForAsync(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }

    private static async Task WaitForStandbyAsync(SecondScreenViewModel vm)
    {
        // The wake window is closed by a DispatcherTimer, so pump the loop rather than sleeping blind.
        for (var i = 0; i < 60 && !vm.IsStandby; i++)
        {
            await Task.Delay(20);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }

    // The rendered proof of the fix above: on the real companion surface exactly ONE copy of the running
    // game's logo may be painted, browsing or in standby. Both copies are always mounted, and neither is
    // hidden with IsVisible — they are faded — so this composes each one's opacity with its ancestors'
    // rather than reading the leaf, which is also what makes it catch the browsing direction (where the
    // standby copy's own Opacity is a constant 0.16 and only its parent's fade turns it off).
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExactlyOneCopyOfTheLogoIsEverPainted(bool gameRunning)
    {
        var logo = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888);
        var model = new SecondScreenViewModel { LogoOpacity = 1 };
        model.SetSpotlight(fanart: null, wheel: logo);
        // Set the state BEFORE the view is shown, so the opacity transitions are not mid-flight when read.
        model.IsGameRunning = gameRunning;

        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            await PumpAsync();

            var logoImages = view.GetVisualDescendants()
                .OfType<Image>()
                .Where(image => ReferenceEquals(image.Source, logo))
                .ToList();
            Assert.Equal(2, logoImages.Count); // both are mounted...
            Assert.Single(logoImages, image => image.IsVisible && PaintedOpacity(image) > 0.01); // ...one is painted
        }
        finally
        {
            window.Close();
        }
    }

    // The two copies are centred on the SAME rect, so when the dim fades in or out they cross over on top
    // of each other instead of sliding past each other half a dock bar apart — the artefact this whole
    // pair of properties exists to avoid, which the crossfade brought back for ~300ms on every transition.
    [AvaloniaFact]
    public async Task TheTwoLogoCopiesShareOneCentre()
    {
        var logo = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888);
        var model = new SecondScreenViewModel { LogoOpacity = 1 };
        model.SetSpotlight(fanart: null, wheel: logo);
        model.IsGameRunning = true;

        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            await PumpAsync();

            var centres = view.GetVisualDescendants()
                .OfType<Image>()
                .Where(image => ReferenceEquals(image.Source, logo))
                .Select(image => image.TranslatePoint(image.Bounds.Center, view))
                .ToList();
            Assert.Equal(2, centres.Count);
            Assert.All(centres, centre => Assert.NotNull(centre));
            Assert.Equal(centres[0]!.Value.Y, centres[1]!.Value.Y, 1);
            Assert.Equal(centres[0]!.Value.X, centres[1]!.Value.X, 1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A control's opacity composed with every ancestor's — what actually reaches the screen.</summary>
    private static double PaintedOpacity(Visual visual)
    {
        var opacity = 1.0;
        for (Visual? node = visual; node is not null; node = node.GetVisualParent())
            opacity *= node.Opacity;
        return opacity;
    }

    [Fact]
    public void IsStandby_RaisesChange_WhenGameStartsAndStops()
    {
        var vm = new SecondScreenViewModel();
        var changes = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SecondScreenViewModel.IsStandby))
                changes++;
        };

        vm.IsGameRunning = true;
        vm.IsGameRunning = false;

        Assert.Equal(2, changes);
    }
}
