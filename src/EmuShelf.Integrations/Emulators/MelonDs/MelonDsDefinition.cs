using EmuShelf.Core.Launching;

namespace EmuShelf.Integrations.Emulators.MelonDs;

/// <summary>
/// Standalone melonDS, the DS/DSi emulator, launched with the ROM as one argv entry. Every DS
/// container EmuShelf imports (<c>.nds</c>, <c>.dsi</c>, <c>.srl</c>, and the zipped variants melonDS
/// unpacks itself) is handed over the same way, so no container needs launch-argument handling of its
/// own.
///
/// <para>
/// melonDS ships two channels — tagged releases and the master-branch nightlies — and they are far
/// enough apart in practice (the nightlies are where DSi, save-state format, and config changes land
/// first) that people keep both installed. Each is its own emulator here, so each carries its own
/// executable, launch arguments, and save-folder override, and the DS row's picker offers both.
/// Nintendo DS is also servable by RetroArch, which stays the default for an install that has never
/// chosen; battery saves interoperate across all three (see <c>NintendoDsBatterySaveKey</c>).
/// </para>
/// </summary>
public static class MelonDsDefinition
{
    /// <summary>melonDS release builds.</summary>
    public static EmulatorDefinition Instance { get; } = new(
        "melonds",
        "melonDS",
        ["nds"],
        "\"{GamePath}\"",
        RequiresContentFile: true);

    /// <summary>melonDS nightly (master) builds, configured independently of the release channel.</summary>
    public static EmulatorDefinition Nightly { get; } = new(
        "melonds-nightly",
        "melonDS (nightly)",
        ["nds"],
        "\"{GamePath}\"",
        RequiresContentFile: true);

    /// <summary>Both channels, in picker order.</summary>
    public static IReadOnlyList<EmulatorDefinition> All { get; } = [Instance, Nightly];
}
