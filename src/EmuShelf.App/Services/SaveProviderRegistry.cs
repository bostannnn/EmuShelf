using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
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
public sealed record SaveProviderContext(
    string? DirectoryOverride,
    string? EmulatorDirectory,
    bool IsFlatpak,
    IAppPaths Paths,
    string? CorePath = null,
    Func<IReadOnlyCollection<string>>? GameFileNames = null,
    string? LaunchArguments = null,
    string? ExecutablePath = null,
    string? FlatpakApplicationId = null);

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
public sealed record SaveProviderDescriptor(
    string SystemId,
    string DisplayName,
    string SaveShapeDescription,
    string OverridePlaceholder,
    Func<SaveProviderContext, ISaveLocationProvider?> CreateProvider,
    Func<ISaveLocationProvider, CancellationToken, Task<SaveProviderDetection>> DetectAsync,
    bool SupportsCheatsAndPatches = false,
    bool SupportsSaveStates = false);

/// <summary>The supported save-sync platforms, in the order Settings presents them.</summary>
public static class SaveProviderRegistry
{
    private static readonly ConcurrentDictionary<string, Lazy<string?>> FlatpakVersions =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<string?>> FlatpakArchitectures =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<string?>> ExecutableVersions =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

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
                        ? "This machine uses filename-based cards, which are synced under their exact file names. " +
                          "Another machine only picks one up if its DuckStation enables the same card type in the same " +
                          "slot and the game file has the same name; otherwise that save stays in the cloud."
                        : "Cards are synced per slot and card type. A machine whose DuckStation uses a different card " +
                          "type in a slot has no place for the other machine's cards there, and leaves them in the cloud.");
            },
            SupportsCheatsAndPatches: true,
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
            SupportsCheatsAndPatches: true,
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
            SupportsCheatsAndPatches: true,
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
            SupportsCheatsAndPatches: true,
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
            SupportsSaveStates: true),

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
            }),

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
                          "named after games in your library for this system. To sync all of them, turn on " +
                          "RetroArch → Settings → Saving → \"Sort Saves Into Folders By Core Name\", then move the " +
                          "existing saves into the new per-core folder — RetroArch does not move them for you, and " +
                          "will not find them until you do.") +
                    perGame + duplicates);
            },
            SupportsCheatsAndPatches: true,
            SupportsSaveStates: true);
    }

    /// <summary>The descriptor for one system id, or null when the platform is not supported.</summary>
    public static SaveProviderDescriptor? Find(string systemId) =>
        All.FirstOrDefault(descriptor => string.Equals(descriptor.SystemId, systemId, StringComparison.Ordinal));

    /// <summary>Every supported system id, in presentation order.</summary>
    public static IReadOnlyList<string> SystemIds { get; } = All.Select(descriptor => descriptor.SystemId).ToArray();

    /// <summary>Adds the optional, per-file namespaces selected for one platform.</summary>
    internal static ISaveLocationProvider WithOptionalContent(
        SaveProviderDescriptor descriptor,
        ISaveLocationProvider saves,
        SaveProviderContext context,
        bool includeCheatsAndPatches,
        bool includeSaveStates,
        int stateRetention)
    {
        var sources = new List<AuxiliaryFileSource>();
        if (includeCheatsAndPatches && descriptor.SupportsCheatsAndPatches)
            AddCheatAndPatchSources(saves, sources);
        if (includeSaveStates && descriptor.SupportsSaveStates)
            AddStateSources(saves, sources);
        if (sources.Count == 0)
            return saves;

        StateCompatibility? compatibility = null;
        if (includeSaveStates)
        {
            var coreVersion = ResolveCoreVersion(context);
            var emulatorVersion = saves is RetroArchSaveLocationProvider && string.IsNullOrWhiteSpace(coreVersion)
                ? null
                : ResolveEmulatorVersion(context);
            compatibility = StateCompatibility.Create(
                EmulatorId(saves),
                emulatorVersion,
                coreVersion,
                ResolveEmulatorArchitecture(context));
        }
        return new AuxiliarySyncProvider(saves, sources, compatibility, stateRetention);
    }

    private static void AddCheatAndPatchSources(
        ISaveLocationProvider provider,
        ICollection<AuxiliaryFileSource> sources)
    {
        switch (provider)
        {
            case DuckStationSaveLocationProvider duckStation:
                sources.Add(Source(AuxiliaryContentKind.Cheats, "cheats", Root(duckStation.GetUserDirectoryAsync, "cheats"), ".cht"));
                sources.Add(Source(AuxiliaryContentKind.Patches, "patches", Root(duckStation.GetUserDirectoryAsync, "patches"), ".cht"));
                break;
            case Pcsx2SaveLocationProvider pcsx2:
                sources.Add(Source(AuxiliaryContentKind.Cheats, "cheats", token => Content(pcsx2, token).Cheats, ".pnach"));
                sources.Add(Source(AuxiliaryContentKind.Patches, "patches", token => Content(pcsx2, token).Patches, ".pnach"));
                break;
            case PpssppSaveLocationProvider ppsspp:
                sources.Add(Source(
                    AuxiliaryContentKind.Cheats,
                    "cheats",
                    token => Path.Combine(Await(ppsspp.GetMemoryStickDirectoryAsync(token)), "PSP", "Cheats"),
                    ".ini"));
                break;
            case Rpcs3SaveLocationProvider rpcs3:
                sources.Add(new AuxiliaryFileSource(
                    AuxiliaryContentKind.Patches,
                    "patches",
                    token => Content(rpcs3, token).Patches,
                    path => Path.GetFileName(path).Equals("patch.yml", StringComparison.OrdinalIgnoreCase),
                    Recursive: false));
                break;
            case RetroArchSaveLocationProvider retroArch:
                sources.Add(Source(
                    AuxiliaryContentKind.Cheats,
                    "cheats",
                    token => Await(retroArch.GetContentDirectoriesAsync(token)).Cheats,
                    ".cht"));
                break;
        }
    }

    private static void AddStateSources(
        ISaveLocationProvider provider,
        ICollection<AuxiliaryFileSource> sources)
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
                path => Path.GetFileName(path).Contains(".state", StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };
        if (source is not null)
            sources.Add(source);
    }

    private static AuxiliaryFileSource Source(
        AuxiliaryContentKind kind,
        string unitNamespace,
        Func<CancellationToken, string?> root,
        params string[] extensions) =>
        new(kind, unitNamespace, root, path => HasExtension(path, extensions));

    private static AuxiliaryFileSource State(
        Func<CancellationToken, string?> root,
        Func<string, bool> include) =>
        new(
            AuxiliaryContentKind.SaveStates,
            "states",
            root,
            path => AuxiliarySyncProvider.IsManualState(path) && include(path),
            StateGroup: AuxiliarySyncProvider.DefaultStateGroup);

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
        => ReadFlatpakInfo(applicationId, "--show-version", normalizeVersion: true);

    private static string? ReadFlatpakInfo(string applicationId, string option, bool normalizeVersion)
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
            return normalizeVersion ? FirstVersion(output) : NormalizeArchitecture(output);
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
        if (!context.IsFlatpak || string.IsNullOrWhiteSpace(context.FlatpakApplicationId))
            return null;
        return FlatpakArchitectures.GetOrAdd(
            context.FlatpakApplicationId,
            applicationId => new Lazy<string?>(
                () => ReadFlatpakInfo(applicationId, "--show-arch", normalizeVersion: false),
                true)).Value;
    }

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
            if (FirstVersion(info.ProductVersion, info.FileVersion) is { } embedded)
                return embedded;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // AppImages and other native Unix executables commonly have no Windows-style version
            // resource. Their own version command below is the authoritative fallback.
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "--version" },
            });
            if (process is null)
                return null;
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                return null;
            }
            if (process.ExitCode != 0)
                return null;
            return FirstVersion(process.StandardOutput.ReadToEnd(), process.StandardError.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
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
