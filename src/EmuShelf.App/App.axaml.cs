using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Launching;

namespace EmuShelf.App;

public partial class App : Application
{
    public AppBootstrapper Bootstrapper { get; private set; } = null!;

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
            var mainWindow = new MainWindow();
            var launchService = new EmulatorLaunchService(
                Bootstrapper.EmulatorConfigurations,
                Bootstrapper.ProcessRunner,
                new WindowFrontendController(mainWindow),
                Bootstrapper.Emulators,
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
                new GameCoverService(Bootstrapper.Paths),
                themeService,
                Bootstrapper.Logger);

            mainWindow.DataContext = viewModel;
            desktop.MainWindow = mainWindow;

            // Availability check runs after the UI paints — background, no discovery scan.
            desktop.MainWindow.Opened += (_, _) =>
                Dispatcher.UIThread.Post(
                    () => _ = viewModel.RefreshAvailabilityAsync(),
                    DispatcherPriority.Background);
            desktop.Exit += (_, _) => Bootstrapper.Logger.Information("EmuShelf exited.");

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
