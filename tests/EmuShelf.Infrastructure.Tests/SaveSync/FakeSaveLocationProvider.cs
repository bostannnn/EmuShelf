using EmuShelf.Core.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

/// <summary>A save-location provider that returns a fixed set of units for one system.</summary>
internal sealed class FakeSaveLocationProvider : ISaveLocationProvider
{
    private readonly IReadOnlyList<SaveUnit> _units;
    private readonly string _unitIdPrefix;

    public FakeSaveLocationProvider(string systemId, params SaveUnit[] units)
    {
        SystemId = systemId;
        _units = units;
        _unitIdPrefix = units.FirstOrDefault()?.UnitId[..(units[0].UnitId.IndexOf('/') + 1)] ??
            (systemId == "playstation2" ? "pcsx2/" : systemId + "/");
    }

    public string SystemId { get; }

    public string UnitIdPrefix => _unitIdPrefix;

    /// <summary>
    /// Units this provider can materialize on request, keyed by unit id, with the kind
    /// <see cref="ResolveUnit"/> reports. A cloud-only unit must be listed here (and owned by the
    /// prefix) to be exported; anything absent resolves to null, standing in for an unresolvable
    /// remote save.
    /// </summary>
    public Dictionary<string, SaveUnitKind> ResolvableUnitKinds { get; } = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_units);

    public SaveUnitLocation? ResolveUnit(string unitId) =>
        ResolvableUnitKinds.TryGetValue(unitId, out var kind)
            ? new SaveUnitLocation(unitId, unitId, kind)
            : null;
}
