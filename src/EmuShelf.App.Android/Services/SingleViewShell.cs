using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using EmuShelf.App.Android.Views;
using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// The single-view (Android) platform shell — the mirror of the desktop <c>DesktopShell</c>. It owns
/// the one <see cref="MainView"/> the Activity displays and the view-based equivalents of the
/// window-typed services the shared composition root cannot build itself. Everything else in the
/// service graph is identical to desktop, which is the whole point of the A0 seam.
/// </summary>
public sealed class SingleViewShell : IPlatformShell
{
    private readonly ISingleViewApplicationLifetime _singleView;
    private readonly MainView _mainView;

    public SingleViewShell(
        ISingleViewApplicationLifetime singleView,
        AppBootstrapper boot,
        PlatformShellDependencies deps)
    {
        _singleView = singleView;
        _mainView = new MainView();

        InterfaceMode = new AndroidInterfaceModeService();
        Frontend = new AndroidFrontendController();
        Lifetime = new SingleViewApplicationLifetimeService();
        // The dialog service needs the live TopLevel for the SAF folder picker; resolve it lazily each
        // call since it only exists once the view is attached to the Activity's visual tree.
        Dialog = new SingleViewDialogService(boot.Logger, () => TopLevel.GetTopLevel(_mainView));
    }

    public IInterfaceModeService InterfaceMode { get; }
    public IFrontendController Frontend { get; }
    public IApplicationLifetimeService Lifetime { get; }
    public IDialogService Dialog { get; }

    public void Show(MainViewModel viewModel, ShellCallbacks callbacks)
    {
        _mainView.DataContext = viewModel;
        _singleView.MainView = _mainView;

        // Point the Activity's couch key-event bridge at this view model's dispatcher. The Activity
        // owns the key events (Android gamepad buttons never reach Avalonia's KeyDown), so this is how
        // Menu / D-pad / A-B reach the shared UI on device.
        AndroidGamepadInput.Dispatch = viewModel.DispatchGamepadAction;

        // Opened must run exactly ONCE — the shared contract, honoured on desktop by Window.Opened.
        // AttachedToVisualTree is a *recurring* event: it re-fires on activity recreation (any config
        // change outside the declared ConfigurationChanges set), the "Don't keep activities" developer
        // option, split-screen, and process-death restore. Without this guard each re-attach would
        // re-run the whole startup background pass — availability rescan, a RetroAchievements network
        // refresh, the GitHub update check, and overlapping grid rebuilds — the exact stampede that
        // pass is designed to avoid. See the A1 code review / DECISIONS 2026-08-17.
        var opened = false;
        _mainView.AttachedToVisualTree += (_, _) =>
        {
            if (opened)
                return;
            opened = true;
            Dispatcher.UIThread.Post(callbacks.Opened, DispatcherPriority.Background);
        };

        // The durable save point on Android is backgrounding (onPause/onStop), which Avalonia surfaces
        // as IActivatableLifetime.Deactivated(ActivationKind.Background) — NOT visual-tree detach, which
        // also fires on the transient re-attaches above. Flushing pending library-view state here means
        // an OS process kill after backgrounding does not lose it. FlushPendingLibraryViewStateSave is
        // guarded and idempotent, so firing on every background is safe.
        if (Avalonia.Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Deactivated += (_, e) =>
            {
                if (e.Kind == ActivationKind.Background)
                    callbacks.Closing();
            };
        }

        // No Exit wiring: Avalonia's Android single-view lifetime is not IControlledApplicationLifetime
        // and exposes no terminating event, so callbacks.Exit (dispose the HttpClients/gamepad service,
        // final log) has no honest moment to run and the OS reclaims the process on teardown. This is
        // benign for the skeleton (sockets/handles are reclaimed; the gamepad service is a no-op on
        // Android); a real teardown/persistence-across-process-death path is Milestone B.
    }
}
