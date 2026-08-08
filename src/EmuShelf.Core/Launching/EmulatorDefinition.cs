using EmuShelf.Core.Emulators;

namespace EmuShelf.Core.Launching;

/// <summary>
/// Stable, integration-owned metadata for an emulator. The user configures executable
/// installations separately from per-system launch arguments, so one installation can serve
/// several systems without flattening their launch behavior.
/// </summary>
public sealed record EmulatorDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> SupportedSystemIds,
    string DefaultLaunchArguments,
    bool RequiresCorePath = false,
    bool SharesDefaultInstallation = false,
    bool RequiresContentFile = false)
{
    /// <summary>
    /// Where EmuShelf can download this emulator from, when it is managed by the in-app install/update
    /// manager. Null for emulators EmuShelf never offers to install. Set in the Integrations definition.
    /// </summary>
    public EmulatorReleaseSource? ReleaseSource { get; init; }

    public bool Supports(string systemId) =>
        SupportedSystemIds.Contains(systemId, StringComparer.Ordinal);

    /// <summary>
    /// The starting installation mapping for a newly configured system. Existing user mappings
    /// are always retained by storage migrations.
    /// </summary>
    public string GetDefaultInstallationId(string systemId) =>
        SharesDefaultInstallation ? Id : $"{Id}-{systemId}";
}
