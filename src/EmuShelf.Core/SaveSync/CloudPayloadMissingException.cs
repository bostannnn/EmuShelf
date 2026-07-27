namespace EmuShelf.Core.SaveSync;

/// <summary>
/// Raised when the remote index describes a unit whose payload is not actually on the remote. It is
/// a per-unit condition, not a failed sync: every other unit in the pass still reconciles, and the
/// machine that still holds the save re-uploads it once the stale index entry is dropped.
/// </summary>
public sealed class CloudPayloadMissingException : IOException
{
    public CloudPayloadMissingException(string unitId)
        : base($"The cloud save payload for '{unitId}' is missing from the remote.") =>
        UnitId = unitId;

    /// <summary>The unit whose payload could not be found.</summary>
    public string UnitId { get; }
}
