using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Android.Opengl;
using Android.Views;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.Rendering.Gl;
using EmuShelf.Rendering.Stage;
using Javax.Microedition.Khronos.Opengles;
using Silk.NET.Core.Contexts;
using EglConfig = Javax.Microedition.Khronos.Egl.EGLConfig;
using GL = Silk.NET.OpenGL.GL;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// A Handheld screen drawn natively on Android (M45 H4, option 5): a <see cref="GLSurfaceView"/> laid over
/// the window's Avalonia surface, with its own GL thread running a native scene (the Stage's
/// <see cref="StageCompositor"/> on the main activity, the Launcher's <see cref="LauncherCompositor"/> on
/// Screen-2's Presentation). It draws only when told something changed or while something animates, so an
/// idle screen presents nothing. It keeps the latest command per piece (<see cref="StageCommand.RetainKey"/>)
/// so a lost context (the app backgrounded, a GPU reset) is rebuilt from it. If GL ES 3 cannot start it
/// reports <see cref="Failed"/> and the Avalonia view draws instead.
/// </summary>
internal sealed class NativeSceneSurface : Java.Lang.Object, IHandheldNativeStage, GLSurfaceView.IRenderer
{
    // The main activity's window as an owner (declared before the surfaces: static fields initialise in order).
    private static readonly object MainOwner = new();

    // Each display keeps a Stage and a Launcher (M45 H5): the pair in front draws, the other is parked out of sight,
    // already drawn, until X swaps the screens.

    /// <summary>The Stage on the main (16:9) display.</summary>
    public static NativeSceneSurface StageMain { get; } = new("StageMain", main: true,
        surface => gl => new StageCompositor(gl, GlslDialect.Es300, 3, 0, () => surface._view?.RequestRender()));

    /// <summary>The Launcher on the main (16:9) display, in front once the screens are swapped.</summary>
    public static NativeSceneSurface LauncherMain { get; } = new("LauncherMain", main: true,
        _ => gl => new LauncherCompositor(gl, GlslDialect.Es300, 3, 0));

    /// <summary>The Stage on Screen-2 (1240×1080), in front once the screens are swapped.</summary>
    public static NativeSceneSurface StageSecond { get; } = new("StageSecond", main: false,
        surface => gl => new StageCompositor(gl, GlslDialect.Es300, 3, 0, () => surface._view?.RequestRender()));

    /// <summary>The Launcher on Screen-2, attached by its Presentation.</summary>
    public static NativeSceneSurface LauncherSecond { get; } = new("LauncherSecond", main: false,
        _ => gl => new LauncherCompositor(gl, GlslDialect.Es300, 3, 0));

    public static IReadOnlyList<NativeSceneSurface> All { get; } = [StageMain, LauncherMain, StageSecond, LauncherSecond];

    private readonly string _name;
    private readonly Func<GL, INativeScene> _create;
    private bool _placing;
    private readonly ConcurrentQueue<StageCommand> _queue = new();
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, StageCommand> _retained = new(StringComparer.Ordinal);
    private readonly List<string> _retainedOrder = [];

    private GLSurfaceView? _view;
    private object? _owner;
    private Func<global::Android.Content.Context?>? _context;
    private Action<View>? _addView;
    private bool _active;
    private bool _allowed = true;
    private bool _failed;
    private INativeScene? _compositor;

    private NativeSceneSurface(string name, bool main, Func<NativeSceneSurface, Func<GL, INativeScene>> create)
    {
        _name = name;
        _create = create(this);
        // Parked, a surface waits just past the edge toward the hinge, where a swap slides it in from.
        ParkOffset = main ? ParkDistance : -ParkDistance;
        if (main)
            AttachToMain();
    }

    /// <summary>How far past its display's height a parked surface waits (the slide's 104%).</summary>
    public const float ParkDistance = 1.04f;

    /// <summary>Where this surface parks, as a share of its view's height: below the main display, above Screen-2.</summary>
    public float ParkOffset { get; }

    private volatile bool _parked;

    /// <summary>The surface's view (null until it is first shown), for the swap's slide.</summary>
    public View? SurfaceView => _view;

    /// <summary>
    /// Parks the surface just out of sight (kept alive with its pictures, drawing only what it is asked to) or brings it
    /// back in place. A parked surface does not animate or drift.
    /// </summary>
    /// <param name="keepWhereItIs">Unparking for a slide in: the view stays where it is and the slide moves it.</param>
    public void Park(bool parked, bool keepWhereItIs = false)
    {
        var wasParked = _parked;
        _parked = parked;
        if (!keepWhereItIs)
            PlaceView();
        // Back in front: frames drawn while parked stopped their animations (a selector spring, the glow) and the
        // backdrop's next step was never scheduled. One frame now picks all of it up again.
        if (wasParked && !parked)
            _view?.RequestRender();
    }

    // A parked view sits past the edge; one in front at rest. (The swap's slide moves it in between.)
    private void PlaceView()
    {
        if (_view is not { } view)
            return;
        var height = view.Height > 0 ? view.Height : view.Resources?.DisplayMetrics?.HeightPixels ?? 0;
        view.TranslationY = _parked ? ParkOffset * height : 0f;
    }

    /// <summary>
    /// Hosts this surface in the main activity's window, whichever activity is current (the Stage normally; the
    /// Launcher once the screens are swapped, M45 H5).
    /// </summary>
    public void AttachToMain()
    {
        if (ReferenceEquals(_owner, MainOwner))
            return;
        _owner = MainOwner;
        _context = () => MainActivity.Current;
        _addView = view => MainActivity.Current?.AddContentView(view,
            new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        DropView();
        SetActive(_active);
    }

    /// <summary>
    /// Hosts this surface in <paramref name="owner"/>'s window (a Presentation). The surface is shown only
    /// while the owner allows it (<see cref="SetAllowed"/>) and the feed has it active.
    /// </summary>
    public void AttachTo(object owner, global::Android.Content.Context context, Action<View> addView)
    {
        if (ReferenceEquals(owner, _owner))
            return;
        _owner = owner;
        _context = () => context;
        _addView = addView;
        DropView();
        SetActive(_active);
    }

    // Moving to another window: the old window's view goes (its GL thread and context with it); the next one is
    // made in the new window and rebuilds the scene from the retained commands.
    private void DropView()
    {
        if (_view is { } old)
        {
            try
            {
                (old.Parent as ViewGroup)?.RemoveView(old);
            }
            catch (Exception)
            {
                // A window already torn down took its views with it.
            }
        }
        _view = null;
        _viewHost = null;
    }

    /// <summary>The owner's say: Screen-2 showing the companion instead of the Launcher hides the surface.</summary>
    public void SetAllowed(bool allowed)
    {
        _allowed = allowed;
        SetActive(_active);
    }

    private int _width;
    private int _height;
    private System.Threading.Timer? _drift;

    public event Action? Failed;

    public void SetActive(bool active)
    {
        _active = active && !_failed;
        var show = _active && _allowed;
        if (show)
            EnsureView();
        if (_view is { } view)
            view.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
    }

    public void Post(StageCommand command)
    {
        if (command.RetainKey is { } key)
        {
            lock (_gate)
            {
                if (command.Removes)
                {
                    _retained.Remove(key);
                    _retainedOrder.Remove(key);
                }
                else
                {
                    if (!_retained.ContainsKey(key))
                        _retainedOrder.Add(key);
                    _retained[key] = command;
                }
            }
        }
        _queue.Enqueue(command);
        // A sway step while the scene is drawing anyway (an animated backdrop's step, an animation) waits for
        // that frame instead of adding one; the sway and a backdrop together then post 30 frames a second, not 50.
        if (command is StageMediumPoseCommand { Sway: true } && _frameComing)
            return;
        _view?.RequestRender();
    }

    // Whether the GL thread will draw again soon without being asked: it is animating, or a drift step is due.
    private volatile bool _frameComing;

    private TaskCompletionSource? _nextFrame;

    /// <summary>
    /// Completes once the surface has drawn a frame holding everything posted so far (the swap waits for it before the
    /// screen slides back in, so a new surface never shows its black background). Never completes while hidden.
    /// </summary>
    public Task NextFrameAsync()
    {
        TaskCompletionSource next;
        lock (_gate)
            next = _nextFrame ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _view?.RequestRender();
        return next.Task;
    }

    private void CompleteFrame()
    {
        TaskCompletionSource? done;
        lock (_gate)
        {
            done = _nextFrame;
            _nextFrame = null;
        }
        done?.TrySetResult();
    }

    /// <summary>The activity paused or resumed; a GLSurfaceView must be told.</summary>
    public void OnPause() => _view?.OnPause();

    public void OnResume() => _view?.OnResume();

    // The view belongs to one window; a recreated activity (or a new Presentation) gets a fresh one.
    private object? _viewHost;

    private void EnsureView()
    {
        var context = _context?.Invoke();
        if (context is null || _addView is null || (ReferenceEquals(context, _viewHost) && _view is not null))
            return;
        _viewHost = context;
        var view = new GLSurfaceView(context);
        view.SetEGLContextClientVersion(3);
        view.SetEGLConfigChooser(8, 8, 8, 8, 0, 0);
        view.PreserveEGLContextOnPause = true;
        // Above the activity's own (Avalonia) surface, which is also a SurfaceView.
        view.SetZOrderMediaOverlay(true);
        view.SetRenderer(this);
        view.RenderMode = Rendermode.WhenDirty;
        _addView(view);
        _view = view;
        // Its height is known once laid out; a parked view goes (and stays) out of sight then.
        view.LayoutChange += (_, _) =>
        {
            if (_parked && !_placing)
            {
                _placing = true;
                PlaceView();
                _placing = false;
            }
        };
        PlaceView();
    }

    // ---- The GL thread ----

    private float Now => (float)_clock.Elapsed.TotalMilliseconds;

    public void OnSurfaceCreated(IGL10? gl, EglConfig? config)
    {
        try
        {
            _compositor?.Dispose();
        }
        catch (Exception)
        {
            // The old context is gone; its objects went with it.
        }
        _compositor = null;
        try
        {
            var api = GL.GetApi(new LamdaNativeContext(GetProcAddress));
            _compositor = _create(api);
        }
        catch (Exception ex)
        {
            Fail(ex);
            return;
        }
        // A new context starts from the latest of everything (no animation), in the order it first came,
        // ahead of anything still queued.
        var now = Now;
        lock (_gate)
        {
            foreach (var key in _retainedOrder)
                _compositor.Apply(_retained[key].ForReplay(), now);
        }
    }

    public void OnSurfaceChanged(IGL10? gl, int width, int height)
    {
        _vote = -1f; // a new surface has no hint yet
        _width = width;
        _height = height;
    }

    public void OnDrawFrame(IGL10? gl)
    {
        if (_compositor is not { } compositor)
            return;
        var now = Now;
        try
        {
            while (_queue.TryDequeue(out var command))
                compositor.Apply(command, now);
            var animating = compositor.Draw(now, _width, _height);
            CompleteFrame();
            // Parked out of sight: what was asked for is drawn, nothing more (no animation, no drift).
            if (_parked)
            {
                _frameComing = false;
                return;
            }
            var next = animating ? null : compositor.NextFrameMilliseconds;
            VoteFrameRate(next is { } step ? 1000f / step : 0f);
            _frameComing = animating || (next is not null && _active);
            if (animating)
            {
                _view?.RequestRender();
                return;
            }
            ScheduleDrift(next);
        }
        catch (Exception ex)
        {
            Fail(ex);
        }
    }

    private float _vote = -1f;

    // Tells Android the rate this surface is posting at while only a stepped backdrop moves (30 Hz), and takes
    // the hint back (0) while something animates at the panel's rate or nothing moves, so the compositor and
    // display can pace for it instead of the 120 Hz panel rate.
    private void VoteFrameRate(float rate)
    {
        if (rate == _vote || !OperatingSystem.IsAndroidVersionAtLeast(30))
            return;
        _vote = rate;
        try
        {
            _view?.Holder?.Surface?.SetFrameRate(rate, (int)SurfaceFrameRateCompatibility.Default);
        }
        catch (Exception)
        {
            // Only a hint; a surface that is going away can refuse it.
        }
    }

    // Nothing animates: the backdrop's mover still steps a few times a second.
    private void ScheduleDrift(float? step)
    {
        if (step is not { } milliseconds || !_active)
            return;
        _drift ??= new System.Threading.Timer(_ => _view?.RequestRender());
        _drift.Change((int)Math.Max(16, milliseconds), System.Threading.Timeout.Infinite);
    }

    private void Fail(Exception ex)
    {
        global::Android.Util.Log.Error("EmuShelfStage", $"The native {_name} cannot draw; the Avalonia view takes over: {ex}");
        _compositor = null;
        Dispatcher.UIThread.Post(() =>
        {
            if (_failed)
                return;
            _failed = true;
            SetActive(false);
            Failed?.Invoke();
        });
    }

    [DllImport("libEGL.so")]
    private static extern IntPtr eglGetProcAddress(string name);

    private static IntPtr _gles;

    private static IntPtr GetProcAddress(string name)
    {
        if (_gles == IntPtr.Zero)
            NativeLibrary.TryLoad("libGLESv3.so", out _gles);
        if (_gles != IntPtr.Zero && NativeLibrary.TryGetExport(_gles, name, out var export))
            return export;
        return eglGetProcAddress(name);
    }
}
