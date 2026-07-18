using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Launching;
using EmuShelf.Infrastructure.Metadata;

namespace EmuShelf.App;

public partial class App : Application
{
    public AppBootstrapper Bootstrapper { get; private set; } = null!;
    private HttpClient? _metadataHttpClient;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Bootstrapper = new AppBootstrapper();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var themeService = new AppThemeService(
                Bootstrapper.SettingsService,
                Bootstrapper.Settings);
            var metadataPreferences = new MetadataPreferencesService(
                Bootstrapper.SettingsService,
                Bootstrapper.Settings);
            var mainWindow = new MainWindow();
            var launchService = new EmulatorLaunchService(
                Bootstrapper.EmulatorConfigurations,
                Bootstrapper.ProcessRunner,
                new WindowFrontendController(mainWindow),
                Bootstrapper.Emulators,
                Bootstrapper.Logger);
            var coverService = new GameCoverService(Bootstrapper.Paths);
            // A pooled handler with a raised per-server connection limit lets the download
            // stage fetch many small covers from one host concurrently.
            var metadataHandler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 16,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AutomaticDecompression = System.Net.DecompressionMethods.All,
            };
            _metadataHttpClient = new HttpClient(metadataHandler)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            _metadataHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EmuShelf/1.0");
            var metadataService = new GameMetadataService(
                Bootstrapper.MetadataStore,
                Bootstrapper.MetadataProfiles,
                new LibretroDatCatalog(Bootstrapper.Paths, _metadataHttpClient),
                new RemoteArtworkDownloader(
                    Bootstrapper.Paths,
                    _metadataHttpClient,
                    Bootstrapper.Logger),
                coverService,
                Bootstrapper.Logger);
            var viewModel = new MainViewModel(
                Bootstrapper.Library,
                Bootstrapper.FolderScanner,
                Bootstrapper.ImportRules,
                Bootstrapper.AvailabilityChecker,
                new DialogService(desktop, Bootstrapper.Logger),
                Bootstrapper.Systems,
                launchService,
                Bootstrapper.EmulatorConfigurations,
                Bootstrapper.Emulators,
                coverService,
                themeService,
                metadataService,
                metadataPreferences,
                Bootstrapper.Logger,
                Bootstrapper.RetroAchievementsIdentification);

            mainWindow.DataContext = viewModel;
            desktop.MainWindow = mainWindow;

            // Availability check runs after the UI paints — background, no discovery scan.
            desktop.MainWindow.Opened += (_, _) =>
                Dispatcher.UIThread.Post(
                    () => _ = viewModel.RefreshAvailabilityAsync(),
                    DispatcherPriority.Background);
            desktop.Exit += (_, _) =>
            {
                _metadataHttpClient?.Dispose();
                Bootstrapper.Logger.Information("EmuShelf exited.");
            };

            Dispatcher.UIThread.UnhandledException += (_, args) =>
                Bootstrapper.Logger.Error("Unhandled UI-thread exception.", args.Exception);
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Bootstrapper.Logger.Error("Unobserved background task exception.", args.Exception);
                args.SetObserved();
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var exception = args.ExceptionObject as Exception;
                Bootstrapper.Logger.Error(
                    $"Unhandled process exception (terminating: {args.IsTerminating}).",
                    exception);
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
