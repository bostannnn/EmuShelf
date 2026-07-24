namespace EmuShelf.Core.Settings;

public interface IInterfaceModeService
{
    InterfaceMode Current { get; }
    bool IsCommandLineOverride { get; }
    event EventHandler<InterfaceMode>? ModeChanged;
    Task SetModeAsync(InterfaceMode mode, CancellationToken cancellationToken = default);
}
