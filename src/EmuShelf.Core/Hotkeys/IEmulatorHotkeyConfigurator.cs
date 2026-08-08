namespace EmuShelf.Core.Hotkeys;

/// <summary>
/// Writes EmuShelf's keyboard-hotkey scheme into one emulator's own configuration, and reverts it.
/// Implementations own every format-specific detail — file names, section and key names, the keyboard
/// token syntax, and any feature-enable flag a bound action depends on — so the coordinator and the
/// Settings view model never name an emulator.
///
/// Everything here is scoped to configuration only: an implementation never touches a game file, and
/// (per the M40 design) always backs a file up before its first modification and edits it surgically,
/// preserving comments, ordering, unknown keys, and version markers.
/// </summary>
public interface IEmulatorHotkeyConfigurator
{
    /// <summary>Stable integration id of the emulator this configures.</summary>
    string EmulatorId { get; }

    /// <summary>The emulator's display name, for Settings rows and messages.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Static capability: which actions this emulator can express as a keyboard binding, and why not
    /// when it can't. Does not read the machine's config files, so the Settings grid can show it
    /// immediately.
    /// </summary>
    IReadOnlyList<HotkeyActionSupport> DescribeSupport(HotkeyProfile profile);

    /// <summary>
    /// Computes what applying the profile would change, without writing anything. Reads the config
    /// files to report absent/unsupported-format cases and to diff against current values.
    /// </summary>
    HotkeyApplyResult Preview(HotkeyProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the profile into the emulator's configuration, backing up each file before its first
    /// modification. Callers must ensure the emulator is not running first — a running emulator
    /// rewrites its config on exit and would clobber the edit.
    /// </summary>
    HotkeyApplyResult Apply(HotkeyProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Restores the most recent backup this configurator made, or reports there is none.</summary>
    HotkeyApplyResult Revert(CancellationToken cancellationToken = default);
}
