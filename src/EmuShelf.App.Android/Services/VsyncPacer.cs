using System;
using Android.OS;
using Android.Views;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// The display's vsync, on a thread of its own (M47 B11). A native screen that animates under the Big Type look asks for its next
/// frame here rather than at once: the frame then starts on the next vsync and its motion is timed at that vsync, so frames come one
/// per refresh, evenly timed. Asking at once (as the Classic look still does) drew two frames back to back and then missed a refresh
/// while a direction was held on the Thor: every other frame showed nearly the same moment and the ring looked jagged. A thread of its
/// own (a Looper the Choreographer can run on) keeps the pacing free of the UI thread's work.
/// </summary>
internal sealed class VsyncPacer : Java.Lang.Object, Choreographer.IFrameCallback
{
    private static readonly Lazy<VsyncPacer> Shared = new(() => new VsyncPacer());

    public static VsyncPacer Instance => Shared.Value;

    private readonly Handler _handler;
    private readonly object _gate = new();
    private readonly System.Collections.Generic.List<Action<long>> _waiting = [];
    private Choreographer? _choreographer;
    private bool _posted;

    private VsyncPacer()
    {
        var thread = new HandlerThread("EmuShelfVsync", (int)global::Android.OS.ThreadPriority.Display);
        thread.Start();
        _handler = new Handler(thread.Looper!);
        _handler.Post(() => _choreographer = Choreographer.Instance);
    }

    /// <summary>Calls <paramref name="next"/> once, on the next vsync, with that vsync's time (<c>System.nanoTime</c>'s clock).</summary>
    public void OnNextVsync(Action<long> next)
    {
        lock (_gate)
        {
            _waiting.Add(next);
            if (_posted)
                return;
            _posted = true;
        }
        _handler.Post(() =>
        {
            if (_choreographer is { } choreographer)
                choreographer.PostFrameCallback(this);
            else
                DoFrame(Java.Lang.JavaSystem.NanoTime());
        });
    }

    public void DoFrame(long frameTimeNanos)
    {
        Action<long>[] callbacks;
        lock (_gate)
        {
            callbacks = [.. _waiting];
            _waiting.Clear();
            _posted = false;
        }
        foreach (var callback in callbacks)
            callback(frameTimeNanos);
    }
}
