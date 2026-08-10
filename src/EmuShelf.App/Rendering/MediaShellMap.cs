using EmuShelf.Rendering.Shells;

namespace EmuShelf.App.Rendering;

/// <summary>
/// Maps an EmuShelf system id to the physical medium its games shipped on.
/// </summary>
/// <remarks>
/// This is the app layer's job rather than the renderer's: EmuShelf.Rendering knows about media,
/// not about consoles. Systems absent from this table keep their flat cover on the shelf, which is
/// the deliberate fallback — a generic box would look worse than the cover it replaced.
/// </remarks>
public static class MediaShellMap
{
    private static readonly Dictionary<string, MediaShell> BySystemId = new(StringComparer.Ordinal)
    {
        ["snes"] = MediaShell.SnesCartridge,
        ["gba"] = MediaShell.GbaCartridge,

        // One shell, four consoles: PS2, PS3, GameCube and Wii all shipped in the same
        // 135x190x14mm keep case. PS1 (jewel case), Dreamcast (jewel case) and PSP (UMD case) are
        // genuinely different shapes and stay on flat covers until those shells are authored.
        ["playstation2"] = MediaShell.DiscKeepCase,
        ["playstation3"] = MediaShell.DiscKeepCase,
        ["gamecube"] = MediaShell.DiscKeepCase,
        ["wii"] = MediaShell.DiscKeepCase,
    };

    /// <summary>The shell for a system, or null when it should keep its flat cover.</summary>
    public static MediaShell? ForSystem(string systemId) =>
        BySystemId.TryGetValue(systemId, out var shell) ? shell : null;
}
