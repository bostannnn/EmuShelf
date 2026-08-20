namespace EmuShelf.Core.Launching.Android;

/// <summary>
/// How an Android emulator expects the ROM reference to be carried in the launch <c>Intent</c>. The
/// three conventions were measured first-hand on the Thor and cross-checked against Cocoon's live
/// launch log and NeoStation's emulator table (0b, <c>docs/android-port-plan.md</c>). There is no single
/// shape — the handoff is per-emulator, which is exactly why an Android launch definition must carry the
/// slot rather than a single argument string the way the desktop <see cref="EmulatorDefinition"/> does.
/// </summary>
public enum AndroidRomPayloadSlot
{
    /// <summary>
    /// The ROM content URI is the intent's <c>data</c> (<c>setData</c>) under action <c>VIEW</c> —
    /// measured for PPSSPP, Azahar and ARMSX2.
    /// </summary>
    DataUri,

    /// <summary>
    /// The ROM content URI is a string extra named by <see cref="AndroidLaunchProfile.PayloadExtraName"/>
    /// — DuckStation's <c>bootPath</c>, Dolphin's <c>AutoStartFile</c>, WatermelonDS's <c>uri</c>.
    /// </summary>
    ExtraUri,

    /// <summary>
    /// RetroArch's shape: a plain-path <c>ROM</c> extra plus a <c>LIBRETRO</c> core-path extra. RetroArch
    /// is the only target handed a plain path (its <c>targetSdk 28</c> predates scoped storage), and the
    /// only one that needs a core.
    /// </summary>
    RetroArchCore,
}

/// <summary>
/// Maintenance status of a specific Android emulator build. The 0a/0b kill criterion is "at least one
/// <em>maintained</em> emulator per shipped system accepts a constructible handoff", so this is a
/// first-class field, not a note: DuckStation's Android build is frozen (still works, unsupported), and
/// the PS1 plan therefore does not stake itself on it.
/// </summary>
public enum AndroidEmulatorMaintenance
{
    /// <summary>Actively developed as of the recorded measurement.</summary>
    Maintained,

    /// <summary>Still on the store and working, but development has stopped (e.g. DuckStation Android).</summary>
    Frozen,

    /// <summary>No longer distributed or usable; kept only to explain its absence.</summary>
    Abandoned,
}

/// <summary>
/// A per-emulator Android launch definition: which app and activity to target, and how to hand it the
/// ROM. This is the Android analogue of <see cref="EmulatorDefinition"/> and its <c>DefaultLaunchArguments</c>
/// — the same data-not-code intent — but Android launching is component + action + payload-slot rather
/// than an executable and an argument template, so it is a distinct record populated from the measured
/// intents rather than an overload of the desktop one.
/// </summary>
public sealed record AndroidLaunchProfile(
    string Id,
    string DisplayName,
    IReadOnlyList<string> SupportedSystemIds,
    string PackageName,
    string ActivityName,
    AndroidRomPayloadSlot PayloadSlot,
    string? Action = null,
    string? PayloadExtraName = null,
    bool BootOneShot = false,
    bool RequiresOwnTreeGrant = true,
    AndroidEmulatorMaintenance Maintenance = AndroidEmulatorMaintenance.Maintained)
{
    /// <summary>True when this emulator is a launch option for <paramref name="systemId"/>.</summary>
    public bool Supports(string systemId) =>
        SupportedSystemIds.Contains(systemId, StringComparer.Ordinal);
}
