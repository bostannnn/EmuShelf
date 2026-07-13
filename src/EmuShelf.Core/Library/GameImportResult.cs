namespace EmuShelf.Core.Library;

/// <summary>The rows inserted by one import reconciliation.</summary>
public sealed record GameImportResult(IReadOnlyList<long> AddedGameIds)
{
    public int AddedCount => AddedGameIds.Count;

    public static GameImportResult Empty { get; } = new([]);
}
