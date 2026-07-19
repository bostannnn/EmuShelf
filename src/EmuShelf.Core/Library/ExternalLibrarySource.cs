namespace EmuShelf.Core.Library;

/// <summary>
/// A user-selected, read-only external game catalogue. The id identifies the configured source,
/// not a game path, so a later refresh can reconcile source records even after an emulator moves
/// an installed game.
/// </summary>
public sealed record ExternalLibrarySource(
    string Id,
    string SystemId,
    string DisplayName,
    string Location);

/// <summary>One game entry returned by an external library source's own authoritative list.</summary>
public sealed record ExternalLibraryGameEntry(
    string SourceEntryId,
    string Path,
    string Title,
    bool IsAvailable = true,
    GameTitleOrigin TitleOrigin = GameTitleOrigin.Embedded);

/// <summary>Result of reconciling one external source without deleting library records.</summary>
public sealed record ExternalLibraryImportResult(
    IReadOnlyList<long> AddedGameIds,
    int UpdatedCount,
    int MarkedSourceMissingCount)
{
    public int AddedCount => AddedGameIds.Count;
}

/// <summary>
/// Raised when a source entry would claim a path that already belongs to a different library
/// record. The source refresh is rejected before it can partially change provenance.
/// </summary>
public sealed class ExternalLibrarySourceConflictException : Exception
{
    public ExternalLibrarySourceConflictException(string message) : base(message)
    {
    }
}
