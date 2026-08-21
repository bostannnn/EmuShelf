using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Input;
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

    /// <summary>
    /// Builds the platform shell for a single-view lifetime. Registered by the Android head before
    /// Avalonia starts; the mirror image of <see cref="DesktopShellFactory"/>. Only one of the two
    /// factories is ever set in a given process, so the composition root below stays identical under
    /// either lifetime and never names a concrete window or activity type.
    /// </summary>
    public static Func<ISingleViewApplicationLifetime, AppBootstrapper, PlatformShellDependencies, IPlatformShell>?
        SingleViewShellFactory { get; set; }

    /// <summary>
    /// Builds the native controller reader the poll loop reads. Desktop leaves this null and the
    /// composition root uses <see cref="SdlGamepadReader"/>; the Android head sets it to a reader fed by the
    /// Activity's <c>MotionEvent</c> stream (SDL cannot read Android input — and its native payload is
    /// excluded from the APK). Set before Avalonia starts.
    /// </summary>
    public static Func<IGamepadReader>? GamepadReaderFactory { get; set; }

    /// <summary>
    /// The portable-storage root the head hands to <see cref="AppBootstrapper"/>, for platforms that
    /// cannot resolve it themselves. Desktop leaves this null and <see cref="AppBootstrapper"/> uses
    /// <c>AppContext.BaseDirectory</c> / the per-user macOS location; the Android head sets it to the
    /// app-private files directory (the only reliably writable path there), which it alone can obtain
    /// through the Android context. Set before Avalonia starts.
    /// </summary>
    public static string? BaseDirectoryOverride { get; set; }

    /// <summary>
    /// The Android head's first-run data-folder gate. When set and it reports no resolved folder yet, the
    /// composition root shows the onboarding view instead of opening the database, and resumes composition
    /// in-process once the user picks a folder. Desktop leaves this null and boots straight through — its
    /// data folder is resolved from the environment. Set before Avalonia starts.
    /// </summary>
    public static IDataLocationBootstrap? DataLocation { get; set; }

    /// <summary>
    /// The couch controller dispatcher for the first-run onboarding screen, or null when onboarding is not
    /// showing. The Android head points its key-event bridge at this so the D-pad and A button work on the
    /// onboarding card too — before the shared shell (and its own dispatcher) exists. Set while onboarding
    /// is up and cleared once a folder is chosen.
    /// </summary>
    public static Func<GamepadAction, bool>? OnboardingGamepadDispatch { get; private set; }

    /// <summary>
    /// Restarts the process, supplied by the Android head. Used to hand off from onboarding to the real
    /// shell: Avalonia's Android single-view host captures its <c>MainView</c> at startup and does not
    /// re-render when it is reassigned live, so the composed shell must come up in a fresh process — which
    /// then resolves the just-persisted data-folder pointer and boots straight to the library. Null on
    /// desktop, which never onboards.
    /// </summary>
    public static Action? RestartRequested { get; set; }

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
        // The Avalonia dev-tools (AvaloniaUI.DiagnosticsSupport) are a desktop-only debugging aid and
        // ship no Android asset, so attaching them there throws FileNotFound during AppBuilder setup.
        // Keep the reference in a separate method so JITting Initialize on Android never has to resolve
        // that assembly — it is only ever loaded when the desktop branch actually calls in.
        if (!OperatingSystem.IsAndroid())
            AttachDesktopDeveloperTools();
#endif
    }

#if DEBUG
    // NoInlining makes the guard robust even in a hypothetical future build where the diagnostics
    // assembly *is* present on Android: the method (and thus its reference to that assembly) is only
    // ever JITted when the desktop branch actually calls it, never when Initialize runs on Android.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void AttachDesktopDeveloperTools() => this.AttachDeveloperTools();
#endif

    public override void OnFrameworkInitializationCompleted()
    {
        // First-run data-folder gate (Android). When the head reports no resolved folder, show onboarding
        // instead of opening the database against a folder that does not exist yet; composition resumes
        // in-process from OnDataFolderChosen once the user picks one. Desktop leaves DataLocation null.
        if (DataLocation is { ResolvedBaseDirectory: null } bootstrap
            && ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            StartDataFolderOnboarding(singleView, bootstrap);
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // Resolved on Android (the head persisted a pointer previously) or desktop (null): a resolved
        // base directory is the single source of truth for the portable root.
        if (DataLocation?.ResolvedBaseDirectory is { } resolvedBaseDirectory)
            BaseDirectoryOverride = resolvedBaseDirectory;

        BuildAndRun();
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Shows the onboarding view as the initial single-view content. Its view-model completes by handing
    /// back the chosen base directory, at which point <see cref="OnDataFolderChosen"/> resumes the normal
    /// composition and swaps the real shell in over the onboarding view.
    /// </summary>
    private void StartDataFolderOnboarding(
        ISingleViewApplicationLifetime singleView,
        IDataLocationBootstrap bootstrap)
    {
        var onboarding = new OnboardingViewModel(
            bootstrap,
            bootstrap.OnboardingReason,
            onCompleted: OnDataFolderChosen);
        // Route the Android key-event bridge into onboarding until the real shell takes over.
        OnboardingGamepadDispatch = onboarding.DispatchGamepadAction;
        singleView.MainView = new Views.OnboardingView { DataContext = onboarding };
    }

    private void OnDataFolderChosen(string baseDirectory)
    {
        OnboardingGamepadDispatch = null;
        BaseDirectoryOverride = baseDirectory;

        // The bootstrap has already persisted the pointer, so a restart re-runs the composition root, which
        // resolves it and boots straight to the library. This is required on Android because the single-view
        // host will not swap in a live-reassigned MainView; where no restarter is supplied (desktop, which
        // never onboards), compose in-process.
        if (RestartRequested is { } restart)
            restart();
        else
            BuildAndRun();
    }

    /// <summary>
    /// Builds the composition root and shows the platform shell. Runs once per process — either straight
    /// from <see cref="OnFrameworkInitializationCompleted"/> when a data folder is already resolved, or
    /// after first-run onboarding picks one.
    /// </summary>
    private void BuildAndRun()
    {
        Bootstrapper = new AppBootstrapper(BaseDirectoryOverride);

        // Route Avalonia's framework log into the portable Logs/ file. The default .LogToTrace() sink
        // (Program.cs) writes to System.Diagnostics.Trace, which has no listener in a Steam Game Mode
        // AppImage — so the reason a GL context failed on the Steam Deck was being discarded. The
        // shelf's GL init happens after this point, so its diagnosis is captured. See DECISIONS 2026-08-16.
        Avalonia.Logging.Logger.Sink = new Diagnostics.AvaloniaFileLogSink(Bootstrapper.Logger);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && DesktopShellFactory is { } createDesktopShell)
        {
            Compose(deps => createDesktopShell(desktop, Bootstrapper, deps));
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView
            && SingleViewShellFactory is { } createSingleViewShell)
        {
            Compose(deps => createSingleViewShell(singleView, Bootstrapper, deps));
        }
        else
        {
            // No shell was composed — the surface will be blank. This only happens on a misconfigured
            // or new head (the desktop head always sets DesktopShellFactory in Program.Main), so turn
            // the otherwise silent blank-window mystery into a diagnosable log line.
            Bootstrapper.Logger.Error(
                $"No platform shell composed: lifetime is {ApplicationLifetime?.GetType().Name ?? "null"} "
                + "and no matching shell factory was registered. The window/view will be blank.");
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

        // The Android head supplies its own intent-based launch service; every desktop head leaves this
        // null and gets the shared process-tracking launcher.
        var launchService = shell.LaunchService ?? new EmulatorLaunchService(
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

        // Native controller input drives the same Gamepad-mode routing as Steam Input's keyboard
        // mapping. It polls only in Gamepad mode and degrades to no-op if no controller is available,
        // so keyboard/Steam Input remains the fallback everywhere. Desktop reads SDL2; the Android head
        // supplies a MotionEvent-fed reader via GamepadReaderFactory (SDL cannot read Android input).
        _gamepadInput = new GamepadInputService(
            GamepadReaderFactory?.Invoke() ?? new SdlGamepadReader(),
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
