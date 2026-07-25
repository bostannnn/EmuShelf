using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Launching;
using EmuShelf.Infrastructure.Achievements;
using EmuShelf.Infrastructure.Input;
using EmuShelf.Infrastructure.Metadata;

namespace EmuShelf.App;

public partial class App : Application
{
    public AppBootstrapper Bootstrapper { get; private set; } = null!;
    private HttpClient? _metadataHttpClient;
    private HttpClient? _retroAchievementsHttpClient;
    private GamepadInputService? _gamepadInput;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
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
            var interfaceModeService = new WindowInterfaceModeService(
                Bootstrapper.SettingsService,
                Bootstrapper.Settings,
                mainWindow,
                AppLaunchOptions.GamepadUiRequested);
            var launchService = new EmulatorLaunchService(
                Bootstrapper.EmulatorConfigurations,
                Bootstrapper.ProcessRunner,
                new WindowFrontendController(mainWindow, interfaceModeService),
                Bootstrapper.Emulators,
                Bootstrapper.Logger,
                Bootstrapper.LaunchTargetInspector,
                Bootstrapper.GameLaunchDependencies);
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
                Bootstrapper.Logger,
                new LibretroArtworkTitleIndex(Bootstrapper.Paths, _metadataHttpClient));
            _retroAchievementsHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            _retroAchievementsHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EmuShelf/1.0");
            var retroAchievementsWebClient = new RetroAchievementsWebClient(
                _retroAchievementsHttpClient, Bootstrapper.Logger);
            var retroAchievementsRequests = new RetroAchievementsRequestCoordinator(
                retroAchievementsWebClient,
                logger: Bootstrapper.Logger);
            var automaticRetroAchievementsClient = retroAchievementsRequests.CreateClient(
                RetroAchievementsRequestMode.Automatic);
            var manualRetroAchievementsClient = retroAchievementsRequests.CreateClient(
                RetroAchievementsRequestMode.Manual);
            var retroAchievementsAccount = new RetroAchievementsAccountService(
                Bootstrapper.SettingsService,
                Bootstrapper.Settings,
                Bootstrapper.RetroAchievementsCredentialStore,
                manualRetroAchievementsClient,
                Bootstrapper.Logger);
            var retroAchievementsCatalogue = new RetroAchievementsCatalogueCache(
                Bootstrapper.Paths, automaticRetroAchievementsClient, Bootstrapper.Logger);
            var retroAchievementsMatching = new RetroAchievementsMatchingService(
                Bootstrapper.RetroAchievementsStore, retroAchievementsCatalogue, Bootstrapper.Logger);
            var retroAchievementsProgress = new RetroAchievementsProgressService(
                Bootstrapper.RetroAchievementsProgressStore,
                automaticRetroAchievementsClient,
                logger: Bootstrapper.Logger);
            var retroAchievementsDetails = new RetroAchievementsDetailsService(
                Bootstrapper.RetroAchievementsDetailsStore,
                Bootstrapper.RetroAchievementsProgressStore,
                automaticRetroAchievementsClient,
                logger: Bootstrapper.Logger,
                manualClient: manualRetroAchievementsClient);
            var retroAchievementsRefresh = new RetroAchievementsRefreshService(
                retroAchievementsAccount,
                Bootstrapper.RetroAchievementsProgressStore,
                retroAchievementsProgress,
                retroAchievementsDetails,
                logger: Bootstrapper.Logger);
            var retroAchievementsBadges = new RetroAchievementsBadgeCache(
                Bootstrapper.Paths,
                _retroAchievementsHttpClient,
                Bootstrapper.Logger);
            var viewModel = new MainViewModel(
                Bootstrapper.Library,
                Bootstrapper.FolderScanner,
                Bootstrapper.ImportRules,
                Bootstrapper.AvailabilityChecker,
                new DialogService(
                    desktop,
                    Bootstrapper.Logger,
                    retroAchievementsDetails,
                    retroAchievementsAccount,
                    retroAchievementsBadges),
                Bootstrapper.Systems,
                launchService,
                Bootstrapper.EmulatorConfigurations,
                Bootstrapper.Emulators,
                coverService,
                themeService,
                metadataService,
                metadataPreferences,
                Bootstrapper.Logger,
                Bootstrapper.RetroAchievementsIdentification,
                Bootstrapper.RetroAchievementsReadStore,
                retroAchievementsAccount,
                retroAchievementsMatching,
                retroAchievementsProgress,
                retroAchievementsDetails,
                retroAchievementsRefresh,
                Bootstrapper.MetadataStore,
                interfaceModeService,
                retroAchievementsBadges,
                Bootstrapper.CloudSaveSync);

            mainWindow.DataContext = viewModel;
            desktop.MainWindow = mainWindow;

            // Native controller input (SDL2) drives the same Gamepad-mode routing as Steam Input's
            // keyboard mapping. It polls only in Gamepad mode and degrades to no-op if SDL2 or a
            // controller is unavailable, so keyboard/Steam Input remains the fallback everywhere.
            _gamepadInput = new GamepadInputService(
                new SdlGamepadReader(),
                viewModel,
                interfaceModeService,
                Bootstrapper.Logger);

            // Availability check runs after the UI paints — background, no discovery scan.
            desktop.MainWindow.Opened += (_, _) =>
                Dispatcher.UIThread.Post(() =>
                {
                    _ = viewModel.RefreshAvailabilityAsync();
                    _ = viewModel.RefreshRetroAchievementsProgressAtStartupAsync();
                }, DispatcherPriority.Background);
            desktop.Exit += (_, _) =>
            {
                _gamepadInput?.Dispose();
                _metadataHttpClient?.Dispose();
                _retroAchievementsHttpClient?.Dispose();
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
