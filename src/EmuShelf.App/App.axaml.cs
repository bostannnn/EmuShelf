using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;

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
            var viewModel = new MainViewModel(
                Bootstrapper.Library,
                Bootstrapper.FolderScanner,
                Bootstrapper.ImportRules,
                Bootstrapper.AvailabilityChecker,
                new DialogService(desktop),
                Bootstrapper.Systems);

            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Availability check runs after the UI paints — background, no discovery scan.
            desktop.MainWindow.Opened += (_, _) =>
                Dispatcher.UIThread.Post(
                    () => _ = viewModel.RefreshAvailabilityAsync(),
                    DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }
}