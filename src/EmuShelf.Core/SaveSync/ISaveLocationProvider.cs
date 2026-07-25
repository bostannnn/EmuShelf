namespace EmuShelf.Core.SaveSync;

/// <summary>Enumerates the syncable save units for one emulator/system, read-only.</summary>
public interface ISaveLocationProvider
{
    /// <summary>The stable system id these save units belong to (e.g. <c>playstation2</c>).</summary>
    string SystemId { get; }

    /// <summary>The unit-id namespace owned by this provider (for example <c>pcsx2/</c>).</summary>
    string UnitIdPrefix { get; }

    /// <summary>The save units currently present for this system on this machine.</summary>
    Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a stable unit id to its allow-listed local location. This is also called for a
    /// remote-only unit before download, so providers must return <see langword="null"/> for an
    /// inactive card/profile, an unsupported layout, or any id they cannot materialize safely.
    /// </summary>
    SaveUnitLocation? ResolveUnit(string unitId);
}
