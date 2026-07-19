using EmuShelf.Core.Systems;

namespace EmuShelf.Core.Importing;

/// <summary>
/// Per-system file recognition for the "suggest a system, user confirms" flow
/// and folder scanning.
///
/// Implementations also collapse related files so descriptor/playlist entries win
/// over the disc components they reference.
/// </summary>
public interface IGameImportRules
{
    /// <summary>
    /// Inspects one file and returns all system matches and ordered suggestions.
    /// This may perform file I/O and must be called off the UI thread.
    /// </summary>
    GameFileAnalysis AnalyzeFile(string path);

    /// <summary>
    /// Whether a file should be discovered automatically during a
    /// folder scan. This is intentionally stricter than an explicit user pick.
    /// </summary>
    bool IsFolderCandidate(string path, GameSystem system);

    /// <summary>
    /// Selects the launchable game entries from accepted paths and reports component
    /// paths referenced by descriptors or playlists so previously imported components
    /// can also be suppressed.
    /// </summary>
    GameEntrySelection SelectGameEntries(
        IReadOnlyList<string> candidates,
        GameSystem system);

    /// <summary>
    /// Reads small, format-specific embedded evidence for an accepted entry. Implementations
    /// must be read-only and return <see cref="GameImportMetadata.Empty"/> when no trustworthy
    /// evidence is available. The default keeps existing systems independent of this optional
    /// import-time enrichment path.
    /// </summary>
    GameImportMetadata ReadImportMetadata(string path, GameSystem system) =>
        GameImportMetadata.Empty;
}
