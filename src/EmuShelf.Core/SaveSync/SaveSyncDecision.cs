namespace EmuShelf.Core.SaveSync;

/// <summary>A planner verdict for one unit: what to do and a human-readable why.</summary>
/// <param name="Action">The action to take for the unit.</param>
/// <param name="Reason">A short, user-facing explanation of the verdict.</param>
public sealed record SaveSyncDecision(SaveSyncAction Action, string Reason)
{
    /// <summary>True when the two sides diverged and one had to be kept as a backup.</summary>
    public bool IsConflict =>
        Action is SaveSyncAction.ConflictLocalWins or SaveSyncAction.ConflictRemoteWins;
}
