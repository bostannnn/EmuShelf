using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Feeds the native Launcher (M45 H4, option 5) from the Avalonia Launcher, which stays laid out but
/// invisible and is only pictured. Every tile, chip and dock button is drawn once when what it shows changes
/// (padded wrappers keep their shadows in the picture) and handed over with where it rests; the native side
/// does the lift, the tracks, the selector, the bump and the folder's zoom. The Title page and the menu
/// still draw on the Avalonia Launcher, as do the actions sheet, naming and move mode: the feed hands the screen back while one is open.
/// </summary>
internal sealed class HandheldNativeLauncherFeed
{
    private const float PageWidth = 1240;
    private const float ColumnPitch = 264;
    private const float DotPitch = 30;
    private const float DotsShift = -17;
    private const double GlowBlur = 28;

    private readonly Control _view;
    private readonly HandheldLauncherMotion _motion;
    private readonly IHandheldNativeStage _host;
    private readonly Panel _root;
    private readonly Control _canvas;
    private readonly Control _homeClip;
    private readonly ItemsControl _pageTrack;
    private readonly Control _folderPage;
    private readonly ItemsControl _folderTiles;
    private readonly Control _backShot;
    private readonly Control _sortShot;
    private readonly Control _folderTitle;
    private readonly Control _folderEmpty;
    private readonly ItemsControl _pageDots;
    private readonly Control _pageIndicator;
    private readonly Control _edgeLeft;
    private readonly Control _edgeRight;
    private readonly Control _dockShadow;
    private readonly Control _dockPill;
    private readonly ItemsControl _dockItems;
    private readonly Control _titleBody;
    private readonly ItemsControl _titleButtons;
    private readonly Control _optionSheet;
    private readonly Control _screenSheet;
    private readonly Control[] _screenShots;
    private readonly Control _launchCover;
    private readonly Control _launchText;
    private readonly Control _launchProgress;
    private HandheldTitlePageViewModel? _title;
    private bool _launchPending;

    private readonly Dictionary<string, string> _sent = new(StringComparer.Ordinal);
    private readonly Dictionary<Control, (string Id, LauncherTrack Track, float ColumnShift)> _anchors = [];
    private readonly Dictionary<(int, int, int), StageImage> _glows = [];
    private readonly HashSet<INotifyPropertyChanged> _watched = [];
    private StageImage? _liftShadow;
    private readonly Dictionary<string, FolderArtState> _folderStates = new(StringComparer.Ordinal);
    private LauncherFocusCommand? _focusSent;
    private (int Page, int Column)? _tracksSent;
    private HandheldShellViewModel? _shell;
    private HandheldFolderViewModel? _folder;
    private bool _active;
    private bool _failed;
    private bool _syncPending;

    public HandheldNativeLauncherFeed(Control view, HandheldLauncherMotion motion, IHandheldNativeStage host)
    {
        _view = view;
        _motion = motion;
        _host = host;
        _root = (Panel)Find("Root");
        _canvas = Find("Canvas");
        _homeClip = Find("HomeClip");
        _pageTrack = (ItemsControl)Find("PageTrack");
        _folderPage = Find("FolderPage");
        _folderTiles = (ItemsControl)Find("FolderTiles");
        _backShot = Find("BackShot");
        _sortShot = Find("SortShot");
        _folderTitle = Find("FolderTitle");
        _folderEmpty = Find("FolderEmpty");
        _pageDots = (ItemsControl)Find("PageDots");
        _pageIndicator = Find("PageIndicator");
        _edgeLeft = Find("EdgeLeft");
        _edgeRight = Find("EdgeRight");
        _dockShadow = Find("DockShadow");
        _dockPill = Find("DockPill");
        _dockItems = (ItemsControl)Find("DockItems");
        _titleBody = Find("TitleBody");
        _titleButtons = (ItemsControl)Find("TitleButtons");
        _optionSheet = Find("OptionSheet");
        _screenSheet = Find("ScreenSheet");
        _screenShots = [Find("ScreenTopShot"), Find("ScreenBottomShot"), Find("RememberShot")];
        _launchCover = Find("LaunchCoverShot");
        _launchText = Find("LaunchText");
        _launchProgress = Find("LaunchProgress");
        view.LayoutUpdated += (_, _) => Schedule();
        host.Failed += () =>
        {
            _failed = true;
            UpdateActive();
        };
    }

    private Control Find(string name) =>
        _view.FindControl<Control>(name) ?? throw new InvalidOperationException($"The Launcher has no {name}.");

    public void Attach(HandheldShellViewModel? shell)
    {
        if (ReferenceEquals(shell, _shell))
            return;
        if (_shell is { } old)
        {
            old.PropertyChanged -= OnShellChanged;
            old.Bumped -= OnBumped;
            old.FolderClosing -= OnFolderClosing;
            old.LaunchStarting -= OnLaunchStarting;
        }
        _shell = shell;
        WatchFolder(shell?.OpenFolder);
        if (shell is null)
            return;
        shell.PropertyChanged += OnShellChanged;
        shell.Bumped += OnBumped;
        shell.FolderClosing += OnFolderClosing;
        shell.LaunchStarting += OnLaunchStarting;
        UpdateActive();
    }

    /// <summary>The view's selector host passes focus changes on; the selector and the lift follow them.</summary>
    public void OnFocusChanged() => Schedule();

    // ---- Which Launcher draws ----

    private void UpdateActive()
    {
        // The menu, a sheet, naming and move mode draw on the Avalonia Launcher, which takes the screen back meanwhile.
        var active = !_failed && _shell is { IsLauncherOverlayOpen: false };
        if (active == _active)
            return;
        _active = active;
        _motion.IsOnScreen = !active;
        _view.Opacity = active ? 0 : 1;
        _root.Classes.Set("native", active);
        _host.SetActive(active);
        if (active)
        {
            // The native side kept every picture and state while the Avalonia Launcher had the screen (the host
            // retains and replays them), and anything that changed meanwhile has a new key, so only that is pictured
            // again. Re-picturing every tile here cost ~0.3 s of CPU each time a sheet or naming closed (M45 H4 polish).
            // Focus and tracks are re-sent: they are small and the selector may have moved.
            _focusSent = null;
            _tracksSent = null;
            Schedule();
        }
    }

    // ---- Changes ----

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HandheldShellViewModel.IsLauncherOverlayOpen):
                UpdateActive();
                break;
            case nameof(HandheldShellViewModel.TitlePage):
                WatchTitle(_shell?.TitlePage);
                Schedule();
                break;
            case nameof(HandheldShellViewModel.OpenFolder):
                var folder = _shell?.OpenFolder;
                if (folder is not null && !ReferenceEquals(folder, _folder))
                    OnFolderOpening(folder);
                WatchFolder(folder);
                Schedule();
                break;
            default:
                Schedule();
                break;
        }
    }

    private void WatchFolder(HandheldFolderViewModel? folder)
    {
        if (ReferenceEquals(folder, _folder))
            return;
        if (_folder is { } old)
        {
            old.PropertyChanged -= OnWatchedChanged;
            old.VisibleTiles.CollectionChanged -= OnCollectionChanged;
            // The closed folder's tiles are not watched any longer.
            foreach (var tile in _watched.OfType<HandheldGameTileViewModel>().ToList())
            {
                tile.PropertyChanged -= OnWatchedChanged;
                _watched.Remove(tile);
            }
        }
        _folder = folder;
        if (folder is null)
            return;
        folder.PropertyChanged += OnWatchedChanged;
        folder.VisibleTiles.CollectionChanged += OnCollectionChanged;
    }

    private void OnWatchedChanged(object? sender, PropertyChangedEventArgs e) => Schedule();

    private void WatchTitle(HandheldTitlePageViewModel? title)
    {
        if (ReferenceEquals(title, _title))
            return;
        if (_title is { } old)
        {
            old.PropertyChanged -= OnWatchedChanged;
            old.Buttons.CollectionChanged -= OnCollectionChanged;
            old.Options.CollectionChanged -= OnCollectionChanged;
        }
        _title = title;
        if (title is null)
            return;
        title.PropertyChanged += OnWatchedChanged;
        title.Buttons.CollectionChanged += OnCollectionChanged;
        title.Options.CollectionChanged += OnCollectionChanged;
    }

    // The cover lifts off once its picture is on the native side (the next sync sends it, then the lift).
    private void OnLaunchStarting(HandheldTitlePageViewModel page)
    {
        _launchPending = true;
        Schedule();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Schedule();

    private void OnBumped(HandheldDirection direction)
    {
        if (!_active)
            return;
        var (x, y) = direction switch
        {
            HandheldDirection.Left => (-1, 0),
            HandheldDirection.Right => (1, 0),
            HandheldDirection.Up => (0, -1),
            _ => (0, 1),
        };
        _host.Post(new LauncherBumpCommand(x, y));
    }

    private void OnFolderOpening(HandheldFolderViewModel folder)
    {
        if (!_active || _shell?.CurrentPage is not { } page)
            return;
        var (id, origin) = HomeTile(page.Index, folder.HomeSlot);
        _host.Post(new LauncherZoomCommand(true, id, (float)origin.X, (float)origin.Y));
        _host.Post(new LauncherFolderCommand(true));
    }

    private void OnFolderClosing(HandheldFolderViewModel folder)
    {
        if (!_active || _shell?.CurrentPage is not { } page)
            return;
        var (id, origin) = HomeTile(page.Index, folder.HomeSlot);
        _host.Post(new LauncherZoomCommand(false, id, (float)origin.X, (float)origin.Y));
        _host.Post(new LauncherFolderCommand(false));
    }

    /// <summary>A home tile's sprite and its centre on screen (its page scrolled into view).</summary>
    private (string Id, Point Centre) HomeTile(int page, int slot)
    {
        var id = $"h:{page}:{slot}";
        if (SlotContainer(page, slot) is { } wrapper && HandheldStageShots.ToCanvas(wrapper, new Point(wrapper.Bounds.Width / 2, wrapper.Bounds.Height / 2), _canvas) is { } centre)
            return (id, new Point(centre.X - (page * PageWidth), centre.Y));
        return (id, new Point(PageWidth / 2, 400));
    }

    private Control? SlotContainer(int page, int slot)
    {
        if (_pageTrack.ContainerFromIndex(page) is not { } pageContainer)
            return null;
        var slots = pageContainer.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault();
        return (slots?.ContainerFromIndex(slot) as ContentPresenter)?.Child as Control;
    }

    // ---- Keeping the native side in step ----

    private void Schedule()
    {
        if (!_active || _syncPending)
            return;
        _syncPending = true;
        Dispatcher.UIThread.Post(Sync, DispatcherPriority.Loaded);
    }

    private void Sync()
    {
        _syncPending = false;
        if (!_active || _shell is not { } shell)
            return;
        var present = new HashSet<string>(StringComparer.Ordinal);
        _anchors.Clear();

        SendLayout();
        Static("bg", "1", present, () => Background());
        Static("dock-shadow", "1", present, () => Sprite("dock-shadow", HandheldStageShots.Shot(_dockShadow, _canvas), LauncherGroup.Dock));
        Static("dock-pill", "1", present, () =>
        {
            // The pill without its buttons (they are sprites of their own).
            _dockItems.IsVisible = false;
            try
            {
                return Sprite("dock-pill", HandheldStageShots.Shot(_dockPill, _canvas), LauncherGroup.Dock);
            }
            finally
            {
                _dockItems.IsVisible = true;
            }
        });

        SyncHome(shell, present);
        SyncDots(shell, present);
        SyncFolder(shell, present);
        SyncDock(present);
        SyncTitle(shell, present);

        foreach (var gone in _sent.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _sent.Remove(gone);
            _host.Post(new LauncherSpriteCommand(gone, null));
        }

        var tracks = (shell.CurrentPage?.Index ?? 0, shell.OpenFolder?.FirstVisibleColumn ?? 0);
        if (_tracksSent != tracks)
        {
            var columnJump = _tracksSent is { } previous && Math.Abs(previous.Column - tracks.Item2) > 1;
            _host.Post(new LauncherTracksCommand(tracks.Item1, tracks.Item2, SnapPage: _tracksSent is null,
                SnapColumn: _tracksSent is null || columnJump || shell.OpenFolder?.IsResorting == true));
            _tracksSent = tracks;
        }
        SyncFocus();
        if (_launchPending && present.Contains("t-cover"))
        {
            _launchPending = false;
            _host.Post(new LauncherLaunchCommand("t-cover"));
        }
    }

    private bool _layoutSent;

    private void SendLayout()
    {
        if (_layoutSent)
            return;
        var home = Rect(_homeClip);
        var folder = Rect(_folderPage);
        if (home is null || folder is null)
            return;
        _host.Post(new LauncherLayoutCommand(1240, 1080, home.Value, folder.Value, PageWidth, ColumnPitch, DotPitch, DotsShift));
        _layoutSent = true;
    }

    private StageRect? Rect(Control control) =>
        control.Bounds.Width > 0 && HandheldStageShots.ToCanvas(control, default(Point), _canvas) is { } at
            ? new StageRect((float)at.X, (float)at.Y, (float)control.Bounds.Width, (float)control.Bounds.Height)
            : null;

    private void Static(string id, string key, HashSet<string> present, Func<LauncherSprite?> make)
    {
        present.Add(id);
        if (_sent.TryGetValue(id, out var sent) && sent == key)
            return;
        if (make() is not { } sprite)
        {
            present.Remove(id);
            return;
        }
        _sent[id] = key;
        _host.Post(new LauncherSpriteCommand(id, sprite));
    }

    private static LauncherSprite? Sprite(string id, StageImage? image, LauncherGroup group, LauncherTrack track = LauncherTrack.None, int z = 0) =>
        image is null ? null : new LauncherSprite(id, image, group, track, Liftable: false, 0, 0, Z: z);

    private LauncherSprite Background()
    {
        var plain = new Border
        {
            Width = 1240,
            Height = 1080,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#FAFBFD"), 0), new GradientStop(Color.Parse("#E4E8EF"), 1) },
            },
        };
        return new LauncherSprite("bg", HandheldStageShots.Picture(plain, 0, 0, 1240, 1080), LauncherGroup.Background,
            LauncherTrack.None, false, 0, 0);
    }

    // A liftable piece: its wrapper is pictured; it lifts about its focusable panel's centre.
    private void Liftable(string id, string key, Control wrapper, LauncherGroup group, LauncherTrack track, float columnShift,
        HashSet<string> present, StageImage? liftShadow = null, HandheldFolderArt? folderArt = null, int z = 0)
    {
        if (wrapper is not Decorator { Child: Control panel } || !wrapper.IsVisible)
            return;
        present.Add(id);
        _anchors[panel] = (id, track, columnShift);
        foreach (var art in wrapper.GetVisualDescendants().OfType<HandheldFolderArt>())
            art.Classes.Set("frozen", true);
        if (_sent.TryGetValue(id, out var sent) && sent == key)
            return;
        // A folder's art is sent as parts that glide (its flap peeks on focus): the tile is pictured without it.
        FolderArt? parts = null;
        StageImage? image;
        if (folderArt is not null)
        {
            parts = HandheldFolderArtShots.Capture(folderArt, _canvas);
            folderArt.IsVisible = false;
            try
            {
                image = HandheldStageShots.Shot(wrapper, _canvas);
            }
            finally
            {
                folderArt.IsVisible = true;
            }
        }
        else
        {
            image = HandheldStageShots.Shot(wrapper, _canvas);
        }
        if (image is null || HandheldStageShots.ToCanvas(panel, new Point(panel.Bounds.Width / 2, panel.Bounds.Height / 2), _canvas) is not { } centre)
        {
            present.Remove(id);
            return;
        }
        image = image with { X = image.X + columnShift };
        var shadow = liftShadow is null || HandheldStageShots.ToCanvas(panel, default(Point), _canvas) is not { } topLeft
            ? null
            : liftShadow with { X = (float)topLeft.X + columnShift - 32, Y = (float)topLeft.Y - 16 };
        _sent[id] = key;
        _host.Post(new LauncherSpriteCommand(id, new LauncherSprite(id, image, group, track, Liftable: true,
            (float)centre.X + columnShift, (float)centre.Y, shadow, parts, z)));
    }

    private void SyncHome(HandheldShellViewModel shell, HashSet<string> present)
    {
        // Slots replaced by an edit or a move-mode step are not watched any longer.
        var current = shell.Pages.SelectMany(page => page.Slots).ToHashSet();
        foreach (var gone in _watched.OfType<HandheldHomeSlotViewModel>().Where(slot => !current.Contains(slot)).ToList())
        {
            gone.PropertyChanged -= OnWatchedChanged;
            _watched.Remove(gone);
        }
        foreach (var page in shell.Pages)
        {
            foreach (var slot in page.Slots)
            {
                Watch(slot);
                if (SlotContainer(page.Index, slot.Index) is not { } wrapper)
                    continue;
                var id = $"h:{page.Index}:{slot.Index}";
                var art = slot.IsFolder ? wrapper.GetVisualDescendants().OfType<HandheldFolderArt>().FirstOrDefault() : null;
                var key = $"{slot.PictureKey}|{Ref(slot.PeekA)}|{Ref(slot.PeekB)}|{Ref(slot.PeekC)}";
                Liftable(id, key, wrapper, LauncherGroup.Home, LauncherTrack.Page, 0, present, folderArt: art);
                if (art is null)
                    continue;
                // The flap peeks while the folder is focused and swings open with its folder.
                var open = shell.OpenFolder is { } folder && folder.HomeSlot == slot.Index && shell.CurrentPage?.Index == page.Index;
                var state = open ? FolderArtState.Open : slot.IsFocused ? FolderArtState.Peek : FolderArtState.Closed;
                var known = _folderStates.TryGetValue(id, out var sentState);
                if (!known || sentState != state)
                {
                    _folderStates[id] = state;
                    // A tile seen for the first time starts in its state; after that every change glides.
                    _host.Post(new LauncherFolderStateCommand(id, state, Animate: known));
                }
            }
        }
    }

    private void SyncDots(HandheldShellViewModel shell, HashSet<string> present)
    {
        // The edge hints fade with home, like the dots.
        if (shell.CanPageLeft)
            Static("edge-l", "1", present, () => Sprite("edge-l", HandheldStageShots.Shot(_edgeLeft, _canvas), LauncherGroup.Dots));
        if (shell.CanPageRight)
            Static("edge-r", "1", present, () => Sprite("edge-r", HandheldStageShots.Shot(_edgeRight, _canvas), LauncherGroup.Dots));
        if (!shell.HasMultiplePages)
            return;
        Static("dots", shell.PageCount.ToString(System.Globalization.CultureInfo.InvariantCulture), present,
            () => Sprite("dots", HandheldStageShots.Shot(_pageDots, _canvas), LauncherGroup.Dots));
        Static("dots-pill", "1", present, () => Sprite("dots-pill", HandheldStageShots.Shot(_pageIndicator, _canvas), LauncherGroup.Dots, LauncherTrack.Dots));
    }

    private void SyncFolder(HandheldShellViewModel shell, HashSet<string> present)
    {
        if (shell.OpenFolder is not { } folder || !_folderPage.IsVisible)
            return;
        Liftable("f-back", "1", _backShot, LauncherGroup.Folder, LauncherTrack.None, 0, present);
        Liftable("f-sort", folder.SortLabel, _sortShot, LauncherGroup.Folder, LauncherTrack.None, 0, present);
        Static("f-title", $"{folder.Name}|{folder.CountText}", present,
            () => Sprite("f-title", HandheldStageShots.Shot(_folderTitle, _canvas), LauncherGroup.Folder));
        if (folder.IsEmpty)
            Static("f-empty", "1", present, () => Sprite("f-empty", HandheldStageShots.Shot(_folderEmpty, _canvas), LauncherGroup.Folder));

        var shift = folder.WindowFirstColumn * ColumnPitch;
        _liftShadow ??= LiftShadow();
        for (var i = 0; i < folder.VisibleTiles.Count; i++)
        {
            var tile = folder.VisibleTiles[i];
            Watch(tile);
            if ((_folderTiles.ContainerFromIndex(i) as ContentPresenter)?.Child is not Control wrapper || wrapper.Bounds.Width <= 0)
                continue;
            var key = $"{Ref(tile.Cover)}|{tile.Title}|{tile.IsUnavailable}";
            Liftable($"f:{tile.Index}", key, wrapper, LauncherGroup.Folder, LauncherTrack.Column, shift, present, _liftShadow);
        }
    }

    private void SyncDock(HashSet<string> present)
    {
        if (_shell is not { } shell)
            return;
        for (var i = 0; i < shell.DockItems.Count; i++)
        {
            var item = shell.DockItems[i];
            if (item.Slot is { } slot)
                Watch(slot);
            if ((_dockItems.ContainerFromIndex(i) as ContentPresenter)?.Child is not Control wrapper)
                continue;
            var key = $"{item.Kind}|{Ref(item.Slot?.Icon)}|{item.Slot?.IsEmpty}";
            Liftable($"d:{i}", key, wrapper, LauncherGroup.Dock, LauncherTrack.None, 0, present);
        }
    }

    // ---- The Title page: drawn over everything while it is up ----

    private void SyncTitle(HandheldShellViewModel shell, HashSet<string> present)
    {
        if (shell.TitlePage is not { } page)
        {
            if (_sent.ContainsKey("t-bg"))
                _host.Post(new LauncherProgressCommand(null));
            return;
        }
        var accent = (page.AccentBrush as ISolidColorBrush)?.Color.ToString() ?? "-";
        Static("t-bg", accent, present, () => TitleBackground(page));
        if (page.IsBrowsable)
        {
            var body = string.Join("|", page.CrumbLine, page.Title, page.PlatformLine, page.LastPlayedText, page.PlaytimeText,
                page.ScreenText, page.DiscText, page.AchievementLine, page.AchievementRatio, Ref(page.Cover));
            Static("t-body", body, present, () =>
            {
                // The page without its buttons (they lift on their own).
                _titleButtons.IsVisible = false;
                try
                {
                    return Sprite("t-body", HandheldStageShots.Shot(_titleBody, _canvas), LauncherGroup.Title, z: 1);
                }
                finally
                {
                    _titleButtons.IsVisible = true;
                }
            });
            for (var i = 0; i < page.Buttons.Count; i++)
            {
                var button = page.Buttons[i];
                if ((_titleButtons.ContainerFromIndex(i) as ContentPresenter)?.Child is Control wrapper)
                    Liftable($"t-b:{i}", $"{button.Kind}|{button.Label}|{button.Note}", wrapper, LauncherGroup.Title, LauncherTrack.None, 0, present, z: 2);
            }
        }
        if (page.IsOptionList)
        {
            // The sheet's rows highlight in place (no selector): the focused row is part of the picture.
            foreach (var option in page.Options)
                Watch(option);
            var rows = string.Join("|", page.Options.Select(option => $"{option.Label}:{option.Note}:{option.IsFocused}"));
            Static("t-sheet", $"{page.OptionsHeading}|{rows}", present,
                () => Sprite("t-sheet", HandheldStageShots.Shot(_optionSheet, _canvas), LauncherGroup.Title, z: 3));
        }
        if (page.IsScreenChooser)
        {
            Static("t-screen", page.Title, present, () =>
            {
                foreach (var shot in _screenShots)
                    shot.IsVisible = false;
                try
                {
                    return Sprite("t-screen", HandheldStageShots.Shot(_screenSheet, _canvas), LauncherGroup.Title, z: 3);
                }
                finally
                {
                    foreach (var shot in _screenShots)
                        shot.IsVisible = true;
                }
            });
            Liftable("t-top", "1", _screenShots[0], LauncherGroup.Title, LauncherTrack.None, 0, present, z: 4);
            Liftable("t-bottom", "1", _screenShots[1], LauncherGroup.Title, LauncherTrack.None, 0, present, z: 4);
            Liftable("t-remember", $"{page.RememberScreen}|{page.RememberScreenLabel}", _screenShots[2], LauncherGroup.Title,
                LauncherTrack.None, 0, present, z: 4);
        }
        if (page.IsLaunching)
        {
            Static("t-cover", Ref(page.Cover), present, () => Sprite("t-cover", HandheldStageShots.Shot(_launchCover, _canvas), LauncherGroup.Title, z: 1));
            Static("t-launch-text", $"{page.LaunchingHeading}|{page.LaunchingDetail}", present,
                () => Sprite("t-launch-text", HandheldStageShots.Shot(_launchText, _canvas), LauncherGroup.Title, z: 1));
            if (!_sent.ContainsKey("progress") && Rect(_launchProgress) is { } track)
            {
                _sent["progress"] = "1";
                _host.Post(new LauncherProgressCommand(track));
            }
            present.Add("progress");
        }
        else if (_sent.Remove("progress"))
        {
            _host.Post(new LauncherProgressCommand(null));
        }
    }

    private LauncherSprite TitleBackground(HandheldTitlePageViewModel page)
    {
        var background = new Panel { Width = 1240, Height = 1080 };
        background.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#FAFBFD"), 0), new GradientStop(Color.Parse("#E4E8EF"), 1) },
            },
        });
        // The platform's colour as a soft wash from the top, as the Title layer draws it.
        background.Children.Add(new Border
        {
            Background = page.AccentBrush,
            Opacity = 0.14,
            OpacityMask = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#FF000000"), 0), new GradientStop(Color.Parse("#00000000"), 0.7) },
            },
        });
        return new LauncherSprite("t-bg", HandheldStageShots.Picture(background, 0, 0, 1240, 1080), LauncherGroup.Title,
            LauncherTrack.None, false, 0, 0, Z: 0);
    }

    private void SyncFocus()
    {
        LauncherFocusCommand focus;
        if (_motion.FocusedAnchor() is { } anchor && _anchors.TryGetValue(anchor, out var known))
        {
            focus = new LauncherFocusCommand(known.Id, Target(anchor, known.Track, known.ColumnShift));
        }
        else
        {
            focus = new LauncherFocusCommand(null, null);
        }
        if (_focusSent is { } sent && sent.SpriteId == focus.SpriteId && sent.Selector == focus.Selector)
            return;
        _focusSent = focus;
        _host.Post(focus);
    }

    /// <summary>The selector's frame for an element: its ring as it will be once lifted, at rest on its track.</summary>
    private LauncherSelectorTarget? Target(Control anchor, LauncherTrack track, float columnShift)
    {
        var ring = anchor.GetVisualChildren().OfType<Border>().FirstOrDefault(border => border.Classes.Contains("ring"));
        var local = ring?.Bounds ?? new Rect(anchor.Bounds.Size).Inflate(16);
        var lift = anchor.Classes.Contains("focusable") ? 1.13 : 1;
        var cx = anchor.Bounds.Width / 2;
        var cy = anchor.Bounds.Height / 2;
        var lifted = new Rect(cx + ((local.X - cx) * lift), cy + ((local.Y - cy) * lift), local.Width * lift, local.Height * lift);
        if (HandheldStageShots.ToCanvas(anchor, lifted.TopLeft, _canvas) is not { } at)
            return null;
        var radius = (ring?.CornerRadius.TopLeft ?? 32) * lift;
        return new LauncherSelectorTarget((float)at.X + columnShift, (float)at.Y, (float)lifted.Width, (float)lifted.Height,
            (float)radius, track, Glow(lifted.Width, lifted.Height, radius));
    }

    private StageImage Glow(double width, double height, double radius)
    {
        var key = ((int)Math.Round(width), (int)Math.Round(height), (int)Math.Round(radius));
        if (_glows.TryGetValue(key, out var glow))
            return glow;
        var shadow = new HandheldShadow
        {
            Blur = GlowBlur,
            Color = Color.Parse("#B05B58D9"),
            Radius = radius,
            Width = Math.Round(width) + (2 * GlowBlur),
            Height = Math.Round(height) + (2 * GlowBlur),
        };
        glow = HandheldStageShots.Picture(shadow, 0, 0, (float)shadow.Width, (float)shadow.Height);
        if (_glows.Count > 16)
            _glows.Clear();
        _glows[key] = glow;
        return glow;
    }

    /// <summary>A game tile's deeper shadow while lifted (the XAML's `lift` shadow, which never shows here).</summary>
    private static StageImage LiftShadow()
    {
        var shadow = new HandheldShadow { Radius = 56, Blur = 32, Color = Color.Parse("#33000000"), Width = 296, Height = 296, Filled = true };
        return HandheldStageShots.Picture(shadow, 0, 0, 296, 296);
    }

    private void Watch(INotifyPropertyChanged item)
    {
        if (_watched.Add(item))
            item.PropertyChanged += OnWatchedChanged;
    }

    private static string Ref(object? value) => value is null ? "-" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
