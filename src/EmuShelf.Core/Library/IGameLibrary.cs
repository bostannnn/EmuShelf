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

    /// <summary>The newest games across all systems, ordered newest first and limited in SQL.</summary>
    IReadOnlyList<Game> GetRecentlyAddedGames(int limit);

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
    GameImportResult ReconcileImport(
        string systemId,
        IEnumerable<Game> entries,
        IReadOnlyList<string> suppressedPaths);

    /// <summary>
    /// Reconciles entries from one explicit, emulator-owned external library. It never deletes
    /// local game rows: source entries that disappear are instead retained as unavailable.
    /// </summary>
    ExternalLibraryImportResult ReconcileExternalLibrary(
        ExternalLibrarySource source,
        IReadOnlyList<ExternalLibraryGameEntry> entries);

    /// <summary>Updates the availability flag for a single game.</summary>
    void SetAvailability(long gameId, bool isAvailable);

    /// <summary>Updates availability flags together in one transaction.</summary>
    void SetAvailabilities(IReadOnlyList<GameAvailabilityUpdate> updates);

    /// <summary>Updates the user-visible title for one library entry.</summary>
    void UpdateTitle(long gameId, string title);

    /// <summary>Updates the copied cover path for one library entry.</summary>
    void UpdateCoverPath(long gameId, string? coverPath);

    /// <summary>Gets the persisted default disc id for each derived multi-disc title-set key.</summary>
    IReadOnlyDictionary<string, long> GetDiscSelections();

    /// <summary>Remembers a disc after it has launched successfully.</summary>
    void SetDiscSelection(string titleSetKey, long gameId);

    /// <summary>Removes only the library record. Game and cover files are never touched.</summary>
    void RemoveGame(long gameId);

    /// <summary>Removes only the specified library records in one transaction. Game and cover files are never touched.</summary>
    void RemoveGames(IReadOnlyList<long> gameIds);

    /// <summary>Folders remembered for rescanning, optionally filtered to one system.</summary>
    IReadOnlyList<LibraryFolder> GetLibraryFolders(string? systemId = null);

    /// <summary>Remembers a folder for a system if that exact folder isn't already tracked.</summary>
    void AddLibraryFolder(string systemId, string folderPath);
}
