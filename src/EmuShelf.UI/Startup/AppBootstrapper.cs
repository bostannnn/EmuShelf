using System.IO;
using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Core.Systems;
using EmuShelf.Infrastructure.Importing;
using EmuShelf.Infrastructure.Achievements;
using EmuShelf.Infrastructure.Diagnostics;
using EmuShelf.Infrastructure.Launching;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Metadata;
using EmuShelf.Infrastructure.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.Settings;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Core.Storage.Android;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Achievements;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Android;
using EmuShelf.Integrations.Systems;
using EmuShelf.Integrations.Metadata;
using EmuShelf.Integrations.Launching;

namespace EmuShelf.App.Startup;

/// <summary>
/// Composition root: ensures the portable on-disk layout exists, opens the library
/// database, loads settings, and builds the library/import services before the UI appears.
/// </summary>
public sealed class AppBootstrapper
{
    public IAppPaths Paths { get; }
    public IAppLogger Logger { get; }
    public IRelativePathResolver PathResolver { get; }
    public ISettingsService SettingsService { get; }
    public AppSettings Settings { get; }
    public IReadOnlyList<GameSystem> Systems { get; }
    public IGameLibrary Library { get; }
    public IGameMetadataStore MetadataStore { get; }
    public IGameDetailsStore GameDetailsStore { get; }
    public IGameScrapeProviderRegistry ScrapeProviders { get; }
    public IScreenScraperCredentialStore ScreenScraperCredentialStore { get; }
    public IGameFileFingerprintStore GameFileFingerprintStore { get; }
    public IScreenScraperFingerprintService ScreenScraperFingerprints { get; }
    public ScreenScraperRequestCoordinator ScreenScraperRequests { get; }
    public IScreenScraperClient? ScreenScraperClient { get; }
    public IScreenScraperPreviewService? ScreenScraperPreview { get; }
    public IRetroAchievementsStore RetroAchievementsStore { get; }
    public IRetroAchievementsReadStore RetroAchievementsReadStore { get; }
    public IRetroAchievementsProgressStore RetroAchievementsProgressStore { get; }
    public IRetroAchievementsDetailsStore RetroAchievementsDetailsStore { get; }
    public IRetroAchievementsCredentialStore RetroAchievementsCredentialStore { get; }
    public IRetroAchievementsGameHasher RetroAchievementsHasher { get; }
    public IRetroAchievementsIdentificationService RetroAchievementsIdentification { get; }
    public IReadOnlyList<MetadataSystemProfile> MetadataProfiles { get; }
    public IFolderScanner FolderScanner { get; }
    public IGameImportRules ImportRules { get; }
    public IAvailabilityChecker AvailabilityChecker { get; }
    public IReadOnlyList<EmulatorDefinition> Emulators { get; }
    public IEmulatorConfigurationStore EmulatorConfigurations { get; }
    public ITrackedProcessRunner ProcessRunner { get; }
    public ILaunchTargetInspector LaunchTargetInspector { get; }
    public IGameLaunchDependencyResolver GameLaunchDependencies { get; }
    public CloudSaveSyncCoordinator CloudSaveSync { get; }
    public TexturePackCoordinator TexturePacks { get; }
    public HotkeyCoordinator Hotkeys { get; }

    /// <param name="baseDirectoryOverride">
    /// The portable-storage root, or null to resolve it from the environment. Desktop passes null and
    /// <see cref="AppPaths"/> uses the executable directory (or the per-user macOS location); the
    /// Android head passes its app-private files directory, which is the only reliably writable path
    /// there and which Infrastructure cannot obtain without the Android context.
    /// </param>
    public AppBootstrapper(string? baseDirectoryOverride = null)
    {
        // On Android a missing/blank override would otherwise silently fall through to
        // AppContext.BaseDirectory (read-only there), and EnsureDirectoriesExist() below would then
        // throw UnauthorizedAccessException from deep in Directory.CreateDirectory — one line before
        // Logger exists, so with no log entry and no on-screen reason. Fail fast with an actionable
        // message instead; the Android head must set App.BaseDirectoryOverride before composing.
        if (OperatingSystem.IsAndroid() && string.IsNullOrWhiteSpace(baseDirectoryOverride))
        {
            throw new InvalidOperationException(
                "On Android, App.BaseDirectoryOverride must be set to the app-private files directory "
                + "before the composition root runs (AppContext.BaseDirectory is read-only there). "
                + "EmuShelfAndroidApplication.CustomizeAppBuilder sets it from FilesDir.");
        }

        // Android relativization is meaningless (app-private and shared storage do not move together),
        // so game paths are stored absolute there; the desktop targets keep portable relative paths.
        Paths = string.IsNullOrWhiteSpace(baseDirectoryOverride)
            ? new AppPaths()
            : new AppPaths(baseDirectoryOverride, usesPortableStorage: !OperatingSystem.IsAndroid());
        Paths.EnsureDirectoriesExist();
        Logger = new FileAppLogger(Paths);
        Logger.Information("EmuShelf startup began.");

        // Android indexes shared storage into MediaStore, and EmuShelf's data root lives there (not in
        // app-private storage). Without a marker, Covers/ leaks into the system gallery and every
        // transient settings temp file gets scanned, producing a constant stream of MediaProvider churn.
        // A .nomedia at the root tells the scanner to skip this folder and all its subfolders. It only
        // matters on Android, so it's only paid for there. Runs per data folder — a fresh pick rebuilds
        // this bootstrapper — so a folder chosen later gets the marker too.
        if (OperatingSystem.IsAndroid())
        {
            try
            {
                var noMediaMarker = Path.Combine(Paths.BaseDirectory, ".nomedia");
                if (!File.Exists(noMediaMarker))
                    File.WriteAllText(noMediaMarker, string.Empty);
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not write the .nomedia marker to the data root: {ex.Message}");
            }
        }

        PathResolver = new RelativePathResolver(Paths);

        SettingsService = new JsonSettingsService(Paths, Logger);
        var isFirstRun = !File.Exists(Paths.SettingsFilePath);
        Settings = SettingsService.Load();
        if (isFirstRun)
        {
            // The CRT tube defaults on because the shelf's premise is physical media under a TV, but on
            // Android it defaults off: the effect holds the screen at the compositor's frame rate and
            // captures the couch UI on a timer, a cost that isn't worth paying by default on a handheld's
            // battery and GPU. This only seeds the first-run default — the in-app toggle still persists a
            // later explicit choice either way.
            if (OperatingSystem.IsAndroid())
                Settings = Settings with { CrtScreenEffect = false };
            try
            {
                SettingsService.Save(Settings);
            }
            catch (Exception ex)
            {
                Logger.Error("Could not create the initial portable settings file.", ex);
                throw;
            }
        }

        var database = new LibraryDatabase(Paths);
        try
        {
            database.Initialize();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not initialize Data/library.db.", ex);
            throw;
        }

        Systems = KnownSystems.All;
        Emulators = KnownEmulators.All;
        Library = new GameLibrary(database, PathResolver);
        MetadataStore = new SqliteGameMetadataStore(database, PathResolver);
        GameDetailsStore = new SqliteGameDetailsStore(database, PathResolver);
        ScrapeProviders = new GameScrapeProviderRegistry(KnownScrapeProviders.All);
        ScreenScraperCredentialStore = ScreenScraperCredentialStoreFactory.Create(Paths, Logger);
        GameFileFingerprintStore = new SqliteGameFileFingerprintStore(database, PathResolver);
        ScreenScraperFingerprints = new ScreenScraperFingerprintService(GameFileFingerprintStore);
        ScreenScraperRequests = new ScreenScraperRequestCoordinator();
        if (ScreenScraperDeveloperCredentialSource.TryLoad(out var developerCredentials))
        {
            ScreenScraperClient = new ScreenScraperClient(
                new HttpClient { Timeout = TimeSpan.FromSeconds(45) },
                developerCredentials!,
                ScreenScraperRequests);
            ScreenScraperPreview = new ScreenScraperPreviewService(
                MetadataStore,
                GameDetailsStore,
                ScreenScraperCredentialStore,
                ScreenScraperFingerprints,
                ScreenScraperClient,
                KnownScreenScraperProfiles.All,
                KnownMetadataProfiles.All.ToDictionary(
                    profile => profile.SystemId,
                    profile => profile.IdentifierExtractor,
                    StringComparer.OrdinalIgnoreCase));
        }
        else
        {
            // Without this the absence is completely silent: no client is built, every scrape entry
            // point quietly disables itself, and nothing anywhere says why. That is indistinguishable
            // from a broken scraper, and it has cost real debugging time.
            Logger.Warning(
                "ScreenScraper is unavailable: no developer credentials were found. They are baked in "
                + $"at build time from {ScreenScraperDeveloperCredentialSource.DeveloperIdVariable}, "
                + $"{ScreenScraperDeveloperCredentialSource.DeveloperPasswordVariable} and "
                + $"{ScreenScraperDeveloperCredentialSource.SoftwareNameVariable}, and the same "
                + "variables override the baked values at run time. Scraping stays disabled until one "
                + "of those two routes supplies all three.");
        }
        var retroAchievementsStore = new SqliteRetroAchievementsStore(database, PathResolver);
        RetroAchievementsStore = retroAchievementsStore;
        RetroAchievementsReadStore = retroAchievementsStore;
        RetroAchievementsProgressStore = retroAchievementsStore;
        RetroAchievementsDetailsStore = retroAchievementsStore;
        RetroAchievementsCredentialStore =
            RetroAchievementsCredentialStoreFactory.Create(Paths, Logger);
        RetroAchievementsHasher = new RetroAchievementsGameHasher();
        RetroAchievementsIdentification = new RetroAchievementsIdentificationService(
            RetroAchievementsStore,
            RetroAchievementsHasher,
            Logger);
        MetadataProfiles = KnownMetadataProfiles.All;
        EmulatorConfigurations = new SqliteEmulatorConfigurationStore(database, PathResolver);
        ProcessRunner = new TrackedProcessRunner();
        LaunchTargetInspector = new FlatpakLaunchTargetInspector();
        GameLaunchDependencies = new GameLaunchDependencyResolver();
        ImportRules = new FileImportRules(Systems);
        FolderScanner = new FolderScanner(ImportRules);
        AvailabilityChecker = new FileAvailabilityChecker();
        CloudSaveSync = new CloudSaveSyncCoordinator(
            Paths,
            SettingsService,
            Settings,
            Logger,
            emulatorInstallations: ResolveConfiguredEmulator,
            // RetroArch writes every core's saves into one folder unless the user turns on
            // per-core sorting, so the save providers need the library to tell whose save is whose.
            gamesForSystem: systemId => Library.GetGames(systemId),
            // The startup legacy-override migration resolves every system at once; this reads them all
            // in one query instead of opening one SQLite connection per system before the first frame.
            emulatorInstallationsBatch: ResolveConfiguredEmulators);
        TexturePacks = new TexturePackCoordinator(
            Paths,
            MetadataStore,
            Settings,
            Logger,
            emulatorInstallations: ResolveConfiguredEmulator,
            // The settings service persists texture-root overrides. The library and metadata
            // profiles let an explicit rescan extract the disc evidence it matches on, so texture
            // marks no longer depend on the opt-in network-metadata pass having run first.
            settingsService: SettingsService,
            gamesForSystem: systemId => Library.GetGames(systemId),
            metadataProfiles: MetadataProfiles);
        Hotkeys = new HotkeyCoordinator(
            Paths,
            Systems,
            Logger,
            resolveInstallation: ResolveConfiguredEmulator,
            // Durable, AV-tolerant writes; the coordinator only edits config while the emulator is closed.
            writeFile: AtomicFile.WriteAllText);
        Logger.Information("EmuShelf startup services initialized.");
    }

    // The emulator data directory EmuShelf already knows about from the Emulators settings — the
    // folder containing the configured executable — used to pre-fill cloud save sync so the user
    // does not select the same emulator twice. Flatpak targets have no local executable path, so
    // they report a null directory and rely on the provider's documented Flatpak layout instead.
    private SaveEmulatorInstallation? ResolveConfiguredEmulator(string systemId) =>
        OperatingSystem.IsAndroid()
            ? ResolveAndroidEmulator(systemId, EmulatorConfigurations.Get(systemId))
            : BuildInstallation(EmulatorConfigurations.Get(systemId));

    // Batched form of the above: one database read for every requested system, used by the cloud-sync
    // startup migration so it does not open a connection per system on the pre-first-frame path.
    private IReadOnlyDictionary<string, SaveEmulatorInstallation?> ResolveConfiguredEmulators(
        IReadOnlyList<string> systemIds)
    {
        var result = new Dictionary<string, SaveEmulatorInstallation?>(StringComparer.Ordinal);
        if (OperatingSystem.IsAndroid())
        {
            var androidConfigurations = EmulatorConfigurations.GetAll(systemIds);
            foreach (var systemId in systemIds)
                result[systemId] = ResolveAndroidEmulator(
                    systemId, androidConfigurations.GetValueOrDefault(systemId));
            return result;
        }

        var configurations = EmulatorConfigurations.GetAll(systemIds);
        foreach (var systemId in systemIds)
            result[systemId] = BuildInstallation(configurations.GetValueOrDefault(systemId));
        return result;
    }

    // On Android there is no configured executable path to derive a save location from. Dolphin
    // (GameCube/Wii) keeps its saves at a package-derived path under Android/data that EmuShelf reads
    // directly under all-files access (DECISIONS 2026-08-20), so its installation is synthesised from the
    // package name here — no user pick. RetroArch is auto-located too: its retroarch.cfg lives in the
    // same package Android/data files dir and is group-readable, so EmuShelf reads the configured
    // savefile_directory itself — and this is also the PS1 path (Beetle PSX). PS1 has no fixed-root
    // branch of its own: DuckStation's cards are owner-only and unreadable on Android (there is no
    // Android DuckStation save provider), so PS1 syncs only when configured for a RetroArch PS1 core.
    // PPSSPP and Azahar remain folder-configurable (their save folder is chosen by the user and recorded
    // only in the emulator's own unreadable private config), so they return null and rely on the
    // per-system save-location override the user sets once.
    internal static SaveEmulatorInstallation? ResolveAndroidEmulator(
        string systemId,
        EmulatorConfiguration? configuration) => systemId switch
    {
        "gamecube" or "wii" => new SaveEmulatorInstallation(
            AndroidExternalStorageUri.ExternalAppFilesDirectory(
                AndroidEmulatorLaunchProfiles.Dolphin.PackageName),
            IsFlatpak: false,
            EmulatorId: "dolphin"),
        // PlayStation has no branch of its own: DuckStation is unreadable on Android, so PS1 syncs only
        // when configured for a RetroArch PS1 core (Beetle PSX), which the fallthrough resolves like any
        // other RetroArch system (null for a non-RetroArch or unconfigured emulator).
        _ => ResolveAndroidRetroArch(configuration),
    };

    // A RetroArch system auto-locates like the fixed-root emulators: retroarch.cfg lives in the
    // package's Android/data files dir (group-readable — measured on the Thor), and its
    // savefile_directory there points at a normal shared-storage folder EmuShelf reads. So the package
    // files dir is the "installation" the provider reads the config from, and the DB-configured core is
    // carried through so the provider can name the per-core save folder and, for a core shared by two
    // systems (mGBA → GBA and GBC), claim only this system's own saves. Systems whose emulator is not
    // RetroArch (or that have no emulator configured) stay null. See docs/android-save-sync-model.md.
    private static SaveEmulatorInstallation? ResolveAndroidRetroArch(EmulatorConfiguration? configuration)
    {
        if (configuration is null ||
            !string.Equals(configuration.EmulatorId, "retroarch", StringComparison.Ordinal))
        {
            return null;
        }

        return new SaveEmulatorInstallation(
            AndroidExternalStorageUri.ExternalAppFilesDirectory(
                AndroidEmulatorLaunchProfiles.RetroArch.PackageName),
            IsFlatpak: false,
            CorePath: configuration.CorePath,
            LaunchArguments: configuration.LaunchArguments,
            EmulatorId: configuration.EmulatorId);
    }

    private static SaveEmulatorInstallation? BuildInstallation(EmulatorConfiguration? configuration)
    {
        if (configuration is null)
            return null;

        var executablePath = configuration.LaunchTarget switch
        {
            DirectExecutableTarget direct => direct.Path,
            _ => configuration.ExecutablePath,
        };
        var directory = string.IsNullOrWhiteSpace(executablePath)
            ? null
            : Path.GetDirectoryName(executablePath);
        return new SaveEmulatorInstallation(
            directory,
            configuration.LaunchTarget is FlatpakApplicationTarget,
            configuration.CorePath,
            configuration.LaunchArguments,
            executablePath,
            (configuration.LaunchTarget as FlatpakApplicationTarget)?.AppId,
            configuration.EmulatorId);
    }
}
