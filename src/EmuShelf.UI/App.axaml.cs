using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Updates;
using EmuShelf.Infrastructure.Achievements;
using EmuShelf.Infrastructure.Input;
using EmuShelf.Infrastructure.Metadata;
using EmuShelf.Infrastructure.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.Shell;
using EmuShelf.Infrastructure.Updates;

namespace EmuShelf.App;

public partial class App : Application
{
    /// <summary>
    /// Builds the platform shell for a desktop lifetime. Registered by the desktop head's
    /// <c>Program</c> before Avalonia starts; a future single-view (Android) head registers its own
    /// shell against <see cref="ISingleViewApplicationLifetime"/>. Kept as a hook so the shared
    /// composition root below never references a concrete window type.
    /// </summary>
    public static Func<IClassicDesktopStyleApplicationLifetime, AppBootstrapper, PlatformShellDependencies, IPlatformShell>?
        DesktopShellFactory { get; set; }

    public AppBootstrapper Bootstrapper { get; private set; } = null!;
    private HttpClient? _metadataHttpClient;
    private HttpClient? _webArtworkHttpClient;
    private HttpClient? _retroAchievementsHttpClient;
    private HttpClient? _updateHttpClient;
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

        // Route Avalonia's framework log into the portable Logs/ file. The default .LogToTrace() sink
        // (Program.cs) writes to System.Diagnostics.Trace, which has no listener in a Steam Game Mode
        // AppImage — so the reason a GL context failed on the Steam Deck was being discarded. The
        // shelf's GL init happens after this point, so its diagnosis is captured. See DECISIONS 2026-08-16.
        Avalonia.Logging.Logger.Sink = new Diagnostics.AvaloniaFileLogSink(Bootstrapper.Logger);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && DesktopShellFactory is { } createShell)
        {
            Compose(deps => createShell(desktop, Bootstrapper, deps));
        }

        // Lifetime-agnostic: these fire regardless of which shell (or none) was built.
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

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The lifetime-agnostic composition root. Builds the whole shared service graph, hands the
    /// window-typed subset to <paramref name="shellFactory"/>, then assembles the view model on top
    /// of the shell's collaborators and shows it. The only thing that varies per platform is the
    /// shell the factory returns.
    /// </summary>
    private void Compose(Func<PlatformShellDependencies, IPlatformShell> shellFactory)
    {
        var themeService = new AppThemeService(
            Bootstrapper.SettingsService,
            Bootstrapper.Settings);
        var metadataPreferences = new MetadataPreferencesService(
            Bootstrapper.SettingsService,
            Bootstrapper.Settings);
        var libraryViewState = new LibraryViewStateService(
            Bootstrapper.SettingsService,
            Bootstrapper.Settings,
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
        var artworkDownloader = new RemoteArtworkDownloader(
            Bootstrapper.Paths,
            _metadataHttpClient,
            Bootstrapper.Logger);
        var publicArtworkPolicy = new PublicArtworkUriPolicy();
        _webArtworkHttpClient = new HttpClient(
            PublicArtworkHttpTransport.CreateHandler(publicArtworkPolicy))
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        // User-driven web covers are fetched from arbitrary third-party hosts and CDNs, many of
        // which refuse an unknown "EmuShelf/1.0" agent with a 403 or a hotlink-protection page.
        // A mainstream browser agent is the reliable way to retrieve the full-resolution original
        // the user picked; the picker still falls back to the proxied thumbnail if it is refused.
        // (The automatic metadata client above keeps its honest EmuShelf agent.)
        _webArtworkHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
        var webArtworkDownloader = new RemoteArtworkDownloader(
            Bootstrapper.Paths,
            _webArtworkHttpClient,
            Bootstrapper.Logger,
            publicArtworkPolicy);
        // One provider instance, shared by the Desktop "Set cover" dialog and the Gamepad
        // controller-native cover search.
        var webArtworkSearch = new DuckDuckGoArtworkSearchProvider(
            _metadataHttpClient,
            publicArtworkPolicy);
        // ScreenScraper media are fetched through the SSRF-checked public downloader, then
        // imported atomically under Data/Media/ by the provider-neutral apply service.
        var scrapeApply = new GameScrapeApplicationService(
            Bootstrapper.GameDetailsStore,
            Bootstrapper.MetadataStore,
            webArtworkDownloader,
            Bootstrapper.Paths,
            Bootstrapper.Logger);
        var screenScraperAccount = new ScreenScraperAccountService(
            Bootstrapper.SettingsService,
            Bootstrapper.ScreenScraperCredentialStore,
            Bootstrapper.ScreenScraperClient,
            Bootstrapper.Logger);
        var scrapeBatch = Bootstrapper.ScreenScraperPreview is null
            ? null
            : new ScreenScraperBatchService(
                Bootstrapper.ScreenScraperPreview,
                scrapeApply,
                Bootstrapper.MetadataStore,
                Bootstrapper.GameDetailsStore,
                Bootstrapper.Logger);
        var metadataService = new GameMetadataService(
            Bootstrapper.MetadataStore,
            Bootstrapper.MetadataProfiles,
            new LibretroDatCatalog(Bootstrapper.Paths, _metadataHttpClient),
            artworkDownloader,
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

        // Hand the window-typed subset of the graph to the platform shell.
        var shell = shellFactory(
            new PlatformShellDependencies(
                retroAchievementsDetails,
                retroAchievementsAccount,
                retroAchievementsBadges,
                webArtworkSearch,
                webArtworkDownloader,
                scrapeApply,
                screenScraperAccount,
                scrapeBatch));

        var launchService = new EmulatorLaunchService(
            Bootstrapper.EmulatorConfigurations,
            Bootstrapper.ProcessRunner,
            shell.Frontend,
            Bootstrapper.Emulators,
            Bootstrapper.Logger,
            Bootstrapper.LaunchTargetInspector,
            Bootstrapper.GameLaunchDependencies);

        // In-app auto-update from GitHub Releases. The check hits only the public API; the applier
        // is platform-specific (AppImage re-exec on the Steam Deck, helper swap on Windows/macOS).
        _updateHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _updateHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"EmuShelf/{AppBuildInfo.Version}");
        var updateApplier = UpdateApplierFactory.Create(Bootstrapper.Paths, Bootstrapper.Logger);
        var updateService = new GitHubUpdateService(
            _updateHttpClient,
            SemanticVersion.ParseOrZero(AppBuildInfo.Version),
            Bootstrapper.Paths,
            Bootstrapper.Logger);
        var updateCoordinator = new AppUpdateCoordinator(
            updateService,
            updateApplier,
            Bootstrapper.SettingsService,
            Bootstrapper.Settings,
            Bootstrapper.Logger,
            requestExit: () => shell.Lifetime.Shutdown());

        var viewModel = new MainViewModel(
            Bootstrapper.Library,
            Bootstrapper.FolderScanner,
            Bootstrapper.ImportRules,
            Bootstrapper.AvailabilityChecker,
            shell.Dialog,
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
            shell.InterfaceMode,
            retroAchievementsBadges,
            Bootstrapper.CloudSaveSync,
            applicationLifetime: shell.Lifetime,
            texturePacks: Bootstrapper.TexturePacks,
            hotkeys: Bootstrapper.Hotkeys,
            libraryViewState: libraryViewState,
            screenScraperAccount: screenScraperAccount,
            screenScraperPreview: Bootstrapper.ScreenScraperPreview,
            scrapeApply: scrapeApply,
            scrapeBatch: scrapeBatch,
            artworkDownloader: webArtworkDownloader,
            artworkSearch: webArtworkSearch,
            settingsService: Bootstrapper.SettingsService,
            onScreenKeyboard: new PlatformOnScreenKeyboardService(),
            gameDetails: Bootstrapper.GameDetailsStore,
            appPaths: Bootstrapper.Paths,
            updates: updateCoordinator,
            fileReveal: new FileRevealService());

        // Native controller input (SDL2) drives the same Gamepad-mode routing as Steam Input's
        // keyboard mapping. It polls only in Gamepad mode and degrades to no-op if SDL2 or a
        // controller is unavailable, so keyboard/Steam Input remains the fallback everywhere.
        _gamepadInput = new GamepadInputService(
            new SdlGamepadReader(),
            viewModel,
            shell.InterfaceMode,
            Bootstrapper.Logger);

        shell.Show(viewModel, new ShellCallbacks(
            // Post-open background work runs after the UI paints — no discovery scan. The view model
            // orders these internally: the two passes that rebuild the grid (availability,
            // RetroAchievements) wait for the initial load and run sequentially instead of racing it,
            // so the library is built once rather than stampeded into two or three overlapping rebuilds.
            Opened: () => _ = viewModel.RunStartupBackgroundTasksAsync(),
            Closing: viewModel.FlushPendingLibraryViewStateSave,
            Exit: () =>
            {
                _gamepadInput?.Dispose();
                _webArtworkHttpClient?.Dispose();
                _metadataHttpClient?.Dispose();
                _retroAchievementsHttpClient?.Dispose();
                _updateHttpClient?.Dispose();
                Bootstrapper.Logger.Information("EmuShelf exited.");
            }));
    }
}
