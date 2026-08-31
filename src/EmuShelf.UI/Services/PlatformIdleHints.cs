namespace EmuShelf.App.Services;

/// <summary>
/// Moments the UI declares "a good time for platform housekeeping", for platform heads to act on.
/// Same shape as <see cref="Diagnostics.PerfTrace.Sink"/>: a static hook the head installs at
/// startup, a no-op everywhere else — the shared UI states WHEN, only the platform decides WHAT.
/// </summary>
public static class PlatformIdleHints
{
    /// <summary>
    /// Raised (on the UI thread) when a couch scroll glide settles. The Android head installs a
    /// throttled gen0 collection here: on MonoVM a minor GC during an ACTIVE glide freezes the
    /// scroll ~100 ms (busy threads are slow to reach safepoints) while the same collection at rest
    /// costs ~1 ms, so together with the enlarged nursery (the Android head's environment.txt) this
    /// keeps the glide itself collection-free — the nursery is emptied between holds instead of
    /// overflowing mid-hold. Desktop CoreCLR has no such pause and installs nothing. See
    /// DECISIONS 2026-08-31.
    /// </summary>
    public static Action? ScrollGlideSettled { get; set; }

    /// <summary>UI-side notifier; the null-conditional keeps un-hooked platforms allocation-free.</summary>
    public static void NotifyScrollGlideSettled() => ScrollGlideSettled?.Invoke();
}
