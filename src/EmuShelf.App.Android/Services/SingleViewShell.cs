using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using EmuShelf.App.Android.Views;
using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Launching;

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
    // The view for the current Activity. Rebuilt fresh by the MainViewFactory on every (re)creation
    // (see Show), so this tracks whichever instance is live now — null before the first build.
    private MainView? _currentView;

    public SingleViewShell(
        ISingleViewApplicationLifetime singleView,
        AppBootstrapper boot,
        PlatformShellDependencies deps)
    {
        _singleView = singleView;

        InterfaceMode = new AndroidInterfaceModeService();
        Frontend = new AndroidFrontendController();
        // "Quit EmuShelf" from the couch menu finishes the launcher Activity and drops it from the
        // recents list — the closest thing to a desktop quit on Android. Without a requestClose the
        // shared lifetime service no-ops, which is why the menu item did nothing. Runs on the UI
        // thread (the command dispatches there) and no-ops if the Activity is already gone.
        Lifetime = new SingleViewApplicationLifetimeService(
            () => MainActivity.Current?.FinishAndRemoveTask());
        // The dialog service needs the live TopLevel for the SAF folder picker; resolve it lazily each
        // call against the current Activity's view, which only exists once the factory has built it and
        // it is attached to the visual tree.
        Dialog = new SingleViewDialogService(
            boot.Logger,
            () => _currentView is { } view ? TopLevel.GetTopLevel(view) : null);

        // The Android launch path: fire an Intent at the emulator app. The application context starts the
        // emulator as a new task (AndroidGameLauncher adds NEW_TASK), which is enough without a live
        // Activity reference and works even when the launch originates off the Activity.
        var gameLauncher = new AndroidGameLauncher(
            () => global::Android.App.Application.Context,
            boot.Logger);

        // A durable single-slot record of a launch awaiting post-play completion, in the durable Settings
        // dir (not Cache) so it survives EmuShelf being killed while an emulator is foregrounded.
        _pendingSessions = new FilePendingPlaySessionStore(
            Path.Combine(boot.Paths.SettingsDirectory, "pending-play-session.json"),
            boot.Logger);
        _logger = boot.Logger;

        LaunchService = new AndroidEmulatorLaunchService(
            gameLauncher,
            boot.EmulatorConfigurations,
            _pendingSessions,
            boot.Library,
            boot.Logger);
    }

    private readonly IPendingPlaySessionStore _pendingSessions;
    private readonly IAppLogger _logger;
    private bool _completingSession;

    public IInterfaceModeService InterfaceMode { get; }
    public IFrontendController Frontend { get; }
    public IApplicationLifetimeService Lifetime { get; }
    public IDialogService Dialog { get; }
    public IEmulatorLaunchService LaunchService { get; }

    public void Show(MainViewModel viewModel, ShellCallbacks callbacks)
    {
        // Feed the log-based perf sampler this view model's state snapshot (layout / CRT / platform / render
        // path), so each PerfTrace sample line is tagged with what the user is actually looking at. Sink and
        // sampler are started in the Android application; this supplies the "what" for the "how fast".
        // View-model-level, so it is wired once here — not rebuilt with each view the factory below produces.
        global::EmuShelf.App.Diagnostics.PerfTrace.StateProvider = () => viewModel.PerfStateSnapshot;

        // Point the Activity's couch key-event bridge at this view model's dispatcher. The Activity
        // owns the key events (Android gamepad buttons never reach Avalonia's KeyDown), so this is how
        // Menu / D-pad / A-B reach the shared UI on device. View-model-level, not view-level, so it is
        // wired once here rather than rebuilt with each view the factory below produces.
        AndroidGamepadInput.Dispatch = viewModel.DispatchGamepadAction;

        // The Android system Back button / gesture: close an open couch overlay if one is open, otherwise
        // let the Activity fall through to the platform (exit). Kept off the Dispatch path because the
        // library-level Cancel swallows B, which would trap Back and make the app impossible to leave.
        AndroidGamepadInput.DispatchBack = viewModel.DispatchBackButton;

        // Point the Activity's return signal at deferred play-session completion. This fires on every
        // return to the foreground (including the first, cold-start one), which is exactly what recovers a
        // session interrupted by process death: if a pending record exists, complete it; otherwise no-op.
        AndroidActivityLifecycle.ReturnedToForeground = () => CompletePendingSession(viewModel);

        // Opened must run exactly ONCE per process — the shared contract, honoured on desktop by
        // Window.Opened. AttachedToVisualTree is a *recurring* event: with the factory below a NEW view
        // (and its AttachedToVisualTree) is built on every activity recreation (any config change outside
        // the declared ConfigurationChanges set, "Don't keep activities", split-screen, process-death
        // restore). The guard lives on the shell, not the view, so no matter how many views are built the
        // whole startup background pass — availability rescan, a RetroAchievements network refresh, the
        // GitHub update check, overlapping grid rebuilds — runs once. See the A1 review / DECISIONS
        // 2026-08-17.
        var opened = false;

        MainView BuildMainView()
        {
            var view = new MainView { DataContext = viewModel };
            _currentView = view;
            view.AttachedToVisualTree += (_, _) =>
            {
                if (opened)
                    return;
                opened = true;
                Dispatcher.UIThread.Post(callbacks.Opened, DispatcherPriority.Background);
            };
            return view;
        }

        // Use the supported Android hosting model: hand the Activity a factory it invokes to build a
        // FRESH MainView on every (re)creation, bound to the one long-lived view model. Setting
        // ISingleViewApplicationLifetime.MainView instead caches a single instance, reuses it across
        // activities, and makes Avalonia log "…MainView is not fully supported on Android. Consider
        // setting IActivityApplicationLifetime.MainViewFactory." on every cold start. The concrete
        // Android lifetime implements IActivityApplicationLifetime too, so the cast succeeds there; the
        // else branch is a harmless fallback for any other single-view host.
        if (_singleView is IActivityApplicationLifetime activityLifetime)
            activityLifetime.MainViewFactory = () => BuildMainView();
        else
            _singleView.MainView = BuildMainView();

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

    // Runs the post-play work for a launch that has returned. Called on the Android main thread (the same
    // thread as Avalonia's UI thread) from the top-resumed signal, so the reentrancy guard is a plain bool.
    // The launch duration is wall-clock from launch to this return — approximate, since time spent away
    // from the game before returning to EmuShelf counts too; good enough for a play-time estimate.
    private void CompletePendingSession(MainViewModel viewModel)
    {
        if (_completingSession)
            return;

        var session = _pendingSessions.Get();
        if (session is null)
            return;

        _completingSession = true;
        var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - session.StartedAtUnixMs;
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, elapsedMs));

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await viewModel.CompleteDeferredPlaySessionAsync(session.GameId, duration);
            }
            catch (Exception ex)
            {
                _logger.Error($"Could not complete the deferred play session for {session.GameTitle}.", ex);
            }
            finally
            {
                // Clear only after completion so a crash mid-completion leaves the session for the next
                // return/startup to retry.
                _pendingSessions.Clear();
                _completingSession = false;
            }
        });
    }
}
