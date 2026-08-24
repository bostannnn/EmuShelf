namespace EmuShelf.Core.Input;

/// <summary>
/// Optional capability for an <see cref="IGamepadReader"/> whose input arrives by push (events)
/// rather than needing a continuous poll. Android delivers stick and hat motion as events at the
/// Activity, so its reader can announce each one; a reader that does not implement this (the desktop
/// SDL reader) must be polled continuously because nothing pushes input into it.
/// </summary>
/// <remarks>
/// When the poll loop sees a push source, it can stop ticking once the pad is fully at rest and rely
/// on <see cref="InputReceived"/> to wake it. That matters because the ~60 Hz idle tick is a
/// measurable UI-thread CPU cost on Android — the couch shell's resting fan/battery drain — even
/// though each tick reads nothing and draws nothing. Letting the loop go quiet at rest removes it.
/// </remarks>
public interface IPushGamepadSource
{
    /// <summary>
    /// Raised when fresh input arrives that the poll loop should sample. Fires on the same thread the
    /// reader is polled on (on Android, the main thread, which is Avalonia's UI thread), so the loop
    /// can react to it without marshalling.
    /// </summary>
    event Action? InputReceived;
}
