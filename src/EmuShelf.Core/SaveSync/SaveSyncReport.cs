namespace EmuShelf.Core.SaveSync;

/// <summary>The aggregate result of one sync pass over a system's save units.</summary>
/// <param name="Results">The per-unit outcomes, in unit-id order.</param>
public sealed record SaveSyncReport(IReadOnlyList<SaveUnitSyncResult> Results)
{
    public int Uploaded => Count(SaveSyncAction.Upload);

    public int Downloaded => Count(SaveSyncAction.Download);

    public int Unchanged => Count(SaveSyncAction.None);

    /// <summary>Units left alone on purpose, each with a reason.</summary>
    public IReadOnlyList<SaveUnitSyncResult> Skipped =>
        Results.Where(result => result.Action == SaveSyncAction.Skipped).ToList();

    public int Conflicts => Results.Count(result =>
        result.Action is SaveSyncAction.ConflictLocalWins or SaveSyncAction.ConflictRemoteWins);

    private int Count(SaveSyncAction action) => Results.Count(result => result.Action == action);
}
