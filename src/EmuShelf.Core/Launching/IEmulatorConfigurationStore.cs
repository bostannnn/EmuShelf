namespace EmuShelf.Core.Launching;

public interface IEmulatorConfigurationStore
{
    EmulatorConfiguration? Get(string systemId);

    /// <summary>
    /// Reads every requested system's configuration in a single pass. The result contains an entry
    /// for each id in <paramref name="systemIds"/> (null when that system has no configuration), so
    /// callers avoid opening one database connection per system on hot paths such as Settings. The
    /// default falls back to per-system <see cref="Get"/>; backing stores override it with one query.
    /// </summary>
    IReadOnlyDictionary<string, EmulatorConfiguration?> GetAll(IEnumerable<string> systemIds)
    {
        var result = new Dictionary<string, EmulatorConfiguration?>(StringComparer.Ordinal);
        foreach (var systemId in systemIds)
            result[systemId] = Get(systemId);
        return result;
    }

    void Save(EmulatorConfiguration configuration);
    void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations);
}
