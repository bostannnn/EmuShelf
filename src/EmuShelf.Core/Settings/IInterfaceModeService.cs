namespace EmuShelf.Core.Settings;

public interface IInterfaceModeService
{
    InterfaceMode Current { get; }
    bool IsCommandLineOverride { get; }

    /// <summary>
    /// Whether this platform has a desktop window shell to switch to at all. Distinct from
    /// <see cref="IsCommandLineOverride"/>, which forces the current mode while the shell still exists
    /// (e.g. Steam Gaming Mode on desktop): here, <c>false</c> means Desktop mode is not merely locked
    /// but absent (Android), so the shared gamepad UI must hide every "switch to Desktop" affordance
    /// and route desktop-only handoffs to an honest "not available here" instead.
    /// </summary>
    bool SupportsDesktopMode { get; }

    event EventHandler<InterfaceMode>? ModeChanged;
    Task SetModeAsync(InterfaceMode mode, CancellationToken cancellationToken = default);
}
