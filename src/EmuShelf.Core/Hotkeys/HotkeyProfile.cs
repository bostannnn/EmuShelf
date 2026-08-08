namespace EmuShelf.Core.Hotkeys;

/// <summary>
/// The keyboard-hotkey scheme: one <see cref="HotkeyKey"/> per <see cref="HotkeyAction"/>. A
/// per-emulator configurator translates each key into that emulator's own keyboard-binding token, or
/// reports the action unsupported when the emulator has no such feature. The controller→key step is
/// done once outside the emulators, in a Steam Input layout the user configures (see
/// <c>docs/hotkey-keyboard-scheme.md</c>), so one mapping works for every emulator.
/// </summary>
public sealed class HotkeyProfile
{
    private readonly IReadOnlyDictionary<HotkeyAction, HotkeyKey> _keys;

    public HotkeyProfile(IReadOnlyDictionary<HotkeyAction, HotkeyKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _keys = keys;
    }

    /// <summary>The key bound to an action.</summary>
    public HotkeyKey this[HotkeyAction action] => _keys[action];

    /// <summary>The actions this profile assigns, in enum order.</summary>
    public IReadOnlyList<HotkeyAction> Actions =>
        Enum.GetValues<HotkeyAction>().Where(_keys.ContainsKey).ToArray();

    /// <summary>
    /// The canonical scheme: rewind=<c>R</c>, fast-forward=<c>L</c>, save state=<c>F2</c>,
    /// load state=<c>F4</c>, close game=<c>F8</c>. The keys match RetroArch's own defaults so it needs
    /// almost nothing, and <c>F8</c> is free of conflicts across the emulators checked.
    /// </summary>
    public static HotkeyProfile Default { get; } = new(new Dictionary<HotkeyAction, HotkeyKey>
    {
        [HotkeyAction.CloseGame] = HotkeyKey.F8,
        [HotkeyAction.Rewind] = HotkeyKey.R,
        [HotkeyAction.FastForward] = HotkeyKey.L,
        [HotkeyAction.SaveState] = HotkeyKey.F2,
        [HotkeyAction.LoadState] = HotkeyKey.F4,
    });
}
