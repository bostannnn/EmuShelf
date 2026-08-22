using System;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// The bridge from the Activity's foreground-transition callback to the head's deferred-completion
/// handler, in the same static-hook style as <see cref="AndroidGamepadInput"/>. The Activity owns
/// <c>OnTopResumedActivityChanged</c> (fired when EmuShelf gains or loses the single top-resumed spot),
/// so it is the head's return signal; <see cref="SingleViewShell"/> points <see cref="ReturnedToForeground"/>
/// at the handler that completes a pending play session once the view model exists.
/// </summary>
public static class AndroidActivityLifecycle
{
    /// <summary>The single Activity became usable for Presentation/window work.</summary>
    public static event Action<MainActivity>? ActivityAvailable;

    /// <summary>The current Activity is being destroyed; the flag distinguishes app exit from recreation.</summary>
    public static event Action<MainActivity, bool>? ActivityDestroyed;

    /// <summary>
    /// Both edges of Android's top-resumed signal, retained for SS0 diagnostics and Presentation
    /// lifetime handling. The existing return callback below remains the post-play completion seam.
    /// </summary>
    public static event Action<bool>? TopResumedChanged;

    /// <summary>
    /// Invoked when EmuShelf becomes the top-resumed activity again — i.e. the user returned from a
    /// launched emulator. Deliberately uses <c>onTopResumedActivityChanged(true)</c> rather than
    /// <c>onResume</c>: since Android 10 several activities can be resumed at once (the Thor is a
    /// multi-display device), so "am I the one in front" is the correct question. Set once the shell is
    /// shown; may be null before then and after teardown.
    /// </summary>
    public static Action? ReturnedToForeground { get; set; }

    internal static void NotifyActivityAvailable(MainActivity activity) => ActivityAvailable?.Invoke(activity);

    internal static void NotifyActivityDestroyed(MainActivity activity, bool finishing) =>
        ActivityDestroyed?.Invoke(activity, finishing);

    internal static void NotifyTopResumedChanged(bool topResumed) => TopResumedChanged?.Invoke(topResumed);
}
