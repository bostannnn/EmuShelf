namespace EmuShelf.Core.SaveSync;

/// <summary>Enumerates the syncable save units for one emulator/system, read-only.</summary>
public interface ISaveLocationProvider
{
    /// <summary>The stable system id these save units belong to (e.g. <c>playstation2</c>).</summary>
    string SystemId { get; }

    /// <summary>
    /// The cloud-key namespace for this system's <em>battery</em> saves. It is the console
    /// <see cref="SystemId"/>, not the emulator, so every emulator that serves the system emits the same
    /// key and their saves interoperate by construction (see DECISIONS 2026-08-21). A provider only
    /// overrides this if its system id is not the desired namespace.
    /// </summary>
    string UnitIdPrefix => SystemId + "/";

    /// <summary>
    /// The cloud-key namespace for this provider's <em>save states</em>, which — unlike battery saves —
    /// stays <em>emulator</em>-scoped (for example <c>duckstation/</c>, <c>retroarch/nds/</c>). Two
    /// emulators for one system can write same-named state files, so the emulator-scoped namespace plus
    /// the state compatibility gate are what keep them apart; folding states into the system namespace
    /// would collide them. State-supporting providers override this with their former emulator prefix;
    /// providers without states can ignore it. The auxiliary save-state provider keys its
    /// <c>states/</c> sub-namespace off this value.
    /// </summary>
    string StateNamespacePrefix => UnitIdPrefix;

    /// <summary>
    /// Whether this provider owns a unit from the cloud index. Providers with optional namespaces
    /// override this so disabled content remains visible in the cloud without being downloaded.
    /// </summary>
    /// <remarks>
    /// This default guards the <em>battery</em> namespace (the system-scoped <see cref="UnitIdPrefix"/>).
    /// Save states, cheats, and patches are all emulator-scoped (under <see cref="StateNamespacePrefix"/>)
    /// and so never appear under <see cref="UnitIdPrefix"/> at all — the <c>states/cheats/patches</c>
    /// check here is therefore a belt-and-braces guard against a battery <em>save</em> whose own name
    /// happens to be exactly one of those words, not the mechanism that keeps states/cheats/patches out
    /// (that is the prefix check on the line above). States are claimed by the auxiliary provider under
    /// their own emulator-scoped prefix; cheats/patches are no longer written at all.
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
