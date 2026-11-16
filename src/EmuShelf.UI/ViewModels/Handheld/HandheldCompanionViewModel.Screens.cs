using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Core.SecondScreen;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The companion's three layers in the Big Type look (M47 B15; the owner's picks from proposals-screens.html, 16 November 2026): All apps
/// as the Shelf (option 2), every achievement as the Wall (option 4) and the photo viewer as the Reel (option 9). Each keeps everything
/// today's layer does, on the same commands: this adds only what the new drawing reads (the focused app's name and place, the figures, the
/// lines, the side a press moved to, the pictures either side of the one shown) and the hints as touch buttons. Today's look draws its
/// own layers, untouched.
/// </summary>
public sealed partial class HandheldCompanionViewModel
{
    private CancellationTokenSource? _reelLoad;
    private long _lastScreenPress;

    // ---- Which drawing ----

    /// <summary>Today's badge grid, All apps and viewer (Classic only; Big Type draws its own).</summary>
    public bool ShowsClassicGrid => IsAchievementsLayer && HandheldTokens.IsClassic;

    public bool ShowsClassicDrawer => IsDrawerLayer && HandheldTokens.IsClassic;

    public bool ShowsClassicViewer => IsPhotosLayer && HandheldTokens.IsClassic;

    /// <summary>The Launcher's colour while browsing (the shell's), the ground All apps sits on when opened from home's dock.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LayerGround))]
    public partial IBrush? BrowsingGround { get; set; }

    /// <summary>A Big Type layer's ground: the in-game colour while a game runs, the Launcher's colour over the Launcher.</summary>
    public IBrush? LayerGround => IsGameRunning ? InGameBrush ?? BrowsingGround : BrowsingGround;

    partial void OnInGameBrushChanged(IBrush? value) => OnPropertyChanged(nameof(LayerGround));

    /// <summary>When the layer in front last changed (a <see cref="Stopwatch"/> timestamp): the companion's parts drop out under a layer.</summary>
    [ObservableProperty]
    public partial long LayerChangedSince { get; private set; }

    /// <summary>Where the last press's change comes from (the page's <c>dirOf</c>: right from the right, down from below).</summary>
    [ObservableProperty]
    public partial BigTypeScreenSide ScreenSide { get; private set; } = BigTypeScreenSide.Up;

    /// <summary>The last press came within a run of the one before (M47 B11's <c>LetterRun</c>): the next name comes in its short form.</summary>
    [ObservableProperty]
    public partial bool ScreenQuick { get; private set; }

    private void InitScreens()
    {
        Model.AchievementRows.CollectionChanged += OnAchievementRowsChanged;
        RebuildWall();
    }

    private void DisposeScreens()
    {
        Model.AchievementRows.CollectionChanged -= OnAchievementRowsChanged;
        _reelLoad?.Cancel();
        ReleaseReel();
    }

    // Every press reaching a layer: which way it moved, and whether it came within a run.
    private void NoteScreenPress(GamepadAction action)
    {
        var now = Stopwatch.GetTimestamp();
        ScreenQuick = _lastScreenPress != 0 &&
            BigTypeMotion.InRun((float)Stopwatch.GetElapsedTime(_lastScreenPress, now).TotalMilliseconds);
        _lastScreenPress = now;
        ScreenSide = action switch
        {
            GamepadAction.NavigateRight => BigTypeScreenSide.Right,
            GamepadAction.NavigateLeft => BigTypeScreenSide.Left,
            GamepadAction.NavigateUp => BigTypeScreenSide.Down,
            _ => BigTypeScreenSide.Up,
        };
    }

    // A tap or a new layer: it rises, never a short form.
    private void NoteScreenTouch()
    {
        ScreenQuick = false;
        ScreenSide = BigTypeScreenSide.Up;
    }

    // Every badge over the running game opens and closes on IsGridOpen, which RaiseLayerChanged does not see.
    partial void OnIsGridOpenChanged(bool value) => RaiseScreensChanged();

    private void RaiseScreensChanged()
    {
        if (HandheldTokens.IsBigType)
        {
            LayerChangedSince = Stopwatch.GetTimestamp();
            ScreenQuick = false;
            ScreenSide = BigTypeScreenSide.Up;
            // The companion's parts rise back in when a layer over the running game closes (the page's C.show(true)).
            if (IsGameRunning && !HasLayer && ArrivedSince != 0)
                ArrivedSince = Stopwatch.GetTimestamp();
        }
        OnPropertyChanged(nameof(ShowsClassicGrid));
        OnPropertyChanged(nameof(ShowsClassicDrawer));
        OnPropertyChanged(nameof(ShowsClassicViewer));
        OnPropertyChanged(nameof(LayerGround));
        OnPropertyChanged(nameof(ScreenHints));
        RaiseShelf();
        RaiseWall();
        if (IsPhotosLayer && HandheldTokens.IsBigType)
            BuildReel();
        RaiseReel();
    }

    // ---- The hints on the bottom screen, each a touch button ----

    private static readonly ObservableCollection<HandheldHint> NoHints = [];

    /// <summary>
    /// The layer's hints at the foot of the bottom screen: All apps' (none when it was opened from home's dock: they are on the Stage then),
    /// every badge's, the viewer's.
    /// </summary>
    public ObservableCollection<HandheldHint> ScreenHints => Layer switch
    {
        HandheldCompanionLayer.Drawer when IsGameRunning => DrawerHints,
        HandheldCompanionLayer.Achievements => GridHints,
        HandheldCompanionLayer.Photos => ViewerHints,
        _ => NoHints,
    };

    /// <summary>A tap on a hint: the press it names (every hint is also a touch button).</summary>
    public void TapHint(HandheldHint hint)
    {
        GamepadAction? action = hint.Button switch
        {
            "A" => GamepadAction.Confirm,
            "B" => GamepadAction.Cancel,
            "Y" => GamepadAction.Actions,
            "LB" => GamepadAction.PreviousPlatform,
            "RB" => GamepadAction.NextPlatform,
            _ => null,
        };
        if (action is { } press)
        {
            Dispatch(press);
            NoteScreenTouch();
        }
    }

    // ---- 2 Shelf (All apps) ----

    /// <summary>The line over the big name: what a pin did, else the title ("All apps", "Choose an app for slot 2").</summary>
    public string ShelfKicker => DrawerNotice is { Length: > 0 } notice ? notice : Model.DrawerTitle;

    /// <summary>The app the ring is on.</summary>
    public HandheldDrawerAppViewModel? FocusedDrawerApp => DrawerIndex < DrawerApps.Count ? DrawerApps[DrawerIndex] : null;

    /// <summary>The focused app's name: the big name.</summary>
    public string ShelfName => FocusedDrawerApp?.App.Label ?? string.Empty;

    /// <summary>"7 / 18".</summary>
    public string ShelfCount => DrawerApps.Count == 0 ? string.Empty
        : string.Create(CultureInfo.CurrentCulture, $"{DrawerIndex + 1} / {DrawerApps.Count}");

    /// <summary>Where the focused app is in the dock ("In the dock, slot 3"), or that it is not (the Stage's line when opened from home).</summary>
    public string ShelfPlace
    {
        get
        {
            if (FocusedDrawerApp is not { } app)
                return string.Empty;
            var slot = Model.Dock.FirstOrDefault(candidate => !candidate.IsEmpty && candidate.Label == app.App.Label);
            return slot is null ? "Not in the dock" : string.Create(CultureInfo.CurrentCulture, $"In the dock, slot {slot.Index + 1}");
        }
    }

    /// <summary>The Stage's second line when opened from home: "7 of 18 · everything on this device".</summary>
    public string ShelfFacts => DrawerApps.Count == 0 ? string.Empty
        : string.Create(CultureInfo.CurrentCulture, $"{DrawerIndex + 1} of {DrawerApps.Count}  ·  everything on this device");

    /// <summary>All apps changed what the Stage says when it was opened from home (the shell follows it).</summary>
    public event Action? ShelfChanged;

    partial void OnDrawerIndexChanged(int value) => RaiseShelf();

    partial void OnDrawerNoticeChanged(string? value) => RaiseShelf();

    private void RaiseShelf()
    {
        OnPropertyChanged(nameof(ShelfKicker));
        OnPropertyChanged(nameof(FocusedDrawerApp));
        OnPropertyChanged(nameof(ShelfName));
        OnPropertyChanged(nameof(ShelfCount));
        OnPropertyChanged(nameof(ShelfPlace));
        OnPropertyChanged(nameof(ShelfFacts));
        ShelfChanged?.Invoke();
    }

    // ---- 4 Wall (every achievement) ----

    /// <summary>Every badge under the filter, in order (the rows the model lays out, flattened): the wall draws the rows it shows.</summary>
    public IReadOnlyList<AchievementRowViewModel> WallItems { get; private set; } = [];

    /// <summary>The line over the figure: what a refresh is doing or why it could not, else "Achievements · 60 pts".</summary>
    public string WallKicker
    {
        get
        {
            if (Model.HasInlineStatus && Model.AchievementsStatus is { Length: > 0 } status)
                return status;
            var rows = Model.Achievements;
            if (rows.Count == 0 || !rows.Any(row => row.HasPoints))
                return "Achievements";
            var earned = rows.Where(row => row.IsUnlocked).Sum(row => row.Points);
            return string.Create(CultureInfo.CurrentCulture, $"Achievements  ·  {earned:N0} pts");
        }
    }

    /// <summary>The focused badge's name (or "Hidden achievement"); with no set, what the set is doing; under an empty filter, "Nothing here yet".</summary>
    public string WallName => !Model.HasAchievements ? Model.AchievementsStatus is { Length: > 0 } status ? status : AchievementsMessage
        : IsFilterEmpty ? "Nothing here yet"
        : Model.SelectedAchievement?.Title ?? string.Empty;

    /// <summary>What it asks (or that A reveals it), or the empty filter's sentence.</summary>
    public string WallDescription => !Model.HasAchievements ? string.Empty
        : IsFilterEmpty ? FilterEmptyText
        : Model.SelectedAchievement?.Description ?? string.Empty;

    /// <summary>"25 pts · Earned 3 Nov".</summary>
    public string WallMeta => Model.HasAchievements && !IsFilterEmpty ? Model.SelectedAchievementMeta ?? string.Empty : string.Empty;

    private void OnAchievementRowsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildWall();

    private void RebuildWall()
    {
        WallItems = Model.AchievementRows.SelectMany(row => row).ToList();
        OnPropertyChanged(nameof(WallItems));
        RaiseWall();
    }

    private void RaiseWall()
    {
        OnPropertyChanged(nameof(WallKicker));
        OnPropertyChanged(nameof(WallName));
        OnPropertyChanged(nameof(WallDescription));
        OnPropertyChanged(nameof(WallMeta));
    }

    // ---- 9 Reel (the photo viewer) ----

    /// <summary>Every picture of the game, in the viewer's order; the one shown and one either side are decoded, the rest let go.</summary>
    public ObservableCollection<HandheldCompanionShotViewModel> ReelPictures { get; } = [];

    /// <summary>The muted video plays over the Reel's picture (Big Type's viewer; today's viewer has its own).</summary>
    public bool ShowsBigTypeVideo => HandheldTokens.IsBigType && IsPhotosLayer && Model.IsVideoPlaying;

    /// <summary>"2 / 6".</summary>
    public string ReelFigure => Model.MediaItems.Count == 0 ? string.Empty
        : string.Create(CultureInfo.CurrentCulture, $"{Math.Min(Model.MediaIndex, Model.MediaItems.Count - 1) + 1} / {Model.MediaItems.Count}");

    /// <summary>What kind of picture ("Screenshot", "Gameplay video"), "Playing muted", or the viewer's status ("Loading…", what a fetch found).</summary>
    public string ReelKind
    {
        get
        {
            if (Model.MediaStatus is { Length: > 0 } status)
                return status;
            var items = Model.MediaItems;
            if (items.Count == 0)
                return "No photos of this game yet";
            return Model.IsVideoPlaying ? "Playing muted" : items[Math.Min(Model.MediaIndex, items.Count - 1)].Title;
        }
    }

    /// <summary>The game's title, under the kind.</summary>
    public string ReelTitle => Model.MediaTitle;

    /// <summary>
    /// Where the picture shown grows out of on the Photos tab (and goes back into), as an index into the tab's six; the first when the
    /// picture is not among them.
    /// </summary>
    public int ReelThumbIndex => Model.MediaIndex < Shots.Count ? Model.MediaIndex : 0;

    private void RaiseReel()
    {
        OnPropertyChanged(nameof(ShowsBigTypeVideo));
        OnPropertyChanged(nameof(ReelFigure));
        OnPropertyChanged(nameof(ReelKind));
        OnPropertyChanged(nameof(ReelTitle));
        OnPropertyChanged(nameof(ReelThumbIndex));
    }

    // Called on the model's changes (OnModelChanged): the reel follows the viewer's place, its status and its video.
    private void FollowReel(string? property)
    {
        switch (property)
        {
            case nameof(SecondScreenViewModel.MediaItems):
                if (IsPhotosLayer && HandheldTokens.IsBigType)
                    BuildReel();
                RaiseReel();
                break;
            case nameof(SecondScreenViewModel.MediaIndex):
                if (IsPhotosLayer && HandheldTokens.IsBigType)
                    _ = LoadReelAsync();
                RaiseReel();
                break;
            case nameof(SecondScreenViewModel.MediaStatus):
            case nameof(SecondScreenViewModel.IsVideoPlaying):
            case nameof(SecondScreenViewModel.MediaTitle):
                RaiseReel();
                break;
            case nameof(SecondScreenViewModel.HasInlineStatus):
            case nameof(SecondScreenViewModel.AchievementsStatus):
            case nameof(SecondScreenViewModel.SelectedAchievement):
            case nameof(SecondScreenViewModel.AchievementCount):
            case nameof(SecondScreenViewModel.UnlockedAchievementCount):
            case nameof(SecondScreenViewModel.AchievementFilter):
            case nameof(SecondScreenViewModel.HasVisibleAchievements):
                RaiseWall();
                break;
            case nameof(SecondScreenViewModel.DrawerTitle):
                RaiseShelf();
                break;
        }
    }

    private void BuildReel()
    {
        var items = Model.MediaItems;
        if (ReelPictures.Count != items.Count || ReelPictures.Select((picture, i) => picture.IsVideo != items[i].IsVideo).Any(changed => changed))
        {
            ReleaseReel();
            for (var i = 0; i < items.Count; i++)
                ReelPictures.Add(new HandheldCompanionShotViewModel(i, items[i].IsVideo));
        }
        _ = LoadReelAsync();
    }

    private void ReleaseReel()
    {
        foreach (var picture in ReelPictures)
            picture.Image?.Dispose();
        ReelPictures.Clear();
    }

    /// <summary>
    /// The picture shown and one either side, decoded at the reel's size off the UI thread (a video shows the first screenshot as its
    /// poster, as the viewer does; the video itself still downloads only when played). Pictures further away are let go.
    /// </summary>
    private async Task LoadReelAsync()
    {
        _reelLoad?.Cancel();
        _reelLoad?.Dispose();
        var load = _reelLoad = new CancellationTokenSource();
        var items = Model.MediaItems;
        if (items.Count == 0 || ReelPictures.Count != items.Count)
            return;
        var at = Math.Min(Model.MediaIndex, items.Count - 1);
        int Distance(int index)
        {
            var d = Math.Abs(index - at);
            return Math.Min(d, items.Count - d);
        }
        foreach (var picture in ReelPictures)
        {
            if (Distance(picture.Index) > 2 && picture.Image is { } far)
            {
                picture.Image = null;
                far.Dispose();
            }
        }
        var poster = items.Select((item, index) => (item, index)).FirstOrDefault(pair => !pair.item.IsVideo);
        foreach (var picture in ReelPictures.OrderBy(picture => Distance(picture.Index)).Where(picture => Distance(picture.Index) <= 1).ToArray())
        {
            if (picture.Image is not null)
                continue;
            var item = picture.IsVideo ? poster.item : items[picture.Index];
            if (item is null || item.IsVideo)
                continue;
            try
            {
                var path = Model.ResolveMediaPath is { } resolve ? await resolve(item, load.Token) : item.LocalPath;
                if (load.IsCancellationRequested || path is null)
                    return;
                var bitmap = await Task.Run(() => SafeImageDecoder.DecodeToFit(path, (int)HandheldTokens.ReelWidth, (int)HandheldTokens.ReelHeight), load.Token);
                if (load.IsCancellationRequested || _disposed || !ReelPictures.Contains(picture) || picture.Image is not null)
                {
                    bitmap.Dispose();
                    if (load.IsCancellationRequested || _disposed)
                        return;
                    continue;
                }
                picture.Image = bitmap;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // An unreadable picture leaves its place on the ground.
            }
        }
    }
}
