using EmuShelf.Core.Hotkeys;

namespace EmuShelf.Integrations.Emulators.DuckStation;

/// <summary>
/// Writes the keyboard-hotkey scheme into DuckStation's <c>settings.ini</c> <c>[Hotkeys]</c> as
/// <c>Keyboard/&lt;Key&gt;</c> tokens (version gate <c>[Main] SettingsVersion = 3</c>). Rewind exists
/// here but is off by default, so binding it also flips <c>[Main] RewindEnable</c> — a bound key to a
/// disabled feature does nothing. Load state uses <c>F4</c>, which displaces the default
/// <c>SelectNextSaveStateSlot = Keyboard/F4</c>; the base's conflict-clearing unbinds it.
/// </summary>
public sealed class DuckStationHotkeyConfigurator : IniKeyboardHotkeyConfigurator
{
    public DuckStationHotkeyConfigurator(string configurationDirectory, string backupRoot, Action<string, string>? writeFile = null)
        : base(
            DuckStationDefinition.Instance.Id,
            "DuckStation",
            configurationDirectory,
            ["settings.ini"],
            section: "Hotkeys",
            versionSection: "Main",
            versionKey: "SettingsVersion",
            supportedVersion: "3",
            actionKeys: new Dictionary<HotkeyAction, string?>
            {
                [HotkeyAction.CloseGame] = "PowerOff",
                [HotkeyAction.Rewind] = "Rewind",
                [HotkeyAction.FastForward] = "FastForward",
                [HotkeyAction.SaveState] = "SaveSelectedSaveState",
                [HotkeyAction.LoadState] = "LoadSelectedSaveState",
            },
            unsupportedReasons: new Dictionary<HotkeyAction, string>(),
            backupRoot,
            writeFile)
    {
    }

    protected override void ApplyExtraSettings(
        EmulatorConfigDocument document,
        string fileName,
        IReadOnlySet<HotkeyAction> boundActions,
        List<HotkeyChange> changes)
    {
        // Rewind is off by default and costs memory; without this the R key is a dead binding.
        if (boundActions.Contains(HotkeyAction.Rewind))
            SetFlag(document, fileName, "Main", "RewindEnable", "true", changes);
    }
}
