using System.IO;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Settings;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.App.Startup;

/// <summary>
/// Composition root for portable storage: ensures the on-disk layout exists,
/// opens the library database, and loads settings before the UI appears.
/// </summary>
public sealed class AppBootstrapper
{
    public IAppPaths Paths { get; }
    public IRelativePathResolver PathResolver { get; }
    public ISettingsService SettingsService { get; }
    public AppSettings Settings { get; }

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

        new LibraryDatabase(Paths).Initialize();
    }
}
