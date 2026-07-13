using System.IO;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Library;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Core.Systems;
using EmuShelf.Infrastructure.Importing;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Settings;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Startup;

/// <summary>
/// Composition root: ensures the portable on-disk layout exists, opens the library
/// database, loads settings, and builds the library/import services before the UI appears.
/// </summary>
public sealed class AppBootstrapper
{
    public IAppPaths Paths { get; }
    public IRelativePathResolver PathResolver { get; }
    public ISettingsService SettingsService { get; }
    public AppSettings Settings { get; }
    public IReadOnlyList<GameSystem> Systems { get; }
    public IGameLibrary Library { get; }
    public IFolderScanner FolderScanner { get; }
    public IGameImportRules ImportRules { get; }
    public IAvailabilityChecker AvailabilityChecker { get; }

    public AppBootstrapper()
    {
        Paths = new AppPaths();
        Paths.EnsureDirectoriesExist();

        PathResolver = new RelativePathResolver(Paths);

        SettingsService = new JsonSettingsService(Paths);
        var isFirstRun = !File.Exists(Paths.SettingsFilePath);
        Settings = SettingsService.Load();
        if (isFirstRun)
        {
            SettingsService.Save(Settings);
        }

        var database = new LibraryDatabase(Paths);
        database.Initialize();

        Systems = KnownSystems.All;
        Library = new GameLibrary(database, PathResolver);
        ImportRules = new FileImportRules(Systems);
        FolderScanner = new FolderScanner(ImportRules);
        AvailabilityChecker = new FileAvailabilityChecker();
    }
}
