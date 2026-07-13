namespace EmuShelf.Core.Importing;

/// <summary>
/// Result of resolving descriptors and playlists for one import batch. Entry paths
/// are persisted; suppressed paths are removed from the same system's library while
/// their underlying files remain untouched.
/// </summary>
public sealed record GameEntrySelection(
    IReadOnlyList<string> EntryPaths,
    IReadOnlyList<string> SuppressedPaths)
{
    public static GameEntrySelection Empty { get; } = new([], []);
}
