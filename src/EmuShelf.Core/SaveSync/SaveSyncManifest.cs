namespace EmuShelf.Core.SaveSync;

/// <summary>
/// The per-unit record of what was last synced, persisted locally and mirrored in the cloud.
/// Immutable: <see cref="With"/> returns an updated copy so a failed step never half-writes it.
/// </summary>
public sealed class SaveSyncManifest
{
    private readonly Dictionary<string, SaveUnitBaseline> _baselines;

    public SaveSyncManifest()
        : this(Array.Empty<SaveUnitBaseline>())
    {
    }

    public SaveSyncManifest(IEnumerable<SaveUnitBaseline> baselines)
    {
        _baselines = baselines.ToDictionary(baseline => baseline.UnitId, StringComparer.Ordinal);
    }

    /// <summary>All recorded baselines, ordered by unit id for a stable, diff-friendly file.</summary>
    public IReadOnlyList<SaveUnitBaseline> Baselines =>
        _baselines.Values.OrderBy(baseline => baseline.UnitId, StringComparer.Ordinal).ToList();

    /// <summary>The last-synced baseline for a unit, or null if it has never synced.</summary>
    public SaveUnitBaseline? Get(string unitId) =>
        _baselines.TryGetValue(unitId, out var baseline) ? baseline : null;

    /// <summary>Returns a copy with <paramref name="baseline"/> added or replaced.</summary>
    public SaveSyncManifest With(SaveUnitBaseline baseline)
    {
        var next = new Dictionary<string, SaveUnitBaseline>(_baselines, StringComparer.Ordinal)
        {
            [baseline.UnitId] = baseline,
        };
        return new SaveSyncManifest(next.Values);
    }
}
