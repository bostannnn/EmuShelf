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

    public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_units);

    public SaveUnitLocation? ResolveUnit(string unitId) => null;
}
