namespace EmuShelf.Core.SaveSync;

/// <summary>Enumerates the syncable save units for one emulator/system, read-only.</summary>
public interface ISaveLocationProvider
{
    /// <summary>The stable system id these save units belong to (e.g. <c>playstation2</c>).</summary>
    string SystemId { get; }

    /// <summary>The unit-id namespace owned by this provider (for example <c>pcsx2/</c>).</summary>
    string UnitIdPrefix { get; }

    /// <summary>
    /// Whether this provider owns a unit from the cloud index. Providers with optional namespaces
    /// override this so disabled content remains visible in the cloud without being downloaded.
    /// </summary>
    /// <remarks>
    /// <c>cheats</c> and <c>patches</c> are still excluded although nothing writes them any more: a
    /// remote written by an older build holds those payloads, and a save provider that started
    /// claiming them would try to resolve every one of them to a local save path.
    /// </remarks>
    bool OwnsUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || !unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal))
            return false;
        var localId = unitId[UnitIdPrefix.Length..];
        var separator = localId.IndexOf('/');
        var unitNamespace = separator < 0 ? localId : localId[..separator];
        return unitNamespace is not ("cheats" or "patches" or "states");
    }

    /// <summary>
    /// Selects the owned remote units that should participate in this pass.
    /// </summary>
    IReadOnlyList<SaveUnitSnapshot> SelectRemoteUnits(IReadOnlyList<SaveUnitSnapshot> snapshots) =>
        snapshots.Where(snapshot => OwnsUnit(snapshot.UnitId)).ToArray();

    /// <summary>Compatibility metadata written beside a unit in the cloud index, when required.</summary>
    string? GetCompatibility(string unitId) => null;

    /// <summary>A reason a remote unit cannot be restored here, or null when it is compatible.</summary>
    string? GetRemoteIncompatibilityReason(SaveUnitSnapshot remoteSnapshot) => null;

    /// <summary>The save units currently present for this system on this machine.</summary>
    Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a stable unit id to its allow-listed local location. This is also called for a
    /// remote-only unit before download, so providers must return <see langword="null"/> for an
    /// inactive card/profile, an unsupported layout, or any id they cannot materialize safely.
    /// </summary>
    SaveUnitLocation? ResolveUnit(string unitId);
}
