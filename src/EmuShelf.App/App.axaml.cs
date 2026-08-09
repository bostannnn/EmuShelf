using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Updates;
using EmuShelf.Infrastructure.Achievements;
using EmuShelf.Infrastructure.Emulators;
using EmuShelf.Infrastructure.Input;
using EmuShelf.Infrastructure.Metadata;
using EmuShelf.Infrastructure.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.Shell;
using EmuShelf.Infrastructure.Updates;

namespace EmuShelf.App;

public partial class App : Application
{
    public AppBootstrapper Bootstrapper { get; private set; } = null!;
    private HttpClient? _metadataHttpClient;
    private HttpClient? _webArtworkHttpClient;
    private HttpClient? _retroAchievementsHttpClient;
    private HttpClient? _updateHttpClient;
    private HttpClient? _emulatorInstallHttpClient;
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
            var libraryViewState = new LibraryViewStateService(
                Bootstrapper.SettingsService,
                Bootstrapper.Settings,
                Bootstrapper.Logger);
            // Applies the saved geometry before the interface-mode service reads the window state,
            // so starting in Gamepad mode still records a maximized desktop window to return to.
            _ = new WindowLayoutService(
                Bootstrapper.SettingsService,
                Bootstrapper.Settings,
                mainWindow,
                Bootstrapper.Logger);
            var interfaceModeService = new WindowInterfaceModeService(
                Bootstrapper.SettingsService,
                Bootstrapper.Settings,
                mainWindow,
                AppLaunchOptions.InterfaceModeOverride);
            // macOS: turn the (native-no-op) FullScreen state into a real screen-filling window.
            // Subscribed after the interface-mode service has set the launch state so it fills on open.
            // Inert on Windows/Linux, where native FullScreen works. Held by the window's event
            // subscription for the app's lifetime.
            _ = new MacFullScreenController(mainWindow);
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
            _webArtworkHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EmuShelf/1.0");
            var webArtworkDownloader = new RemoteArtworkDownloader(
                Bootstrapper.Paths,
                _webArtworkHttpClient,
                Bootstrapper.Logger,
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
                requestExit: () => desktop.Shutdown());
            // In-app emulator install/update manager. Downloads supported emulators into a portable
            // Emulators/ folder and auto-wires them for systems the user hasn't configured. Its own
            // HttpClient has a long timeout for large downloads.
            _emulatorInstallHttpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            _emulatorInstallHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"EmuShelf/{AppBuildInfo.Version}");
            var emulatorInstallService = new EmulatorInstallService(
                Bootstrapper.Emulators,
                Bootstrapper.Paths,
                new JsonEmulatorInstallManifestStore(Bootstrapper.Paths, Bootstrapper.Logger),
                new GitHubEmulatorReleaseClient(_emulatorInstallHttpClient, Bootstrapper.Logger),
                Bootstrapper.Logger,
                userProvidedExecutableProbe: emulatorId =>
                    ResolveUserProvidedExecutable(Bootstrapper, emulatorId));
            var emulatorInstallCoordinator = new EmulatorInstallCoordinator(
                emulatorInstallService,
                Bootstrapper.Emulators,
                Bootstrapper.Logger,
                onInstalled: (emulatorId, executablePath) =>
                    AutoAssignManagedExecutableAsync(Bootstrapper, emulatorId, executablePath),
                openDownloadPage: OpenExternalUrlAsync);
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
                    retroAchievementsBadges,
                    new DuckDuckGoArtworkSearchProvider(
                        _metadataHttpClient,
                        publicArtworkPolicy),
                    webArtworkDownloader,
                    Bootstrapper.ScreenScraperPreview,
                    scrapeApply,
                    screenScraperAccount,
                    scrapeBatch,
                    Bootstrapper.SettingsService),
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
                Bootstrapper.CloudSaveSync,
                applicationLifetime: new ApplicationLifetimeService(desktop),
                texturePacks: Bootstrapper.TexturePacks,
                hotkeys: Bootstrapper.Hotkeys,
                libraryViewState: libraryViewState,
                screenScraperAccount: screenScraperAccount,
                screenScraperPreview: Bootstrapper.ScreenScraperPreview,
                scrapeApply: scrapeApply,
                scrapeBatch: scrapeBatch,
                artworkDownloader: webArtworkDownloader,
                settingsService: Bootstrapper.SettingsService,
                onScreenKeyboard: new PlatformOnScreenKeyboardService(),
                gameDetails: Bootstrapper.GameDetailsStore,
                appPaths: Bootstrapper.Paths,
                updates: updateCoordinator,
                fileReveal: new FileRevealService(),
                emulatorInstalls: emulatorInstallCoordinator);

            mainWindow.DataContext = viewModel;
            desktop.MainWindow = mainWindow;

            // Subscribed after WindowLayoutService's own Closing handler, so the layout is written
            // first and this read-modify-write picks it up rather than racing it.
            mainWindow.Closing += (_, _) => viewModel.FlushPendingLibraryViewStateSave();

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
                    _ = viewModel.LoadTexturePacksAtStartupAsync();
                    _ = viewModel.Updates?.CheckOnLaunchAsync();
                }, DispatcherPriority.Background);
            desktop.Exit += (_, _) =>
            {
                _gamepadInput?.Dispose();
                _webArtworkHttpClient?.Dispose();
                _metadataHttpClient?.Dispose();
                _retroAchievementsHttpClient?.Dispose();
                _updateHttpClient?.Dispose();
                _emulatorInstallHttpClient?.Dispose();
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

    /// <summary>
    /// The user's own configured executable for an emulator when it is not one EmuShelf manages (i.e. it
    /// lives outside the managed <c>Emulators/&lt;id&gt;/</c> folder), so the install manager reports it as
    /// user-provided instead of offering to overwrite it. Null when only a managed/no install is configured.
    /// </summary>
    private static string? ResolveUserProvidedExecutable(AppBootstrapper bootstrapper, string emulatorId)
    {
        var definition = bootstrapper.Emulators
            .FirstOrDefault(emulator => string.Equals(emulator.Id, emulatorId, StringComparison.Ordinal));
        if (definition is null)
            return null;

        var managedRoot = Path.Combine(bootstrapper.Paths.EmulatorsDirectory, emulatorId);
        foreach (var systemId in definition.SupportedSystemIds)
        {
            var executable = bootstrapper.EmulatorConfigurations.GetForEmulator(systemId, emulatorId)?.ExecutablePath;
            if (!string.IsNullOrWhiteSpace(executable) && !IsUnderDirectory(executable, managedRoot))
                return executable;
        }

        return null;
    }

    /// <summary>
    /// After a managed install, point every system the emulator supports that has no executable configured
    /// yet at the managed executable. Never overrides a system the user already set up. Skips core-based
    /// emulators (RetroArch), where an executable alone would not make a system launchable.
    /// </summary>
    private static Task AutoAssignManagedExecutableAsync(
        AppBootstrapper bootstrapper,
        string emulatorId,
        string executablePath) =>
        Task.Run(() =>
        {
            var definition = bootstrapper.Emulators
                .FirstOrDefault(emulator => string.Equals(emulator.Id, emulatorId, StringComparison.Ordinal));
            if (definition is null || definition.RequiresCorePath)
                return;

            var toSave = new List<EmulatorConfiguration>();
            foreach (var systemId in definition.SupportedSystemIds)
            {
                // Skip a system the user already configured with any emulator (its active profile has a
                // launch target). SaveAll activates every system it writes, so already-configured systems
                // must be excluded entirely rather than saved.
                if (bootstrapper.EmulatorConfigurations.Get(systemId)?.LaunchTarget is not null)
                    continue;

                toSave.Add(new EmulatorConfiguration(systemId, executablePath, LaunchArguments: null)
                {
                    LaunchTarget = new DirectExecutableTarget(executablePath),
                    EmulatorId = emulatorId,
                    // Explicit so RetroArch would share one install and per-system emulators stay separate;
                    // the store converts the absolute path to a portable relative one on write.
                    EmulatorInstallationId = definition.GetDefaultInstallationId(systemId),
                });
            }

            if (toSave.Count > 0)
                bootstrapper.EmulatorConfigurations.SaveAll(toSave);
        });

    private static bool IsUnderDirectory(string path, string directory)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetFullPath(directory);
            return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Opens an emulator's download page in the user's browser. Best-effort; the caller logs failures.</summary>
    private static Task OpenExternalUrlAsync(string url)
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo { FileName = url, UseShellExecute = true }
            : new ProcessStartInfo
            {
                FileName = OperatingSystem.IsMacOS() ? "open" : "xdg-open",
                UseShellExecute = false,
            };
        if (!OperatingSystem.IsWindows())
            startInfo.ArgumentList.Add(url);
        using var process = Process.Start(startInfo);
        return Task.CompletedTask;
    }
}
