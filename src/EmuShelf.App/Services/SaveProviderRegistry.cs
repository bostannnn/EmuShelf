using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;
using EmuShelf.Integrations.Emulators.Pcsx2;
using EmuShelf.Integrations.Emulators.Ppsspp;

namespace EmuShelf.App.Services;

/// <summary>
/// What a descriptor needs to decide whether its emulator participates on this machine: the user's
/// explicit override, the directory derived from the configured emulator installation, and whether
/// that installation is a Flatpak. All paths are already resolved against the portable base.
/// </summary>
/// <param name="DirectoryOverride">The user's explicit save location, or null.</param>
/// <param name="EmulatorDirectory">The directory derived from the configured emulator, or null.</param>
/// <param name="IsFlatpak">Whether the configured installation is a Flatpak target.</param>
/// <param name="Paths">Portable app paths, for providers that need a base directory.</param>
public sealed record SaveProviderContext(
    string? DirectoryOverride,
    string? EmulatorDirectory,
    bool IsFlatpak,
    IAppPaths Paths);

/// <summary>
/// One supported save-sync platform. Everything the coordinator and Settings need per platform
/// lives here, so adding an emulator is a new provider class plus one entry in
/// <see cref="SaveProviderRegistry.All"/> rather than edits scattered across the coordinator,
/// the settings record, the view model, and the view.
/// </summary>
/// <param name="SystemId">The stable system id, matching the library's own ids.</param>
/// <param name="DisplayName">The platform name shown in Settings and progress messages.</param>
/// <param name="SaveShapeDescription">One short line describing what this platform syncs.</param>
/// <param name="OverridePlaceholder">Placeholder text for the override path box.</param>
/// <param name="CreateProvider">
/// Builds the provider, or returns null when this machine has nothing to sync for the platform.
/// This is the single source of truth for participation: the coordinator's "can this system sync"
/// answer calls exactly this, so the two can never disagree.
/// </param>
/// <param name="DescribeDetectedPathAsync">Resolves the concrete directory Settings should display.</param>
public sealed record SaveProviderDescriptor(
    string SystemId,
    string DisplayName,
    string SaveShapeDescription,
    string OverridePlaceholder,
    Func<SaveProviderContext, ISaveLocationProvider?> CreateProvider,
    Func<ISaveLocationProvider, CancellationToken, Task<string?>> DescribeDetectedPathAsync);

/// <summary>The supported save-sync platforms, in the order Settings presents them.</summary>
public static class SaveProviderRegistry
{
    public static IReadOnlyList<SaveProviderDescriptor> All { get; } =
    [
        new SaveProviderDescriptor(
            SystemId: "playstation2",
            DisplayName: "PlayStation 2",
            SaveShapeDescription: "PCSX2 memory cards · uses configured emulator unless overridden",
            OverridePlaceholder: "Use configured PCSX2, or choose its folder",
            CreateProvider: static context =>
            {
                // PCSX2 needs a configuration directory to read PCSX2.ini from; without one there
                // is no trustworthy memory-card location and the platform sits out.
                var directory = context.DirectoryOverride ?? context.EmulatorDirectory;
                return string.IsNullOrWhiteSpace(directory)
                    ? null
                    : new Pcsx2SaveLocationProvider(directory);
            },
            DescribeDetectedPathAsync: static (provider, cancellationToken) =>
                ((Pcsx2SaveLocationProvider)provider).GetMemoryCardsDirectoryAsync(cancellationToken)!),

        new SaveProviderDescriptor(
            SystemId: "psp",
            DisplayName: "PSP",
            SaveShapeDescription: "PPSSPP saves · uses configured emulator unless overridden",
            OverridePlaceholder: "Use configured PPSSPP, or choose its Memory Stick folder",
            CreateProvider: static context =>
            {
                // A Flatpak PPSSPP has a documented fixed Memory Stick location, so it can
                // participate with neither an override nor a resolvable installation directory.
                if (string.IsNullOrWhiteSpace(context.DirectoryOverride) &&
                    string.IsNullOrWhiteSpace(context.EmulatorDirectory) &&
                    !context.IsFlatpak)
                {
                    return null;
                }

                return new PpssppSaveLocationProvider(
                    context.EmulatorDirectory ?? context.Paths.BaseDirectory,
                    memoryStickDirectoryOverride: context.DirectoryOverride,
                    isFlatpak: context.IsFlatpak);
            },
            DescribeDetectedPathAsync: static (provider, cancellationToken) =>
                ((PpssppSaveLocationProvider)provider).GetSaveDataDirectoryAsync(cancellationToken)!),
    ];

    /// <summary>The descriptor for one system id, or null when the platform is not supported.</summary>
    public static SaveProviderDescriptor? Find(string systemId) =>
        All.FirstOrDefault(descriptor => string.Equals(descriptor.SystemId, systemId, StringComparison.Ordinal));

    /// <summary>Every supported system id, in presentation order.</summary>
    public static IReadOnlyList<string> SystemIds { get; } = All.Select(descriptor => descriptor.SystemId).ToArray();
}
