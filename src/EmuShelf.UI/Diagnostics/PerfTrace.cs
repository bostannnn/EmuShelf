using System;
using System.Diagnostics;
using System.Threading;

namespace EmuShelf.App.Diagnostics;

/// <summary>
/// A lightweight, log-based performance tracer for the couch UI, so the on-device fan-on-scroll cost can be
/// diagnosed from <c>adb logcat</c> alone — without watching the panel (the visual overlay in
/// <see cref="RenderOverlayDiagnostics"/> cannot be read remotely, and the Thor's burn-in screensaver
/// corrupts screenshots). Two channels: discrete <see cref="Event"/>s for user actions (layout/platform/CRT
/// changes) and a once-a-second <see cref="Sample"/> line carrying the current state plus GL frame rate,
/// worst render time, managed-allocation rate, and GC frequency.
/// </summary>
/// <remarks>
/// The sink is set by the platform head (the Android head routes it to <c>Android.Util.Log</c> under tag
/// <c>EmuShelfPerf</c>); it is null on heads that do not wire it, making every call a cheap no-op. Sampling
/// runs on a <see cref="System.Threading.Timer"/> — a pool thread, not the UI thread — so it neither adds
/// UI-thread load nor gets starved and under-reports exactly when the UI is busiest. Deliberately NOT gated
/// to <c>DEBUG</c>: the whole point is to read it from a Release build.
/// </remarks>
public static class PerfTrace
{
    /// <summary>Where trace lines go. Null (the default) makes the tracer inert.</summary>
    public static Action<string>? Sink { get; set; }

    /// <summary>
    /// Supplies the current state string appended to each perf sample (e.g. <c>layout=Shelf crt=off
    /// path=shelf-tube sys=PlayStation games=42</c>). Set by the shell so the sampler, on a pool thread,
    /// reads a cheaply-computed snapshot rather than reaching into the view model itself.
    /// </summary>
    public static Func<string>? StateProvider { get; set; }

    // GL shelf frame counters, written from the render thread in MediaShelf3DControl.OnOpenGlRender.
    private static long _glFrames;
    private static long _maxRenderTicks;

    /// <summary>A discrete, timestamped trace line for a user action or state transition.</summary>
    public static void Event(string message) => Sink?.Invoke(message);

    /// <summary>Records one drawn GL shelf frame and its render duration (Stopwatch ticks), for the FPS/worst-frame sample.</summary>
    public static void RecordGlFrame(long renderTicks)
    {
        Interlocked.Increment(ref _glFrames);
        long prev;
        do
        {
            prev = Interlocked.Read(ref _maxRenderTicks);
            if (renderTicks <= prev)
                return;
        }
        while (Interlocked.CompareExchange(ref _maxRenderTicks, renderTicks, prev) != prev);
    }

    private static Timer? _timer;
    private static long _lastAllocBytes;
    private static int _lastGen0;

    /// <summary>Starts the once-a-second perf sampler. Idempotent; a no-op without a <see cref="Sink"/>.</summary>
    public static void StartSampling()
    {
        if (_timer is not null || Sink is null)
            return;

        _lastAllocBytes = GC.GetTotalAllocatedBytes();
        _lastGen0 = GC.CollectionCount(0);
        _timer = new Timer(_ => Sample(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private static void Sample()
    {
        var sink = Sink;
        if (sink is null)
            return;

        var frames = Interlocked.Exchange(ref _glFrames, 0);
        var maxTicks = Interlocked.Exchange(ref _maxRenderTicks, 0);
        var maxRenderMs = maxTicks * 1000.0 / Stopwatch.Frequency;

        var alloc = GC.GetTotalAllocatedBytes();
        var allocPerSecMb = (alloc - _lastAllocBytes) / 1_000_000.0;
        _lastAllocBytes = alloc;

        var gen0 = GC.CollectionCount(0);
        var gen0PerSec = gen0 - _lastGen0;
        _lastGen0 = gen0;

        string state;
        try { state = StateProvider?.Invoke() ?? "state=?"; }
        catch { state = "state=(unavailable)"; }

        sink($"PERF {state} glfps={frames} glRenderMaxMs={maxRenderMs:F1} allocMB/s={allocPerSecMb:F1} gen0/s={gen0PerSec}");
    }
}
