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
            (systemId == "playstation2" ? "playstation2/" : systemId + "/");
    }

    public string SystemId { get; }

    /// <summary>Forces the system-scoped battery prefix instead of deriving it from the first unit.</summary>
    public string? UnitIdPrefixOverride { get; init; }

    public string UnitIdPrefix => UnitIdPrefixOverride ?? _unitIdPrefix;

    /// <summary>
    /// Set to reproduce the real providers' two-namespace split, where save states (and legacy
    /// cheats/patches, and frozen pre-migration battery keys) live under an emulator-scoped prefix
    /// distinct from the system-scoped <see cref="UnitIdPrefix"/> — e.g. <c>"pcsx2/"</c> for a
    /// <c>"playstation2"</c> provider. Null leaves it equal to <see cref="UnitIdPrefix"/>.
    /// </summary>
    public string? StateNamespacePrefixOverride { get; init; }

    public string StateNamespacePrefix => StateNamespacePrefixOverride ?? UnitIdPrefix;

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
