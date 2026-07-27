namespace EmuShelf.Core.SaveSync;

/// <summary>Progress for a running sync: which unit is being processed and how far along.</summary>
/// <param name="Completed">Units finished before the one now being processed.</param>
/// <param name="Total">Total units to process this pass.</param>
/// <param name="CurrentUnit">Display name of the unit being processed.</param>
/// <param name="Action">What is happening to the current unit.</param>
/// <param name="Phase">Which part of the pass is running.</param>
/// <param name="TransferPercent">
/// How far the cloud transfer has got, 0-100, or null while it is not known — the provider reports
/// it only once bytes start moving. Only meaningful in <see cref="SaveSyncPhase.Transferring"/>.
/// </param>
public sealed record SaveSyncProgress(
    int Completed,
    int Total,
    string CurrentUnit,
    SaveSyncAction Action,
    SaveSyncPhase Phase = SaveSyncPhase.Reconciling,
    int? TransferPercent = null);
