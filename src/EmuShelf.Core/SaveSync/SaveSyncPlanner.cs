namespace EmuShelf.Core.SaveSync;

/// <summary>
/// Decides, for a single save unit, how to reconcile the local and remote copies against the
/// last-synced baseline. Pure and side-effect free, so the rule that protects saves from being
/// lost is exhaustively unit-testable. Content hashes are the evidence for what changed; a
/// modified time only breaks a genuine two-sided conflict and never decides direction alone —
/// PC and Deck clocks drift, and copies rewrite timestamps, so "newest wins" is not trusted.
/// </summary>
public static class SaveSyncPlanner
{
    public static SaveSyncDecision Decide(
        SaveUnitSnapshot? local,
        SaveUnitSnapshot? remote,
        SaveUnitBaseline? baseline)
    {
        if (local is null && remote is null)
            return new SaveSyncDecision(SaveSyncAction.None, "Nothing to sync on either side.");

        if (local is not null && remote is null)
        {
            return new SaveSyncDecision(
                SaveSyncAction.Upload,
                baseline is null
                    ? "New local save not yet on the remote."
                    : "Remote copy is missing; restoring it from local.");
        }

        if (local is null && remote is not null)
        {
            return new SaveSyncDecision(
                SaveSyncAction.Download,
                baseline is null
                    ? "New remote save not present locally."
                    : "Local copy is missing; restoring it from the remote.");
        }

        var here = local!;
        var there = remote!;

        if (ContentEquals(here.ContentHash, there.ContentHash))
            return new SaveSyncDecision(SaveSyncAction.None, "Local and remote content already match.");

        var localChanged = baseline is null || !ContentEquals(here.ContentHash, baseline.ContentHash);
        var remoteChanged = baseline is null || !ContentEquals(there.ContentHash, baseline.ContentHash);

        if (localChanged && !remoteChanged)
            return new SaveSyncDecision(SaveSyncAction.Upload, "Local changed since the last sync; remote is unchanged.");

        if (!localChanged && remoteChanged)
            return new SaveSyncDecision(SaveSyncAction.Download, "Remote changed since the last sync; local is unchanged.");

        // Both sides diverged from the baseline (or there is no shared baseline and the two
        // sides differ). This is a real conflict: keep the newer copy active and preserve the
        // other as a backup so nothing is ever lost.
        var noBaseline = baseline is null;
        if (here.ModifiedUtc >= there.ModifiedUtc)
        {
            return new SaveSyncDecision(
                SaveSyncAction.ConflictLocalWins,
                noBaseline
                    ? "Both sides have a save with no shared history; local is newer."
                    : "Both sides changed since the last sync; local is newer.");
        }

        return new SaveSyncDecision(
            SaveSyncAction.ConflictRemoteWins,
            noBaseline
                ? "Both sides have a save with no shared history; remote is newer."
                : "Both sides changed since the last sync; remote is newer.");
    }

    private static bool ContentEquals(string first, string second) =>
        string.Equals(first, second, StringComparison.Ordinal);
}
