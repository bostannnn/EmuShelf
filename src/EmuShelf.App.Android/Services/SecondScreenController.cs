using System.ComponentModel;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Hardware.Display;
using Android.OS;
using Android.Views;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.SecondScreen;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Owns the Thor companion display for the lifetime of the Android frontend. The visible surface is an
/// embedded Avalonia view (<see cref="ThorSecondScreenPresentation.Model"/>); this controller is the
/// Android glue: it finds the presentation display, drives the view model, enumerates launchable apps,
/// launches them on Screen-2, and keeps the process alive while a game owns the main panel. Only dock
/// mutation and running-vs-focused target selection cross into Core.
/// </summary>
internal sealed class SecondScreenController
    : Java.Lang.Object, DisplayManager.IDisplayListener, IExternalDisplayProbe, ISecondScreenLaunchCoordinator, IDisposable
{
    private static readonly TimeSpan DetailRefreshAge = TimeSpan.FromMinutes(5);

    private readonly ISecondScreenDockStore _dockStore;
    private readonly IRetroAchievementsReadStore _readStore;
    private readonly IRetroAchievementsDetailsService _details;
    private readonly IRetroAchievementsAccountService _account;
    private readonly IRetroAchievementsBadgeCache _badges;
    private readonly IGameDetailsStore _gameDetails;
    private readonly IAppLogger _logger;
    private readonly Handler _mainHandler = new(Looper.MainLooper!);

    private readonly Dictionary<string, SecondScreenApp> _apps = new(StringComparer.Ordinal);
    private SecondScreenDock _dock;
    private SecondScreenNavigationState _navigation = SecondScreenNavigationState.Initial;
    private MainViewModel? _viewModel;
    private MainActivity? _activity;
    private DisplayManager? _displayManager;
    private ThorSecondScreenPresentation? _presentation;
    private long? _runningGameId;
    private string? _runningGameTitle;
    private long? _achievementTargetGameId;
    private string? _achievementTargetTitle;
    // Short-lived cache of the RA game-link table so the browse-follow does not open a fresh SQLite
    // connection and full-scan it on the UI thread for every settled game. A newly linked game shows up
    // within the TTL; links change rarely and only from the main app.
    private IReadOnlyDictionary<long, RetroAchievementsGameLink>? _cachedLinks;
    private DateTimeOffset _cachedLinksAt;
    private static readonly TimeSpan LinksCacheTtl = TimeSpan.FromSeconds(3);
    private long _spotlightGeneration;
    private long _achievementsFollowGeneration;
    private bool _disposed;
    private bool _appsLoaded;
    private bool _appsLoadInFlight;
    // Set while a dock-launched app owns Screen-2. The companion Presentation is hidden for its duration
    // (see LaunchOnSecondScreen) and re-shown when EmuShelf next returns to the foreground.
    private bool _appLaunchedOnSecondScreen;
    // Set while a *game* owns Screen-2 (launched there from the library). The companion swaps onto the
    // built-in panel for its duration and swaps back on return (see GameStarted / ReturnedToBrowse).
    private bool _gameOnSecondScreen;

    public SecondScreenController(
        ISecondScreenDockStore dockStore,
        IRetroAchievementsReadStore readStore,
        IRetroAchievementsDetailsService details,
        IRetroAchievementsAccountService account,
        IRetroAchievementsBadgeCache badges,
        IGameDetailsStore gameDetails,
        IAppLogger logger)
    {
        _dockStore = dockStore;
        _readStore = readStore;
        _details = details;
        _account = account;
        // Feeds the achievement grid's badge tiles (each AchievementRowViewModel loads its badge from here).
        _badges = badges;
        _gameDetails = gameDetails;
        _logger = logger;
        _dock = dockStore.Load();
    }

    internal void Start(MainViewModel viewModel)
    {
        if (_disposed || ReferenceEquals(_viewModel, viewModel))
            return;

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModelPropertyChanged;

        AndroidActivityLifecycle.ActivityAvailable += AttachActivity;
        AndroidActivityLifecycle.ActivityDestroyed += ActivityDestroyed;
        AndroidActivityLifecycle.TopResumedChanged += TopResumedChanged;
        // The optional accessibility watcher re-shows the companion when a dock-launched app is dismissed
        // (see SecondScreenReturnWatcher). It is only useful while EmuShelf drives Screen-2, so point it
        // here; it is a no-op until the user enables the service in Settings → Accessibility.
        SecondScreenAccessibility.ForegroundWindowChanged = OnForegroundWindowChanged;
        if (MainActivity.Current is { } activity)
            AttachActivity(activity);
    }

    /// <summary>
    /// Invoked when a game running on Screen-2 is detected as closed (via the accessibility watcher), so the
    /// head can finish the deferred play session — the game-on-external mirror of the top-resumed return the
    /// head uses when the game is on the built-in screen. Set by <c>SingleViewShell</c>.
    /// </summary>
    public Action? ExternalGameReturned { get; set; }

    // Fired by SecondScreenReturnWatcher (accessibility) on every window-state change, on any display.
    private void OnForegroundWindowChanged(string? package, string? className)
    {
        // Relevant only while EmuShelf has handed Screen-2 to something else: a dock app (companion hidden)
        // or a library game launched onto the external screen (companion swapped to the built-in panel).
        if (className is null || !(_appLaunchedOnSecondScreen || _gameOnSecondScreen))
            return;

        // The stock secondary-display launcher returning to the front means whatever we put on Screen-2 has
        // been closed (backed out of / exited). Its class is display-specific, so main-screen launcher events
        // never match here.
        if (!className.Contains("secondarydisplay.SecondaryDisplayLauncher", StringComparison.Ordinal))
            return;

        RunOnMain(() =>
        {
            if (_appLaunchedOnSecondScreen)
            {
                // A dock app closed: re-show the companion over it immediately — NeoStation's instant return.
                _appLaunchedOnSecondScreen = false;
                _presentation?.Show();
                _logger.Information("Second-screen: companion re-shown after a dock app closed on Screen-2.");
            }
            else if (_gameOnSecondScreen)
            {
                // A game on Screen-2 was closed. Return to browsing (swaps the companion back to Screen-2 and
                // lifts the dim standby) and let the head finish the play session. This is the ONLY return
                // signal for a game-on-external launch: unlike a game on the built-in screen, EmuShelf never
                // lost the foreground, so the top-resumed edge cannot be used (see SingleViewShell).
                _logger.Information("Second-screen: game closed on Screen-2; returning to browse.");
                ReturnedToBrowse();
                ExternalGameReturned?.Invoke();
            }
        });
    }

    // The display id of the second screen we own, or null when the companion is not attached (no second
    // screen, or another app owns the presentation display). This is the target a game-on-external launch
    // aims at, so gating on our own presentation is deliberate: if we can't put the companion there, we
    // shouldn't send a game there either.
    public int? ExternalDisplayId => _presentation?.Display?.DisplayId;

    public bool HasExternalDisplay => _presentation?.Display is not null;

    // True only while the accessibility watcher is actually bound and delivering window-change events — the
    // single signal that lets a game launched onto Screen-2 be detected as closed so the library returns.
    // The launch path gates external-screen launches on this: without the watcher, a game sent to Screen-2
    // can never return (EmuShelf stays foregrounded on the built-in panel, so the top-resumed edge never
    // fires), which would strand the companion in standby and never complete the play session.
    public bool IsSecondScreenReturnReady => SecondScreenAccessibility.IsConnected;

    // Sends the user to the system accessibility screen to enable the watcher, mirroring the onboarding
    // step's action. The switch is flipped there and observed the next time EmuShelf regains the foreground.
    public void RequestSecondScreenReturn()
    {
        try
        {
            using var intent = new Intent(global::Android.Provider.Settings.ActionAccessibilitySettings);
            intent.AddFlags(ActivityFlags.NewTask);
            Application.Context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not open the accessibility settings screen.", ex);
        }
    }

    // True while a game launched from the library owns Screen-2 and EmuShelf stays interactive on the
    // built-in panel. The head gates its top-resumed "returned from a game" handling on this: with the game
    // on the second screen, EmuShelf regaining the foreground (e.g. the user taps the built-in panel) does
    // NOT mean the game ended — treating it as a return would re-show the companion over the still-running
    // game and prematurely close out the play session (the "tap closes the game" bug). The real return is
    // detected by the accessibility watcher (OnForegroundWindowChanged) instead.
    public bool IsGameOnExternalScreen => _gameOnSecondScreen;

    void ISecondScreenLaunchCoordinator.GameStarted(Game game, string title, GameLaunchScreen screen) =>
        GameStarted(game, title, screen);

    internal void GameStarted(Game game, string title, GameLaunchScreen screen = GameLaunchScreen.BuiltIn)
    {
        // All companion state lives on the main thread. GameStarted runs on the launch continuation,
        // which is not guaranteed to be the main thread, so marshal the whole transition onto it so
        // nothing races the main-thread readers / Avalonia bindings.
        RunOnMain(() =>
        {
            var keepAchievements = _navigation.Overlay == SecondScreenOverlay.Achievements;
            _runningGameId = game.Id;
            _runningGameTitle = title;
            _navigation = _navigation.StartGame();
            ResetAchievementTarget();
            // The game took Screen-2: hand the companion's view model to the built-in panel (MainView hosts
            // it as an overlay) and hide the Presentation so the game is visible underneath. The spotlight
            // update below then renders on the built-in panel through the same model.
            if (screen == GameLaunchScreen.External)
                SwapCompanionToMainScreen();
            if (_presentation is { } presentation)
            {
                // A game is now playing on the other screen: this idle surface drops into the dim standby
                // (unless an overlay is/becomes open — IsStandby gates on that). Both surfaces bind this one
                // model, so setting it here covers the Screen-2 presentation and the built-in companion alike.
                presentation.Model.IsGameRunning = true;
                ReopenOrCloseOverlayForContextChange(presentation, keepAchievements);
                ScheduleSpotlightUpdate();
            }
            StartKeepAliveIfNeeded();
        });
    }

    // Moves the companion onto the built-in screen for the duration of a game on Screen-2. The Presentation
    // keeps its model (so its bitmaps and state survive); MainView renders a second view bound to the same
    // model. Only one is visible at a time — the Presentation is hidden here and re-shown on return.
    private void SwapCompanionToMainScreen()
    {
        if (_gameOnSecondScreen || _presentation is not { } presentation || _viewModel is null)
            return;
        _gameOnSecondScreen = true;
        _viewModel.MainScreenCompanion = presentation.Model;
        presentation.Hide();
        _logger.Information("Second-screen: game took Screen-2; companion moved to the built-in panel.");
    }

    private void RestoreCompanionToSecondScreen()
    {
        if (!_gameOnSecondScreen)
            return;
        _gameOnSecondScreen = false;
        if (_viewModel is not null)
            _viewModel.MainScreenCompanion = null;
        _presentation?.Show();
        _logger.Information("Second-screen: game left Screen-2; companion restored to the second screen.");
    }

    internal void ReturnedToBrowse()
    {
        RunOnMain(() =>
        {
            var keepAchievements = _navigation.Overlay == SecondScreenOverlay.Achievements;
            _runningGameId = null;
            _runningGameTitle = null;
            _navigation = _navigation.ReturnToBrowse();
            ResetAchievementTarget();
            // If a game had taken Screen-2, swap the companion back before touching its overlay/spotlight,
            // so the restored surface renders on the second screen it now owns again.
            RestoreCompanionToSecondScreen();
            if (_presentation is { } presentation)
            {
                // The game is gone: lift the dim standby back to the browse spotlight.
                presentation.Model.IsGameRunning = false;
                ReopenOrCloseOverlayForContextChange(presentation, keepAchievements);
                ScheduleSpotlightUpdate();
            }
            StopKeepAlive();
        });
    }

    // A game start / return-to-browse resets the navigation overlay to None. The achievements panel is
    // sticky, so if it was open, re-open it against the new context (running game, or focused game once
    // browsing) instead of dropping to the spotlight — it stays up until the user closes it. Transient
    // overlays (the app drawer) are simply left closed.
    private void ReopenOrCloseOverlayForContextChange(ThorSecondScreenPresentation presentation, bool keepAchievements)
    {
        if (keepAchievements)
            OpenAchievementsCore(forceRefresh: false);
        else
            presentation.Model.Overlay = SecondScreenOverlayKind.None;
    }

    private void ToggleDrawer()
    {
        // The ☰ chrome button toggles: a second press on an open all-apps drawer closes it.
        if (_navigation.Overlay == SecondScreenOverlay.AppDrawer)
            CloseOverlay();
        else
            ShowDrawer(pickSlot: null);
    }

    private void ToggleAchievements()
    {
        // The trophy chrome button toggles, mirroring the drawer.
        if (_navigation.Overlay == SecondScreenOverlay.Achievements)
            CloseOverlay();
        else
            OpenAchievementsCore(forceRefresh: false);
    }

    private void ActivateDockSlot(int slot)
    {
        var component = _dock[slot];
        if (component is null)
        {
            ShowDrawer(slot);
            return;
        }

        LaunchOnSecondScreen(component);
    }

    private void RefreshAchievements() => OpenAchievementsCore(forceRefresh: true);

    private void OnDrawerAppSelected(string component)
    {
        if (_navigation.DockSlot is { } slot)
        {
            _dock = _dock.Pin(slot, component);
            _dockStore.Save(_dock);
            RenderDock();
            CloseOverlay();
        }
        else
        {
            LaunchOnSecondScreen(component);
        }
    }

    private void OnClearSlot()
    {
        if (_navigation.DockSlot is not { } slot)
            return;

        _dock = _dock.Clear(slot);
        _dockStore.Save(_dock);
        RenderDock();
        CloseOverlay();
    }

    public void OnDisplayAdded(int displayId)
    {
        _logger.Information($"Second-screen display added: {displayId}.");
        RunOnMain(EnsurePresentation);
    }

    public void OnDisplayChanged(int displayId)
    {
        if (_presentation?.Display?.DisplayId == displayId)
            _logger.Information($"Second-screen display changed: {Describe(_presentation.Display)}.");
    }

    public void OnDisplayRemoved(int displayId)
    {
        if (_presentation?.Display?.DisplayId != displayId)
            return;

        _logger.Information($"Second-screen display removed: {displayId}.");
        DismissPresentation();
    }

    private void AttachActivity(MainActivity activity)
    {
        if (_disposed)
            return;

        RunOnMain(() =>
        {
            if (ReferenceEquals(_activity, activity) && _presentation is not null)
                return;

            if (_activity is not null && !ReferenceEquals(_activity, activity))
            {
                DetachDisplayManager();
                // A different Activity is taking over while the old one is still around (the multi-window /
                // "new instance resumes before the old is destroyed" ordering). The existing Presentation was
                // constructed with the outgoing Activity as its outer context, so it must be torn down here:
                // otherwise EnsurePresentation() below early-returns on the non-null field and keeps a
                // Presentation bound to a dead window token, and the old Activity's OnDestroy no longer matches
                // _activity so ActivityDestroyed never dismisses it — leaking it (and its bitmaps) and rendering
                // the companion against a dead window.
                DismissPresentation();
            }

            _activity = activity;
            _displayManager = (DisplayManager?)activity.GetSystemService(Context.DisplayService);
            _displayManager?.RegisterDisplayListener(this, _mainHandler);
            EnsurePresentation();
        });
    }

    private void ActivityDestroyed(MainActivity activity, bool _)
    {
        if (!ReferenceEquals(_activity, activity))
            return;

        RunOnMain(() =>
        {
            if (_navigation.Overlay != SecondScreenOverlay.None)
            {
                _navigation = _navigation.CloseOverlay();
                ResetAchievementTarget();
            }
            DismissPresentation();
            DetachDisplayManager();
            _activity = null;
        });
    }

    private void TopResumedChanged(bool topResumed)
    {
        var state = topResumed ? "top-resumed" : "backgrounded";
        _logger.Information(
            $"Second-screen SS0 lifecycle: EmuShelf {state}; presentation showing={_presentation?.IsShowing == true}.");

        // Coarse fallback re-show, ONLY when the accessibility watcher is not enabled. It brings the
        // companion back when EmuShelf regains the foreground — but "EmuShelf is in front" is not the same
        // as "the dock app closed": simply touching the main screen while a Screen-2 app is still open also
        // regains the foreground, and re-showing there would cover the app the user deliberately left up.
        // So when the watcher IS live, trust it exclusively (it re-shows the instant the app is dismissed,
        // and leaves a still-open app alone); the fallback runs only in the degraded, no-service mode.
        if (topResumed && _appLaunchedOnSecondScreen && !SecondScreenAccessibility.IsConnected)
        {
            _appLaunchedOnSecondScreen = false;
            RunOnMain(() => _presentation?.Show());
        }
    }

    private void EnsurePresentation()
    {
        if (_disposed || _presentation is not null || _activity is null || _displayManager is null)
            return;

        var display = (_displayManager
            .GetDisplays(DisplayManager.DisplayCategoryPresentation) ?? [])
            .OrderBy(candidate => candidate.DisplayId == 4 ? 0 : 1)
            .FirstOrDefault();
        if (display is null)
        {
            _logger.Information("Second-screen SS0: no FLAG_PRESENTATION display is currently available.");
            return;
        }

        try
        {
            _presentation = new ThorSecondScreenPresentation(_activity, display);
            WireModel(_presentation.Model);
            _presentation.Show();
            _logger.Information($"Second-screen Presentation attached: {Describe(display)}.");
            EnsureAppsLoadedAsync();
            RenderDock();
            RenderRestingSurface();
            ScheduleSpotlightUpdate();
            StartKeepAliveIfNeeded();
            // If couch text entry was already open when the second screen attached, move the keyboard onto it.
            SyncKeyboardToSecondScreen();
        }
        catch (Exception ex)
        {
            _logger.Error(
                "Second-screen SS0: Presentation could not attach. AYN's dual-screen assistant may own the display.",
                ex);
            DismissPresentation();
        }
    }

    // Wire the view model's Android-side callbacks once, when the presentation is created. These fire on
    // the Avalonia UI thread, which is the Android main thread, so no extra marshalling is needed.
    private void WireModel(SecondScreenViewModel model)
    {
        model.DrawerToggled = ToggleDrawer;
        model.AchievementsToggled = ToggleAchievements;
        model.OverlayClosed = CloseOverlay;
        model.SlotCleared = OnClearSlot;
        model.AchievementsRefreshed = RefreshAchievements;
        model.SlotActivated = ActivateDockSlot;
        model.SlotEditRequested = EditDockSlot;
        model.AppLaunched = app => OnDrawerAppSelected(app.Component);
        // The gamepad drives this companion's achievements grid directly through the Presentation window:
        // Android delivers Screen-2 key events to it when it is the top-focused display (a touch on Screen-2),
        // and ThorSecondScreenPresentation.DispatchKeyEvent forwards each mapped action to model here.
    }

    // Long-press on a dock slot opens its picker, where a filled slot can be re-pinned or cleared. Tap
    // still launches (ActivateDockSlot), so this is the "manage" gesture without a permanent affordance.
    private void EditDockSlot(int slot) => ShowDrawer(slot);

    // Mirror the main head's couch keyboard onto Screen-2 whenever one is live and this device actually has
    // the second display: the keys move to the panel the user can look down at, and the search field +
    // results stay fully visible on the main screen. When there is no presentation (no second screen), the
    // main head keeps its own strip — IsGamepadKeyboardHostedRemotely stays false so that strip shows.
    private void SyncKeyboardToSecondScreen()
    {
        var keyboard = _viewModel?.GamepadKeyboard;
        if (_presentation is { } presentation && keyboard is not null)
        {
            presentation.Model.Keyboard = keyboard;
            if (_viewModel is not null)
                _viewModel.IsGamepadKeyboardHostedRemotely = true;
        }
        else
        {
            if (_presentation is { } current)
                current.Model.Keyboard = null;
            if (_viewModel is not null)
                _viewModel.IsGamepadKeyboardHostedRemotely = false;
        }
    }

    private void EnsureAppsLoadedAsync()
    {
        if (_appsLoaded || _appsLoadInFlight ||
            _activity is not { PackageManager: { } manager } activity)
            return;

        _appsLoadInFlight = true;
        var ownPackage = activity.PackageName;
        _ = Task.Run(() =>
        {
            try
            {
                var loaded = new Dictionary<string, SecondScreenApp>(StringComparer.Ordinal);
                using var launcherIntent = new Intent(Intent.ActionMain);
                launcherIntent.AddCategory(Intent.CategoryLauncher);
                var activities = manager.QueryIntentActivities(launcherIntent, PackageInfoFlags.MatchAll);
                foreach (var resolved in activities)
                {
                    var activityInfo = resolved.ActivityInfo;
                    if (activityInfo?.PackageName is null || activityInfo.Name is null ||
                        string.Equals(activityInfo.PackageName, ownPackage, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    using var component = new ComponentName(activityInfo.PackageName, activityInfo.Name);
                    var flattened = component.FlattenToString();
                    if (string.IsNullOrEmpty(flattened))
                        continue;
                    var label = resolved.LoadLabel(manager)?.ToString() ?? activityInfo.PackageName;
                    // Rasterise the launcher icon to a small Avalonia bitmap once, so the drawer and dock
                    // show real icons. Bounded to 96px so the whole set stays a few MB.
                    var icon = DrawableToAvaloniaBitmap(resolved.LoadIcon(manager), 96);
                    loaded[flattened] = new SecondScreenApp(flattened, label, icon);
                }

                RunOnMain(() =>
                {
                    foreach (var existing in _apps.Values)
                        existing.Icon?.Dispose();
                    _apps.Clear();
                    foreach (var app in loaded)
                        _apps[app.Key] = app.Value;
                    _appsLoaded = true;
                    _appsLoadInFlight = false;
                    RenderDock();
                    if (_navigation.Overlay is SecondScreenOverlay.AppDrawer or SecondScreenOverlay.DockPicker)
                        RenderDrawer(_navigation.DockSlot);
                });
            }
            catch (Exception ex)
            {
                _logger.Error("Could not enumerate launchable apps for the second screen.", ex);
                RunOnMain(() => _appsLoadInFlight = false);
            }
        });
    }

    private void ShowDrawer(int? pickSlot)
    {
        _navigation = _navigation.OpenDrawer(pickSlot);
        ResetAchievementTarget();
        EnsureAppsLoadedAsync();
        RenderDrawer(pickSlot);
    }

    private void RenderDrawer(int? pickSlot)
    {
        if (_presentation is not { } presentation)
            return;

        presentation.Model.Apps.Clear();
        foreach (var app in _apps.Values.OrderBy(app => app.Label, StringComparer.CurrentCultureIgnoreCase))
            presentation.Model.Apps.Add(new SecondScreenAppViewModel(app.Component, app.Label, app.Icon));
        presentation.Model.DrawerTitle = pickSlot is { } slot ? $"Choose an app for slot {slot + 1}" : "All apps";
        presentation.Model.CanClearSlot = pickSlot is not null;
        presentation.Model.Overlay = SecondScreenOverlayKind.Drawer;
    }

    private void RenderDock()
    {
        if (_presentation is not { } presentation)
            return;

        for (var slot = 0; slot < SecondScreenDock.SlotCount; slot++)
        {
            var component = _dock[slot];
            var app = component is not null && _apps.TryGetValue(component, out var found) ? found : null;
            presentation.Model.Dock[slot].Label = app?.Label;
            presentation.Model.Dock[slot].Icon = app?.Icon;
        }
    }

    private void CloseOverlay()
    {
        _navigation = _navigation.CloseOverlay();
        ResetAchievementTarget();
        RenderRestingSurface();
    }

    private void RenderRestingSurface()
    {
        // Closing an overlay returns to the resting spotlight, which is already loaded — don't reload it
        // (that would re-run the crossfade). Fresh art loads only on focus/running-game changes.
        if (_presentation is { } presentation)
            presentation.Model.Overlay = SecondScreenOverlayKind.None;
    }

    private void ScheduleSpotlightUpdate()
    {
        if (_presentation is null)
            return;

        // Debounce: focus changes fire rapidly while scrolling the library, so only the settled game's
        // art is loaded. A monotonic generation both debounces and guards the async load below.
        var generation = ++_spotlightGeneration;
        _mainHandler.PostDelayed(
            () =>
            {
                if (generation == _spotlightGeneration)
                    UpdateSpotlight(generation);
            },
            110);
    }

    private void UpdateSpotlight(long generation)
    {
        if (_presentation is null)
            return;

        var targetId = _runningGameId ?? _viewModel?.FocusedGame?.Id;
        if (targetId is null)
        {
            ClearSpotlight();
            return;
        }

        var id = targetId.Value;
        _ = Task.Run(() =>
        {
            Avalonia.Media.Imaging.Bitmap? fanart = null;
            Avalonia.Media.Imaging.Bitmap? wheel = null;
            try
            {
                var media = _gameDetails.GetDetails(id).Media;
                var fanartPath = media
                    .Where(item => item.Kind == GameMediaKind.Fanart && item.IsSelected)
                    .OrderByDescending(item => item.Id)
                    .Select(item => item.LocalPath)
                    .FirstOrDefault();
                var wheelPath = media
                    .Where(item => item.Kind == GameMediaKind.Wheel && item.IsSelected)
                    .OrderByDescending(item => item.Id)
                    .Select(item => item.LocalPath)
                    .FirstOrDefault();
                fanart = LoadAvaloniaBitmap(fanartPath, decodeWidth: 1240);
                wheel = LoadAvaloniaBitmap(wheelPath, decodeWidth: 900);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not resolve second-screen spotlight art for game {id}.", ex);
            }

            RunOnMain(() =>
            {
                if (generation != _spotlightGeneration || _presentation is not { } presentation)
                {
                    fanart?.Dispose();
                    wheel?.Dispose();
                    return;
                }

                var model = presentation.Model;
                // Fan art swaps instantly (no fade-out to the background), so scrolling the library game to
                // game never blinks the panel. The logo is held back and faded in after a short delay — the
                // original "logo appears after the art" entrance — and that touches only the logo, not the
                // background, so it adds no blink.
                model.ShowBranding = fanart is null && wheel is null;
                model.FanartImage = fanart;
                model.FanartOpacity = fanart is not null ? 1 : 0;
                model.LogoOpacity = 0;
                _mainHandler.PostDelayed(
                    () =>
                    {
                        if (generation != _spotlightGeneration)
                        {
                            wheel?.Dispose();
                            return;
                        }
                        model.WheelImage = wheel;
                        model.LogoOpacity = wheel is not null ? 1 : 0;
                    },
                    190);
            });
        });
    }

    private void ClearSpotlight()
    {
        if (_presentation is not { } presentation)
            return;
        presentation.Model.FanartOpacity = 0;
        presentation.Model.LogoOpacity = 0;
        presentation.Model.SetSpotlight(null, null);
    }

    private static Avalonia.Media.Imaging.Bitmap? LoadAvaloniaBitmap(string? path, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, decodeWidth);
        }
        catch
        {
            return null;
        }
    }

    private static Avalonia.Media.Imaging.Bitmap? DrawableToAvaloniaBitmap(Drawable? drawable, int size)
    {
        if (drawable is null)
            return null;
        try
        {
            using var androidBitmap = global::Android.Graphics.Bitmap.CreateBitmap(
                size, size, global::Android.Graphics.Bitmap.Config.Argb8888!);
            using var canvas = new Canvas(androidBitmap);
            drawable.SetBounds(0, 0, size, size);
            drawable.Draw(canvas);
            using var stream = new MemoryStream();
            androidBitmap.Compress(global::Android.Graphics.Bitmap.CompressFormat.Png!, 100, stream);
            androidBitmap.Recycle();
            stream.Position = 0;
            return new Avalonia.Media.Imaging.Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private void LaunchOnSecondScreen(string flattenedComponent)
    {
        if (_activity is not { } activity || _presentation?.Display is not { } display)
            return;

        using var component = ComponentName.UnflattenFromString(flattenedComponent);
        if (component is null)
        {
            _logger.Warning($"Second-screen dock component is invalid: {flattenedComponent}.");
            return;
        }

        using var intent = new Intent(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.SetComponent(component);
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ResetTaskIfNeeded);
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                using var options = ActivityOptions.MakeBasic();
                if (options is not null)
                {
                    options.SetLaunchDisplayId(display.DisplayId);
                    activity.StartActivity(intent, options.ToBundle());
                }
                else
                {
                    activity.StartActivity(intent);
                }
            }
            else
            {
                activity.StartActivity(intent);
            }
            _logger.Information($"Launched {flattenedComponent} on second-screen display {display.DisplayId}.");

            // The launched app is an ordinary window (layer 21000); the companion is a Presentation
            // (layer 31000), so the app would render *behind* the dock and be invisible. Match NeoStation:
            // hide the companion while the app owns Screen-2, and re-show it when EmuShelf returns to the
            // foreground (TopResumedChanged). A short delay lets the app's own window come up first so the
            // stock secondary-display launcher never flashes through underneath during the hand-off.
            _appLaunchedOnSecondScreen = true;
            var launched = _presentation;
            _mainHandler.PostDelayed(
                () =>
                {
                    if (_appLaunchedOnSecondScreen && ReferenceEquals(launched, _presentation))
                        launched?.Hide();
                },
                350);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not launch {flattenedComponent} on the second screen.", ex);
        }
    }

    // allowNetworkRefresh is false on the debounced browse-follow: while the panel follows the library
    // selection, it shows only cached details and never fires a network refresh, so scrolling game to game
    // can't burst the RetroAchievements API. The explicit Refresh button (forceRefresh) and the first
    // manual open still fetch.
    private void OpenAchievementsCore(bool forceRefresh, bool allowNetworkRefresh = true)
    {
        _navigation = _navigation.OpenAchievements();
        var surfaceRevision = _navigation.Revision;
        ResetAchievementTarget();

        var focused = _viewModel?.FocusedGame;
        var gameId = SecondScreenTargetResolver.Resolve(_runningGameId, focused?.Id);
        var title = _runningGameId is not null
            ? _runningGameTitle ?? "Now playing"
            : focused?.DisplayTitle ?? "Achievements";
        if (gameId is null)
        {
            ShowAchievementsMessage("Achievements", "Select a game first.", canRefresh: false);
            return;
        }

        if (!GetLinks().TryGetValue(gameId.Value, out var link) ||
            link is not { HasAchievements: true, RetroAchievementsGameId: { } raGameId })
        {
            ShowAchievementsMessage(title, "No RetroAchievements set is linked to this game.", canRefresh: false);
            return;
        }

        _achievementTargetGameId = gameId;
        _achievementTargetTitle = title;
        var cached = _details.GetCached(raGameId);
        var credentials = _account.CurrentCredentials;
        var willRefresh = credentials is not null &&
            (forceRefresh || (allowNetworkRefresh && (cached is null || IsAchievementDetailStale(cached))));

        if (cached is not null)
        {
            ShowAchievementsSnapshot(
                title,
                cached,
                credentials is null
                    ? "Reconnect RetroAchievements to refresh these cached details."
                    : willRefresh
                        ? "Refreshing achievement details…"
                        // Normal, up-to-date case: no status line — the grid speaks for itself.
                        : null,
                canRefresh: credentials is not null);
        }
        else
        {
            ShowAchievementsMessage(
                title,
                credentials is null
                    ? "Connect RetroAchievements in Settings to load details."
                    : willRefresh
                        ? "Loading achievement details…"
                        : "Press Refresh to load achievement details.",
                canRefresh: credentials is not null && !willRefresh);
        }

        if (!willRefresh)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var response = await _details.RefreshAsync(credentials, raGameId, manual: forceRefresh);
                RunOnMain(() => ApplyAchievementRefresh(surfaceRevision, gameId.Value, title, cached, response));
            }
            catch (Exception ex)
            {
                _logger.Error($"Second-screen achievement refresh failed for game id {gameId}.", ex);
                RunOnMain(() => ShowAchievementFailure(
                    surfaceRevision, gameId.Value, title, cached, "Achievement details could not be refreshed."));
            }
        });
    }

    private void ApplyAchievementRefresh(
        long surfaceRevision,
        long gameId,
        string title,
        RetroAchievementsDetailsSnapshot? cached,
        RetroAchievementsResponse<RetroAchievementsDetailsSnapshot> response)
    {
        if (!IsAchievementRequestCurrent(surfaceRevision, gameId, title))
            return;

        if (response.IsSuccess)
        {
            // Freshly refreshed — no status line; the updated grid is the confirmation.
            ShowAchievementsSnapshot(title, response.Value!, status: null, canRefresh: true);
            return;
        }

        ShowAchievementFailure(
            surfaceRevision,
            gameId,
            title,
            cached,
            response.Status switch
            {
                RetroAchievementsRequestStatus.AuthenticationFailed => "Reconnect RetroAchievements in Settings.",
                RetroAchievementsRequestStatus.Offline => "Offline — showing cached achievement details.",
                RetroAchievementsRequestStatus.RateLimited => "RetroAchievements is rate limiting requests. Try again shortly.",
                _ => "Achievement details could not be refreshed.",
            });
    }

    private void ShowAchievementFailure(
        long surfaceRevision,
        long gameId,
        string title,
        RetroAchievementsDetailsSnapshot? cached,
        string status)
    {
        if (!IsAchievementRequestCurrent(surfaceRevision, gameId, title))
            return;

        if (cached is not null)
            ShowAchievementsSnapshot(title, cached, status, canRefresh: true);
        else
            ShowAchievementsMessage(title, status, canRefresh: true);
    }

    private void ShowAchievementsSnapshot(
        string title,
        RetroAchievementsDetailsSnapshot snapshot,
        string? status,
        bool canRefresh)
    {
        if (_presentation is not { } presentation)
            return;

        presentation.Model.AchievementsTitle = title;
        presentation.Model.AchievementsStatus = status;
        presentation.Model.CanRefresh = canRefresh;
        // Compact progress line for the header: softcore unlocked / total · earned points.
        var details = snapshot.Details;
        presentation.Model.AchievementsSummary =
            $"{details.UnlockedAchievements} / {details.TotalAchievements} · {details.EarnedPoints} pts";
        // Badges are deferred (loadBadge:false) and requested per tile as it attaches (see the view), so
        // only the on-screen badges of the virtualized grid load — a big set never fires hundreds at once.
        // Locked/hardcore state drives the tile's dimming and gold ring.
        var rows = snapshot.Details.Achievements
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.AchievementId)
            .Select(achievement => new AchievementRowViewModel(achievement, _badges, loadBadge: false))
            .ToList();
        presentation.Model.SetAchievements(rows);
        presentation.Model.Overlay = SecondScreenOverlayKind.Achievements;
    }

    private void ShowAchievementsMessage(string title, string message, bool canRefresh)
    {
        if (_presentation is not { } presentation)
            return;

        presentation.Model.AchievementsTitle = title;
        presentation.Model.AchievementsStatus = message;
        presentation.Model.CanRefresh = canRefresh;
        presentation.Model.AchievementsSummary = null;
        presentation.Model.ClearAchievements();
        presentation.Model.Overlay = SecondScreenOverlayKind.Achievements;
    }

    private bool IsAchievementRequestCurrent(long surfaceRevision, long gameId, string title) =>
        IsAchievementSurfaceCurrent(surfaceRevision) &&
        _achievementTargetGameId == gameId &&
        string.Equals(_achievementTargetTitle, title, StringComparison.Ordinal);

    private bool IsAchievementSurfaceCurrent(long surfaceRevision) =>
        _navigation.Overlay == SecondScreenOverlay.Achievements &&
        _navigation.Revision == surfaceRevision;

    private void ResetAchievementTarget()
    {
        _achievementTargetGameId = null;
        _achievementTargetTitle = null;
    }

    private static bool IsAchievementDetailStale(RetroAchievementsDetailsSnapshot cached) =>
        DateTimeOffset.UtcNow - cached.LastRefreshedAt >= DetailRefreshAge;

    // Main-thread only (every OpenAchievementsCore caller is), so the cache needs no locking.
    private IReadOnlyDictionary<long, RetroAchievementsGameLink> GetLinks()
    {
        var now = DateTimeOffset.UtcNow;
        if (_cachedLinks is null || now - _cachedLinksAt >= LinksCacheTtl)
        {
            _cachedLinks = _readStore.GetAllLinks();
            _cachedLinksAt = now;
        }
        return _cachedLinks;
    }

    private void StartKeepAliveIfNeeded()
    {
        if (_runningGameId is null || _presentation?.IsShowing != true || _activity is not { } activity)
            return;

        try
        {
            SecondScreenKeepAliveService.Start(activity.ApplicationContext ?? activity);
        }
        catch (Exception ex)
        {
            // The companion is optional launch chrome. Android may reject an FGS start during a
            // transition; that must never turn an already-started emulator into a failed launch.
            _logger.Warning("Could not start the second-screen keep-alive service.", ex);
        }
    }

    private void StopKeepAlive()
    {
        if (_activity is { } activity)
            SecondScreenKeepAliveService.Stop(activity.ApplicationContext ?? activity);
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.GamepadKeyboard))
        {
            RunOnMain(SyncKeyboardToSecondScreen);
            return;
        }

        if (e.PropertyName != nameof(MainViewModel.FocusedGame) || _runningGameId is not null)
            return;

        // Any pending achievement request for the previously focused game must not overwrite the new
        // context, so drop the target first.
        ResetAchievementTarget();
        // The achievements panel is sticky and follows the library selection: while it is open, re-point
        // it at the newly focused game (debounced so only the settled game loads) instead of leaving a
        // stale snapshot. It stays open until the user closes it, so they can browse game to game with it up.
        ScheduleAchievementsFollow();
        // While browsing, the spotlight follows the library selection (a running game always wins).
        ScheduleSpotlightUpdate();
    }

    // Debounced re-point of an open achievements panel to the currently focused game. Focus changes fire
    // rapidly while scrolling the library, so only the SETTLED game's achievements load — mirrors the
    // spotlight debounce. A no-op unless the panel is open.
    private void ScheduleAchievementsFollow()
    {
        if (_navigation.Overlay != SecondScreenOverlay.Achievements)
            return;

        var generation = ++_achievementsFollowGeneration;
        _mainHandler.PostDelayed(
            () =>
            {
                if (generation == _achievementsFollowGeneration &&
                    _navigation.Overlay == SecondScreenOverlay.Achievements &&
                    _runningGameId is null)
                {
                    // Follow the selection AND fetch: the panel should populate as the user browses game to
                    // game, not sit on "Press Refresh" for anything never opened before. This is not a
                    // burst risk — the debounce means only the settled game fetches, and willRefresh is
                    // already gated to uncached-or-stale sets, so already-cached games make no request.
                    OpenAchievementsCore(forceRefresh: false, allowNetworkRefresh: true);
                }
            },
            // Longer than the spotlight debounce: this can hit the network, so give a fast scroll more time
            // to settle before the settled game's set is fetched.
            400);
    }

    private void DismissPresentation()
    {
        if (_presentation is not { } presentation)
            return;
        _presentation = null;
        StopKeepAlive();
        // The second screen is going away. If a game had swapped the companion onto the built-in panel,
        // drop that overlay — its model belongs to the presentation we are tearing down.
        if (_gameOnSecondScreen)
        {
            _gameOnSecondScreen = false;
            if (_viewModel is not null)
                _viewModel.MainScreenCompanion = null;
        }
        // The second screen is going away; if it was hosting the keyboard, hand it back to the main-screen
        // strip so text entry does not vanish mid-type.
        if (_viewModel is { GamepadKeyboard: not null } viewModel)
            viewModel.IsGamepadKeyboardHostedRemotely = false;

        try
        {
            presentation.ReleaseResources();
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not release second-screen resources cleanly.", ex);
        }

        try
        {
            presentation.Dismiss();
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not dismiss the second-screen Presentation cleanly.", ex);
        }
        finally
        {
            presentation.Dispose();
        }
    }

    private void DetachDisplayManager()
    {
        try
        {
            _displayManager?.UnregisterDisplayListener(this);
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not unregister the second-screen display listener.", ex);
        }
        _displayManager = null;
    }

    private void RunOnMain(Action action)
    {
        if (_disposed)
            return;
        if (Looper.MyLooper() == Looper.MainLooper)
            action();
        else
            _mainHandler.Post(() =>
            {
                if (!_disposed)
                    action();
            });
    }

    private static string Describe(Display display)
        => $"id={display.DisplayId}, name={display.Name}, flags={display.Flags}, rotation={display.Rotation}";

    public new void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        AndroidActivityLifecycle.ActivityAvailable -= AttachActivity;
        AndroidActivityLifecycle.ActivityDestroyed -= ActivityDestroyed;
        AndroidActivityLifecycle.TopResumedChanged -= TopResumedChanged;
        SecondScreenAccessibility.ForegroundWindowChanged = null;
        DismissPresentation();
        DetachDisplayManager();
        StopKeepAlive();
        _activity = null;
        _mainHandler.Dispose();
        base.Dispose();
    }
}

internal sealed record SecondScreenApp(string Component, string Label, Avalonia.Media.Imaging.Bitmap? Icon);
