namespace EmuShelf.Core.SaveSync;

/// <summary>
/// Raised when a provider will not materialize a unit in this machine's current configuration —
/// the cloud holds a save from a layout this machine is not using, such as a DuckStation card
/// scheme the local settings do not enable.
/// </summary>
/// <remarks>
/// A per-unit condition, not a failed sync. Two machines are allowed to be configured differently,
/// and the save that cannot be placed here is still safe where it came from; refusing to place it
/// is the fail-closed behaviour working, so every other unit must still reconcile.
/// </remarks>
public class SaveUnitNotResolvableException : ArgumentException
{
    public SaveUnitNotResolvableException(string unitId)
        : base(
            $"The save provider cannot safely materialize unit '{unitId}' in its active configuration.",
            nameof(unitId)) =>
        UnitId = unitId;

    public SaveUnitNotResolvableException(string unitId, string reason)
        : base(reason, nameof(unitId))
    {
        UnitId = unitId;
        UserReason = reason;
    }

    /// <summary>The unit this machine's configuration cannot place.</summary>
    public string UnitId { get; }

    /// <summary>A provider-specific explanation suitable for the sync report, when supplied.</summary>
    public string? UserReason { get; }
}
