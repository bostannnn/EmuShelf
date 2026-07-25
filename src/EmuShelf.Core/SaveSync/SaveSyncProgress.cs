namespace EmuShelf.Core.SaveSync;

/// <summary>Progress for a running sync: which unit is being processed and how far along.</summary>
/// <param name="Completed">Units finished before the one now being processed.</param>
/// <param name="Total">Total units to process this pass.</param>
/// <param name="CurrentUnit">Display name of the unit being processed.</param>
/// <param name="Action">What is happening to the current unit.</param>
public sealed record SaveSyncProgress(int Completed, int Total, string CurrentUnit, SaveSyncAction Action);
