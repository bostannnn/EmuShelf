using EmuShelf.Core.Systems;

namespace EmuShelf.Core.Importing;

/// <summary>
/// Per-system file recognition: which systems a file might belong to (for the
/// "suggest a system, user confirms" flow) and whether a file counts as a game
/// for a given system (for folder scanning).
///
/// M3 ships a minimal extension-based implementation. M4 replaces it with the
/// authoritative format rules — .cue/.bin de-duplication, .m3u playlists, and
/// GameCube/Wii disc-header disambiguation — behind this same interface.
/// </summary>
public interface IGameImportRules
{
    /// <summary>Systems whose formats plausibly match this path, best guess first. Empty if none.</summary>
    IReadOnlyList<GameSystem> SuggestSystems(string path);

    /// <summary>Whether this path should be imported as a game for <paramref name="system"/>.</summary>
    bool IsCandidate(string path, GameSystem system);
}
