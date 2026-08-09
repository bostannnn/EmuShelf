using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using EmuShelf.Integrations.Emulators.Azahar;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.Dolphin;
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
/// <param name="LaunchArguments">The configured launch template, for emulators whose arguments can relocate data.</param>
/// <param name="StateDirectoryOverride">The user's explicit save-state folder, or null to derive it.</param>
/// <param name="CoreSharedAcrossSystems">
/// True when the configured libretro core also serves another EmuShelf system, so this system's
/// provider must claim only its own library's saves/states rather than the whole shared core folder.
/// </param>
public sealed record SaveProviderContext(
    string? DirectoryOverride,
    string? EmulatorDirectory,
    bool IsFlatpak,
    IAppPaths Paths,
    string? CorePath = null,
    Func<IReadOnlyCollection<string>>? GameFileNames = null,
    string? LaunchArguments = null,
    string? ExecutablePath = null,
    string? FlatpakApplicationId = null,
    string? StateDirectoryOverride = null,
    string? ActiveEmulatorId = null,
    bool CoreSharedAcrossSystems = false);

/// <summary>A provider's resolved save directory plus optional display text and compatibility note.</summary>
public sealed record SaveProviderDetection(
    string Directory,
    string? Warning = null,
    string? DisplayLocation = null,
    string? OptionalContentSummary = null,
    IReadOnlyList<OptionalContentDetection>? OptionalContent = null);

/// <summary>One independently resolved optional sync root shown under a platform row.</summary>
public sealed record OptionalContentDetection(
    string Kind,
    string? Directory,
    int EligibleFileCount,
    int TotalFileCount,
    long EligibleBytes,
    string? Compatibility = null,
    string? Warning = null);

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
/// <param name="SaveStatesLabel">
/// Overrides the "Automatically sync save states" checkbox label for this platform, or null for the
/// default. Dolphin keeps GameCube and Wii save states in one shared folder synced from the GameCube
/// row, so that row's label names both platforms (the Wii row has no separate toggle by design).
/// </param>
/// <param name="StateSyncSystemId">
/// The system whose save-state phase also covers this platform's states, or null when the platform
/// syncs its own. Set when several systems share one emulator state folder (Dolphin's GameCube and
/// Wii): a launch/exit of this platform also runs that system's state phase, so the shared states are
/// uploaded under that system's namespace — one identity, no double-sync — and this platform needs no
/// save-state toggle of its own.
/// </param>
public sealed record SaveProviderDescriptor(
    string SystemId,
    string DisplayName,
    string SaveShapeDescription,
    string OverridePlaceholder,
    Func<SaveProviderContext, ISaveLocationProvider?> CreateProvider,
    Func<ISaveLocationProvider, CancellationToken, Task<SaveProviderDetection>> DetectAsync,
    bool SupportsSaveStates = false,
    string? SaveStatesLabel = null,
    string? StateSyncSystemId = null);

/// <summary>The supported save-sync platforms, in the order Settings presents them.</summary>
public static class SaveProviderRegistry
{
    private static readonly ConcurrentDictionary<string, Lazy<string?>> FlatpakVersions =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<string?>> FlatpakArchitectures =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<string?>> ExecutableVersions =
        new(FilePathComparison.Comparer);

    public static IReadOnlyList<SaveProviderDescriptor> All { get; } =
    [
        // PlayStation has two emulator profiles: DuckStation (default) and RetroArch (Beetle PSX /
        // SwanStation / PCSX ReARMed). The active profile decides which save layout is resolved, so
        // switching the emulator in Settings switches which saves are synced.
        new SaveProviderDescriptor(
            SystemId: "playstation",
            DisplayName: "PlayStation",
            SaveShapeDescription: "Memory-card saves from the configured PlayStation emulator",
            OverridePlaceholder: "Use the configured emulator, or choose its save/user data folder",
            CreateProvider: static context => IsRetroArch(context.ActiveEmulatorId)
                ? CreateRetroArchProvider("playstation", context)
                : CreateDuckStationProvider(context),
            DetectAsync: static (provider, cancellationToken) => provider is RetroArchSaveLocationProvider
                ? DetectRetroArchAsync(provider, cancellationToken)
                : DetectDuckStationAsync(provider, cancellationToken),
            SupportsSaveStates: true),

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
                        .GetMemoryCardsDirectoryAsync(cancellationToken)),
            SupportsSaveStates: true),

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
            },
            SupportsSaveStates: true),

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
                        .GetSaveDataDirectoryAsync(cancellationToken)),
            SupportsSaveStates: true),

        new SaveProviderDescriptor(
            SystemId: "gamecube",
            DisplayName: "GameCube",
            SaveShapeDescription: "Dolphin memory cards · configured raw cards or individual GCI files",
            OverridePlaceholder: "Use configured Dolphin, or choose its user data folder",
            CreateProvider: static context => CreateDolphinProvider("gamecube", context),
            DetectAsync: static async (provider, cancellationToken) =>
            {
                var info = await ((DolphinSaveLocationProvider)provider)
                    .GetSaveLocationInfoAsync(cancellationToken);
                return new SaveProviderDetection(
                    info.UserDirectory,
                    "Dolphin's configured card type and save paths are followed on each machine. " +
                    "A raw-card save is portable only when the other machine uses a compatible card in the same slot; " +
                    "GCI saves are synced as individual files.",
                    DescribeDolphinLocations(info));
            },
            SupportsSaveStates: true,
            // Dolphin stores GameCube and Wii save states in one shared folder, synced from this row
            // (see AddStateSources' gamecube guard), so the label names both — the Wii row has no toggle.
            SaveStatesLabel: "Automatically sync save states (GameCube + Wii)"),

        new SaveProviderDescriptor(
            SystemId: "wii",
            DisplayName: "Wii",
            SaveShapeDescription: "Dolphin Wii title saves · follows the configured NAND root",
            OverridePlaceholder: "Use configured Dolphin, or choose its user data folder",
            CreateProvider: static context => CreateDolphinProvider("wii", context),
            DetectAsync: static async (provider, cancellationToken) =>
            {
                var info = await ((DolphinSaveLocationProvider)provider)
                    .GetSaveLocationInfoAsync(cancellationToken);
                return new SaveProviderDetection(
                    info.UserDirectory,
                    "Game save data is synced per Wii title. Console identity, Mii data, and channels stay local. " +
                    "Dolphin's shared StateSaves folder is configured once on the GameCube row.",
                    DescribeDolphinLocations(info));
            },
            // Dolphin writes GameCube and Wii save states into one shared StateSaves folder, synced
            // from the GameCube row. Delegating states here makes a Wii launch/exit also sweep and
            // upload the (shared) states under the same dolphin/gc/states/ identity — no separate Wii
            // toggle, and never a second copy.
            StateSyncSystemId: "gamecube"),

        new SaveProviderDescriptor(
            SystemId: "3ds",
            DisplayName: "Nintendo 3DS",
            SaveShapeDescription:
                "Azahar in-game save data · per-title save archives and extdata on the emulated SD card",
            OverridePlaceholder: "Use configured Azahar, or choose its user data folder (contains sdmc)",
            CreateProvider: static context =>
            {
                // A Flatpak Azahar has a documented fixed data location, so it can participate with
                // neither an override nor a resolvable installation directory.
                if (string.IsNullOrWhiteSpace(context.DirectoryOverride) &&
                    string.IsNullOrWhiteSpace(context.EmulatorDirectory) &&
                    !context.IsFlatpak)
                {
                    return null;
                }

                return new AzaharSaveLocationProvider(
                    context.EmulatorDirectory ?? context.Paths.BaseDirectory,
                    userDirectoryOverride: context.DirectoryOverride,
                    isFlatpak: context.IsFlatpak);
            },
            DetectAsync: static async (provider, cancellationToken) =>
                new SaveProviderDetection(
                    await ((AzaharSaveLocationProvider)provider).GetSaveDataDirectoryAsync(cancellationToken),
                    "3DS saves live on the emulated SD card under a console-unique ID folder. EmuShelf syncs " +
                    "each game's save by its title id and places it under this machine's own SD card, so run " +
                    "Azahar once on a new machine to create the SD card before the first download.")),

        // RetroArch serves several systems from one installation, so each row resolves the save
        // directory for its own configured core.
        .. RetroArchPlatform("megadrive", "Mega Drive / Genesis"),
        .. RetroArchPlatform("snes", "Super Nintendo"),
        .. RetroArchPlatform("nds", "Nintendo DS"),
        .. RetroArchPlatform("gba", "Game Boy Advance"),
        .. RetroArchPlatform("gbc", "Game Boy Color"),
        .. RetroArchPlatform("nes", "Nintendo Entertainment System"),
        // Flycast's shared VMU images live in RetroArch's system directory, outside any save
        // folder; only its per-game VMUs land in the save directory, where the same name matching
        // as every other core applies.
        .. RetroArchPlatform("dreamcast", "Dreamcast"),
        // FinalBurn Neo writes battery/NVRAM saves (.srm) and save states (.state) into RetroArch's
        // save and state folders, named after the loaded zip — the same one-file-per-game shape as
        // every other RetroArch core, so arcade reuses the generic RetroArch descriptor unchanged.
        .. RetroArchPlatform("arcade", "Arcade"),
    ];

    private static ISaveLocationProvider? CreateDolphinProvider(
        string systemId,
        SaveProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(context.DirectoryOverride) &&
            string.IsNullOrWhiteSpace(context.EmulatorDirectory) &&
            !context.IsFlatpak)
        {
            return null;
        }

        return new DolphinSaveLocationProvider(
            systemId,
            context.EmulatorDirectory ?? context.Paths.BaseDirectory,
            userDirectoryOverride: context.DirectoryOverride,
            launchArguments: context.LaunchArguments,
            isFlatpak: context.IsFlatpak);
    }

    private static string DescribeDolphinLocations(DolphinSaveLocationInfo info) =>
        info.SaveLocations.Count == 0
            ? $"No saves found; Dolphin configuration: {info.UserDirectory}"
            : string.Join(" • ", info.SaveLocations.Take(3)) +
              (info.SaveLocations.Count > 3 ? $" • +{info.SaveLocations.Count - 3} more" : string.Empty);

    private static IEnumerable<SaveProviderDescriptor> RetroArchPlatform(string systemId, string displayName)
    {
        yield return new SaveProviderDescriptor(
            SystemId: systemId,
            DisplayName: displayName,
            SaveShapeDescription: "RetroArch battery saves · one file per game, named after the game file",
            OverridePlaceholder: "Use configured RetroArch, or choose its saves folder",
            CreateProvider: context => CreateRetroArchProvider(systemId, context),
            DetectAsync: static (provider, cancellationToken) => DetectRetroArchAsync(provider, cancellationToken),
            SupportsSaveStates: true);
    }

    private static bool IsRetroArch(string? emulatorId) =>
        string.Equals(emulatorId, "retroarch", StringComparison.Ordinal);

    private static ISaveLocationProvider? CreateDuckStationProvider(SaveProviderContext context)
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
    }

    private static async Task<SaveProviderDetection> DetectDuckStationAsync(
        ISaveLocationProvider provider,
        CancellationToken cancellationToken)
    {
        var info = await ((DuckStationSaveLocationProvider)provider).GetMemoryCardInfoAsync(cancellationToken);
        return new SaveProviderDetection(
            info.Directory,
            info.UsesFileTitleCards
                ? "This machine uses filename-based cards, which are synced under their exact file names. " +
                  "Another machine only picks one up if its DuckStation enables the same card type in the same " +
                  "slot and the game file has the same name; otherwise that save stays in the cloud."
                : "Cards are synced per slot and card type. A machine whose DuckStation uses a different card " +
                  "type in a slot has no place for the other machine's cards there, and leaves them in the cloud.");
    }

    private static ISaveLocationProvider? CreateRetroArchProvider(string systemId, SaveProviderContext context)
    {
        // Without a core there is nothing to identify this system's saves among the other cores
        // writing into the same folder, and RetroArch rows always configure one.
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
            gameFileNames: context.GameFileNames,
            coreSharedAcrossSystems: context.CoreSharedAcrossSystems);
    }

    private static async Task<SaveProviderDetection> DetectRetroArchAsync(
        ISaveLocationProvider provider,
        CancellationToken cancellationToken)
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
                  "named after games in your library for this system. To sync all of them, turn on " +
                  "RetroArch → Settings → Saving → \"Sort Saves Into Folders By Core Name\", then move the " +
                  "existing saves into the new per-core folder — RetroArch does not move them for you, and " +
                  "will not find them until you do.") +
            perGame + duplicates);
    }

    /// <summary>The descriptor for one system id, or null when the platform is not supported.</summary>
    public static SaveProviderDescriptor? Find(string systemId) =>
        All.FirstOrDefault(descriptor => string.Equals(descriptor.SystemId, systemId, StringComparison.Ordinal));

    /// <summary>Every supported system id, in presentation order.</summary>
    public static IReadOnlyList<string> SystemIds { get; } = All.Select(descriptor => descriptor.SystemId).ToArray();

    /// <summary>Adds the optional, per-file namespaces selected for one platform.</summary>
    /// <remarks>
    /// Save states only. Cheats and patches were carried here too, sourced from each emulator's
    /// whole cheats/patches folder — which on DuckStation and PCSX2 is the community database the
    /// emulator ships and can redownload, not anything the user wrote. That put ~5,900 files
    /// averaging a few KB each into every manual sync, and on a per-file-metered provider those
    /// files, not the saves, were the entire cost of the pass. They are not user data and are no
    /// longer synced; see DECISIONS.md.
    /// </remarks>
    internal static ISaveLocationProvider WithOptionalContent(
        SaveProviderDescriptor descriptor,
        ISaveLocationProvider saves,
        SaveProviderContext context,
        bool includeSaveStates,
        bool includeBaseSaves = true,
        IReadOnlyCollection<string>? gameStateKeys = null)
    {
        if (!includeSaveStates || !descriptor.SupportsSaveStates)
            return includeBaseSaves
                ? saves
                : new AuxiliarySyncProvider(saves, [], compatibility: null, includeBaseSaves: false);

        var sources = new List<AuxiliaryFileSource>();
        AddStateSources(saves, sources, context.StateDirectoryOverride);
        if (sources.Count == 0)
            return includeBaseSaves
                ? saves
                : new AuxiliarySyncProvider(saves, [], compatibility: null, includeBaseSaves: false);

        var architecture = ResolveEmulatorArchitecture(context);
        StateCompatibility? compatibility;
        if (saves is RetroArchSaveLocationProvider)
        {
            // A libretro save state is produced by the core, not by the RetroArch frontend, so its
            // portability depends on the core (id + version) and CPU architecture. The core's published
            // display_version is platform-independent, so a state made on Linux restores on Windows for
            // the same core version. When the info file is absent (a bare core dropped beside EmuShelf,
            // a Flatpak with no info dir) the version is deliberately left UNKNOWN rather than standing
            // in an OS-specific token: a .so and a .dll differ in byte length, so a length token made
            // every Deck state read as a different version on Windows. An unknown-version state restores
            // on any machine running the same core id on the same architecture, and the emulator refuses
            // a genuinely incompatible state on load.
            var coreId = RetroArchCoreId(context.CorePath);
            var coreVersion = NormalizeVersion(ResolveCoreVersion(context));
            compatibility = StateCompatibility.Create($"retroarch:{coreId}", coreVersion, architecture);
        }
        else
        {
            // A standalone emulator's state format is tied to its build, so enforce an equal version —
            // but only when both machines can read a real, comparable one. A Flatpak that publishes no
            // version, or a native binary with no version resource, records an unknown version and syncs
            // on emulator id + architecture, so a Deck state reaches Windows and the emulator's own
            // savestate version tag adjudicates on load (rather than being silently dropped in transit).
            compatibility = StateCompatibility.Create(EmulatorId(saves), ResolveEmulatorVersion(context), architecture);
        }
        return new AuxiliarySyncProvider(saves, sources, compatibility, includeBaseSaves, gameStateKeys);
    }

    private static void AddStateSources(
        ISaveLocationProvider provider,
        ICollection<AuxiliaryFileSource> sources,
        string? stateOverride)
    {
        AuxiliaryFileSource? source = provider switch
        {
            DuckStationSaveLocationProvider duckStation => State(
                Root(duckStation.GetUserDirectoryAsync, "savestates"),
                path => HasExtension(path, ".sav") || Path.GetFileName(path).Contains(".savestate", StringComparison.OrdinalIgnoreCase)),
            Pcsx2SaveLocationProvider pcsx2 => State(
                token => Content(pcsx2, token).SaveStates,
                path => HasExtension(path, ".p2s")),
            PpssppSaveLocationProvider ppsspp => State(
                token => Path.Combine(Await(ppsspp.GetMemoryStickDirectoryAsync(token)), "PSP", "PPSSPP_STATE"),
                path => HasExtension(path, ".ppst")),
            DolphinSaveLocationProvider dolphin when dolphin.SystemId == "gamecube" => State(
                token => Path.Combine(Await(dolphin.GetUserDirectoryAsync(token)), "StateSaves"),
                path => HasExtension(path, ".sav", ".s01", ".s02", ".s03", ".s04", ".s05", ".s06", ".s07", ".s08", ".s09", ".s10")),
            Rpcs3SaveLocationProvider rpcs3 => State(
                token => Content(rpcs3, token).SaveStates,
                path => HasExtension(path, ".savestat")),
            RetroArchSaveLocationProvider retroArch => State(
                token => Await(retroArch.GetContentDirectoriesAsync(token)).SaveStates,
                path => Path.GetFileName(path).Contains(".state", StringComparison.OrdinalIgnoreCase) &&
                    retroArch.StateBelongsToThisSystem(path)),
            _ => null,
        };
        if (source is null)
            return;

        // An explicit save-state folder wins over whatever the emulator configuration resolves to,
        // exactly like the base-save override — the escape hatch for a mis-detected state folder.
        if (!string.IsNullOrWhiteSpace(stateOverride))
        {
            var full = Path.GetFullPath(stateOverride);
            source = source with { ResolveRoot = _ => full };
        }

        sources.Add(source);
    }

    // The libretro core's stable short id (the file name without the _libretro suffix), so the state
    // compatibility key names the exact core across machines and never collides between cores.
    private static string RetroArchCoreId(string? corePath)
    {
        if (string.IsNullOrWhiteSpace(corePath))
            return "core";
        return Path.GetFileNameWithoutExtension(corePath)
            .Replace("_libretro", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static AuxiliaryFileSource State(
        Func<CancellationToken, string?> root,
        Func<string, bool> include) =>
        new("states", root, path => AuxiliarySyncProvider.IsManualState(path) && include(path));

    private static Func<CancellationToken, string?> Root(
        Func<CancellationToken, Task<string>> root,
        string child) =>
        token => Path.Combine(Await(root(token)), child);

    private static Pcsx2ContentDirectories Content(Pcsx2SaveLocationProvider provider, CancellationToken token) =>
        Await(provider.GetContentDirectoriesAsync(token));

    private static Rpcs3ContentDirectories Content(Rpcs3SaveLocationProvider provider, CancellationToken token) =>
        Await(provider.GetContentDirectoriesAsync(token));

    private static T Await<T>(Task<T> task) => task.ConfigureAwait(false).GetAwaiter().GetResult();

    private static bool HasExtension(string path, params string[] extensions) =>
        extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static string EmulatorId(ISaveLocationProvider provider) => provider switch
    {
        DuckStationSaveLocationProvider => "duckstation",
        Pcsx2SaveLocationProvider => "pcsx2",
        PpssppSaveLocationProvider => "ppsspp",
        DolphinSaveLocationProvider => "dolphin",
        Rpcs3SaveLocationProvider => "rpcs3",
        RetroArchSaveLocationProvider => "retroarch",
        _ => provider.SystemId,
    };

    private static string? ResolveEmulatorVersion(SaveProviderContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.ExecutablePath) && File.Exists(context.ExecutablePath))
        {
            return ExecutableVersions.GetOrAdd(
                Path.GetFullPath(context.ExecutablePath),
                path => new Lazy<string?>(() => ReadExecutableVersion(path), true)).Value;
        }

        if (!context.IsFlatpak || string.IsNullOrWhiteSpace(context.FlatpakApplicationId))
            return null;
        return FlatpakVersions.GetOrAdd(
            context.FlatpakApplicationId,
            applicationId => new Lazy<string?>(() => ReadFlatpakVersion(applicationId), true)).Value;
    }

    private static string? ReadFlatpakVersion(string applicationId)
    {
        // Only a real, published version is authoritative for cross-machine state compatibility. Many
        // Flathub emulators (PCSX2 among them) publish none; the build commit is NOT used as a stand-in,
        // because it is unique to the Flatpak build and can never equal a native build's version — that
        // made every Deck-Flatpak state read as a different version on Windows. With no published
        // version the state keys on emulator id + architecture instead (unknown version), so it still
        // reaches the other machine, which the emulator's own savestate version tag then adjudicates.
        return FirstVersion(ReadFlatpakInfoRaw(applicationId, "--show-version"));
    }

    private static string? ReadFlatpakInfoRaw(string applicationId, string option)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "flatpak",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "info", option, applicationId },
            });
            if (process is null)
                return null;
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            if (process.ExitCode != 0)
                return null;
            var output = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? ResolveEmulatorArchitecture(SaveProviderContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.CorePath) && File.Exists(context.CorePath) &&
            ReadBinaryArchitecture(context.CorePath) is { } coreArchitecture)
        {
            return coreArchitecture;
        }
        if (!string.IsNullOrWhiteSpace(context.ExecutablePath) && File.Exists(context.ExecutablePath) &&
            ReadBinaryArchitecture(context.ExecutablePath) is { } executableArchitecture)
        {
            return executableArchitecture;
        }
        if (context.IsFlatpak && !string.IsNullOrWhiteSpace(context.FlatpakApplicationId) &&
            FlatpakArchitectures.GetOrAdd(
                context.FlatpakApplicationId,
                applicationId => new Lazy<string?>(
                    () => NormalizeArchitecture(ReadFlatpakInfoRaw(applicationId, "--show-arch")),
                    true)).Value is { } flatpakArchitecture)
        {
            return flatpakArchitecture;
        }

        // The emulator runs on THIS machine, so when its binary/Flatpak architecture can't be read the
        // host's own architecture is a sound proxy — a machine cannot run a foreign-arch emulator
        // natively. Without this, a Flatpak/AppImage/wrapper-script emulator whose arch is unreadable
        // resolved to null, which made StateCompatibility.Create return null and silently dropped every
        // save state: on the Steam Deck, PCSX2 uploaded and restored nothing for exactly this reason.
        return HostArchitecture();
    }

    private static string HostArchitecture() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        Architecture.Arm => "arm",
        var other => other.ToString().ToLowerInvariant(),
    };

    internal static string? ReadBinaryArchitecture(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);
            var magic = reader.ReadBytes(4);
            if (magic.Length < 4)
                return null;

            // Portable Executable: machine id follows PE\0\0 at e_lfanew.
            if (magic[0] == 'M' && magic[1] == 'Z')
            {
                stream.Position = 0x3c;
                var peOffset = reader.ReadInt32();
                if (peOffset < 0 || peOffset > stream.Length - 6)
                    return null;
                stream.Position = peOffset;
                if (reader.ReadUInt32() != 0x00004550)
                    return null;
                return MachineName(reader.ReadUInt16());
            }

            // ELF: e_machine is a two-byte field at offset 18 and follows EI_DATA endianness.
            if (magic is [0x7f, (byte)'E', (byte)'L', (byte)'F'])
            {
                stream.Position = 5;
                var littleEndian = reader.ReadByte() != 2;
                stream.Position = 18;
                var machineBytes = reader.ReadBytes(2);
                if (machineBytes.Length != 2)
                    return null;
                var machine = littleEndian
                    ? (ushort)(machineBytes[0] | machineBytes[1] << 8)
                    : (ushort)(machineBytes[1] | machineBytes[0] << 8);
                return MachineName(machine);
            }

            // Thin Mach-O binaries. Universal binaries are deliberately left unknown rather than
            // guessing which slice the emulator process will execute.
            var littleMach = magic is [0xce, 0xfa, 0xed, 0xfe] or [0xcf, 0xfa, 0xed, 0xfe];
            var bigMach = magic is [0xfe, 0xed, 0xfa, 0xce] or [0xfe, 0xed, 0xfa, 0xcf];
            if (littleMach || bigMach)
            {
                var cpuBytes = reader.ReadBytes(4);
                if (cpuBytes.Length != 4)
                    return null;
                var cpu = littleMach
                    ? BitConverter.ToUInt32(cpuBytes)
                    : ((uint)cpuBytes[0] << 24) | ((uint)cpuBytes[1] << 16) | ((uint)cpuBytes[2] << 8) | cpuBytes[3];
                return cpu switch
                {
                    0x01000007 => "x64",
                    0x0100000c => "arm64",
                    7 => "x86",
                    12 => "arm",
                    _ => null,
                };
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        static string? MachineName(ushort machine) => machine switch
        {
            0x014c or 3 => "x86",
            0x8664 or 62 => "x64",
            0x01c0 or 40 => "arm",
            0xaa64 or 183 => "arm64",
            _ => null,
        };
    }

    private static string? ReadExecutableVersion(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            return FirstVersion(info.ProductVersion, info.FileVersion);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // AppImages and other native Unix executables commonly have no Windows-style version
            // resource. That is not an authoritative version, so it is left unknown rather than
            // substituted by a file-length token: the length differs across OS/packaging and would
            // wrongly gate a cross-machine restore. The state then keys on emulator id + architecture.
            //
            // The emulator is deliberately never launched to read a version — GUI emulators treat an
            // unknown argument as a fatal error and pop a modal dialog ("Unknown parameter: --version"
            // on the Steam Deck), and the process never exits on its own.
            return null;
        }
    }

    private static string? ResolveCoreVersion(SaveProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(context.CorePath))
            return null;
        var coreId = Path.GetFileNameWithoutExtension(context.CorePath)
            .Replace("_libretro", string.Empty, StringComparison.OrdinalIgnoreCase);
        var fileName = coreId + "_libretro.info";
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.EmulatorDirectory))
            candidates.Add(Path.Combine(context.EmulatorDirectory, "info", fileName));
        if (context.IsFlatpak)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                candidates.Add(Path.Combine(
                    home, ".var", "app", "org.libretro.RetroArch", "config", "retroarch", "info", fileName));
            }
        }
        var infoPath = candidates.FirstOrDefault(File.Exists);
        if (infoPath is null)
            return null;
        try
        {
            return File.ReadLines(infoPath)
                .Select(line => line.Split('=', 2))
                .Where(parts => parts.Length == 2 && parts[0].Trim().Equals("display_version", StringComparison.OrdinalIgnoreCase))
                .Select(parts => parts[1].Trim().Trim('"'))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FirstVersion(params string?[] candidates) => candidates
        .Select(NormalizeVersion)
        .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));

    internal static string? NormalizeVersion(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;
        var match = Regex.Match(
            candidate,
            @"(?<![A-Za-z0-9])v?(?<version>\d+(?:\.\d+)*(?:[-+][0-9A-Za-z.-]+)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
            return candidate.Trim().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        var version = match.Groups["version"].Value.ToLowerInvariant();
        var suffix = version.IndexOfAny(['-', '+']);
        var numeric = suffix < 0 ? version : version[..suffix];
        var remainder = suffix < 0 ? string.Empty : version[suffix..];
        var parts = numeric.Split('.').ToList();
        while (parts.Count > 3 && parts[^1] == "0")
            parts.RemoveAt(parts.Count - 1);
        return string.Join('.', parts) + remainder;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeArchitecture(string? value) => NullIfWhiteSpace(value)?.ToLowerInvariant() switch
    {
        "x86_64" or "amd64" or "x64" => "x64",
        "aarch64" or "arm64" => "arm64",
        "i386" or "i686" or "x86" => "x86",
        "arm" or "armhf" => "arm",
        _ => null,
    };
}
