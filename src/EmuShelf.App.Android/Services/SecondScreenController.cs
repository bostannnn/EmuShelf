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
internal sealed class SecondScreenController : Java.Lang.Object, DisplayManager.IDisplayListener, IDisposable
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
    private long _spotlightGeneration;
    private bool _disposed;
    private bool _appsLoaded;
    private bool _appsLoadInFlight;
    // Set while a dock-launched app owns Screen-2. The companion Presentation is hidden for its duration
    // (see LaunchOnSecondScreen) and re-shown when EmuShelf next returns to the foreground.
    private bool _appLaunchedOnSecondScreen;

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
        // Reserved for the next iteration (achievement badges + game-idle artwork as Avalonia bitmaps).
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

    // Fired by SecondScreenReturnWatcher (accessibility) on every window-state change, on any display.
    private void OnForegroundWindowChanged(string? package, string? className)
    {
        // Only relevant while a dock app owns Screen-2 and the companion is hidden for it.
        if (!_appLaunchedOnSecondScreen || className is null)
            return;

        // The stock secondary-display launcher returning to the front means the app we launched on Screen-2
        // has been closed (backed out of). Re-show the companion over it immediately — NeoStation's instant
        // dock-return. Its class is display-specific, so main-screen launcher events never match here.
        if (!className.Contains("secondarydisplay.SecondaryDisplayLauncher", StringComparison.Ordinal))
            return;

        RunOnMain(() =>
        {
            if (!_appLaunchedOnSecondScreen)
                return;
            _appLaunchedOnSecondScreen = false;
            _presentation?.Show();
            _logger.Information("Second-screen: companion re-shown after a dock app closed on Screen-2.");
        });
    }

    internal void GameStarted(Game game, string title)
    {
        // All companion state lives on the main thread. GameStarted runs on the launch continuation,
        // which is not guaranteed to be the main thread, so marshal the whole transition onto it so
        // nothing races the main-thread readers / Avalonia bindings.
        RunOnMain(() =>
        {
            _runningGameId = game.Id;
            _runningGameTitle = title;
            _navigation = _navigation.StartGame();
            ResetAchievementTarget();
            if (_presentation is { } presentation)
            {
                presentation.Model.Overlay = SecondScreenOverlayKind.None;
                ScheduleSpotlightUpdate();
            }
            StartKeepAliveIfNeeded();
        });
    }

    internal void ReturnedToBrowse()
    {
        RunOnMain(() =>
        {
            _runningGameId = null;
            _runningGameTitle = null;
            _navigation = _navigation.ReturnToBrowse();
            ResetAchievementTarget();
            if (_presentation is { } presentation)
            {
                presentation.Model.Overlay = SecondScreenOverlayKind.None;
                ScheduleSpotlightUpdate();
            }
            StopKeepAlive();
        });
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
                DetachDisplayManager();

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

        // Returning to EmuShelf after a dock app owned Screen-2 is the fallback re-show path for when the
        // accessibility watcher is not enabled: bring the companion back when EmuShelf regains the
        // foreground. When the watcher IS enabled it re-shows sooner, the instant the app is dismissed.
        if (topResumed && _appLaunchedOnSecondScreen)
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
    }

    // Long-press on a dock slot opens its picker, where a filled slot can be re-pinned or cleared. Tap
    // still launches (ActivateDockSlot), so this is the "manage" gesture without a permanent affordance.
    private void EditDockSlot(int slot) => ShowDrawer(slot);

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

    private void OpenAchievementsCore(bool forceRefresh)
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

        var links = _readStore.GetAllLinks();
        if (!links.TryGetValue(gameId.Value, out var link) ||
            link is not { HasAchievements: true, RetroAchievementsGameId: { } raGameId })
        {
            ShowAchievementsMessage(title, "No RetroAchievements set is linked to this game.", canRefresh: false);
            return;
        }

        _achievementTargetGameId = gameId;
        _achievementTargetTitle = title;
        var cached = _details.GetCached(raGameId);
        var credentials = _account.CurrentCredentials;
        if (cached is not null)
        {
            ShowAchievementsSnapshot(
                title,
                cached,
                credentials is null
                    ? "Reconnect RetroAchievements to refresh these cached details."
                    : forceRefresh
                        ? "Refreshing achievement details…"
                        : $"Updated {cached.LastRefreshedAt.LocalDateTime:g}",
                canRefresh: credentials is not null);
        }
        else
        {
            ShowAchievementsMessage(
                title,
                credentials is null
                    ? "Connect RetroAchievements in Settings to load details."
                    : "Loading achievement details…",
                canRefresh: false);
        }

        var stale = cached is null || DateTimeOffset.UtcNow - cached.LastRefreshedAt >= DetailRefreshAge;
        if (!forceRefresh && !stale)
            return;
        if (credentials is null)
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
            ShowAchievementsSnapshot(title, response.Value!, "Updated just now", canRefresh: true);
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
        presentation.Model.Achievements.Clear();
        foreach (var achievement in snapshot.Details.Achievements
                     .OrderBy(item => item.DisplayOrder)
                     .ThenBy(item => item.AchievementId))
        {
            var state = achievement.IsHardcore ? "Hardcore" : achievement.IsEarned ? "Unlocked" : "Locked";
            var points = achievement.Points == 1 ? "1 pt" : $"{achievement.Points} pts";
            presentation.Model.Achievements.Add(new SecondScreenAchievementViewModel(
                achievement.Title,
                $"{state}  •  {points}",
                achievement.IsEarned));
        }
        presentation.Model.Overlay = SecondScreenOverlayKind.Achievements;
    }

    private void ShowAchievementsMessage(string title, string message, bool canRefresh)
    {
        if (_presentation is not { } presentation)
            return;

        presentation.Model.AchievementsTitle = title;
        presentation.Model.AchievementsStatus = message;
        presentation.Model.CanRefresh = canRefresh;
        presentation.Model.Achievements.Clear();
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
        if (e.PropertyName != nameof(MainViewModel.FocusedGame) || _runningGameId is not null)
            return;

        // An open panel remains a snapshot until the user asks for achievements again, but its pending
        // request must no longer be allowed to overwrite the newly focused context.
        ResetAchievementTarget();
        // While browsing, the spotlight follows the library selection (a running game always wins).
        ScheduleSpotlightUpdate();
    }

    private void DismissPresentation()
    {
        if (_presentation is not { } presentation)
            return;
        _presentation = null;
        StopKeepAlive();

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
