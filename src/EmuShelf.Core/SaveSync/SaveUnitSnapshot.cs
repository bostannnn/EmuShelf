namespace EmuShelf.Core.SaveSync;

/// <summary>The observed state of one save unit's current content on one side (local or remote).</summary>
/// <param name="UnitId">The unit this snapshot describes.</param>
/// <param name="ContentHash">
/// A content hash of the unit's bytes (for a folder, a deterministic hash over its files). This is
/// the primary evidence for change detection; a wall-clock timestamp is never trusted to decide
/// direction on its own.
/// </param>
/// <param name="ModifiedUtc">Last-modified time, used solely to break a genuine two-sided conflict.</param>
public sealed record SaveUnitSnapshot(string UnitId, string ContentHash, DateTimeOffset ModifiedUtc);
