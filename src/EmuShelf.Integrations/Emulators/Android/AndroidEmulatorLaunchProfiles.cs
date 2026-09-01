using EmuShelf.Core.Launching.Android;

namespace EmuShelf.Integrations.Emulators.Android;

/// <summary>
/// The Android launch definitions, one per emulator build, populated from intents measured first-hand on
/// the AYN Thor and corroborated against Cocoon's live launch log and NeoStation's emulator table
/// (Milestone 0b, <c>docs/android-port-plan.md</c>). This is the Android counterpart of
/// <see cref="Emulators.KnownEmulators"/>: it does not map one-to-one onto the desktop emulator set,
/// because Android's PS2 (ARMSX2) and DS (WatermelonDS) emulators have no desktop entry, and the desktop
/// PS2 emulator (PCSX2) is a different application.
///
/// Every package listed here must also appear in the Android head's <c>&lt;queries&gt;</c> manifest block,
/// or presence detection returns "not installed" on API 30+ (Milestone B).
/// </summary>
public static class AndroidEmulatorLaunchProfiles
{
    /// <summary>PS1 — DuckStation. Frozen Android build; boots via <c>EmulationActivity</c> + <c>bootPath</c>.</summary>
    public static AndroidLaunchProfile DuckStation { get; } = new(
        "android.duckstation",
        "duckstation",
        "DuckStation",
        ["playstation"],
        PackageName: "com.github.stenzek.duckstation",
        ActivityName: "com.github.stenzek.duckstation.EmulationActivity",
        PayloadSlot: AndroidRomPayloadSlot.ExtraUri,
        Action: null,
        PayloadExtraName: "bootPath",
        BootOneShot: true,
        Maintenance: AndroidEmulatorMaintenance.Frozen);

    /// <summary>PS2 — ARMSX2 (PCSX2 fork). VIEW + content URI as data; declares all-files access.</summary>
    public static AndroidLaunchProfile Armsx2 { get; } = new(
        "android.armsx2",
        "armsx2",
        "ARMSX2",
        ["playstation2"],
        PackageName: "com.armsx2",
        ActivityName: "com.armsx2.Main",
        PayloadSlot: AndroidRomPayloadSlot.DataUri,
        Action: AndroidIntentActions.View);

    /// <summary>GameCube / Wii — Dolphin. MAIN + <c>AutoStartFile</c> content-URI extra.</summary>
    public static AndroidLaunchProfile Dolphin { get; } = new(
        "android.dolphin",
        "dolphin",
        "Dolphin",
        ["gamecube", "wii"],
        PackageName: "org.dolphinemu.dolphinemu",
        ActivityName: "org.dolphinemu.dolphinemu.ui.main.MainActivity",
        PayloadSlot: AndroidRomPayloadSlot.ExtraUri,
        Action: AndroidIntentActions.Main,
        PayloadExtraName: "AutoStartFile");

    /// <summary>PSP — PPSSPP. VIEW + content URI as data.</summary>
    public static AndroidLaunchProfile Ppsspp { get; } = new(
        "android.ppsspp",
        "ppsspp",
        "PPSSPP",
        ["psp"],
        PackageName: "org.ppsspp.ppsspp",
        ActivityName: "org.ppsspp.ppsspp.PpssppActivity",
        PayloadSlot: AndroidRomPayloadSlot.DataUri,
        Action: AndroidIntentActions.View);

    /// <summary>3DS — Azahar (Citra fork; keeps Citra's activity name). VIEW + content URI as data.</summary>
    public static AndroidLaunchProfile Azahar { get; } = new(
        "android.azahar",
        "azahar",
        "Azahar",
        ["3ds"],
        PackageName: "org.azahar_emu.azahar",
        ActivityName: "org.citra.citra_emu.activities.EmulationActivity",
        PayloadSlot: AndroidRomPayloadSlot.DataUri,
        Action: AndroidIntentActions.View,
        // Citra's single EmulationActivity re-foregrounds its existing task instead of loading the new ROM
        // when launched again from recents (the "3DS game does nothing, no error" report). CLEAR_TASK +
        // CLEAR_TOP force a fresh start — matching every Citra-family entry in NeoStation's and Cocoon's configs.
        ClearTaskOnLaunch: true);

    /// <summary>DS — WatermelonDS (melonDS fork; kept melonDS's package id). Custom action + <c>uri</c> extra.</summary>
    public static AndroidLaunchProfile WatermelonDs { get; } = new(
        "android.watermelonds",
        "watermelonds",
        "WatermelonDS",
        ["nds"],
        PackageName: "me.magnum.melondualds",
        ActivityName: "me.magnum.melonds.ui.emulator.EmulatorActivity",
        PayloadSlot: AndroidRomPayloadSlot.ExtraUri,
        Action: "me.magnum.melondualds.LAUNCH_ROM",
        PayloadExtraName: "uri");

    /// <summary>
    /// DS — standalone melonDS, the app WatermelonDS is forked from, so the same handoff:
    /// <c>&lt;applicationId&gt;.LAUNCH_ROM</c> (its manifest declares the action as
    /// <c>${applicationId}.LAUNCH_ROM</c>) plus the ROM content URI in the <c>uri</c> extra
    /// (<c>EmulatorActivity.KEY_URI</c>). The activity also folds <c>intent.data</c> into that same key,
    /// so the data-URI form works too; the extra is used here because it is the shape already proven on
    /// the Thor by WatermelonDS.
    /// </summary>
    public static AndroidLaunchProfile MelonDs { get; } = new(
        "android.melonds",
        "melonds",
        "melonDS",
        ["nds"],
        PackageName: "me.magnum.melonds",
        ActivityName: "me.magnum.melonds.ui.emulator.EmulatorActivity",
        PayloadSlot: AndroidRomPayloadSlot.ExtraUri,
        Action: "me.magnum.melonds.LAUNCH_ROM",
        PayloadExtraName: "uri");

    /// <summary>
    /// DS — melonDS nightly. Its own application id (the <c>nightly</c> flavor's <c>.nightly</c>
    /// <c>applicationIdSuffix</c>), so both channels can be installed side by side, which is the whole
    /// reason it is a separate profile. The activity <em>class</em> keeps the base package name, and the
    /// action follows the suffixed application id.
    /// </summary>
    public static AndroidLaunchProfile MelonDsNightly { get; } = new(
        "android.melonds.nightly",
        "melonds-nightly",
        "melonDS (nightly)",
        ["nds"],
        PackageName: "me.magnum.melonds.nightly",
        ActivityName: "me.magnum.melonds.ui.emulator.EmulatorActivity",
        PayloadSlot: AndroidRomPayloadSlot.ExtraUri,
        Action: "me.magnum.melonds.nightly.LAUNCH_ROM",
        PayloadExtraName: "uri");

    /// <summary>
    /// RetroArch — the only plain-path target (its <c>targetSdk 28</c> predates scoped storage, so it
    /// holds all-files and needs no tree grant). VIEW + <c>ROM</c> (path) + <c>LIBRETRO</c> (core path).
    /// Backs the systems its libretro cores cover; the active per-system profile decides when it is used.
    /// </summary>
    public static AndroidLaunchProfile RetroArch { get; } = new(
        "android.retroarch",
        "retroarch",
        "RetroArch",
        ["playstation", "megadrive", "nds", "gba", "snes", "nes", "dreamcast", "arcade", "gbc"],
        PackageName: "com.retroarch.aarch64",
        ActivityName: "com.retroarch.browser.retroactivity.RetroActivityFuture",
        PayloadSlot: AndroidRomPayloadSlot.RetroArchCore,
        Action: AndroidIntentActions.View);

    /// <summary>Every Android launch profile, in the order the matrix was measured.</summary>
    public static IReadOnlyList<AndroidLaunchProfile> All { get; } =
    [
        DuckStation,
        Armsx2,
        Dolphin,
        Ppsspp,
        Azahar,
        // WatermelonDS stays first for DS: it is the measured, on-device-proven build, and the first
        // profile for a system is the launch default when nothing has been selected.
        WatermelonDs,
        MelonDs,
        MelonDsNightly,
        RetroArch,
    ];

    /// <summary>Distinct emulator package names — the source of truth for the manifest <c>&lt;queries&gt;</c> block.</summary>
    public static IReadOnlyList<string> AllPackageNames { get; } =
        All.Select(profile => profile.PackageName).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The launch profiles that can serve <paramref name="systemId"/>, maintained builds first.</summary>
    public static IReadOnlyList<AndroidLaunchProfile> ForSystem(string systemId) =>
        ForSystem(systemId, preferredSelectionId: null);

    /// <summary>
    /// The launch profiles that can serve <paramref name="systemId"/>. A profile explicitly selected
    /// in settings is tried first; remaining fallbacks retain the maintained-first order. Persisted
    /// selection ids deliberately stay short and cross-platform while the internal Android profile ids
    /// continue to identify concrete launch definitions.
    /// </summary>
    public static IReadOnlyList<AndroidLaunchProfile> ForSystem(
        string systemId,
        string? preferredSelectionId)
    {
        return All.Where(profile => profile.Supports(systemId))
            .OrderBy(profile => string.Equals(
                profile.SelectionId,
                preferredSelectionId,
                StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(profile => profile.Maintenance)
            .ToList();
    }
}
