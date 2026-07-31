namespace EmuShelf.Core.SaveSync;

/// <summary>
/// How far a cloud transfer has got, in saves rather than only in bytes.
/// </summary>
/// <param name="CompletedUnits">
/// Saves transferred so far. Saves in the batch currently uploading are counted as they go, so this
/// advances continuously rather than only when a batch commits.
/// </param>
/// <param name="TotalUnits">Saves this transfer will move in total.</param>
/// <param name="Percent">
/// Overall completion, 0-100. Derived from the save count and the current batch's byte progress,
/// not from bytes alone: a provider that meters per file spends nearly all of a mixed transfer's
/// time on its smallest saves, so a byte percentage races ahead and then appears to stall for the
/// entire small-file tail.
/// </param>
public sealed record SaveTransferProgress(int CompletedUnits, int TotalUnits, int Percent);
