using Avalonia.Input;

namespace EmuShelf.App.Services;

/// <summary>
/// The one keyboard → <see cref="GamepadAction"/> mapping shared by every head's couch input surface.
/// It encodes the documented Steam Input keyboard contract (LB/RB as Ctrl+PageUp/PageDown, A/B as
/// Enter/Escape, X/Y/Start as X/Y/F10, D-pad as the arrow keys) so native pad input, Steam-Input
/// keyboard mapping, and a plain hardware keyboard all drive exactly the same view-model routing.
/// Extracted from the desktop window so the Android head reuses it verbatim rather than forking it.
/// Rotation (Shift+Arrow / Shift+Enter) is deliberately not here: it is a desktop keyboard nicety the
/// window still owns, not part of the logical controller contract.
/// </summary>
public static class GamepadKeyMap
{
    /// <summary>The logical action for a key press, or null if the key is not part of the contract.</summary>
    public static GamepadAction? Map(Key key, KeyModifiers modifiers)
    {
        if (modifiers.HasFlag(KeyModifiers.Control) && key == Key.PageUp)
            return GamepadAction.PreviousPlatform;
        if (modifiers.HasFlag(KeyModifiers.Control) && key == Key.PageDown)
            return GamepadAction.NextPlatform;

        return key switch
        {
            Key.Enter => GamepadAction.Confirm,
            Key.Escape => GamepadAction.Cancel,
            Key.X => GamepadAction.Search,
            Key.Y => GamepadAction.Actions,
            Key.F10 => GamepadAction.Menu,
            Key.Left => GamepadAction.NavigateLeft,
            Key.Right => GamepadAction.NavigateRight,
            Key.Up => GamepadAction.NavigateUp,
            Key.Down => GamepadAction.NavigateDown,
            _ => null,
        };
    }
}
