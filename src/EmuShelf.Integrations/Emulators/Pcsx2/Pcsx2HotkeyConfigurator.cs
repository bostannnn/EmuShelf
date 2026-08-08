using EmuShelf.Core.Hotkeys;

namespace EmuShelf.Integrations.Emulators.Pcsx2;

/// <summary>
/// Writes the keyboard-hotkey scheme into PCSX2's <c>PCSX2.ini</c> <c>[Hotkeys]</c> as
/// <c>Keyboard/&lt;Key&gt;</c> tokens (version gate <c>[UI] SettingsVersion = 1</c>; PCSX2 shares
/// DuckStation's input engine). PCSX2 has <em>no rewind feature</em>, so rewind is reported
/// unsupported rather than bound. Load state uses <c>F4</c>, which displaces the default
/// <c>ToggleFrameLimit = Keyboard/F4</c>; the base's exact-value conflict-clearing unbinds it while
/// leaving modifier chords such as <c>Keyboard/Shift &amp; Keyboard/F8</c> untouched.
/// </summary>
public sealed class Pcsx2HotkeyConfigurator : IniKeyboardHotkeyConfigurator
{
    public Pcsx2HotkeyConfigurator(string configurationDirectory, string backupRoot, Action<string, string>? writeFile = null)
        : base(
            Pcsx2Definition.Instance.Id,
            "PCSX2",
            configurationDirectory,
            [Path.Combine("inis", "PCSX2.ini"), "PCSX2.ini"],
            section: "Hotkeys",
            versionSection: "UI",
            versionKey: "SettingsVersion",
            supportedVersion: "1",
            actionKeys: new Dictionary<HotkeyAction, string?>
            {
                [HotkeyAction.CloseGame] = "ShutdownVM",
                [HotkeyAction.Rewind] = null,
                [HotkeyAction.FastForward] = "HoldTurbo",
                [HotkeyAction.SaveState] = "SaveStateToSlot",
                [HotkeyAction.LoadState] = "LoadStateFromSlot",
            },
            unsupportedReasons: new Dictionary<HotkeyAction, string>
            {
                [HotkeyAction.Rewind] = "PCSX2 has no rewind feature.",
            },
            backupRoot,
            writeFile)
    {
    }
}
