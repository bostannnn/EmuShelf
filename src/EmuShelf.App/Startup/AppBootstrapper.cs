using System.IO;
using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
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
using EmuShelf.Infrastructure.Settings;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Achievements;
using EmuShelf.Integrations.Emulators;
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

    public AppBootstrapper()
    {
        Paths = new AppPaths();
        Paths.EnsureDirectoriesExist();
        Logger = new FileAppLogger(Paths);
        Logger.Information("EmuShelf startup began.");

        PathResolver = new RelativePathResolver(Paths);

        SettingsService = new JsonSettingsService(Paths, Logger);
        var isFirstRun = !File.Exists(Paths.SettingsFilePath);
        Settings = SettingsService.Load();
        if (isFirstRun)
        {
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
            emulatorInstallations: ResolveConfiguredEmulator);
        TexturePacks = new TexturePackCoordinator(
            Paths,
            MetadataStore,
            Settings,
            Logger,
            emulatorInstallations: ResolveConfiguredEmulator);
        Logger.Information("EmuShelf startup services initialized.");
    }

    // The emulator data directory EmuShelf already knows about from the Emulators settings — the
    // folder containing the configured executable — used to pre-fill cloud save sync so the user
    // does not select the same emulator twice. Flatpak targets have no local executable path, so
    // they report a null directory and rely on the provider's documented Flatpak layout instead.
    private SaveEmulatorInstallation? ResolveConfiguredEmulator(string systemId)
    {
        var configuration = EmulatorConfigurations.Get(systemId);
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
            configuration.LaunchTarget is FlatpakApplicationTarget);
    }
}
