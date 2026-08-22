using EmuShelf.Core.Library;

namespace EmuShelf.Integrations.Emulators.Android;

/// <summary>
/// Chooses the SAF grant root to hand <see cref="AndroidLaunchResolver"/> for a game: the remembered
/// <see cref="LibraryFolder"/> the game was imported from. On Android that folder is, in the normal
/// setup, the very folder the user also granted the emulator (e.g. <c>roms/psx</c>) — so scoping the
/// launch URI's tree to it produces a URI the emulator's own persisted prefix grant authorises.
///
/// This is what fixes nested multi-disc launches: without it the resolver falls back to the game file's
/// own parent folder (e.g. <c>roms/psx/Metal Gear Solid …</c>), which is <em>not</em> a tree the emulator
/// holds a grant to, and the emulator is denied reading it (verified on the Thor: a URI tree-scoped to
/// the game's sub-folder threw <c>SecurityException: Permission Denial</c>, while the same document
/// tree-scoped to <c>roms/psx</c> booted the game).
/// </summary>
public static class AndroidLibraryGrantRoot
{
    /// <summary>
    /// The remembered library folder that contains <paramref name="gamePath"/>, or null when none does.
    /// When several nest, the most specific (longest) ancestor wins. The resolver re-validates the choice
    /// (same volume, genuine ancestor) and ignores it if it does not hold, so a stale record is harmless.
    /// </summary>
    public static string? ForGame(IEnumerable<LibraryFolder> libraryFolders, string gamePath)
    {
        ArgumentNullException.ThrowIfNull(libraryFolders);
        if (string.IsNullOrEmpty(gamePath))
            return null;

        string? best = null;
        foreach (var folder in libraryFolders)
        {
            var path = folder?.Path;
            if (string.IsNullOrEmpty(path))
                continue;

            if (IsAncestorOrSelf(path, gamePath) && (best is null || path.Length > best.Length))
                best = path;
        }

        return best;
    }

    // True when 'ancestor' is the same directory as, or a parent of, 'path'. Both are absolute Android
    // paths ('/'-separated); a trailing slash on the folder is tolerated.
    private static bool IsAncestorOrSelf(string ancestor, string path)
    {
        var trimmed = ancestor.TrimEnd('/');
        if (trimmed.Length == 0)
            return true;

        return path.Equals(trimmed, StringComparison.Ordinal) ||
               path.StartsWith(trimmed + "/", StringComparison.Ordinal);
    }
}
