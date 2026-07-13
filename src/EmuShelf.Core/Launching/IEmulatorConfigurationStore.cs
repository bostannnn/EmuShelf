namespace EmuShelf.Core.Launching;

public interface IEmulatorConfigurationStore
{
    EmulatorConfiguration? Get(string systemId);
    void Save(EmulatorConfiguration configuration);
    void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations);
}
