namespace EmuShelf.Core.SaveSync;

/// <summary>
/// The last content that was successfully synced for a unit — the shared reference point that
/// lets a later sync tell "changed here", "changed there", and "changed on both" apart without
/// trusting the two machines' clocks.
/// </summary>
/// <param name="UnitId">The unit this baseline describes.</param>
/// <param name="ContentHash">Content hash agreed by both sides at the last successful sync.</param>
/// <param name="ModifiedUtc">The modified time recorded at that sync.</param>
/// <param name="Revision">Monotonic counter incremented each time the baseline advances.</param>
/// <param name="Compatibility">Creator-version identity for guarded content such as save states.</param>
public sealed record SaveUnitBaseline(
    string UnitId,
    string ContentHash,
    DateTimeOffset ModifiedUtc,
    long Revision,
    string? Compatibility = null);
