using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Core.SecondScreen;

namespace EmuShelf.App.ViewModels;

/// <summary>The companion's two tabs while a game runs: one big panel at a time (the spec's Companion section).</summary>
public enum HandheldCompanionTab
{
    Achievements,
    Photos,
}

/// <summary>Where the companion's selector is: the big panel, or the dock below it.</summary>
public enum HandheldCompanionZone
{
    Panel,
    Dock,
}

/// <summary>What the companion shows over its body: nothing, every badge, the photo viewer, or All apps.</summary>
public enum HandheldCompanionLayer
{
    None,
    Achievements,
    Photos,
    Drawer,
}

/// <summary>One button of the companion's dock: All apps, then the five pinned apps (the same slots as today's dock).</summary>
public sealed partial class HandheldCompanionDockItemViewModel(int index, SecondScreenSlotViewModel? slot) : ObservableObject
{
    /// <summary>0 is All apps; 1–5 are the pinned slots.</summary>
    public int Index { get; } = index;

    public SecondScreenSlotViewModel? Slot { get; } = slot;

    public bool IsAllApps => Slot is null;
    public bool IsApp => Slot is not null;

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

/// <summary>One app in the companion's All apps grid, with the selector's focus.</summary>
public sealed partial class HandheldDrawerAppViewModel(SecondScreenAppViewModel app) : ObservableObject
{
    public SecondScreenAppViewModel App { get; } = app;

    [ObservableProperty]
    public partial bool IsFocused { get; set; }
}

/// <summary>
/// Handheld mode's face for the Thor companion (M45 H6b): while a game runs, the bottom screen in the Launcher's darker
/// palette, with a slim Now playing row, the Achievements / Photos tabs, one big panel and the dock; it rests to a faint
/// cover after a few seconds alone, and the first press only wakes it. Over the Launcher it also shows the companion's
/// layers when one opens while browsing: All apps from the dock, the badge grid from the Title page.
/// <para>
/// Every feature is today's, on <see cref="SecondScreenViewModel"/>: the Android controller still loads the set, the
/// media and the dock into it, and launches apps and pins slots from its callbacks. This adds the Handheld layout, the
/// D-pad's reach over all of it (the dock, the drawer and the overlay buttons were touch-only), and the rest.
/// </para>
/// </summary>
public sealed partial class HandheldCompanionViewModel : ObservableObject, IDisposable
{
    /// <summary>How long the companion stays lit with nothing pressed (the mockup's twelve seconds).</summary>
    internal static readonly TimeSpan RestDelay = TimeSpan.FromSeconds(12);

    /// <summary>The badge grid's columns: seven on the 1240-wide screen, eleven on the 16:9 one, at the same pitch.</summary>
    public const int NarrowBadgeColumns = 7;
    public const int WideBadgeColumns = 11;

    /// <summary>All apps' columns on each screen.</summary>
    public const int NarrowDrawerColumns = 5;
    public const int WideDrawerColumns = 8;

    /// <summary>The Now playing row's cover decodes at about its on-screen size.</summary>
    public const int CoverDecodeWidth = 200;

    /// <summary>The panel's photo decodes to fit the 16:9 screen's panel.</summary>
    private const int PhotoDecodeWidth = 1824;
    private const int PhotoDecodeHeight = 576;

    private readonly Func<HandheldGameEntry, CancellationToken, Task<Bitmap?>>? _loadCover;
    private DispatcherTimer? _restTimer;
    private CancellationTokenSource? _coverLoad;
    private CancellationTokenSource? _photoLoad;
    private long _photoGeneration;
    private bool _nowPlayingHasAchievements = true;
    private bool _disposed;

    public HandheldCompanionViewModel(SecondScreenViewModel model,
        Func<HandheldGameEntry, CancellationToken, Task<Bitmap?>>? loadCover = null)
    {
        Model = model;
        _loadCover = loadCover;
        DockItems.Add(new HandheldCompanionDockItemViewModel(0, null));
        foreach (var slot in model.Dock)
            DockItems.Add(new HandheldCompanionDockItemViewModel(DockItems.Count, slot));
        _lastOverlay = model.Overlay;
        model.PropertyChanged += OnModelChanged;
        model.Apps.CollectionChanged += OnAppsChanged;
        RebuildDrawer();
        RebuildRecentUnlocks();
        RebuildPhotoDots();
        model.AchievementColumnCount = BadgeColumns;
        if (model.IsGameRunning)
            Begin();
        UpdateFocus();
    }

    /// <summary>The companion's data and actions, shared with Gamepad mode's companion view.</summary>
    public SecondScreenViewModel Model { get; }

    public ObservableCollection<HandheldCompanionDockItemViewModel> DockItems { get; } = [];

    public ObservableCollection<HandheldDrawerAppViewModel> DrawerApps { get; } = [];

    /// <summary>The ≤3 most recent unlocks the Achievements panel lists.</summary>
    public ObservableCollection<AchievementRowViewModel> RecentUnlocks { get; } = [];

    /// <summary>One dot per photo under the panel's picture.</summary>
    public ObservableCollection<HandheldPageDotViewModel> PhotoDots { get; } = [];

    public ObservableCollection<HandheldHint> GridHints { get; } = [];

    public ObservableCollection<HandheldHint> ViewerHints { get; } = [];

    public ObservableCollection<HandheldHint> DrawerHints { get; } = [];

    // ---- Where it is ----

    /// <summary>
    /// On the Thor's 16:9 screen (a game on the bottom screen puts the companion on top, M45 H5) rather than the
    /// 1240-wide one. The host says which; the grids' column counts follow.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOnWideScreen { get; set; }

    public int BadgeColumns => IsOnWideScreen ? WideBadgeColumns : NarrowBadgeColumns;

    public int DrawerColumns => IsOnWideScreen ? WideDrawerColumns : NarrowDrawerColumns;

    partial void OnIsOnWideScreenChanged(bool value)
    {
        Model.AchievementColumnCount = BadgeColumns;
        OnPropertyChanged(nameof(BadgeColumns));
        OnPropertyChanged(nameof(DrawerColumns));
    }

    // ---- What it shows ----

    public bool IsGameRunning => Model.IsGameRunning;

    /// <summary>The full badge grid is open over the running game's companion (A on the Achievements panel).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Layer))]
    [NotifyPropertyChangedFor(nameof(IsAchievementsLayer))]
    [NotifyPropertyChangedFor(nameof(IsPhotosLayer))]
    [NotifyPropertyChangedFor(nameof(IsDrawerLayer))]
    [NotifyPropertyChangedFor(nameof(HasLayer))]
    public partial bool IsGridOpen { get; private set; }

    /// <summary>
    /// The layer in front. While a game runs the model's achievements state only says the set is loaded (the panel
    /// shows it), so the grid is up only once A opened it; while browsing a layer is open exactly when the model's
    /// overlay is (the Title page's Achievements, the dock's All apps).
    /// </summary>
    public HandheldCompanionLayer Layer => Model.Overlay switch
    {
        SecondScreenOverlayKind.Drawer => HandheldCompanionLayer.Drawer,
        SecondScreenOverlayKind.Media => HandheldCompanionLayer.Photos,
        SecondScreenOverlayKind.Achievements when !IsGameRunning || IsGridOpen => HandheldCompanionLayer.Achievements,
        _ when IsGameRunning && IsGridOpen => HandheldCompanionLayer.Achievements,
        _ => HandheldCompanionLayer.None,
    };

    public bool HasLayer => Layer != HandheldCompanionLayer.None;
    public bool IsAchievementsLayer => Layer == HandheldCompanionLayer.Achievements;
    public bool IsPhotosLayer => Layer == HandheldCompanionLayer.Photos;
    public bool IsDrawerLayer => Layer == HandheldCompanionLayer.Drawer;

    /// <summary>All apps opened from the Launcher's dock keeps the Launcher's light look; in a game it is dark.</summary>
    public bool IsDrawerLight => !IsGameRunning;

    /// <summary>
    /// Whether the companion has the pad: a game runs (the companion is the bottom screen), or one of its layers is up
    /// over the Launcher.
    /// </summary>
    public bool OwnsPad => IsGameRunning || Model.Overlay != SecondScreenOverlayKind.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAchievementsTab))]
    [NotifyPropertyChangedFor(nameof(IsPhotosTab))]
    [NotifyPropertyChangedFor(nameof(PanelHint))]
    [NotifyPropertyChangedFor(nameof(HasPanelHint))]
    public partial HandheldCompanionTab Tab { get; private set; }

    public bool IsAchievementsTab => Tab == HandheldCompanionTab.Achievements;
    public bool IsPhotosTab => Tab == HandheldCompanionTab.Photos;

    [ObservableProperty]
    public partial HandheldCompanionZone Zone { get; private set; }

    /// <summary>The panel carries the selector (Up from the dock, the default).</summary>
    [ObservableProperty]
    public partial bool IsPanelFocused { get; private set; } = true;

    [ObservableProperty]
    public partial int DockIndex { get; private set; } = 1;

    [ObservableProperty]
    public partial int DrawerIndex { get; private set; }

    /// <summary>Dimmed to a faint cover and title after <see cref="RestDelay"/> without input; the first press only wakes it.</summary>
    [ObservableProperty]
    public partial bool IsResting { get; private set; }

    // ---- Now playing ----

    [ObservableProperty]
    public partial string NowPlayingTitle { get; private set; } = string.Empty;

    /// <summary>"PlayStation 2 · 12 h 30 min played".</summary>
    [ObservableProperty]
    public partial string NowPlayingLine { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNowPlayingCover))]
    public partial Bitmap? NowPlayingCover { get; private set; }

    public bool HasNowPlayingCover => NowPlayingCover is not null;

    partial void OnNowPlayingCoverChanging(Bitmap? value)
    {
        if (!ReferenceEquals(NowPlayingCover, value))
            NowPlayingCover?.Dispose();
    }

    /// <summary>
    /// The game about to run (the Title page's Play): its title, platform and play time for the Now playing row, and its
    /// cover, read now so the companion arrives with it.
    /// </summary>
    public void SetNowPlaying(HandheldGameEntry entry, string platformName)
    {
        NowPlayingTitle = entry.Title;
        NowPlayingLine = entry.Playtime.TotalMinutes >= 1
            ? $"{platformName} · {HandheldText.Duration(entry.Playtime)} played"
            : $"{platformName} · first time playing";
        _nowPlayingHasAchievements = !string.IsNullOrEmpty(entry.AchievementText);
        _coverLoad?.Cancel();
        _coverLoad?.Dispose();
        _coverLoad = null;
        NowPlayingCover = null;
        if (_loadCover is null)
            return;
        var load = _coverLoad = new CancellationTokenSource();
        _ = LoadNowPlayingCoverAsync(entry, load);
    }

    private async Task LoadNowPlayingCoverAsync(HandheldGameEntry entry, CancellationTokenSource load)
    {
        Bitmap? cover = null;
        try
        {
            cover = await _loadCover!(entry, load.Token);
        }
        catch (Exception)
        {
            // No cover: the row keeps its title and line.
        }
        if (_disposed || !ReferenceEquals(_coverLoad, load) || load.IsCancellationRequested)
        {
            cover?.Dispose();
            return;
        }
        NowPlayingCover = cover;
    }

    // ---- The Achievements panel ----

    public bool HasAchievementSet => Model.HasAchievements;

    /// <summary>Unlocked over total, for the ring.</summary>
    public double ProgressRatio => Model.AchievementCount == 0 ? 0 : (double)Model.UnlockedAchievementCount / Model.AchievementCount;

    /// <summary>The ring's sweep in degrees (an <c>Arc</c> draws it).</summary>
    public double ProgressSweep => 360 * ProgressRatio;

    public string ProgressPercent => string.Create(CultureInfo.CurrentCulture, $"{Math.Round(ProgressRatio * 100):0}%");

    public string ProgressCount => string.Create(CultureInfo.CurrentCulture, $"{Model.UnlockedAchievementCount} / {Model.AchievementCount}");

    /// <summary>"340 of 1,000 points" (sets without points, Steam's, say how many are left instead).</summary>
    public string ProgressPoints
    {
        get
        {
            var rows = Model.Achievements;
            if (rows.Count == 0)
                return string.Empty;
            if (!rows.Any(row => row.HasPoints))
                return Model.LockedAchievementCount == 0 ? "All unlocked" : $"{Model.LockedAchievementCount} to go";
            var earned = rows.Where(row => row.IsUnlocked).Sum(row => row.Points);
            var total = rows.Sum(row => row.Points);
            return string.Create(CultureInfo.CurrentCulture, $"{earned:N0} of {total:N0} points");
        }
    }

    public bool HasRecentUnlocks => RecentUnlocks.Count > 0;

    /// <summary>What the panel says when there is no set to show: loading, not linked, not connected.</summary>
    public string AchievementsMessage => Model.AchievementsStatus is { Length: > 0 } status
        ? status
        : _nowPlayingHasAchievements ? "Loading achievements…" : "This game isn't linked to RetroAchievements or Steam, so there's nothing to track.";

    /// <summary>The grid's empty state under a filter ("Nothing locked").</summary>
    public string FilterEmptyText => Model.AchievementFilter == AchievementDisplayFilter.Locked
        ? "Everything is unlocked."
        : "Unlock an achievement in the game and it shows up here.";

    public bool IsFilterEmpty => Model.HasAchievements && !Model.HasVisibleAchievements;

    public bool IsAllFilter => Model.AchievementFilter == AchievementDisplayFilter.All;
    public bool IsLockedFilter => Model.AchievementFilter == AchievementDisplayFilter.Locked;
    public bool IsUnlockedFilter => Model.AchievementFilter == AchievementDisplayFilter.Unlocked;

    public string AllFilterText => string.Create(CultureInfo.CurrentCulture, $"All  {Model.AchievementCount}");
    public string LockedFilterText => string.Create(CultureInfo.CurrentCulture, $"Locked  {Model.LockedAchievementCount}");
    public string UnlockedFilterText => string.Create(CultureInfo.CurrentCulture, $"Unlocked  {Model.UnlockedAchievementCount}");

    private void RebuildRecentUnlocks()
    {
        var recent = Model.Achievements
            .Where(row => row.IsUnlocked)
            .OrderByDescending(row => row.EarnedAt ?? DateTimeOffset.MinValue)
            .ThenBy(row => row.DisplayOrder)
            .Take(3)
            .ToList();
        if (recent.SequenceEqual(RecentUnlocks))
            return;
        RecentUnlocks.Clear();
        foreach (var row in recent)
        {
            RecentUnlocks.Add(row);
            if (row.Badge is null)
                _ = row.LoadBadgeAsync(row.BadgeName);
        }
        OnPropertyChanged(nameof(HasRecentUnlocks));
    }

    private void RaiseAchievementsChanged()
    {
        RebuildRecentUnlocks();
        OnPropertyChanged(nameof(HasAchievementSet));
        OnPropertyChanged(nameof(ProgressRatio));
        OnPropertyChanged(nameof(ProgressSweep));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressCount));
        OnPropertyChanged(nameof(ProgressPoints));
        OnPropertyChanged(nameof(AchievementsMessage));
        OnPropertyChanged(nameof(PanelHint));
        OnPropertyChanged(nameof(HasPanelHint));
        RaiseFilterChanged();
    }

    private void RaiseFilterChanged()
    {
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsLockedFilter));
        OnPropertyChanged(nameof(IsUnlockedFilter));
        OnPropertyChanged(nameof(AllFilterText));
        OnPropertyChanged(nameof(LockedFilterText));
        OnPropertyChanged(nameof(UnlockedFilterText));
        OnPropertyChanged(nameof(FilterEmptyText));
        OnPropertyChanged(nameof(IsFilterEmpty));
        UpdateGridHints();
    }

    // ---- The Photos panel ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhotoCaption))]
    [NotifyPropertyChangedFor(nameof(IsPhotoVideo))]
    public partial int PhotoIndex { get; private set; }

    /// <summary>The panel's picture: the photo it is on (for the video, the first screenshot as its poster).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPanelPhoto))]
    public partial Bitmap? PanelPhoto { get; private set; }

    public bool HasPanelPhoto => PanelPhoto is not null;

    partial void OnPanelPhotoChanging(Bitmap? value)
    {
        if (!ReferenceEquals(PanelPhoto, value))
            PanelPhoto?.Dispose();
    }

    public bool HasPhotos => Model.MediaItems.Count > 0;

    public bool IsPhotoVideo => PhotoIndex < Model.MediaItems.Count && Model.MediaItems[PhotoIndex].IsVideo;

    public string PhotoCaption
    {
        get
        {
            var items = Model.MediaItems;
            if (items.Count == 0)
                return string.Empty;
            var item = items[Math.Min(PhotoIndex, items.Count - 1)];
            return item.IsVideo
                ? "Gameplay video · plays muted, only downloaded when you play it"
                : string.Create(CultureInfo.CurrentCulture, $"{item.Title} · {PhotoIndex + 1} of {items.Count}");
        }
    }

    /// <summary>The corner hint on the panel: what A does there.</summary>
    public string? PanelHint => Tab switch
    {
        HandheldCompanionTab.Achievements when Model.HasAchievements => "See all",
        HandheldCompanionTab.Photos when HasPhotos => "Full screen",
        _ => null,
    };

    public bool HasPanelHint => PanelHint is not null;

    private void RebuildPhotoDots()
    {
        var count = Model.MediaItems.Count;
        if (PhotoIndex >= count)
            PhotoIndex = Math.Max(0, count - 1);
        if (PhotoDots.Count != count)
        {
            PhotoDots.Clear();
            for (var index = 0; index < count; index++)
                PhotoDots.Add(new HandheldPageDotViewModel(index));
        }
        foreach (var dot in PhotoDots)
            dot.IsCurrent = dot.Index == PhotoIndex;
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(PhotoCaption));
        OnPropertyChanged(nameof(IsPhotoVideo));
        OnPropertyChanged(nameof(PanelHint));
        OnPropertyChanged(nameof(HasPanelHint));
    }

    private void ShowPhoto(int index)
    {
        PhotoIndex = index;
        foreach (var dot in PhotoDots)
            dot.IsCurrent = dot.Index == index;
        _ = LoadPanelPhotoAsync();
    }

    // Decodes the panel's photo off the UI thread. Only while a game runs and the Photos tab shows, so the companion never
    // downloads screenshots nobody is looking at.
    private async Task LoadPanelPhotoAsync()
    {
        var generation = ++_photoGeneration;
        _photoLoad?.Cancel();
        _photoLoad?.Dispose();
        _photoLoad = null;
        var items = Model.MediaItems;
        if (!IsGameRunning || Tab != HandheldCompanionTab.Photos || items.Count == 0)
        {
            if (items.Count == 0)
                PanelPhoto = null;
            return;
        }
        var item = items[Math.Min(PhotoIndex, items.Count - 1)];
        if (item.IsVideo && items.FirstOrDefault(candidate => !candidate.IsVideo) is { } poster)
            item = poster;
        if (item.IsVideo)
        {
            PanelPhoto = null;
            return;
        }
        var load = _photoLoad = new CancellationTokenSource();
        try
        {
            var path = Model.ResolveMediaPath is { } resolve ? await resolve(item, load.Token) : item.LocalPath;
            if (generation != _photoGeneration || path is null)
                return;
            var bitmap = await Task.Run(() => SafeImageDecoder.DecodeToFit(path, PhotoDecodeWidth, PhotoDecodeHeight), load.Token);
            if (generation != _photoGeneration || _disposed)
            {
                bitmap.Dispose();
                return;
            }
            PanelPhoto = bitmap;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // An unreadable photo leaves the panel on its caption.
        }
    }

    // ---- The session ----

    // A game started: the companion comes up on its panel, on the default tab, lit.
    private void Begin()
    {
        IsGridOpen = false;
        Zone = HandheldCompanionZone.Panel;
        DockIndex = 1;
        Model.AchievementFilter = AchievementDisplayFilter.All;
        Model.AchievementColumnCount = BadgeColumns;
        Tab = _nowPlayingHasAchievements ? HandheldCompanionTab.Achievements : HandheldCompanionTab.Photos;
        PhotoIndex = 0;
        RebuildPhotoDots();
        _ = LoadPanelPhotoAsync();
        Wake();
        UpdateFocus();
    }

    // The game ended: nothing of the session stays lit or loaded.
    private void End()
    {
        StopRestTimer();
        IsResting = false;
        IsGridOpen = false;
        ++_photoGeneration;
        _photoLoad?.Cancel();
        PanelPhoto = null;
        _coverLoad?.Cancel();
        NowPlayingCover = null;
        _nowPlayingHasAchievements = true;
        UpdateFocus();
    }

    /// <summary>Lights the companion and restarts its countdown to rest (any press, any touch).</summary>
    public void Wake()
    {
        IsResting = false;
        RestartRestTimer();
    }

    /// <summary>The countdown ran out: rest, unless something is open over the companion (a layer holds the light).</summary>
    internal void Rest()
    {
        StopRestTimer();
        if (IsGameRunning && !HasLayer)
            IsResting = true;
    }

    /// <summary>Whether the rest countdown runs (tests read it; the timer itself is the dispatcher's).</summary>
    internal bool IsRestPending => _restTimer?.IsEnabled == true;

    private void RestartRestTimer()
    {
        if (!IsGameRunning || HasLayer)
        {
            StopRestTimer();
            return;
        }
        if (_restTimer is null)
        {
            _restTimer = new DispatcherTimer { Interval = RestDelay };
            _restTimer.Tick += (_, _) => Rest();
        }
        _restTimer.Stop();
        _restTimer.Start();
    }

    private void StopRestTimer() => _restTimer?.Stop();

    // ---- Input ----

    /// <summary>
    /// The pad, while the companion has it (<see cref="OwnsPad"/>). Every button is consumed: the game has the main
    /// display's pad, and nothing may reach the Launcher behind.
    /// </summary>
    public bool Dispatch(GamepadAction action)
    {
        if (IsGameRunning)
        {
            if (IsResting)
            {
                // The first press only wakes it.
                Wake();
                return true;
            }
            Wake();
        }
        switch (Layer)
        {
            case HandheldCompanionLayer.Achievements:
                DispatchGrid(action);
                return true;
            case HandheldCompanionLayer.Photos:
                DispatchViewer(action);
                return true;
            case HandheldCompanionLayer.Drawer:
                DispatchDrawer(action);
                return true;
        }
        if (IsGameRunning)
            DispatchBody(action);
        return true;
    }

    /// <summary>The system Back: closes the layer in front. With none, it is swallowed while a game runs.</summary>
    public bool Back()
    {
        if (IsGameRunning && IsResting)
        {
            Wake();
            return true;
        }
        if (HasLayer)
        {
            CloseLayer();
            return true;
        }
        return IsGameRunning;
    }

    private void DispatchBody(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp when Zone == HandheldCompanionZone.Dock:
                Zone = HandheldCompanionZone.Panel;
                break;
            case GamepadAction.NavigateDown when Zone == HandheldCompanionZone.Panel:
                Zone = HandheldCompanionZone.Dock;
                break;
            case GamepadAction.NavigateLeft when Zone == HandheldCompanionZone.Dock:
                DockIndex = Math.Max(0, DockIndex - 1);
                break;
            case GamepadAction.NavigateRight when Zone == HandheldCompanionZone.Dock:
                DockIndex = Math.Min(DockItems.Count - 1, DockIndex + 1);
                break;
            // On the panel: photos page in place; right from Achievements goes to Photos (the mockup's pair).
            case GamepadAction.NavigateLeft when Tab == HandheldCompanionTab.Photos:
                if (PhotoIndex > 0)
                    ShowPhoto(PhotoIndex - 1);
                break;
            case GamepadAction.NavigateRight when Tab == HandheldCompanionTab.Photos:
                if (PhotoIndex < Model.MediaItems.Count - 1)
                    ShowPhoto(PhotoIndex + 1);
                break;
            case GamepadAction.NavigateRight:
                SelectTab(HandheldCompanionTab.Photos);
                break;
            case GamepadAction.PreviousPlatform:
                SelectTab(HandheldCompanionTab.Achievements);
                break;
            case GamepadAction.NextPlatform:
                SelectTab(HandheldCompanionTab.Photos);
                break;
            case GamepadAction.Confirm when Zone == HandheldCompanionZone.Panel:
                OpenPanel();
                break;
            case GamepadAction.Confirm:
                ActivateDockItem(DockItems[DockIndex]);
                break;
            // Y on a pinned slot changes or clears it, as a long press does (the dock's Change app… / Clear slot).
            case GamepadAction.Actions when Zone == HandheldCompanionZone.Dock && DockItems[DockIndex].Slot is { } slot:
                Model.EditSlotCommand.Execute(slot.Index);
                break;
            // Y on an empty Photos panel fetches the game's missing artwork, as the viewer's button does.
            case GamepadAction.Actions when Zone == HandheldCompanionZone.Panel && Tab == HandheldCompanionTab.Photos &&
                                            !HasPhotos && Model.CanFetchMedia && !Model.IsFetchingMedia:
                Model.FetchMediaCommand.Execute(null);
                break;
        }
        UpdateFocus();
    }

    private void DispatchGrid(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.Cancel:
                CloseLayer();
                break;
            case GamepadAction.PreviousPlatform:
                StepFilter(-1);
                break;
            case GamepadAction.NextPlatform:
                StepFilter(1);
                break;
            case GamepadAction.NavigateLeft or GamepadAction.NavigateRight or GamepadAction.NavigateUp or GamepadAction.NavigateDown:
                Model.MoveAchievementSelection(action);
                break;
            case GamepadAction.Confirm when Model.SelectedAchievement is { CanReveal: true } hidden:
                hidden.RevealCommand.Execute(null);
                UpdateGridHints();
                break;
            case GamepadAction.Actions when Model.CanRefresh && !Model.IsAchievementsRefreshing:
                Model.RefreshCommand.Execute(null);
                break;
        }
    }

    private void DispatchViewer(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.Cancel:
                CloseLayer();
                break;
            case GamepadAction.NavigateLeft when Model.CanBrowseMedia:
                Model.PreviousMediaCommand.Execute(null);
                break;
            case GamepadAction.NavigateRight when Model.CanBrowseMedia:
                Model.NextMediaCommand.Execute(null);
                break;
            case GamepadAction.Confirm when Model.IsVideoPlaying:
                Model.StopVideoCommand.Execute(null);
                break;
            case GamepadAction.Confirm when Model.ShowMediaPlay:
                Model.PlayMediaCommand.Execute(null);
                break;
            case GamepadAction.Actions when Model.CanFetchMedia && !Model.IsFetchingMedia:
                Model.FetchMediaCommand.Execute(null);
                break;
        }
        UpdateViewerHints();
    }

    private void DispatchDrawer(GamepadAction action)
    {
        var columns = DrawerColumns;
        var count = DrawerApps.Count;
        switch (action)
        {
            case GamepadAction.Cancel:
                CloseLayer();
                return;
            case GamepadAction.NavigateLeft when DrawerIndex % columns > 0:
                DrawerIndex--;
                break;
            case GamepadAction.NavigateRight when DrawerIndex % columns < columns - 1 && DrawerIndex + 1 < count:
                DrawerIndex++;
                break;
            case GamepadAction.NavigateUp when DrawerIndex >= columns:
                DrawerIndex -= columns;
                break;
            case GamepadAction.NavigateDown when DrawerIndex + columns < count:
                DrawerIndex += columns;
                break;
            // A shorter last row: down from a column it does not reach lands on its last app.
            case GamepadAction.NavigateDown when DrawerIndex / columns < (count - 1) / columns:
                DrawerIndex = count - 1;
                break;
            case GamepadAction.Confirm when DrawerIndex < count:
                Model.LaunchAppCommand.Execute(DrawerApps[DrawerIndex].App);
                return;
            case GamepadAction.Actions when Model.CanClearSlot:
                Model.ClearSlotCommand.Execute(null);
                return;
        }
        UpdateDrawerFocus();
    }

    /// <summary>Closes the layer in front: the grid back to the panel, the viewer and All apps back to what was under them.</summary>
    public void CloseLayer()
    {
        switch (Layer)
        {
            case HandheldCompanionLayer.Achievements when IsGameRunning:
                IsGridOpen = false;
                Model.AchievementFilter = AchievementDisplayFilter.All;
                Wake();
                break;
            case HandheldCompanionLayer.None:
                return;
            default:
                Model.CloseOverlayCommand.Execute(null);
                break;
        }
        UpdateFocus();
    }

    private void StepFilter(int direction)
    {
        var next = (int)Model.AchievementFilter + direction;
        if (next is < (int)AchievementDisplayFilter.All or > (int)AchievementDisplayFilter.Unlocked)
            return;
        Model.AchievementFilter = (AchievementDisplayFilter)next;
    }

    /// <summary>A on the panel (or a tap on it): every badge, or the photo it shows full screen.</summary>
    public void OpenPanel()
    {
        Zone = HandheldCompanionZone.Panel;
        if (Tab == HandheldCompanionTab.Achievements)
        {
            if (!Model.HasAchievements)
                return;
            Model.AchievementFilter = AchievementDisplayFilter.All;
            Model.AchievementColumnCount = BadgeColumns;
            IsGridOpen = true;
            StopRestTimer();
        }
        else if (HasPhotos)
        {
            Model.OpenMediaAt(PhotoIndex);
        }
        UpdateFocus();
    }

    private void ActivateDockItem(HandheldCompanionDockItemViewModel item)
    {
        if (item.Slot is { } slot)
            Model.ActivateSlotCommand.Execute(slot);
        else
            Model.ToggleDrawerCommand.Execute(null);
    }

    // ---- Touch ----

    /// <summary>A touch anywhere on the companion: it keeps it lit (the resting wash takes the waking touch itself).</summary>
    public void NoteTouch()
    {
        if (IsGameRunning)
            Wake();
    }

    /// <summary>A tap on a tab.</summary>
    public void SelectTab(HandheldCompanionTab tab)
    {
        Zone = HandheldCompanionZone.Panel;
        if (Tab != tab)
        {
            Tab = tab;
            if (tab == HandheldCompanionTab.Photos)
                _ = LoadPanelPhotoAsync();
        }
        UpdateFocus();
    }

    /// <summary>A swipe across the Photos panel pages through the photos.</summary>
    public void SwipePanel(int direction)
    {
        if (Tab != HandheldCompanionTab.Photos)
            return;
        var next = PhotoIndex + Math.Sign(direction);
        if (next >= 0 && next < Model.MediaItems.Count)
            ShowPhoto(next);
    }

    /// <summary>A tap on a dock button: the first selects it, a second opens it (the controls table's touch rule).</summary>
    public void TapDockItem(HandheldCompanionDockItemViewModel item)
    {
        if (Zone == HandheldCompanionZone.Dock && DockIndex == item.Index)
        {
            ActivateDockItem(item);
            return;
        }
        Zone = HandheldCompanionZone.Dock;
        DockIndex = item.Index;
        UpdateFocus();
    }

    /// <summary>A long press on a pinned slot: change or clear it.</summary>
    public void HoldDockItem(HandheldCompanionDockItemViewModel item)
    {
        if (item.Slot is not { } slot)
            return;
        Zone = HandheldCompanionZone.Dock;
        DockIndex = item.Index;
        UpdateFocus();
        Model.EditSlotCommand.Execute(slot.Index);
    }

    public void TapDrawerApp(HandheldDrawerAppViewModel app) => Model.LaunchAppCommand.Execute(app.App);

    public void SelectFilter(AchievementDisplayFilter filter) => Model.AchievementFilter = filter;

    // ---- Focus and hints ----

    private void UpdateFocus()
    {
        var onPanel = IsGameRunning && !HasLayer && Zone == HandheldCompanionZone.Panel;
        IsPanelFocused = onPanel;
        foreach (var item in DockItems)
            item.IsFocused = IsGameRunning && !HasLayer && Zone == HandheldCompanionZone.Dock && item.Index == DockIndex;
        UpdateDrawerFocus();
        UpdateGridHints();
        UpdateViewerHints();
    }

    private void UpdateDrawerFocus()
    {
        if (DrawerIndex >= DrawerApps.Count)
            DrawerIndex = Math.Max(0, DrawerApps.Count - 1);
        for (var index = 0; index < DrawerApps.Count; index++)
            DrawerApps[index].IsFocused = IsDrawerLayer && index == DrawerIndex;
        // Opened for a slot (Y or a long press on it), A pins the app there and Y clears the slot; else A opens the app.
        SetHints(DrawerHints, Model.CanClearSlot
            ? [new("A", "Pin here"), new("Y", "Clear slot"), new("B", "Close")]
            : [new("A", "Open"), new("B", "Close")]);
    }

    // Hints are rebuilt on every press; the lists only change (and the views only re-template) when the hints do.
    private static void SetHints(ObservableCollection<HandheldHint> target, HandheldHint[] hints)
    {
        if (target.SequenceEqual(hints))
            return;
        target.Clear();
        foreach (var hint in hints)
            target.Add(hint);
    }

    private void UpdateGridHints()
    {
        var hints = new System.Collections.Generic.List<HandheldHint>();
        if (Model.SelectedAchievement is { CanReveal: true })
            hints.Add(new HandheldHint("A", "Reveal"));
        if (Model.CanRefresh)
            hints.Add(new HandheldHint("Y", "Refresh"));
        hints.Add(new HandheldHint("B", "Close"));
        SetHints(GridHints, [.. hints]);
    }

    private void UpdateViewerHints()
    {
        var hints = new System.Collections.Generic.List<HandheldHint>();
        if (Model.IsVideoPlaying)
            hints.Add(new HandheldHint("A", "Stop"));
        else if (Model.ShowMediaPlay)
            hints.Add(new HandheldHint("A", "Play muted"));
        if (Model.CanFetchMedia)
            hints.Add(new HandheldHint("Y", "Fetch artwork"));
        hints.Add(new HandheldHint("B", "Close"));
        SetHints(ViewerHints, [.. hints]);
    }

    // ---- Following the model ----

    private SecondScreenOverlayKind _lastOverlay;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SecondScreenViewModel.IsGameRunning):
                if (Model.IsGameRunning)
                    Begin();
                else
                    End();
                RaiseLayerChanged();
                OnPropertyChanged(nameof(IsGameRunning));
                OnPropertyChanged(nameof(IsDrawerLight));
                OnPropertyChanged(nameof(OwnsPad));
                break;
            case nameof(SecondScreenViewModel.Overlay):
                OnOverlayChanged(_lastOverlay, Model.Overlay);
                _lastOverlay = Model.Overlay;
                break;
            case nameof(SecondScreenViewModel.AchievementCount):
                RaiseAchievementsChanged();
                break;
            case nameof(SecondScreenViewModel.AchievementFilter):
            case nameof(SecondScreenViewModel.HasVisibleAchievements):
                RaiseFilterChanged();
                break;
            case nameof(SecondScreenViewModel.AchievementsStatus):
                OnPropertyChanged(nameof(AchievementsMessage));
                break;
            case nameof(SecondScreenViewModel.SelectedAchievement):
            case nameof(SecondScreenViewModel.CanRefresh):
                UpdateGridHints();
                break;
            case nameof(SecondScreenViewModel.MediaItems):
                RebuildPhotoDots();
                if (HasPanelPhoto != HasPhotos || !HasPanelPhoto)
                    _ = LoadPanelPhotoAsync();
                break;
            case nameof(SecondScreenViewModel.CanFetchMedia):
                OnPropertyChanged(nameof(PanelHint));
                OnPropertyChanged(nameof(HasPanelHint));
                UpdateViewerHints();
                break;
            case nameof(SecondScreenViewModel.ShowMediaPlay):
            case nameof(SecondScreenViewModel.IsVideoPlaying):
                UpdateViewerHints();
                break;
            case nameof(SecondScreenViewModel.CanClearSlot):
                UpdateDrawerFocus();
                break;
        }
    }

    private void OnOverlayChanged(SecondScreenOverlayKind previous, SecondScreenOverlayKind current)
    {
        if (current == SecondScreenOverlayKind.Drawer && previous != SecondScreenOverlayKind.Drawer)
            DrawerIndex = 0;
        if (IsGameRunning && current == SecondScreenOverlayKind.None)
        {
            // The viewer or All apps closed over the running game: the panel follows the viewer to the photo it was on,
            // and the set the panel shows is read again (closing either let the controller's achievements view go).
            if (previous == SecondScreenOverlayKind.Media && Model.MediaItems.Count > 0)
            {
                Tab = HandheldCompanionTab.Photos;
                ShowPhoto(Math.Min(Model.MediaIndex, Model.MediaItems.Count - 1));
            }
            // After the close has finished: the model tells the controller it closed only after this change.
            if (previous != SecondScreenOverlayKind.Achievements)
                Dispatcher.UIThread.Post(() =>
                {
                    if (!_disposed && IsGameRunning && Model.Overlay == SecondScreenOverlayKind.None)
                        Model.RequestAchievements();
                });
            Wake();
        }
        else if (IsGameRunning && current is SecondScreenOverlayKind.Drawer or SecondScreenOverlayKind.Media)
        {
            StopRestTimer();
            IsResting = false;
        }
        RaiseLayerChanged();
        OnPropertyChanged(nameof(OwnsPad));
        UpdateFocus();
    }

    private void RaiseLayerChanged()
    {
        OnPropertyChanged(nameof(Layer));
        OnPropertyChanged(nameof(HasLayer));
        OnPropertyChanged(nameof(IsAchievementsLayer));
        OnPropertyChanged(nameof(IsPhotosLayer));
        OnPropertyChanged(nameof(IsDrawerLayer));
    }

    // The controller refills the list one app at a time (Clear, then Add each): follow it the same way.
    private void OnAppsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { } added && e.NewStartingIndex == DrawerApps.Count)
        {
            foreach (SecondScreenAppViewModel app in added)
                DrawerApps.Add(new HandheldDrawerAppViewModel(app));
            UpdateDrawerFocus();
            return;
        }
        RebuildDrawer();
    }

    private void RebuildDrawer()
    {
        DrawerApps.Clear();
        foreach (var app in Model.Apps)
            DrawerApps.Add(new HandheldDrawerAppViewModel(app));
        UpdateDrawerFocus();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Model.PropertyChanged -= OnModelChanged;
        Model.Apps.CollectionChanged -= OnAppsChanged;
        StopRestTimer();
        _coverLoad?.Cancel();
        _photoLoad?.Cancel();
        NowPlayingCover = null;
        PanelPhoto = null;
    }
}
