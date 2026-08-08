namespace EmuShelf.Core.Hotkeys;

/// <summary>
/// Whether an emulator can express one <see cref="HotkeyAction"/> as a keyboard binding at all —
/// static capability, independent of the machine's config files. Rewind, for instance, is
/// unsupported on PCSX2 and Dolphin because those emulators have no rewind feature, so binding a
/// key to it would be a dead key. The Settings surface uses this to show an honest per-emulator
/// capability grid before anything is applied.
/// </summary>
/// <param name="Action">The action described.</param>
/// <param name="IsSupported">True when this emulator has the feature and can bind it to a key.</param>
/// <param name="Reason">When unsupported, a short human explanation; null when supported.</param>
public sealed record HotkeyActionSupport(HotkeyAction Action, bool IsSupported, string? Reason)
{
    public static HotkeyActionSupport Supported(HotkeyAction action) => new(action, true, null);

    public static HotkeyActionSupport Unsupported(HotkeyAction action, string reason) =>
        new(action, false, reason);
}
