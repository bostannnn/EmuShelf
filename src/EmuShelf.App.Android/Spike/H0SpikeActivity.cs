#if DEBUG
using System;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Hardware.Display;
using Android.OS;
using Android.Views;
using Avalonia.Android;
using Avalonia.Controls;
using Avalonia.Threading;
using Environment = System.Environment;

namespace EmuShelf.App.Android.Spike;

/// <summary>
/// M45 H0 device spike (throwaway, Debug builds only). Never reached from the app; started over adb:
/// <c>adb shell am start -n com.emushelf.app/.H0SpikeActivity [--ei hz 120] [--ez autopilot true]
/// [--ei seconds 60] [--ez autoswap true] [--ez drift false]</c>.
/// The Stage runs in this Activity's view on the main display and the Launcher in a Presentation on
/// Screen-2, both Avalonia, both driven by one <see cref="H0Model"/>. Pad: D-pad moves, X swaps the two
/// views between displays (re-hosting, not moving windows), Y toggles the autopilot, Start logs totals.
/// Results go to logcat tag <c>EmuShelfH0</c> every five seconds.
/// </summary>
[Activity(
    Name = "com.emushelf.app.H0SpikeActivity",
    Label = "EmuShelf H0 spike",
    Theme = "@style/EmuShelfTheme",
    Exported = true,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
        | ConfigChanges.Density | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden | ConfigChanges.Navigation)]
public sealed class H0SpikeActivity : AvaloniaActivity
{
    private const string Tag = "EmuShelfH0";

    private readonly H0Model _model = new();
    private readonly H0FrameStats _stageStats = new("stage");
    private readonly H0FrameStats _launcherStats = new("launcher");
    private H0StageView? _stage;
    private H0LauncherView? _launcher;
    private H0Presentation? _presentation;
    private DispatcherTimer? _report;
    private H0Autopilot? _autopilot;
    private int _hatX;
    private int _hatY;
    private DispatcherTimer? _hatRepeat;
    private int _inputMain;
    private int _inputSecond;
    private int _swaps;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        if (OperatingSystem.IsAndroidVersionAtLeast(30) && Window is { } window)
        {
            window.SetDecorFitsSystemWindows(false);
            window.InsetsController?.Hide(WindowInsets.Type.SystemBars());
        }

        var hz = Intent?.GetIntExtra("hz", 0) ?? 0;
        _stage = new H0StageView(_model, _stageStats, Intent?.GetBooleanExtra("drift", true) ?? true);
        _launcher = new H0LauncherView(_model, _launcherStats);
        if (hz > 0)
            RequestRefreshRate(Window, Display, hz);

        if (GetSystemService(DisplayService) is DisplayManager manager &&
            manager.GetDisplays(DisplayManager.DisplayCategoryPresentation) is { Length: > 0 } displays)
        {
            _presentation = new H0Presentation(this, displays[0]);
            if (hz > 0)
                RequestRefreshRate(_presentation.Window, displays[0], hz);
            _presentation.Show();
        }
        else
        {
            global::Android.Util.Log.Warn(Tag, "No presentation display: the spike needs the Thor's Screen-2.");
        }
        Host();

        _report = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _report.Tick += (_, _) => Report();
        _report.Start();

        if (Intent?.GetBooleanExtra("autopilot", false) == true)
        {
            var seconds = Intent.GetIntExtra("seconds", 60);
            var autoswap = Intent.GetBooleanExtra("autoswap", false);
            DispatcherTimer.RunOnce(() => StartAutopilot(seconds, autoswap), TimeSpan.FromSeconds(3));
        }
        global::Android.Util.Log.Info(Tag, $"H0 spike started (requested hz={hz}, presentation={_presentation is not null})");
    }

    /// <summary>Puts the Stage and Launcher on the displays <see cref="H0Model.IsSwapped"/> asks for.</summary>
    private void Host()
    {
        var started = Environment.TickCount64;
        // Detach both first: a control can only have one parent, and each display owns a separate top level.
        Content = null;
        if (_presentation is not null)
            _presentation.View.Content = null;

        // Attach on a later dispatcher pass. Re-attaching in the same pass crashed on the Thor with
        // "Attempt to call InvalidateArrange on wrong LayoutManager": the old top level's layout manager
        // still had the control queued and ran its arrange pass after the control had a new root.
        // Background priority runs after the pending layout and render jobs have drained.
        _hosting = true;
        Dispatcher.UIThread.Post(() =>
        {
            _hosting = false;
            Control onMain = _model.IsSwapped ? _launcher! : _stage!;
            Control onSecond = _model.IsSwapped ? _stage! : _launcher!;
            Content = onMain;
            if (_presentation is not null)
                _presentation.View.Content = onSecond;
            global::Android.Util.Log.Info(Tag, $"Re-attached after {Environment.TickCount64 - started} ms");
        }, DispatcherPriority.Background);

        var mainHz = Display?.RefreshRate ?? 60;
        var secondHz = _presentation?.Display?.RefreshRate ?? 60;
        (_model.IsSwapped ? _launcherStats : _stageStats).RefreshHz = mainHz;
        (_model.IsSwapped ? _stageStats : _launcherStats).RefreshHz = secondHz;
        global::Android.Util.Log.Info(Tag,
            $"Hosted: main({mainHz:0}Hz)={(_model.IsSwapped ? "launcher" : "stage")}, " +
            $"screen2({secondHz:0}Hz)={(_model.IsSwapped ? "stage" : "launcher")}, " +
            $"selection={_model.Index} kept, re-host took {Environment.TickCount64 - started} ms");
    }

    private bool _hosting;

    internal void Swap()
    {
        if (_hosting)
            return;
        _model.IsSwapped = !_model.IsSwapped;
        _swaps++;
        Host();
    }

    private void StartAutopilot(int seconds, bool autoswap)
    {
        _stageStats.ResetTotal();
        _launcherStats.ResetTotal();
        _autopilot?.Stop();
        _autopilot = new H0Autopilot(_model, this, seconds, autoswap, () =>
        {
            global::Android.Util.Log.Info(Tag, "Autopilot done. Totals:");
            ReportTotals();
            _autopilot = null;
        });
        global::Android.Util.Log.Info(Tag, $"Autopilot started for {seconds}s (autoswap={autoswap})");
        _autopilot.Start();
    }

    private void Report()
    {
        global::Android.Util.Log.Info(Tag, _stageStats.TakeWindow());
        global::Android.Util.Log.Info(Tag, _launcherStats.TakeWindow());
    }

    private void ReportTotals()
    {
        global::Android.Util.Log.Info(Tag, "TOTAL " + _stageStats.DescribeTotal());
        global::Android.Util.Log.Info(Tag, "TOTAL " + _launcherStats.DescribeTotal());
        global::Android.Util.Log.Info(Tag,
            $"TOTAL input: main-display events={_inputMain}, screen2 events={_inputSecond}, swaps={_swaps}, " +
            $"main {Display?.RefreshRate:0}Hz, screen2 {_presentation?.Display?.RefreshRate:0}Hz");
    }

    /// <summary>The one input path both displays feed. Returns true when the event was a spike control.</summary>
    internal bool HandleKey(KeyEvent? e, bool fromSecond)
    {
        if (e is null)
            return false;
        int dx = 0, dy = 0;
        switch (e.KeyCode)
        {
            case Keycode.DpadLeft: dx = -1; break;
            case Keycode.DpadRight: dx = 1; break;
            case Keycode.DpadUp: dy = -1; break;
            case Keycode.DpadDown: dy = 1; break;
            case Keycode.ButtonX:
            case Keycode.ButtonY:
            case Keycode.ButtonStart:
            case Keycode.ButtonA:
            case Keycode.ButtonB:
                break;
            default:
                return false;
        }
        if (e.Action != KeyEventActions.Down)
            return true;
        Count(fromSecond);
        if (dx != 0 || dy != 0)
        {
            _model.Move(dx, dy);
            return true;
        }
        if (e.RepeatCount != 0)
            return true;
        switch (e.KeyCode)
        {
            case Keycode.ButtonX:
                Swap();
                break;
            case Keycode.ButtonY:
                if (_autopilot is not null)
                {
                    _autopilot.Stop();
                    _autopilot = null;
                    global::Android.Util.Log.Info(Tag, "Autopilot stopped by Y. Totals:");
                    ReportTotals();
                }
                else
                {
                    StartAutopilot(60, false);
                }
                break;
            case Keycode.ButtonStart:
                ReportTotals();
                break;
        }
        return true;
    }

    /// <summary>The Thor's D-pad is a hat axis: edge-detect it and auto-repeat while held.</summary>
    internal bool HandleMotion(MotionEvent? e, bool fromSecond)
    {
        if (e is null || (e.Source & InputSourceType.Joystick) != InputSourceType.Joystick ||
            e.Action != MotionEventActions.Move)
            return false;
        var hx = (int)Math.Round(e.GetAxisValue(Axis.HatX));
        var hy = (int)Math.Round(e.GetAxisValue(Axis.HatY));
        if (hx == _hatX && hy == _hatY)
            return true;
        _hatX = hx;
        _hatY = hy;
        _hatRepeat?.Stop();
        if (hx == 0 && hy == 0)
            return true;
        Count(fromSecond);
        _model.Move(hx, hx != 0 ? 0 : hy);
        _hatRepeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _hatRepeat.Tick += (_, _) =>
        {
            _hatRepeat!.Interval = TimeSpan.FromMilliseconds(100);
            if (_hatX != 0 || _hatY != 0)
                _model.Move(_hatX, _hatX != 0 ? 0 : _hatY);
        };
        _hatRepeat.Start();
        return true;
    }

    private void Count(bool fromSecond)
    {
        if (fromSecond)
            _inputSecond++;
        else
            _inputMain++;
    }

    public override bool DispatchKeyEvent(KeyEvent? e) => HandleKey(e, false) || base.DispatchKeyEvent(e!);

    public override bool DispatchGenericMotionEvent(MotionEvent? e) =>
        HandleMotion(e, false) || base.DispatchGenericMotionEvent(e);

    protected override void OnDestroy()
    {
        _autopilot?.Stop();
        _report?.Stop();
        _hatRepeat?.Stop();
        _presentation?.Dismiss();
        base.OnDestroy();
    }

    private static void RequestRefreshRate(global::Android.Views.Window? window, Display? display, int hz)
    {
        if (window is null || display is null)
            return;
        var current = display.GetMode();
        var mode = display.GetSupportedModes()?
            .Where(m => current is null || (m.PhysicalWidth == current.PhysicalWidth && m.PhysicalHeight == current.PhysicalHeight))
            .OrderBy(m => Math.Abs(m.RefreshRate - hz))
            .FirstOrDefault();
        if (mode is null)
            return;
        var attributes = window.Attributes!;
        attributes.PreferredDisplayModeId = mode.ModeId;
        window.Attributes = attributes;
        global::Android.Util.Log.Info(Tag, $"Requested mode {mode.ModeId} ({mode.RefreshRate:0}Hz) on display {display.DisplayId}");
    }
}

/// <summary>Screen-2's window: an Avalonia view whose pad input goes to the same handler as the Activity's.</summary>
internal sealed class H0Presentation : Presentation
{
    private readonly H0SpikeActivity _owner;

    public H0Presentation(H0SpikeActivity owner, Display display)
        : base(owner, display)
    {
        _owner = owner;
        Window?.SetDimAmount(0);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.TurnScreenOn);
        SetCancelable(false);
        View = new AvaloniaView(Context);
        SetContentView(View);
    }

    public AvaloniaView View { get; }

    public override bool DispatchKeyEvent(KeyEvent? e) => _owner.HandleKey(e, true) || base.DispatchKeyEvent(e!);

    public override bool DispatchGenericMotionEvent(MotionEvent? e) =>
        _owner.HandleMotion(e, true) || base.DispatchGenericMotionEvent(e!);
}

/// <summary>
/// A repeatable pad script, so frame times are comparable between runs: held-direction glides (presses
/// 90 ms apart, faster than the 150 ms debounce), single presses with the top screen popping each time,
/// wall bumps, and optionally a screen swap every lap.
/// </summary>
internal sealed class H0Autopilot
{
    private static readonly (int Dx, int Dy, int DelayMs)[] Lap = BuildLap();

    private readonly H0Model _model;
    private readonly H0SpikeActivity _activity;
    private readonly bool _autoswap;
    private readonly Action _done;
    private readonly long _endTicks;
    private DispatcherTimer? _timer;
    private int _step;

    public H0Autopilot(H0Model model, H0SpikeActivity activity, int seconds, bool autoswap, Action done)
    {
        _model = model;
        _activity = activity;
        _autoswap = autoswap;
        _done = done;
        _endTicks = Environment.TickCount64 + seconds * 1000L;
    }

    public void Start() => Schedule(0);

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void Schedule(int delayMs)
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, delayMs)) };
        _timer.Tick += (_, _) =>
        {
            _timer?.Stop();
            if (Environment.TickCount64 >= _endTicks)
            {
                _timer = null;
                _done();
                return;
            }
            var (dx, dy, delay) = Lap[_step % Lap.Length];
            _step++;
            if (dx == 9)
            {
                if (_autoswap)
                    _activity.Swap();
            }
            else
            {
                _model.Move(dx, dy);
            }
            Schedule(delay);
        };
        _timer.Start();
    }

    private static (int, int, int)[] BuildLap()
    {
        var lap = new System.Collections.Generic.List<(int, int, int)>();
        // Held glide: a serpentine through every tile, 90 ms apart (held-direction cadence).
        for (var row = 0; row < H0Model.Rows; row++)
        {
            for (var i = 0; i < H0Model.Columns - 1; i++)
                lap.Add((row % 2 == 0 ? 1 : -1, 0, 90));
            if (row < H0Model.Rows - 1)
                lap.Add((0, 1, 90));
        }
        // Wall bumps at the bottom-left corner.
        lap.Add((0, 1, 300));
        lap.Add((-1, 0, 300));
        // Single presses back up, each far enough apart for the top screen to pop every time.
        foreach (var (dx, dy) in new[] { (0, -1), (1, 0), (0, -1), (1, 0), (0, -1), (1, 0), (-1, 0), (-1, 0), (-1, 0) })
            lap.Add((dx, dy, 550));
        lap.Add((9, 0, 700)); // swap marker
        return lap.ToArray();
    }
}
#endif
