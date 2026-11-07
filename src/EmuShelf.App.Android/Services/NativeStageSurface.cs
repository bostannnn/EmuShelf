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
/// The Handheld Stage drawn natively on Android (M45 H4, option 5): a <see cref="GLSurfaceView"/> laid over
/// the activity's Avalonia surface, with its own GL thread running <see cref="StageCompositor"/>. It draws
/// only when told something changed or while something animates, so an idle top screen presents nothing.
/// It keeps the latest state of every piece so a lost context (the app backgrounded, a GPU reset) is
/// rebuilt from it. If GL ES 3 cannot start, it reports <see cref="Failed"/> and the Avalonia Stage draws.
/// </summary>
internal sealed class NativeStageSurface : Java.Lang.Object, IHandheldNativeStage, GLSurfaceView.IRenderer
{
    public static NativeStageSurface Instance { get; } = new();

    private readonly ConcurrentQueue<StageCommand> _queue = new();
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<string, StageChromeCommand> _chrome = new(StringComparer.Ordinal);
    private StageBackdropCommand? _backdrop;
    private StageCentreCommand? _centre;
    private StageMediumPoseCommand? _pose;
    private StageMediumCoverCommand? _cover;

    private GLSurfaceView? _view;
    private global::Android.App.Activity? _activity;
    private bool _active;
    private bool _failed;
    private StageCompositor? _compositor;
    private int _width;
    private int _height;
    private System.Threading.Timer? _drift;

    public event Action? Failed;

    public void SetActive(bool active)
    {
        _active = active && !_failed;
        if (_active)
            EnsureView();
        if (_view is { } view)
            view.Visibility = _active ? ViewStates.Visible : ViewStates.Gone;
    }

    public void Post(StageCommand command)
    {
        lock (_gate)
        {
            switch (command)
            {
                case StageBackdropCommand backdrop: _backdrop = backdrop; break;
                case StageCentreCommand centre: _centre = centre with { Pop = false }; break;
                case StageMediumPoseCommand pose: _pose = pose; break;
                case StageMediumCoverCommand cover: _cover = cover; break;
                case StageChromeCommand chrome: _chrome[chrome.Key] = chrome; break;
            }
        }
        _queue.Enqueue(command);
        _view?.RequestRender();
    }

    /// <summary>The activity paused or resumed; a GLSurfaceView must be told.</summary>
    public void OnPause() => _view?.OnPause();

    public void OnResume() => _view?.OnResume();

    // The view belongs to one activity; a recreated activity gets a fresh one.
    private void EnsureView()
    {
        var activity = MainActivity.Current;
        if (activity is null || (ReferenceEquals(activity, _activity) && _view is not null))
            return;
        _activity = activity;
        var view = new GLSurfaceView(activity);
        view.SetEGLContextClientVersion(3);
        view.SetEGLConfigChooser(8, 8, 8, 8, 0, 0);
        view.PreserveEGLContextOnPause = true;
        // Above the activity's own (Avalonia) surface, which is also a SurfaceView.
        view.SetZOrderMediaOverlay(true);
        view.SetRenderer(this);
        view.RenderMode = Rendermode.WhenDirty;
        activity.AddContentView(view, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
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
            _compositor = new StageCompositor(api, GlslDialect.Es300, 3, 0, () => _view?.RequestRender());
        }
        catch (Exception ex)
        {
            Fail(ex);
            return;
        }
        // A new context starts from the latest of everything (no pop), ahead of anything still queued.
        var now = Now;
        lock (_gate)
        {
            if (_backdrop is { } backdrop)
                _compositor.Apply(backdrop, now);
            if (_cover is { } cover)
                _compositor.Apply(cover, now);
            if (_pose is { } pose)
                _compositor.Apply(pose, now);
            if (_centre is { } centre)
                _compositor.Apply(centre, now);
            foreach (var chrome in _chrome.Values)
                _compositor.Apply(chrome, now);
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
            ScheduleDrift(compositor.DriftStepMilliseconds);
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
        global::Android.Util.Log.Error("EmuShelfStage", $"The native Stage cannot draw; the Avalonia Stage takes over: {ex}");
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
