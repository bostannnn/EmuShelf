namespace EmuShelf.Core.Launching;

public interface IEmulatorConfigurationStore
{
    /// <summary>
    /// The <em>active</em> emulator profile's configuration for a system, or null when the system has
    /// no active configuration. Several emulators can support one system; this returns the one the
    /// user selected, so launch, save-sync, and texture resolution all agree on a single emulator.
    /// </summary>
    EmulatorConfiguration? Get(string systemId);

    /// <summary>
    /// Reads every requested system's active configuration in a single pass. The result contains an
    /// entry for each id in <paramref name="systemIds"/> (null when that system has no active
    /// configuration), so callers avoid opening one database connection per system on hot paths such
    /// as Settings. The default falls back to per-system <see cref="Get"/>; backing stores override it
    /// with one query.
    /// </summary>
    IReadOnlyDictionary<string, EmulatorConfiguration?> GetAll(IEnumerable<string> systemIds)
    {
        var result = new Dictionary<string, EmulatorConfiguration?>(StringComparer.Ordinal);
        foreach (var systemId in systemIds)
            result[systemId] = Get(systemId);
        return result;
    }

    /// <summary>The stored configuration for one specific (system, emulator) profile, or null.</summary>
    EmulatorConfiguration? GetForEmulator(string systemId, string emulatorId)
    {
        var active = Get(systemId);
        return active is not null && string.Equals(active.EmulatorId, emulatorId, StringComparison.Ordinal)
            ? active
            : null;
    }

    /// <summary>Every stored profile for one system plus which emulator is active.</summary>
    SystemEmulatorProfiles GetProfiles(string systemId)
    {
        var active = Get(systemId);
        return new SystemEmulatorProfiles(
            systemId,
            active?.EmulatorId,
            active is null ? [] : [active]);
    }

    /// <summary>Every requested system's profiles in one pass, one entry per requested id.</summary>
    IReadOnlyDictionary<string, SystemEmulatorProfiles> GetAllProfiles(IEnumerable<string> systemIds)
    {
        var result = new Dictionary<string, SystemEmulatorProfiles>(StringComparer.Ordinal);
        foreach (var systemId in systemIds)
            result[systemId] = GetProfiles(systemId);
        return result;
    }

    /// <summary>The active emulator id for a system, or null when it has never been configured.</summary>
    string? GetActiveEmulatorId(string systemId) => Get(systemId)?.EmulatorId;

    /// <summary>
    /// Records which emulator profile is active for a system without changing that profile's stored
    /// configuration. The selection persists even when the chosen profile has no configuration yet, so
    /// reopening Settings remembers the choice. The default is a no-op for single-emulator test doubles.
    /// </summary>
    void SetActiveEmulator(string systemId, string emulatorId)
    {
    }

    /// <summary>
    /// Pins a system's <see cref="EmulatorConfiguration.LaunchScreen"/> without touching its active
    /// emulator selection or any other field. Updates every stored profile for the system so the choice
    /// is read back whichever emulator is active; when the system has no stored profile yet it seeds a
    /// minimal one carrying only the preference. Unlike <see cref="Save"/>, it never (re)pins the active
    /// emulator — so remembering a screen for a never-configured system can't corrupt which emulator
    /// launches. The default is a no-op for single-emulator test doubles.
    /// </summary>
    void SetLaunchScreen(string systemId, GameLaunchScreen screen)
    {
    }

    void Save(EmulatorConfiguration configuration);

    void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations);
}
