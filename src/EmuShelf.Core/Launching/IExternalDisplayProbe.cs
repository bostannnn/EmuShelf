namespace EmuShelf.Core.Launching;

/// <summary>
/// Reports whether the device currently has a usable external (second) screen a game could launch on.
/// Implemented only by the Android head (the Thor's <c>FLAG_PRESENTATION</c> display); desktop heads
/// leave it null, which disables the whole launch-screen feature — the window manager, not EmuShelf,
/// decides where a desktop emulator opens. Read on the UI thread just before a launch.
/// </summary>
public interface IExternalDisplayProbe
{
    /// <summary>True when a second screen is attached and available to receive a launch right now.</summary>
    bool HasExternalDisplay { get; }

    /// <summary>
    /// Whether an external-screen launch can be safely started: the return watcher that detects the game
    /// closing on the second screen (and brings EmuShelf's library back) is live. When false, a game sent to
    /// the external screen could never return — the head stays foregrounded on the built-in panel, so the
    /// top-resumed edge never fires — so the launch path blocks it and routes the user to
    /// <see cref="RequestSecondScreenReturn"/> instead of stranding the companion. Defaults true for
    /// platforms with no such watcher; only the Android head, which owns the feature, reports the real state.
    /// </summary>
    bool IsSecondScreenReturnReady => true;

    /// <summary>Sends the user to enable the return watcher (the Android accessibility screen). No-op by default.</summary>
    void RequestSecondScreenReturn()
    {
    }
}
