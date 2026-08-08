namespace EmuShelf.Core.Hotkeys;

/// <summary>
/// The emulator functions EmuShelf offers as a uniform keyboard-hotkey scheme, in the order the
/// Settings surface presents them. Each maps to one <see cref="HotkeyKey"/> in a
/// <see cref="HotkeyProfile"/>; a per-emulator configurator translates it into that emulator's own
/// keyboard binding, or reports the action unsupported when the emulator has no such feature.
/// </summary>
public enum HotkeyAction
{
    /// <summary>Close the running game / shut the emulated system down.</summary>
    CloseGame,

    /// <summary>Rewind gameplay (only DuckStation, RetroArch, and PPSSPP have this feature).</summary>
    Rewind,

    /// <summary>Fast-forward / uncap the emulation speed while held.</summary>
    FastForward,

    /// <summary>Save a save state to the current slot.</summary>
    SaveState,

    /// <summary>Load a save state from the current slot.</summary>
    LoadState,
}
