namespace EmuShelf.Core.Hotkeys;

/// <summary>
/// A keyboard key EmuShelf binds an emulator action to. The uniform keyboard scheme (see
/// <c>docs/hotkey-keyboard-scheme.md</c>) uses these same five keys for every emulator, so one Steam
/// Input layout translates a controller chord into these keys once, outside the emulators, and every
/// emulator recognizes them. Keyboard keys are identical on every controller, which is what makes the
/// scheme portable where the original controller-chord approach was driver-specific and fragile.
/// </summary>
public enum HotkeyKey
{
    /// <summary>The <c>R</c> key — rewind (matches RetroArch's default).</summary>
    R,

    /// <summary>The <c>L</c> key — fast-forward (matches RetroArch's default).</summary>
    L,

    /// <summary>The <c>F2</c> function key — save state.</summary>
    F2,

    /// <summary>The <c>F4</c> function key — load state.</summary>
    F4,

    /// <summary>The <c>F8</c> function key — close game (free of conflicts across the emulators checked).</summary>
    F8,
}

/// <summary>Label helpers for <see cref="HotkeyKey"/>.</summary>
public static class HotkeyKeyNames
{
    /// <summary>
    /// The key's canonical label (<c>R</c>, <c>L</c>, <c>F2</c>, <c>F4</c>, <c>F8</c>). This is both the
    /// human-facing label a Settings row shows and the token stem the configurators build their
    /// emulator-specific bindings from (e.g. DuckStation <c>Keyboard/F2</c>, RetroArch <c>"f2"</c>).
    /// </summary>
    public static string Label(this HotkeyKey key) => key switch
    {
        HotkeyKey.R => "R",
        HotkeyKey.L => "L",
        HotkeyKey.F2 => "F2",
        HotkeyKey.F4 => "F4",
        HotkeyKey.F8 => "F8",
        _ => key.ToString(),
    };
}
