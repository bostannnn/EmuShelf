using System.ComponentModel;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Hardware.Display;
using Android.OS;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.SecondScreen;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Drives the Thor companion surface. The visible surface is <see cref="SecondScreenHomeActivity"/> — a
/// real Activity registered as the secondary display's home (<c>CATEGORY_SECONDARY_HOME</c>) that hosts an
/// embedded Avalonia view bound to <see cref="Model"/>. Because the companion is an ordinary activity (not
/// an always-on-top <c>Presentation</c>), an app launched from the dock draws in front of it and Back/Home
/// returns to it — the two things a Presentation could not do (see DECISIONS 2026-08-23).
///
/// The controller is a process-wide singleton (<see cref="Active"/>) so the companion's state survives the
/// home activity being recreated, and so the home activity — which the system may start before or after the
/// shared app composes — can always find it. Only dock mutation and running-vs-focused target selection
/// cross into Core.
/// </summary>
internal sealed class SecondScreenController : IDisposable
{
    private static readonly TimeSpan DetailRefreshAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The live controller for this process, or null before the shared app has composed. The home activity
    /// reads this to bind its view; the controller pushes itself here on construction and clears it on
    /// dispose. Only ever touched on the main thread.
    /// </summary>
    internal static SecondScreenController? Active { get; private set; }

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
    private SecondScreenHomeActivity? _home;
    private long? _runningGameId;
    private string? _runningGameTitle;
    private long? _achievementTargetGameId;
    private string? _achievementTargetTitle;
    private long _spotlightGeneration;
    private bool _disposed;
    private bool _appsLoaded;
    private bool _appsLoadInFlight;

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

        // Wire the view model's Android-side callbacks once. Model outlives every home activity, so its
        // state (dock icons, spotlight art, open overlay) survives activity recreation.
        WireModel(Model);

        Active = this;
        // The system may already have the companion home on screen (it is that display's home) before the
        // shared app finished composing. Adopt it now so it stops showing the cold placeholder model.
        if (SecondScreenHomeActivity.Current is { } home)
            AttachHome(home);
    }

    /// <summary>The shared companion view model. Bound by the home activity; mutated only on the main thread.</summary>
    public SecondScreenViewModel Model { get; } = new();

    internal void Start(MainViewModel viewModel)
    {
        if (_disposed || ReferenceEquals(_viewModel, viewModel))
            return;

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        _viewModel = viewModel;
        _viewModel.PropertyChanged += ViewModelPropertyChanged;
        EnsureCompanionShown();
        ScheduleSpotlightUpdate();
    }

    /// <summary>
    /// Brings the companion home onto Screen-2. The Thor's built-in second-screen launcher
    /// (<c>com.neogamelab.neostation</c>) is the elected <c>CATEGORY_SECONDARY_HOME</c>, so EmuShelf does
    /// not win that election — instead it explicitly launches its own home activity onto the presentation
    /// display while EmuShelf is the active frontend, replacing NeoStation. A no-op when the companion is
    /// already up (we started it, or — if EmuShelf ever is the elected home — the system did). Launching
    /// on Screen-2 does not background the main UI (both displays stay resumed on Android 10+).
    /// </summary>
    internal void EnsureCompanionShown()
    {
        if (_disposed || SecondScreenHomeActivity.Current is not null)
            return;

        var context = global::Android.App.Application.Context;
        var displayManager = (DisplayManager?)context.GetSystemService(Context.DisplayService);
        var display = (displayManager?.GetDisplays(DisplayManager.DisplayCategoryPresentation) ?? [])
            .OrderBy(candidate => candidate.DisplayId == 4 ? 0 : 1)
            .FirstOrDefault();
        if (display is null)
        {
            _logger.Information("Second-screen: no FLAG_PRESENTATION display is available; companion not shown.");
            return;
        }

        try
        {
            using var intent = new Intent(context, typeof(SecondScreenHomeActivity));
            intent.AddFlags(ActivityFlags.NewTask);
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                using var options = ActivityOptions.MakeBasic();
                options?.SetLaunchDisplayId(display.DisplayId);
                context.StartActivity(intent, options?.ToBundle());
            }
            else
            {
                context.StartActivity(intent);
            }
            _logger.Information($"Second-screen companion launched on display {display.DisplayId}.");
        }
        catch (Exception ex)
        {
            // Optional chrome: a failure here must never bubble into the launch/return paths that call it.
            _logger.Error("Could not launch the second-screen companion.", ex);
        }
    }

    // --- Home activity attach / detach -------------------------------------------------------------

    /// <summary>
    /// The companion home activity became visible. Bind it to the shared model and paint the current state
    /// onto it. Called from the activity's OnCreate (main thread) and from the controller ctor when a home
    /// is already up.
    /// </summary>
    internal void AttachHome(SecondScreenHomeActivity home)
    {
        RunOnMain(() =>
        {
            _home = home;
            home.BindModel(Model);
            EnsureAppsLoadedAsync();
            RenderDock();
            RenderRestingSurface();
            ScheduleSpotlightUpdate();
        });
    }

    internal void DetachHome(SecondScreenHomeActivity home)
    {
        RunOnMain(() =>
        {
            if (ReferenceEquals(_home, home))
                _home = null;
        });
    }

    private bool HasSurface => _home is not null;

    internal void GameStarted(Game game, string title)
    {
        // GameStarted runs on the launch continuation, which is not guaranteed to be the main thread, so
        // marshal the whole transition onto it so nothing races the main-thread readers / Avalonia bindings.
        RunOnMain(() =>
        {
            _runningGameId = game.Id;
            _runningGameTitle = title;
            _navigation = _navigation.StartGame();
            ResetAchievementTarget();
            Model.Overlay = SecondScreenOverlayKind.None;
            ScheduleSpotlightUpdate();
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
            Model.Overlay = SecondScreenOverlayKind.None;
            // Returning to EmuShelf: if NeoStation reclaimed Screen-2 while a game was running, bring the
            // companion back to the front.
            EnsureCompanionShown();
            ScheduleSpotlightUpdate();
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

    // Wire the view model's Android-side callbacks. These fire on the Avalonia UI thread, which is the
    // Android main thread, so no extra marshalling is needed.
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
            _home is not { PackageManager: { } manager } home)
            return;

        _appsLoadInFlight = true;
        var ownPackage = home.PackageName;
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
        Model.Apps.Clear();
        foreach (var app in _apps.Values.OrderBy(app => app.Label, StringComparer.CurrentCultureIgnoreCase))
            Model.Apps.Add(new SecondScreenAppViewModel(app.Component, app.Label, app.Icon));
        Model.DrawerTitle = pickSlot is { } slot ? $"Choose an app for slot {slot + 1}" : "All apps";
        Model.CanClearSlot = pickSlot is not null;
        Model.Overlay = SecondScreenOverlayKind.Drawer;
    }

    private void RenderDock()
    {
        for (var slot = 0; slot < SecondScreenDock.SlotCount; slot++)
        {
            var component = _dock[slot];
            var app = component is not null && _apps.TryGetValue(component, out var found) ? found : null;
            Model.Dock[slot].Label = app?.Label;
            Model.Dock[slot].Icon = app?.Icon;
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
        Model.Overlay = SecondScreenOverlayKind.None;
    }

    private void ScheduleSpotlightUpdate()
    {
        if (!HasSurface)
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
        if (!HasSurface)
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
                if (generation != _spotlightGeneration || !HasSurface)
                {
                    fanart?.Dispose();
                    wheel?.Dispose();
                    return;
                }

                // Fan art swaps instantly (no fade-out to the background), so scrolling the library game to
                // game never blinks the panel. The logo is held back and faded in after a short delay — the
                // original "logo appears after the art" entrance — and that touches only the logo, not the
                // background, so it adds no blink.
                Model.ShowBranding = fanart is null && wheel is null;
                Model.FanartImage = fanart;
                Model.FanartOpacity = fanart is not null ? 1 : 0;
                Model.LogoOpacity = 0;
                _mainHandler.PostDelayed(
                    () =>
                    {
                        if (generation != _spotlightGeneration)
                        {
                            wheel?.Dispose();
                            return;
                        }
                        Model.WheelImage = wheel;
                        Model.LogoOpacity = wheel is not null ? 1 : 0;
                    },
                    190);
            });
        });
    }

    private void ClearSpotlight()
    {
        Model.FanartOpacity = 0;
        Model.LogoOpacity = 0;
        Model.SetSpotlight(null, null);
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
        if (_home is not { } home)
        {
            _logger.Information("Second-screen dock launch ignored: the companion home is not attached.");
            return;
        }

        using var component = ComponentName.UnflattenFromString(flattenedComponent);
        if (component is null)
        {
            _logger.Warning($"Second-screen dock component is invalid: {flattenedComponent}.");
            return;
        }

        // Launch from the home activity's context and pin the launch to the home's display, so the app
        // opens on Screen-2. Because the home is an ordinary activity (not a Presentation), the launched
        // app draws in front of it; Back/Home on Screen-2 returns to the companion. Activity.Display is
        // API 30+; the Thor is 33, and the launch still works display-agnostically on anything older.
        int? displayId = OperatingSystem.IsAndroidVersionAtLeast(30) ? home.Display?.DisplayId : null;
        using var intent = new Intent(Intent.ActionMain);
        intent.AddCategory(Intent.CategoryLauncher);
        intent.SetComponent(component);
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ResetTaskIfNeeded);
        try
        {
            if (displayId is { } id && OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                using var options = ActivityOptions.MakeBasic();
                if (options is not null)
                {
                    options.SetLaunchDisplayId(id);
                    home.StartActivity(intent, options.ToBundle());
                }
                else
                {
                    home.StartActivity(intent);
                }
            }
            else
            {
                home.StartActivity(intent);
            }
            _logger.Information($"Launched {flattenedComponent} on second-screen display {displayId?.ToString() ?? "?"}.");
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
        Model.AchievementsTitle = title;
        Model.AchievementsStatus = status;
        Model.CanRefresh = canRefresh;
        Model.Achievements.Clear();
        foreach (var achievement in snapshot.Details.Achievements
                     .OrderBy(item => item.DisplayOrder)
                     .ThenBy(item => item.AchievementId))
        {
            var state = achievement.IsHardcore ? "Hardcore" : achievement.IsEarned ? "Unlocked" : "Locked";
            var points = achievement.Points == 1 ? "1 pt" : $"{achievement.Points} pts";
            Model.Achievements.Add(new SecondScreenAchievementViewModel(
                achievement.Title,
                $"{state}  •  {points}",
                achievement.IsEarned));
        }
        Model.Overlay = SecondScreenOverlayKind.Achievements;
    }

    private void ShowAchievementsMessage(string title, string message, bool canRefresh)
    {
        Model.AchievementsTitle = title;
        Model.AchievementsStatus = message;
        Model.CanRefresh = canRefresh;
        Model.Achievements.Clear();
        Model.Overlay = SecondScreenOverlayKind.Achievements;
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (ReferenceEquals(Active, this))
            Active = null;
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModelPropertyChanged;
        _home = null;
        _mainHandler.Dispose();
    }
}

internal sealed record SecondScreenApp(string Component, string Label, Avalonia.Media.Imaging.Bitmap? Icon);
