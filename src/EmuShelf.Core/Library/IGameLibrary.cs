namespace EmuShelf.Core.Library;

/// <summary>
/// Persistence for library entries and the folders they were imported from.
/// Paths are passed and returned as absolute; the implementation handles the
/// portable relative-path storage transparently.
/// </summary>
public interface IGameLibrary
{
    /// <summary>All games, optionally filtered to one system, ordered by title.</summary>
    IReadOnlyList<Game> GetGames(string? systemId = null);

    /// <summary>
    /// Inserts games that aren't already present (matched by path). Returns the
    /// number actually added. Existing entries are left untouched.
    /// </summary>
    int AddGames(IEnumerable<Game> games);

    /// <summary>
    /// Atomically inserts new entries and removes descriptor/playlist component rows
    /// from the specified system. Only library records are removed; game files are
    /// never modified. Returns the number of entries actually added.
    /// </summary>
    int ReconcileImport(
        string systemId,
        IEnumerable<Game> entries,
        IReadOnlyList<string> suppressedPaths);

    /// <summary>Updates the availability flag for a single game.</summary>
    void SetAvailability(long gameId, bool isAvailable);

    /// <summary>Folders remembered for rescanning, optionally filtered to one system.</summary>
    IReadOnlyList<LibraryFolder> GetLibraryFolders(string? systemId = null);

    /// <summary>Remembers a folder for a system if that exact folder isn't already tracked.</summary>
    void AddLibraryFolder(string systemId, string folderPath);
}
