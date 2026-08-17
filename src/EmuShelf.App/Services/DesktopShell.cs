using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using EmuShelf.App.Startup;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>
/// The desktop platform shell: a real <see cref="MainWindow"/> plus the window-typed services that
/// drive it. Construction order matters and mirrors the historical composition root — the layout
/// service applies saved geometry before the interface-mode service reads the window state, and the
/// macOS full-screen controller subscribes after the launch mode has been set.
/// </summary>
public sealed class DesktopShell : IPlatformShell
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly MainWindow _mainWindow;
    private readonly WindowInterfaceModeService _interfaceMode;

    public DesktopShell(
        IClassicDesktopStyleApplicationLifetime desktop,
        AppBootstrapper boot,
        PlatformShellDependencies deps)
    {
        _desktop = desktop;
        _mainWindow = new MainWindow();

        // Applies the saved geometry before the interface-mode service reads the window state,
        // so starting in Gamepad mode still records a maximized desktop window to return to.
        _ = new WindowLayoutService(
            boot.SettingsService,
            boot.Settings,
            _mainWindow,
            boot.Logger);
        _interfaceMode = new WindowInterfaceModeService(
            boot.SettingsService,
            boot.Settings,
            _mainWindow,
            AppLaunchOptions.InterfaceModeOverride);
        // macOS: turn the (native-no-op) FullScreen state into a real screen-filling window.
        // Subscribed after the interface-mode service has set the launch state so it fills on open.
        // Inert on Windows/Linux, where native FullScreen works. Held by the window's event
        // subscription for the app's lifetime.
        _ = new MacFullScreenController(_mainWindow);

        Frontend = new WindowFrontendController(_mainWindow, _interfaceMode);
        Lifetime = new ApplicationLifetimeService(desktop);
        Dialog = new DialogService(
            _mainWindow,
            boot.Logger,
            deps.RetroAchievementsDetails,
            deps.RetroAchievementsAccount,
            deps.RetroAchievementsBadges,
            deps.WebArtworkSearch,
            deps.WebArtworkDownloader,
            boot.ScreenScraperPreview,
            deps.ScrapeApply,
            deps.ScreenScraperAccount,
            deps.ScrapeBatch,
            boot.SettingsService);
    }

    public IInterfaceModeService InterfaceMode => _interfaceMode;
    public IFrontendController Frontend { get; }
    public IApplicationLifetimeService Lifetime { get; }
    public IDialogService Dialog { get; }

    public void Show(MainViewModel viewModel, ShellCallbacks callbacks)
    {
        _mainWindow.DataContext = viewModel;
        _desktop.MainWindow = _mainWindow;

        // Subscribed after WindowLayoutService's own Closing handler, so the layout is written
        // first and this read-modify-write picks it up rather than racing it.
        _mainWindow.Closing += (_, _) => callbacks.Closing();

        // Availability check and other startup refreshes run after the UI paints — background,
        // no discovery scan.
        _mainWindow.Opened += (_, _) =>
            Dispatcher.UIThread.Post(callbacks.Opened, DispatcherPriority.Background);

        _desktop.Exit += (_, _) => callbacks.Exit();
    }
}
