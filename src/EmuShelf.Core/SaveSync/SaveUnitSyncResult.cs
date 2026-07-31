namespace EmuShelf.Core.SaveSync;

/// <summary>The outcome of reconciling a single save unit.</summary>
/// <param name="UnitId">The unit that was processed.</param>
/// <param name="Action">The action that was taken.</param>
/// <param name="Reason">Why that action was taken.</param>
public sealed record SaveUnitSyncResult(string UnitId, SaveSyncAction Action, string Reason);
