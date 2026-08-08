namespace EmuShelf.Core.Hotkeys;

/// <summary>The overall outcome of a preview, apply, or revert against one emulator's configuration.</summary>
public enum HotkeyApplyStatus
{
    /// <summary>Changes were made (Apply/Revert) or would be made (Preview).</summary>
    Changed,

    /// <summary>The configuration already matched the profile, so nothing needed to change.</summary>
    Unchanged,

    /// <summary>Refused because the target emulator is currently running and would clobber the edit.</summary>
    EmulatorRunning,

    /// <summary>No settings file / user directory was found for this emulator on this machine.</summary>
    ConfigurationNotFound,

    /// <summary>The settings file is not the format/version this configurator was written against.</summary>
    UnsupportedFormat,

    /// <summary>The file could not be read or written (I/O or parse error); details in the diagnostic.</summary>
    Failed,
}

/// <summary>Whether one action was bound during a preview/apply, and why not when it wasn't.</summary>
public enum HotkeyBindingStatus
{
    /// <summary>The key was written (or, in a preview, would be written) for this action.</summary>
    Bound,

    /// <summary>The emulator has no such feature, so nothing was bound (e.g. rewind on PCSX2).</summary>
    Unsupported,
}

/// <summary>One action's binding result within a <see cref="HotkeyApplyResult"/>.</summary>
/// <param name="Action">The action.</param>
/// <param name="Status">Whether it was bound or unsupported.</param>
/// <param name="Key">The key label (e.g. "F2"), always present.</param>
/// <param name="Detail">The reason when not bound; null when bound.</param>
public sealed record HotkeyBindingResult(
    HotkeyAction Action,
    HotkeyBindingStatus Status,
    string Key,
    string? Detail = null);

/// <summary>One concrete key change a preview/apply makes to a configuration file.</summary>
/// <param name="File">The file name (not full path) the change lands in.</param>
/// <param name="Section">The INI section, or null for a section-less config (RetroArch).</param>
/// <param name="Key">The setting key.</param>
/// <param name="PreviousValue">The value before the change, or null when the key was absent.</param>
/// <param name="NewValue">The value after the change.</param>
public sealed record HotkeyChange(
    string File,
    string? Section,
    string Key,
    string? PreviousValue,
    string NewValue)
{
    /// <summary>A one-line human description, e.g. "Hotkeys.ini [Hotkeys] Rewind: (unset) → …".</summary>
    public string Describe()
    {
        var location = Section is null ? File : $"{File} [{Section}]";
        var previous = PreviousValue ?? "(unset)";
        return $"{location} {Key}: {previous} → {NewValue}";
    }
}

/// <summary>
/// The result of previewing, applying, or reverting the hotkey profile against one emulator. Carries
/// the per-action outcomes and the concrete file changes, so the Settings surface can show both an
/// honest capability summary and an exact preview diff.
/// </summary>
public sealed record HotkeyApplyResult(
    HotkeyApplyStatus Status,
    IReadOnlyList<HotkeyBindingResult> Bindings,
    IReadOnlyList<HotkeyChange> Changes,
    string? Diagnostic = null)
{
    private static readonly IReadOnlyList<HotkeyBindingResult> NoBindings = [];
    private static readonly IReadOnlyList<HotkeyChange> NoChanges = [];

    public static HotkeyApplyResult ConfigurationNotFound(string diagnostic) =>
        new(HotkeyApplyStatus.ConfigurationNotFound, NoBindings, NoChanges, diagnostic);

    public static HotkeyApplyResult UnsupportedFormat(string diagnostic) =>
        new(HotkeyApplyStatus.UnsupportedFormat, NoBindings, NoChanges, diagnostic);

    public static HotkeyApplyResult EmulatorRunning(string diagnostic) =>
        new(HotkeyApplyStatus.EmulatorRunning, NoBindings, NoChanges, diagnostic);

    public static HotkeyApplyResult Failed(string diagnostic) =>
        new(HotkeyApplyStatus.Failed, NoBindings, NoChanges, diagnostic);
}
