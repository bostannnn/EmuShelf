namespace EmuShelf.Core.Launching;

/// <summary>
/// Stable, integration-owned metadata for an emulator. User configuration is stored
/// separately per system so shared emulators (Dolphin) can still have distinct settings.
/// </summary>
public sealed record EmulatorDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> SupportedSystemIds,
    string DefaultLaunchArguments)
{
    public bool Supports(string systemId) =>
        SupportedSystemIds.Contains(systemId, StringComparer.Ordinal);
}
