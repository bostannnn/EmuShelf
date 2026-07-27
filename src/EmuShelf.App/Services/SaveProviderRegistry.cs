using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.Pcsx2;
using EmuShelf.Integrations.Emulators.Ppsspp;
using EmuShelf.Integrations.Emulators.RetroArch;
using EmuShelf.Integrations.Emulators.Rpcs3;

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
/// <param name="CorePath">The libretro core configured for this system, or null.</param>
/// <param name="GameFileNames">
/// The library's file names (without extension) for this system, evaluated on demand. Providers
/// whose emulator shares one save folder across systems use it to claim only their own saves.
/// </param>
public sealed record SaveProviderContext(
    string? DirectoryOverride,
    string? EmulatorDirectory,
    bool IsFlatpak,
    IAppPaths Paths,
    string? CorePath = null,
    Func<IReadOnlyCollection<string>>? GameFileNames = null);

/// <summary>A provider's resolved save directory plus an optional non-blocking compatibility note.</summary>
public sealed record SaveProviderDetection(string Directory, string? Warning = null);

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
/// <param name="DetectAsync">Resolves the concrete directory and optional warning Settings should display.</param>
public sealed record SaveProviderDescriptor(
    string SystemId,
    string DisplayName,
    string SaveShapeDescription,
    string OverridePlaceholder,
    Func<SaveProviderContext, ISaveLocationProvider?> CreateProvider,
    Func<ISaveLocationProvider, CancellationToken, Task<SaveProviderDetection>> DetectAsync);

/// <summary>The supported save-sync platforms, in the order Settings presents them.</summary>
public static class SaveProviderRegistry
{
    public static IReadOnlyList<SaveProviderDescriptor> All { get; } =
    [
        new SaveProviderDescriptor(
            SystemId: "playstation",
            DisplayName: "PlayStation",
            SaveShapeDescription: "DuckStation memory cards · shared cards contain saves from every game",
            OverridePlaceholder: "Use configured DuckStation, or choose its user data folder",
            CreateProvider: static context =>
            {
                if (string.IsNullOrWhiteSpace(context.DirectoryOverride) &&
                    string.IsNullOrWhiteSpace(context.EmulatorDirectory) &&
                    !context.IsFlatpak)
                {
                    return null;
                }

                return new DuckStationSaveLocationProvider(
                    context.EmulatorDirectory ?? context.Paths.BaseDirectory,
                    userDirectoryOverride: context.DirectoryOverride,
                    isFlatpak: context.IsFlatpak);
            },
            DetectAsync: static async (provider, cancellationToken) =>
            {
                var info = await ((DuckStationSaveLocationProvider)provider)
                    .GetMemoryCardInfoAsync(cancellationToken);
                return new SaveProviderDetection(
                    info.Directory,
                    info.UsesFileTitleCards
                        ? "Filename-based cards are synced using their exact names. " +
                          "If a game has a different filename on another machine, DuckStation may not select its card automatically."
                        : null);
            }),

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
            DetectAsync: static async (provider, cancellationToken) =>
                new SaveProviderDetection(
                    await ((Pcsx2SaveLocationProvider)provider)
                        .GetMemoryCardsDirectoryAsync(cancellationToken))),

        new SaveProviderDescriptor(
            SystemId: "playstation3",
            DisplayName: "PlayStation 3",
            SaveShapeDescription:
                "RPCS3 save data, trophies, and PS1/PS2 Classics memory cards · synced into this machine's RPCS3 user",
            OverridePlaceholder: "Use configured RPCS3, or choose its folder or one user folder",
            CreateProvider: static context =>
            {
                // A Flatpak RPCS3 has a documented fixed configuration directory, so it can
                // participate with neither an override nor a resolvable installation directory.
                if (string.IsNullOrWhiteSpace(context.DirectoryOverride) &&
                    string.IsNullOrWhiteSpace(context.EmulatorDirectory) &&
                    !context.IsFlatpak)
                {
                    return null;
                }

                return new Rpcs3SaveLocationProvider(
                    context.EmulatorDirectory ?? context.Paths.BaseDirectory,
                    directoryOverride: context.DirectoryOverride,
                    isFlatpak: context.IsFlatpak);
            },
            DetectAsync: static async (provider, cancellationToken) =>
            {
                var info = await ((Rpcs3SaveLocationProvider)provider).GetSaveDataInfoAsync(cancellationToken);
                var profile = info.Profile.Name is null
                    ? info.Profile.Id
                    : $"{info.Profile.Id} ({info.Profile.Name})";
                return new SaveProviderDetection(
                    info.SaveDataDirectory,
                    info.AvailableProfiles.Count > 1
                        ? $"RPCS3 has {info.AvailableProfiles.Count} user accounts on this machine. " +
                          $"EmuShelf syncs {profile}; choose another account's folder above to sync that one instead."
                        : null);
            }),

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
            DetectAsync: static async (provider, cancellationToken) =>
                new SaveProviderDetection(
                    await ((PpssppSaveLocationProvider)provider)
                        .GetSaveDataDirectoryAsync(cancellationToken))),
        // RetroArch serves several systems from one installation, so each row resolves the save
        // directory for its own configured core.
        .. RetroArchPlatform("megadrive", "Mega Drive / Genesis"),
        .. RetroArchPlatform("snes", "Super Nintendo"),
        .. RetroArchPlatform("nds", "Nintendo DS"),
        .. RetroArchPlatform("gba", "Game Boy Advance"),
        // Flycast's shared VMU images live in RetroArch's system directory, outside any save
        // folder; only its per-game VMUs land in the save directory, where the same name matching
        // as every other core applies.
        .. RetroArchPlatform("dreamcast", "Dreamcast"),
    ];

    private static IEnumerable<SaveProviderDescriptor> RetroArchPlatform(string systemId, string displayName)
    {
        yield return new SaveProviderDescriptor(
            SystemId: systemId,
            DisplayName: displayName,
            SaveShapeDescription: "RetroArch battery saves · one file per game, named after the game file",
            OverridePlaceholder: "Use configured RetroArch, or choose its saves folder",
            CreateProvider: context =>
            {
                // Without a core there is nothing to identify this system's saves among the other
                // cores writing into the same folder, and RetroArch rows always configure one.
                if (string.IsNullOrWhiteSpace(context.CorePath) &&
                    string.IsNullOrWhiteSpace(context.DirectoryOverride))
                {
                    return null;
                }

                return new RetroArchSaveLocationProvider(
                    systemId,
                    context.CorePath,
                    context.EmulatorDirectory ?? context.Paths.BaseDirectory,
                    directoryOverride: context.DirectoryOverride,
                    isFlatpak: context.IsFlatpak,
                    gameFileNames: context.GameFileNames);
            },
            DetectAsync: static async (provider, cancellationToken) =>
            {
                var retroArch = (RetroArchSaveLocationProvider)provider;
                var info = await retroArch.GetSaveInfoAsync(cancellationToken);
                var core = info.Core.Name ?? info.Core.FileName;
                var ambiguous = await retroArch.GetAmbiguousSaveNamesAsync(cancellationToken);
                var duplicates = ambiguous.Count == 0
                    ? string.Empty
                    : $" {ambiguous.Count} game(s) here have more than one save file — for example " +
                      $"\"{ambiguous[0]}\" — usually the same save under two extensions from different " +
                      "core versions. All copies are synced, but the emulator loads only one.";
                var perGame = info.HasUnreadPerGameOverride
                    ? " One of RetroArch's per-game overrides sends that game's saves to another folder; " +
                      "those saves are not synced from here."
                    : string.Empty;
                return new SaveProviderDetection(
                    info.SaveDirectory,
                    (info.SortedByCore
                        ? $"RetroArch keeps {core}'s saves in their own folder, so everything in it is synced. " +
                          "Saves are matched by file name, so the same game needs the same file name on both machines."
                        : "RetroArch keeps every core's saves in this one folder, so EmuShelf syncs only the saves " +
                          "named after games in your library for this system. Turning on RetroArch's " +
                          "\"sort saves into folders by core name\" gives this system a folder of its own.") +
                    perGame + duplicates);
            });
    }

    /// <summary>The descriptor for one system id, or null when the platform is not supported.</summary>
    public static SaveProviderDescriptor? Find(string systemId) =>
        All.FirstOrDefault(descriptor => string.Equals(descriptor.SystemId, systemId, StringComparison.Ordinal));

    /// <summary>Every supported system id, in presentation order.</summary>
    public static IReadOnlyList<string> SystemIds { get; } = All.Select(descriptor => descriptor.SystemId).ToArray();
}
