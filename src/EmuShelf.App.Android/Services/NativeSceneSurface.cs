using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
    /// <summary>The top screen, on the main activity.</summary>
    public static NativeSceneSurface Stage { get; } = new("Stage",
        gl => new StageCompositor(gl, GlslDialect.Es300, 3, 0, () => Stage._view?.RequestRender()));

    /// <summary>The bottom screen, on Screen-2's Presentation (attached by it).</summary>
    public static NativeSceneSurface Launcher { get; } = new("Launcher",
        gl => new LauncherCompositor(gl, GlslDialect.Es300, 3, 0));

    private readonly string _name;
    private readonly Func<GL, INativeScene> _create;
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

    private NativeSceneSurface(string name, Func<GL, INativeScene> create)
    {
        _name = name;
        _create = create;
        // The top screen lives on whichever main activity is current.
        if (name == "Stage")
        {
            _context = () => MainActivity.Current;
            _addView = view => MainActivity.Current?.AddContentView(view,
                new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        }
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
        _view = null;
        SetActive(_active);
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
        _view?.RequestRender();
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
            if (animating)
            {
                _view?.RequestRender();
                return;
            }
            ScheduleDrift(compositor.NextFrameMilliseconds);
        }
        catch (Exception ex)
        {
            Fail(ex);
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
