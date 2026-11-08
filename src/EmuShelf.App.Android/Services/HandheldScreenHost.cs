using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Android.Animation;
using Android.Views;
using Android.Views.Animations;
using Avalonia.Controls;
using Avalonia.Threading;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Places Handheld mode's screens on the Thor's two displays (M45 H5). Each display keeps a pair built for it, a Stage
/// and a Launcher, each with its own native surface: the main (16:9) display a 1920-wide Stage and a 7×3 Launcher,
/// Screen-2 a 1240-wide Stage and a 5×4 Launcher. One of each pair is in front; the other is hidden (its feed paused)
/// and its surface parked just past the hinge edge, still holding its last pictures. X swaps which one is in front:
/// the front screens slide out toward the hinge while the ones behind catch up on what changed, then those slide in
/// from the hinge. Nothing moves between windows and nothing is rebuilt, so the swap starts at once and no display is
/// ever left empty for longer than the slide.
/// <para>
/// Each display also keeps a Handheld companion (M45 H6b) over its pair, shown while it is the companion's: the screen
/// the game does not use while one runs, Screen-2 while the companion's All apps or badge grid is open over the
/// Launcher. As a game starts, the screen in front slides away toward the hinge and the companion rises in under it; as
/// the game ends, the screen slides back over the companion. The pair stays attached throughout, so its feeds keep the
/// native screens current (the Recent folder updated by the launch is already there when the Launcher comes back).
/// </para>
/// </summary>
internal sealed class HandheldScreenHost
{
    // The mockup's numbers: out on ease-in (230 ms), back on the pop curve (420 ms), 104% of the screen's height.
    private const long SlideOutMilliseconds = 230;
    private const long SlideInMilliseconds = 420;

    // How long the slide in waits at most for the screens coming in to catch up, then for their frame.
    private const int CatchUpTimeoutMilliseconds = 400;
    private const int FrameTimeoutMilliseconds = 150;

    // The pair behind is built once, out of sight, a little after Handheld mode comes up.
    private static readonly TimeSpan WarmUpDelay = TimeSpan.FromSeconds(2);
    private const int WarmUpTimeoutMilliseconds = 3000;

    private const string LogTag = "EmuShelfHandheld";

    public static HandheldScreenHost Instance { get; } = new();

    private readonly HandheldStageView _stageMain = new(NativeSceneSurface.StageMain, pinnedNarrow: false) { IsVisible = false };
    private readonly HandheldLauncherView _launcherMain = new(NativeSceneSurface.LauncherMain, pinnedWide: true) { IsVisible = false };
    private readonly HandheldStageView _stageSecond = new(NativeSceneSurface.StageSecond, pinnedNarrow: true) { IsVisible = false };
    private readonly HandheldLauncherView _launcherSecond = new(NativeSceneSurface.LauncherSecond, pinnedWide: false) { IsVisible = false };
    private readonly Panel _mainPair;
    private readonly Panel _secondPair;
    // Each display's pair with its companion over it: what the display's window shows in Handheld mode.
    private readonly Panel _mainRoot;
    private readonly Panel _secondRoot;
    private readonly DisplayState _mainDisplay;
    private readonly DisplayState _secondDisplay;
    private bool _inGame;

    private ContentControl? _mainSlot;
    private HandheldShellViewModel? _shell;
    private ThorSecondScreenPresentation? _presentation;
    private bool _presentationShowsHandheld;
    private bool _mainShowsHandheld = true;
    private bool _sliding;
    // Whether each display's screen behind has been built once (a swap then only catches up on what changed).
    private bool _warmMain;
    private bool _warmSecond;
    private bool _warmUpScheduled;

    private HandheldScreenHost()
    {
        _mainPair = new Panel { Children = { _stageMain, _launcherMain } };
        _secondPair = new Panel { Children = { _stageSecond, _launcherSecond } };
        var companionMain = new HandheldCompanionView(wide: true) { IsVisible = false };
        var companionSecond = new HandheldCompanionView(wide: false) { IsVisible = false };
        _mainRoot = new Panel { Children = { _mainPair, companionMain } };
        _secondRoot = new Panel { Children = { _secondPair, companionSecond } };
        _mainDisplay = new DisplayState(companionMain, NativeSceneSurface.StageMain, NativeSceneSurface.LauncherMain);
        _secondDisplay = new DisplayState(companionSecond, NativeSceneSurface.StageSecond, NativeSceneSurface.LauncherSecond);
    }

    /// <summary>One display's companion and surfaces, and where its hand-off between them stands.</summary>
    private sealed class DisplayState(HandheldCompanionView companion, NativeSceneSurface stage, NativeSceneSurface launcher)
    {
        public HandheldCompanionView Companion { get; } = companion;
        public NativeSceneSurface[] Surfaces { get; } = [stage, launcher];

        /// <summary>The companion is this display's now.</summary>
        public bool ShowsCompanion { get; set; }

        /// <summary>It came up for a game, so the screen slides back over it when the game ends.</summary>
        public bool ForGame { get; set; }

        /// <summary>Bumped by every hand-off, so one that finishes late leaves a newer one alone.</summary>
        public int Generation { get; set; }

        /// <summary>The surface sliding away or back right now (nothing else may move or hide it meanwhile).</summary>
        public NativeSceneSurface? Sliding { get; set; }
    }

    /// <summary>The main display's place for Handheld mode (MainView's slot), registered as MainView loads.</summary>
    public void RegisterMainSlot(ContentControl slot)
    {
        if (ReferenceEquals(slot, _mainSlot))
            return;
        // A recreated activity brings a new slot: the pair leaves the old one first and joins the new one on a later
        // pass (never detach and attach in the same pass, DECISIONS 2026-11-07).
        if (_mainSlot is { } old)
        {
            old.Content = null;
            _mainSlot = slot;
            Dispatcher.UIThread.Post(Place, DispatcherPriority.Background);
            return;
        }
        _mainSlot = slot;
        Place();
    }

    /// <summary>
    /// The state the second-screen controller knows: the shell (null outside Handheld mode), Screen-2's presentation,
    /// whether Screen-2 shows Handheld mode's screens (not the companion, which takes it while a game runs or its drawer
    /// is open), whether the main display does (not while the companion sits there over a game on Screen-2), and whether
    /// a game is running (the companion then arrives and leaves with a slide).
    /// </summary>
    public void Update(HandheldShellViewModel? shell, ThorSecondScreenPresentation? presentation, bool presentationShowsHandheld,
        bool mainShowsHandheld, bool inGame)
    {
        if (!ReferenceEquals(shell, _shell))
        {
            if (_shell is { } old)
                old.ScreenSwapHost = null;
            _shell = shell;
            if (shell is not null)
                shell.ScreenSwapHost = SwapAsync;
            foreach (var view in Views)
                view.DataContext = shell;
            _warmMain = _warmSecond = false;
        }
        _presentation = presentation;
        _presentationShowsHandheld = presentationShowsHandheld && presentation is not null;
        _mainShowsHandheld = mainShowsHandheld;
        _inGame = inGame;
        Place();
    }

    private Control[] Views => [_stageMain, _launcherMain, _stageSecond, _launcherSecond];

    // The view in front on each display.
    private (Control Main, Control Second) Front(bool swapped) =>
        swapped ? (_launcherMain, _stageSecond) : (_stageMain, _launcherSecond);

    private (Control Main, Control Second) Back(bool swapped) => Front(!swapped);

    private static NativeSceneSurface SurfaceOf(Control view) => view switch
    {
        HandheldStageView { PinnedNarrow: true } => NativeSceneSurface.StageSecond,
        HandheldStageView => NativeSceneSurface.StageMain,
        HandheldLauncherView { PinnedWide: true } => NativeSceneSurface.LauncherMain,
        _ => NativeSceneSurface.LauncherSecond,
    };

    /// <summary>Puts each display's pair in its window, the right one of each in front, the others parked.</summary>
    private void Place()
    {
        if (_shell is not { } shell)
        {
            if (_mainSlot is { } emptySlot && ReferenceEquals(emptySlot.Content, _mainRoot))
                emptySlot.Content = null;
            _presentation?.ShowHandheld(null, []);
            foreach (var surface in NativeSceneSurface.All)
                surface.SetAllowed(false);
            foreach (var display in new[] { _mainDisplay, _secondDisplay })
            {
                ++display.Generation;
                display.ShowsCompanion = display.ForGame = false;
                display.Sliding = null;
                display.Companion.IsVisible = false;
                display.Companion.DataContext = null;
            }
            return;
        }
        if (_mainSlot is { } slot && !ReferenceEquals(slot.Content, _mainRoot))
            slot.Content = _mainRoot;
        _presentation?.ShowHandheld(_secondRoot, [NativeSceneSurface.StageSecond, NativeSceneSurface.LauncherSecond]);
        var companion = shell.CompanionScreen;
        _mainDisplay.Companion.DataContext = companion;
        _secondDisplay.Companion.DataContext = companion;
        // The companion is on the 16:9 screen while a game has the bottom one.
        if (companion is not null)
            companion.IsOnWideScreen = !_mainShowsHandheld;
        var (frontMain, frontSecond) = Front(shell.IsSwapped);
        HandOff(_mainDisplay, companion: !_mainShowsHandheld, SurfaceOf(frontMain));
        HandOff(_secondDisplay, companion: _presentation is not null && !_presentationShowsHandheld, SurfaceOf(frontSecond));
        if (!_sliding)
            ShowFront(shell.IsSwapped);
        ScheduleWarmUp();
    }

    private void ShowFront(bool swapped)
    {
        var (frontMain, frontSecond) = Front(swapped);
        var (backMain, backSecond) = Back(swapped);
        frontMain.IsVisible = frontSecond.IsVisible = true;
        backMain.IsVisible = backSecond.IsVisible = false;
        ParkUnlessSliding(SurfaceOf(frontMain), false);
        ParkUnlessSliding(SurfaceOf(frontSecond), false);
        ParkUnlessSliding(SurfaceOf(backMain), true);
        ParkUnlessSliding(SurfaceOf(backSecond), true);
    }

    // A surface mid hand-off is the slide's; it is placed when the slide ends.
    private void ParkUnlessSliding(NativeSceneSurface surface, bool parked)
    {
        if (!ReferenceEquals(_mainDisplay.Sliding, surface) && !ReferenceEquals(_secondDisplay.Sliding, surface))
            surface.Park(parked);
    }

    // ---- The companion's hand-off (M45 H6b) ----

    /// <summary>
    /// Gives a display to the companion or takes it back. For a game the screen in front slides away toward the hinge as
    /// the companion rises in under it, and slides back over it when the game ends; for a layer over the Launcher (All
    /// apps, the Title page's badges) the change is immediate.
    /// </summary>
    private void HandOff(DisplayState display, bool companion, NativeSceneSurface front)
    {
        if (companion == display.ShowsCompanion)
        {
            // Steady: no change, unless a slide is still finishing it.
            if (display.Sliding is null)
            {
                display.Companion.IsVisible = companion;
                foreach (var surface in display.Surfaces)
                    surface.SetAllowed(!companion);
            }
            return;
        }
        display.ShowsCompanion = companion;
        var generation = ++display.Generation;
        display.Sliding = null;
        if (companion)
        {
            display.ForGame = _inGame;
            display.Companion.IsVisible = true;
            if (_inGame && front.SurfaceView is { Visibility: ViewStates.Visible, Height: > 0 })
                _ = SlideAwayAsync(display, front, generation);
            else
                foreach (var surface in display.Surfaces)
                    surface.SetAllowed(false);
            global::Android.Util.Log.Info(LogTag, $"Companion up ({(display == _mainDisplay ? "main" : "second")}, game={_inGame})");
            return;
        }
        var slide = display.ForGame;
        display.ForGame = false;
        foreach (var surface in display.Surfaces)
            surface.SetAllowed(true);
        if (slide)
            _ = SlideBackAsync(display, front, generation);
        else
            display.Companion.IsVisible = false;
        global::Android.Util.Log.Info(LogTag, $"Companion down ({(display == _mainDisplay ? "main" : "second")}, slide={slide})");
    }

    private async Task SlideAwayAsync(DisplayState display, NativeSceneSurface front, int generation)
    {
        display.Sliding = front;
        await Slide(front, 0f, front.ParkOffset, SlideOutMilliseconds, new PathInterpolator(0.4f, 0f, 1f, 1f));
        if (generation != display.Generation)
            return;
        display.Sliding = null;
        foreach (var surface in display.Surfaces)
            surface.SetAllowed(false);
        // Back in its place for when it is shown again (hidden, it is not seen there).
        if (_shell is { } shell && !_sliding)
            ShowFront(shell.IsSwapped);
    }

    private async Task SlideBackAsync(DisplayState display, NativeSceneSurface front, int generation)
    {
        display.Sliding = front;
        // Out of sight first; its surface was gone while the companion was up, so let it draw a frame before it moves in.
        if (front.SurfaceView is { Height: > 0 } view)
            view.TranslationY = front.ParkOffset * view.Height;
        await Task.WhenAny(front.NextFrameAsync(), Task.Delay(FrameTimeoutMilliseconds));
        if (generation != display.Generation)
            return;
        front.Park(false, keepWhereItIs: true);
        await Slide(front, front.ParkOffset, 0f, SlideInMilliseconds, new PathInterpolator(0.22f, 1.1f, 0.36f, 1f));
        if (generation != display.Generation)
            return;
        display.Sliding = null;
        display.Companion.IsVisible = false;
        if (_shell is { } shell && !_sliding)
            ShowFront(shell.IsSwapped);
    }

    // ---- Building the pair behind once, out of sight ----

    // Warm-up is due for a display that shows Handheld mode and whose screen behind has not been built yet.
    private bool WarmUpDue => _shell is not null && ((!_warmMain && _mainShowsHandheld) || (!_warmSecond && _presentationShowsHandheld));

    private void ScheduleWarmUp()
    {
        if (!WarmUpDue || _warmUpScheduled)
            return;
        _warmUpScheduled = true;
        DispatcherTimer.RunOnce(() =>
        {
            _warmUpScheduled = false;
            _ = WarmUpAsync();
        }, WarmUpDelay);
    }

    /// <summary>
    /// Lays out the pair behind and lets it picture everything into its parked surfaces, then hides it again, so a
    /// swap only has to catch up on what changed since.
    /// </summary>
    private async Task WarmUpAsync()
    {
        if (!WarmUpDue || _shell is not { } shell)
            return;
        if (_sliding || !shell.IsLoaded)
        {
            ScheduleWarmUp();
            return;
        }
        var clock = Stopwatch.StartNew();
        var swapped = shell.IsSwapped;
        var (backMain, backSecond) = Back(swapped);
        var warmMain = !_warmMain && _mainShowsHandheld;
        var warmSecond = !_warmSecond && _presentationShowsHandheld;
        var ready = new List<Task>();
        if (warmMain)
            ready.Add(CaughtUp(backMain));
        if (warmSecond)
            ready.Add(CaughtUp(backSecond));
        backMain.IsVisible |= warmMain;
        backSecond.IsVisible |= warmSecond;
        await Task.WhenAny(Task.WhenAll(ready), Task.Delay(WarmUpTimeoutMilliseconds));
        // A swap or a change of shell meanwhile has set the screens itself.
        if (!ReferenceEquals(shell, _shell) || shell.IsSwapped != swapped || _sliding)
            return;
        backMain.IsVisible = backSecond.IsVisible = false;
        _warmMain |= warmMain;
        _warmSecond |= warmSecond;
        global::Android.Util.Log.Info(LogTag, $"Warm-up: the screens behind are ready after {clock.ElapsedMilliseconds} ms");
        // A display that was not showing Handheld mode is warmed when it does again (Place schedules it).
    }

    /// <summary>Completes on a view's next NativeCaughtUp (its native screen has every piece it shows).</summary>
    private static Task CaughtUp(Control view)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action? handler = null;
        handler = () =>
        {
            if (view is HandheldStageView stage)
                stage.NativeCaughtUp -= handler;
            else if (view is HandheldLauncherView launcher)
                launcher.NativeCaughtUp -= handler;
            done.TrySetResult();
        };
        if (view is HandheldStageView s)
            s.NativeCaughtUp += handler;
        else if (view is HandheldLauncherView l)
            l.NativeCaughtUp += handler;
        return done.Task;
    }

    // ---- The swap ----

    /// <summary>
    /// The shell's swap. The change applies at once (the pad moves through the new shape from the next press); the
    /// screens behind come forward and catch up while the ones in front slide out toward the hinge, then slide in from
    /// it.
    /// </summary>
    private async Task SwapAsync(Action apply)
    {
        if (_shell is not { } shell)
        {
            apply();
            return;
        }
        var clock = Stopwatch.StartNew();
        _sliding = true;
        var wasSwapped = shell.IsSwapped;
        var (outMain, outSecond) = Front(wasSwapped);
        var (inMain, inSecond) = Back(wasSwapped);
        var bottomShown = _presentationShowsHandheld;
        try
        {
            var caughtUp = new List<Task> { CaughtUp(inMain) };
            if (bottomShown)
                caughtUp.Add(CaughtUp(inSecond));
            apply();
            // The screens coming in wake (their feeds catch up on what changed while they were hidden) while the ones in
            // front leave.
            inMain.IsVisible = true;
            inSecond.IsVisible = bottomShown;
            var (outTop, outBottom) = (SurfaceOf(outMain), SurfaceOf(outSecond));
            var (inTop, inBottom) = (SurfaceOf(inMain), SurfaceOf(inSecond));
            await Task.WhenAll(
                Slide(outTop, 0f, outTop.ParkOffset, SlideOutMilliseconds, new PathInterpolator(0.4f, 0f, 1f, 1f)),
                Slide(outBottom, 0f, outBottom.ParkOffset, SlideOutMilliseconds, new PathInterpolator(0.4f, 0f, 1f, 1f)));
            var slidOut = clock.ElapsedMilliseconds;
            await Task.WhenAny(Task.WhenAll(caughtUp), Task.Delay(CatchUpTimeoutMilliseconds));
            await Task.WhenAny(Task.WhenAll(inTop.NextFrameAsync(), bottomShown ? inBottom.NextFrameAsync() : Task.CompletedTask),
                Task.Delay(FrameTimeoutMilliseconds));
            var ready = clock.ElapsedMilliseconds;
            // The ones that left wait parked; the ones coming in leave their park and slide to their place.
            outTop.Park(true);
            outBottom.Park(true);
            inTop.Park(false, keepWhereItIs: true);
            inBottom.Park(false, keepWhereItIs: true);
            await Task.WhenAll(
                Slide(inTop, inTop.ParkOffset, 0f, SlideInMilliseconds, new PathInterpolator(0.22f, 1.1f, 0.36f, 1f)),
                Slide(inBottom, inBottom.ParkOffset, 0f, SlideInMilliseconds, new PathInterpolator(0.22f, 1.1f, 0.36f, 1f)));
            global::Android.Util.Log.Info(LogTag, $"Swap: swapped={shell.IsSwapped}, slid out {slidOut} ms, caught up +{ready - slidOut} ms, " +
                $"done {clock.ElapsedMilliseconds} ms");
        }
        finally
        {
            _sliding = false;
            if (ReferenceEquals(shell, _shell))
                ShowFront(shell.IsSwapped);
        }
    }

    // The slide each surface is in, so a newer one (a game that ends as it starts) takes over instead of fighting it.
    private readonly Dictionary<NativeSceneSurface, ObjectAnimator> _slides = [];

    // Slides a surface vertically by a share of its height (its own view only: the Avalonia view under it is invisible
    // while the native screens draw).
    private Task Slide(NativeSceneSurface surface, float from, float to, long milliseconds, ITimeInterpolator interpolator)
    {
        if (_slides.Remove(surface, out var running))
            running.Cancel();
        if (surface.SurfaceView is not { } view || view.Height <= 0 || view.Visibility != ViewStates.Visible)
            return Task.CompletedTask;
        var done = new TaskCompletionSource();
        var height = view.Height;
        view.TranslationY = from * height;
        var animator = ObjectAnimator.OfFloat(view, "translationY", from * height, to * height);
        if (animator is null)
        {
            view.TranslationY = to * height;
            return Task.CompletedTask;
        }
        animator.SetDuration(milliseconds);
        animator.SetInterpolator(interpolator);
        animator.AnimationEnd += (_, _) =>
        {
            if (_slides.TryGetValue(surface, out var current) && ReferenceEquals(current, animator))
                _slides.Remove(surface);
            done.TrySetResult();
        };
        animator.AnimationCancel += (_, _) => done.TrySetResult();
        _slides[surface] = animator;
        animator.Start();
        return done.Task;
    }
}
