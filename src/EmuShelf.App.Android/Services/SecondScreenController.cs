using System.ComponentModel;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Hardware.Display;
using Android.OS;
using Android.Views;
using Android.Widget;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.SecondScreen;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Owns the Thor companion display for the lifetime of the Android frontend. All Android UI stays in
/// this head; only dock mutation and running-vs-focused target selection cross into Core.
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
    private long _gameArtworkGeneration;
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
        if (MainActivity.Current is { } activity)
            AttachActivity(activity);
    }

    internal void GameStarted(Game game, string title)
    {
        // All companion state lives on the main thread. GameStarted runs on the launch continuation,
        // which is not guaranteed to be the main thread, so marshal the whole transition — field
        // mutation, presentation update, and the artwork-load kickoff — onto it so nothing races the
        // main-thread readers (OpenAchievementsCore, RenderRestingSurface, ViewModelPropertyChanged).
        RunOnMain(() =>
        {
            _runningGameId = game.Id;
            _runningGameTitle = title;
            var artworkGeneration = ++_gameArtworkGeneration;
            _navigation = _navigation.StartGame();
            ResetAchievementTarget();
            _presentation?.ShowGameIdle(title);
            StartKeepAliveIfNeeded();
            LoadIdleArtwork(game, title, artworkGeneration);
        });
    }

    // Resolve and sample the clear-logo/cover entirely off the UI thread. The generation check
    // prevents a slow lookup for one game from replacing a later game's artwork. Kicked off from the
    // main thread so the captured generation matches the state set in GameStarted.
    private void LoadIdleArtwork(Game game, string title, long artworkGeneration)
    {
        _ = Task.Run(() =>
        {
            Bitmap? bitmap = null;
            try
            {
                var logo = _gameDetails.GetDetails(game.Id).Media
                    .Where(media => media.Kind == GameMediaKind.Wheel && media.IsSelected)
                    .OrderByDescending(media => media.Id)
                    .Select(media => media.LocalPath)
                    .FirstOrDefault();
                bitmap = DecodeSampled(logo ?? game.CoverPath, maxWidth: 960, maxHeight: 760);
                if (bitmap is null)
                    return;

                var loaded = bitmap;
                bitmap = null;
                RunOnMain(() =>
                {
                    if (_runningGameId != game.Id || _gameArtworkGeneration != artworkGeneration ||
                        _presentation is not { } presentation)
                    {
                        loaded.Dispose();
                        return;
                    }

                    presentation.UpdateIdleArtwork(loaded);
                });
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not resolve second-screen artwork for {title}.", ex);
            }
            finally
            {
                bitmap?.Dispose();
            }
        });
    }

    internal void ReturnedToBrowse()
    {
        RunOnMain(() =>
        {
            _runningGameId = null;
            _runningGameTitle = null;
            _gameArtworkGeneration++;
            _navigation = _navigation.ReturnToBrowse();
            ResetAchievementTarget();
            _presentation?.ShowBrowseHome();
            StopKeepAlive();
        });
    }

    internal void ToggleDrawer()
    {
        // The chrome ☰ button is a toggle: a second press on an open all-apps drawer closes it back
        // to the resting surface rather than re-opening the same drawer.
        if (_navigation.Overlay == SecondScreenOverlay.AppDrawer)
            CloseOverlay();
        else
            ShowDrawer(pickSlot: null);
    }

    internal void ActivateDockSlot(int slot)
    {
        var component = _dock[slot];
        if (component is null)
        {
            ShowDrawer(slot);
            return;
        }

        LaunchOnSecondScreen(component);
    }

    internal void EditDockSlot(int slot) => ShowDrawer(slot);

    internal void ToggleAchievements()
    {
        // The chrome ★ button is a toggle, mirroring the drawer: a second press closes the panel.
        if (_navigation.Overlay == SecondScreenOverlay.Achievements)
            CloseOverlay();
        else
            OpenAchievementsCore(forceRefresh: false);
    }

    internal void RefreshAchievements() => OpenAchievementsCore(forceRefresh: true);

    internal void LoadBadge(ImageView image, string badgeName, long surfaceRevision)
    {
        _ = Task.Run(async () =>
        {
            Bitmap? bitmap = null;
            try
            {
                var path = await _badges.GetBadgePathAsync(badgeName);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return;
                bitmap = DecodeSampled(path, maxWidth: 128, maxHeight: 128);
                if (bitmap is null)
                    return;

                var loaded = bitmap;
                bitmap = null;
                RunOnMain(() =>
                {
                    if (!IsAchievementSurfaceCurrent(surfaceRevision) ||
                        !string.Equals(image.Tag?.ToString(), badgeName, StringComparison.Ordinal) ||
                        _presentation is not { } presentation)
                    {
                        loaded.Dispose();
                        return;
                    }

                    presentation.SetPanelBitmap(image, loaded);
                });
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not load second-screen achievement badge {badgeName}.", ex);
            }
            finally
            {
                bitmap?.Dispose();
            }
        });
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
            _presentation = new ThorSecondScreenPresentation(_activity, display, this);
            _presentation.Show();
            _logger.Information($"Second-screen Presentation attached: {Describe(display)}.");
            EnsureAppsLoadedAsync();
            RenderDock();
            RenderRestingSurface();
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
                    loaded[flattened] = new SecondScreenApp(
                        flattened,
                        resolved.LoadLabel(manager)?.ToString() ?? activityInfo.PackageName,
                        resolved.LoadIcon(manager));
                }

                RunOnMain(() =>
                {
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
        var apps = _apps.Values
            .OrderBy(app => app.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _presentation?.ShowDrawer(
            apps,
            pickSlot,
            selected: app =>
            {
                if (pickSlot is { } slot)
                {
                    _dock = _dock.Pin(slot, app.Component);
                    _dockStore.Save(_dock);
                    RenderDock();
                    CloseOverlay();
                }
                else
                {
                    LaunchOnSecondScreen(app.Component);
                }
            },
            clearSlot: pickSlot is { } clearSlot
                ? () =>
                {
                    _dock = _dock.Clear(clearSlot);
                    _dockStore.Save(_dock);
                    RenderDock();
                    CloseOverlay();
                }
                : null,
            close: CloseOverlay);
    }

    private void RenderDock() => _presentation?.RenderDock(_dock, _apps);

    private void CloseOverlay()
    {
        _navigation = _navigation.CloseOverlay();
        ResetAchievementTarget();
        RenderRestingSurface();
    }

    private void RenderRestingSurface()
    {
        if (_presentation is not { } presentation)
            return;

        if (_navigation.BaseSurface == SecondScreenBaseSurface.GameIdle && _runningGameId is not null)
            presentation.ShowGameIdle(_runningGameTitle ?? "Now playing");
        else
            presentation.ShowBrowseHome();
    }

    private void LaunchOnSecondScreen(string flattenedComponent)
    {
        if (_activity is null || _presentation?.Display is not { } display)
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
        var activity = _activity;
        if (activity is null)
            return;
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
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not launch {flattenedComponent} on the second screen.", ex);
            _presentation?.ShowAchievementsMessage(
                "App could not open",
                "This app may not support launching on the Thor's second display.",
                close: CloseOverlay);
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
            _presentation?.ShowAchievementsMessage(
                "Achievements",
                "Select a game first.",
                close: CloseOverlay);
            return;
        }

        var links = _readStore.GetAllLinks();
        if (!links.TryGetValue(gameId.Value, out var link) ||
            link is not { HasAchievements: true, RetroAchievementsGameId: { } raGameId })
        {
            _presentation?.ShowAchievementsMessage(
                title,
                "No RetroAchievements set is linked to this game.",
                close: CloseOverlay);
            return;
        }

        _achievementTargetGameId = gameId;
        _achievementTargetTitle = title;
        var cached = _details.GetCached(raGameId);
        var credentials = _account.CurrentCredentials;
        if (cached is not null)
        {
            _presentation?.ShowAchievements(
                title,
                cached,
                credentials is null
                    ? "Reconnect RetroAchievements to refresh these cached details."
                    : forceRefresh
                        ? "Refreshing achievement details…"
                        : $"Updated {cached.LastRefreshedAt.LocalDateTime:g}",
                canRefresh: credentials is not null,
                close: CloseOverlay,
                surfaceRevision: surfaceRevision);
        }
        else
        {
            _presentation?.ShowAchievementsMessage(
                title,
                credentials is null
                    ? "Connect RetroAchievements in Settings to load details."
                    : "Loading achievement details…",
                close: CloseOverlay);
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
                var response = await _details.RefreshAsync(
                    credentials,
                    raGameId,
                    manual: forceRefresh);
                RunOnMain(() => ApplyAchievementRefresh(
                    surfaceRevision,
                    gameId.Value,
                    title,
                    cached,
                    response));
            }
            catch (Exception ex)
            {
                _logger.Error($"Second-screen achievement refresh failed for game id {gameId}.", ex);
                RunOnMain(() => ShowAchievementFailure(
                    surfaceRevision,
                    gameId.Value,
                    title,
                    cached,
                    "Achievement details could not be refreshed."));
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
            _presentation?.ShowAchievements(
                title,
                response.Value!,
                "Updated just now",
                canRefresh: true,
                close: CloseOverlay,
                surfaceRevision: surfaceRevision);
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
        {
            _presentation?.ShowAchievements(
                title,
                cached,
                status,
                canRefresh: true,
                close: CloseOverlay,
                surfaceRevision: surfaceRevision);
        }
        else
        {
            _presentation?.ShowAchievementsMessage(
                title,
                status,
                canRefresh: true,
                close: CloseOverlay);
        }
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
        if (e.PropertyName == nameof(MainViewModel.FocusedGame) && _runningGameId is null)
        {
            // An open panel remains a snapshot until the user asks for achievements again, but its
            // pending request must no longer be allowed to overwrite the newly focused context.
            ResetAchievementTarget();
        }
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

    private static Bitmap? DecodeSampled(string? path, int maxWidth, int maxHeight)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(path, bounds);
        if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
            return null;

        var sample = 1;
        while (bounds.OutWidth / (sample * 2) >= maxWidth &&
               bounds.OutHeight / (sample * 2) >= maxHeight)
        {
            sample *= 2;
        }

        using var decode = new BitmapFactory.Options { InSampleSize = sample };
        return BitmapFactory.DecodeFile(path, decode);
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
        DismissPresentation();
        DetachDisplayManager();
        StopKeepAlive();
        _activity = null;
        _mainHandler.Dispose();
        base.Dispose();
    }
}

internal sealed record SecondScreenApp(string Component, string Label, Drawable? Icon);
