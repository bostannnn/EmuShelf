namespace EmuShelf.App.Services;

/// <summary>
/// A logical controller command, matching the documented Steam Input contract so native pad input
/// and Steam-Input keyboard mapping drive exactly the same view-model routing.
/// </summary>
public enum GamepadAction
{
    NavigateUp,
    NavigateDown,
    NavigateLeft,
    NavigateRight,
    Confirm,          // A / Enter
    Cancel,           // B / Escape
    PreviousPlatform, // LB / Ctrl+PageUp
    NextPlatform,     // RB / Ctrl+PageDown
    Search,           // X
    Actions,          // Y
    Menu,             // Start / F10
}
