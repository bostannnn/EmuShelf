using EmuShelf.Core.Storage;
using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Azahar;
using EmuShelf.Integrations.Emulators.Dolphin;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.Pcsx2;
using EmuShelf.Integrations.Emulators.Ppsspp;

namespace EmuShelf.App.Services;

/// <summary>What a descriptor needs to decide whether its emulator can be inventoried here.</summary>
/// <param name="DirectoryOverride">The user's explicit texture folder, or null.</param>
/// <param name="EmulatorDirectory">The directory of the configured executable, or null.</param>
/// <param name="IsFlatpak">Whether the configured installation is a Flatpak target.</param>
/// <param name="Paths">Portable app paths.</param>
public sealed record TextureProviderContext(
    string? DirectoryOverride,
    string? EmulatorDirectory,
    bool IsFlatpak,
    IAppPaths Paths);

/// <summary>
/// One emulator installation's texture adapters: where the packs are, how to read them, and whether
/// the emulator would load them. All three are read-only.
/// </summary>
public sealed record TexturePackProvider(
    string InstallationId,
    ITexturePackRootResolver RootResolver,
    Func<string, ITexturePackSource> CreateSource,
    ITexturePackLoadingResolver? LoadingResolver);

/// <summary>
/// One supported texture-pack platform. As with <see cref="SaveProviderRegistry"/>, everything the
/// coordinator and Settings need per platform lives here, so the coordinator, the view model, and
/// the view never name an emulator.
/// </summary>
/// <param name="SystemId">The stable system id, matching the library's own ids.</param>
/// <param name="EmulatorId">The emulator that owns these packs.</param>
/// <param name="DisplayName">The emulator name shown in Settings and tooltips.</param>
/// <param name="OverridePlaceholder">Placeholder text for the override path box.</param>
/// <param name="FolderKind">How this emulator names a game's texture folder, so EmuShelf can build
/// the id folder to open (the inverse of how the scanner reads it back).</param>
/// <param name="CreateProvider">
/// Builds the adapters, or returns null when this machine has nothing to inventory for the
/// platform. This is the single source of truth for participation.
/// </param>
public sealed record TextureProviderDescriptor(
    string SystemId,
    string EmulatorId,
    string DisplayName,
    string OverridePlaceholder,
    TexturePackFolderKind FolderKind,
    Func<TextureProviderContext, TexturePackProvider?> CreateProvider);

/// <summary>The supported texture-pack platforms, in the order Settings presents them.</summary>
public static class TexturePackProviderRegistry
{
    public static IReadOnlyList<TextureProviderDescriptor> All { get; } =
    [
        new TextureProviderDescriptor(
            SystemId: "playstation",
            EmulatorId: DuckStationDefinition.Instance.Id,
            DisplayName: "DuckStation",
            OverridePlaceholder: "Use DuckStation's configured Textures folder, or choose one",
            FolderKind: TexturePackFolderKind.Serial,
            CreateProvider: static context =>
            {
                var userDirectory = EmulatorUserDirectories.FindDuckStation(
                    context.EmulatorDirectory,
                    context.IsFlatpak);
                if (userDirectory is null && context.DirectoryOverride is null)
                    return null;

                var installationId = InstallationId(
                    DuckStationDefinition.Instance.Id,
                    userDirectory ?? context.DirectoryOverride!);
                return new TexturePackProvider(
                    installationId,
                    new DuckStationTextureRootResolver(
                        installationId,
                        userDirectory ?? context.Paths.BaseDirectory,
                        context.DirectoryOverride),
                    root => new DuckStationTexturePackSource(installationId, root),
                    userDirectory is null
                        ? null
                        : new DuckStationTexturePackLoadingResolver(installationId, userDirectory));
            }),

        new TextureProviderDescriptor(
            SystemId: "playstation2",
            EmulatorId: Pcsx2Definition.Instance.Id,
            DisplayName: "PCSX2",
            OverridePlaceholder: "Use PCSX2's configured Textures folder, or choose one",
            FolderKind: TexturePackFolderKind.Serial,
            CreateProvider: static context =>
            {
                var userDirectory = EmulatorUserDirectories.FindPcsx2(
                    context.EmulatorDirectory,
                    context.IsFlatpak);
                if (userDirectory is null && context.DirectoryOverride is null)
                    return null;

                var installationId = InstallationId(
                    Pcsx2Definition.Instance.Id,
                    userDirectory ?? context.DirectoryOverride!);
                return new TexturePackProvider(
                    installationId,
                    new Pcsx2TextureRootResolver(
                        installationId,
                        userDirectory ?? context.Paths.BaseDirectory,
                        context.DirectoryOverride),
                    root => new Pcsx2TexturePackSource(installationId, root),
                    userDirectory is null
                        ? null
                        : new Pcsx2TexturePackLoadingResolver(installationId, userDirectory));
            }),

        new TextureProviderDescriptor(
            SystemId: "gamecube",
            EmulatorId: DolphinDefinition.Instance.Id,
            DisplayName: "Dolphin",
            OverridePlaceholder: "Use Dolphin's User/Load/Textures folder, or choose one",
            FolderKind: TexturePackFolderKind.DolphinDiscId,
            CreateProvider: CreateDolphin),

        new TextureProviderDescriptor(
            SystemId: "wii",
            EmulatorId: DolphinDefinition.Instance.Id,
            DisplayName: "Dolphin",
            OverridePlaceholder: "Use Dolphin's User/Load/Textures folder, or choose one",
            FolderKind: TexturePackFolderKind.DolphinDiscId,
            CreateProvider: CreateDolphin),

        new TextureProviderDescriptor(
            SystemId: "psp",
            EmulatorId: PpssppDefinition.Instance.Id,
            DisplayName: "PPSSPP",
            OverridePlaceholder: "Use PPSSPP's Memory Stick PSP/TEXTURES folder, or choose one",
            FolderKind: TexturePackFolderKind.PspGameId,
            CreateProvider: static context =>
            {
                // PPSSPP reuses the Memory Stick adapter the save sync already proved out, so it can
                // participate from a Flatpak layout with neither an override nor an install path.
                if (context.DirectoryOverride is null &&
                    context.EmulatorDirectory is null &&
                    !context.IsFlatpak)
                {
                    return null;
                }

                var saveProvider = new PpssppSaveLocationProvider(
                    context.EmulatorDirectory ?? context.Paths.BaseDirectory,
                    isFlatpak: context.IsFlatpak);
                var installationId = InstallationId(
                    PpssppDefinition.Instance.Id,
                    context.EmulatorDirectory ?? context.DirectoryOverride ?? "flatpak");
                var configurationDirectory = EmulatorUserDirectories.FindPpssppConfiguration(
                    context.EmulatorDirectory,
                    context.IsFlatpak);
                return new TexturePackProvider(
                    installationId,
                    new PpssppTextureRootResolver(installationId, saveProvider, context.DirectoryOverride),
                    root => new PpssppTexturePackSource(installationId, root),
                    configurationDirectory is null
                        ? null
                        : new PpssppTexturePackLoadingResolver(installationId, configurationDirectory));
            }),

        new TextureProviderDescriptor(
            SystemId: "3ds",
            EmulatorId: AzaharDefinition.Instance.Id,
            DisplayName: "Azahar",
            OverridePlaceholder: "Use Azahar's load/textures folder, or choose one",
            FolderKind: TexturePackFolderKind.Nintendo3dsTitleId,
            CreateProvider: static context =>
            {
                var userDirectory = EmulatorUserDirectories.FindAzahar(
                    context.EmulatorDirectory,
                    context.IsFlatpak);
                if (userDirectory is null && context.DirectoryOverride is null)
                    return null;

                var installationId = InstallationId(
                    AzaharDefinition.Instance.Id,
                    userDirectory ?? context.DirectoryOverride!);
                return new TexturePackProvider(
                    installationId,
                    new AzaharTextureRootResolver(
                        installationId,
                        userDirectory ?? context.Paths.BaseDirectory,
                        context.DirectoryOverride),
                    root => new AzaharTexturePackSource(installationId, root),
                    userDirectory is null
                        ? null
                        : new AzaharTexturePackLoadingResolver(installationId, Path.Combine(userDirectory, "config")));
            }),
    ];

    /// <summary>The descriptor for one system id, or null when the platform is not supported.</summary>
    public static TextureProviderDescriptor? Find(string systemId) =>
        All.FirstOrDefault(descriptor => string.Equals(descriptor.SystemId, systemId, StringComparison.Ordinal));

    /// <summary>The emulator name for one emulator id, for tooltips and Settings rows.</summary>
    public static string DescribeEmulator(string emulatorId) =>
        All.FirstOrDefault(descriptor =>
            string.Equals(descriptor.EmulatorId, emulatorId, StringComparison.Ordinal))?.DisplayName ?? emulatorId;

    /// <summary>Every supported system id, in presentation order.</summary>
    public static IReadOnlyList<string> SystemIds { get; } = All.Select(descriptor => descriptor.SystemId).ToArray();

    private static TexturePackProvider? CreateDolphin(TextureProviderContext context)
    {
        var userDirectory = EmulatorUserDirectories.FindDolphin(
            context.EmulatorDirectory,
            context.IsFlatpak);
        if (userDirectory is null && context.DirectoryOverride is null)
            return null;

        // GameCube and Wii share one Dolphin installation, so they must share one installation id;
        // otherwise the same folder would be scanned and cached twice under different keys.
        var installationId = InstallationId(
            DolphinDefinition.Instance.Id,
            userDirectory ?? context.DirectoryOverride!);
        return new TexturePackProvider(
            installationId,
            new DolphinTextureRootResolver(
                installationId,
                userDirectory ?? context.Paths.BaseDirectory,
                context.DirectoryOverride),
            root => new DolphinTexturePackSource(installationId, root),
            userDirectory is null
                ? null
                : new DolphinTexturePackLoadingResolver(installationId, userDirectory));
    }

    // The cache is keyed by installation, so the key has to be stable across restarts and distinct
    // between two installations of the same emulator. The resolved directory is both.
    private static string InstallationId(string emulatorId, string directory) =>
        $"{emulatorId}:{directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant()}";
}
