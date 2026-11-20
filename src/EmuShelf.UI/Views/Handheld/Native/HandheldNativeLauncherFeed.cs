using System.Diagnostics;
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
using EmuShelf.Rendering.Stage.BigType;

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
    private static float ColumnPitch => (float)HandheldTokens.FolderPitch;
    private static float DotPitch => (float)(HandheldTokens.PageDotWidth + HandheldTokens.PageDotGap);
    private static float DotsShift => (float)(-(HandheldTokens.PageIndicatorWidth - HandheldTokens.PageDotWidth) / 2);
    private static double GlowBlur => HandheldTokens.ClassicRingGlowBlur;

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
    private readonly Control _pageLabel;
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
    // Big Type's parts (M47 B3): the folder's name and count, the dock row's corners.
    private readonly Control _btBackShot;
    private readonly Control _btFolderCount;
    private readonly HandheldBigTypeHeading _btFolderName;
    private readonly Control _btSortShot;
    private readonly Control _btSwap;
    private readonly Control _btPage;
    private Avalonia.Media.Color? _groundSent;
    private HandheldTitlePageViewModel? _title;
    private bool _launchPending;

    private readonly Dictionary<string, string> _sent = new(StringComparer.Ordinal);
    // Only the visible home page keeps both appearances of a tile. A repicture invalidates their geometry.
    private readonly Dictionary<(string Id, bool Focused), (string Key, StageImage Image)> _homeFaces = [];
    private int? _facePage;
    private readonly Dictionary<Control, (string Id, LauncherTrack Track, float ColumnShift)> _anchors = [];
    private readonly Dictionary<(int, int, int), StageImage> _glows = [];
    private readonly HashSet<INotifyPropertyChanged> _watched = [];
    private StageImage? _liftShadow;
    private readonly Dictionary<string, FolderArtState> _folderStates = new(StringComparer.Ordinal);
    private LauncherFocusCommand? _focusSent;
    private LauncherMotionCommand? _motionSent;
    private (int Page, int Column)? _tracksSent;
    private HandheldShellViewModel? _shell;
    private HandheldFolderViewModel? _folder;
    private bool _active;
    private bool _failed;
    private bool _syncPending;

    // The feeds alive, for the Stage's wipe (M47 B6), which grows from the tile the native Launcher has focused.
    private static readonly List<WeakReference<HandheldNativeLauncherFeed>> Feeds = [];

    /// <summary>
    /// Where the native Launcher drawing <paramref name="shell"/> has its focus (M47 B6): the focused element's middle across the
    /// Launcher's canvas as it is on screen at rest, and that canvas's width; null when no native Launcher draws or nothing is focused.
    /// Read when the Stage's backdrop changes, so it is worked out from the views now rather than from the last sync.
    /// </summary>
    public static (float X, float CanvasWidth)? FocusX(HandheldShellViewModel shell)
    {
        foreach (var weak in Feeds)
        {
            if (weak.TryGetTarget(out var feed) && ReferenceEquals(feed._shell, shell) && feed._active && !feed.IsPaused &&
                feed.FocusCentre() is { } x)
            {
                return (x, feed.PageWidth);
            }
        }
        return null;
    }

    private float? FocusCentre()
    {
        if (_motion.FocusedAnchor() is not { } anchor || !_anchors.TryGetValue(anchor, out var known) ||
            Target(anchor, known.Track, known.ColumnShift) is not { } target)
        {
            return null;
        }
        var shift = known.Track switch
        {
            LauncherTrack.Page => -(_screen?.CurrentPage?.Index ?? 0) * PageWidth,
            LauncherTrack.Column => -(_shell?.OpenFolder?.FirstColumnFor(IsWide) ?? 0) * (float)ColumnPitch,
            _ => 0f,
        };
        return target.X + (target.Width / 2f) + shift;
    }

    public HandheldNativeLauncherFeed(Control view, HandheldLauncherMotion motion, IHandheldNativeStage host)
    {
        Feeds.RemoveAll(weak => !weak.TryGetTarget(out _));
        Feeds.Add(new WeakReference<HandheldNativeLauncherFeed>(this));
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
        _pageLabel = Find("PageLabel");
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
        _btBackShot = Find("BtBackShot");
        _btFolderCount = Find("BtFolderCount");
        _btFolderName = (HandheldBigTypeHeading)Find("BtFolderName");
        // M47 B10a: the name fits the room its count leaves (HeaderGap); a new fit is pictured again.
        _btFolderName.FitChanged += (_, _) => Schedule();
        _btSortShot = Find("BtSortShot");
        _btSwap = Find("BtSwap");
        _btPage = Find("BtPage");
        view.LayoutUpdated += (_, _) => Schedule();
        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.IsVisibleProperty)
                OnViewVisibility();
        };
        host.Failed += () =>
        {
            _failed = true;
            UpdateActive();
        };
    }

    private Control Find(string name) =>
        _view.FindControl<Control>(name) ?? throw new InvalidOperationException($"The Launcher has no {name}.");

    // The canvas, and so a home page: 1240 wide, or 1920 on the 16:9 screen (M45 H5).
    private float PageWidth => (float)(_screen?.CanvasWidth ?? HandheldShellViewModel.NarrowCanvasWidth);

    // Home as this Launcher draws it (its screen's shape), set by the view.
    private HandheldHomeScreenViewModel? _screen;

    private bool IsWide => _screen?.IsWide == true;

    /// <summary>
    /// This Launcher's home shape. A Launcher that follows the swap gets the other shape at a swap: every sprite sits
    /// somewhere new, so everything is pictured again.
    /// </summary>
    public void SetScreen(HandheldHomeScreenViewModel? screen)
    {
        if (ReferenceEquals(screen, _screen))
            return;
        if (_screen is { } old)
        {
            old.PropertyChanged -= OnWatchedChanged;
            Repicture();
        }
        _screen = screen;
        if (screen is not null)
            screen.PropertyChanged += OnWatchedChanged;
        Schedule();
    }

    // A Launcher kept for the other display (M45 H5) is hidden until a swap brings it in: it pictures nothing meanwhile,
    // and catches up (only what changed) as soon as it is shown again.
    private bool IsPaused => !_view.IsVisible;

    private bool _catchUpPending;

    private void OnViewVisibility()
    {
        if (IsPaused)
            return;
        _catchUpPending = true;
        OnInput();
        _focusSent = null;
        _tracksSent = null;
        UpdateActive();
        Schedule();
    }

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
            old.InputArrived -= OnInput;
        }
        _shell = shell;
        WatchFolder(shell?.OpenFolder);
        if (shell is null)
            return;
        shell.PropertyChanged += OnShellChanged;
        shell.Bumped += OnBumped;
        shell.FolderClosing += OnFolderClosing;
        shell.LaunchStarting += OnLaunchStarting;
        shell.InputArrived += OnInput;
        UpdateActive();
    }

    /// <summary>The view's selector host passes focus changes on; the selector and the lift follow them.</summary>
    public void OnFocusChanged() => Schedule();

    // ---- Which Launcher draws ----

    private void UpdateActive()
    {
        // A hidden Launcher leaves its native screen as it is (it is kept, out of sight, for the next swap).
        if (IsPaused)
            return;
        // The menu, a sheet, naming and move mode draw on the Avalonia Launcher, which takes the screen back meanwhile.
        var active = !_failed && _shell is { IsLauncherOverlayOpen: false };
        if (active == _active)
            return;
        // Big Type (M47 B7): a list that closed keeps the screen while its rows leave; the native Launcher comes back after. M47 B9: so
        // does the keyboard while its keys drop and a folder named on it is born, and move mode while a tile settles.
        if (active && HandheldTokens.IsBigType)
        {
            _motion.SyncLists();
            if (_motion.ScreenHoldRemaining > 0f)
            {
                _motion.ScreenHoldEnded -= OnListExitEnded;
                _motion.ScreenHoldEnded += OnListExitEnded;
                return;
            }
        }
        _active = active;
        _motion.IsOnScreen = !active;
        _view.Opacity = active ? 0 : 1;
        _root.Classes.Set("native", active);
        var handOver = ++_handOver;
        if (active)
            _host.SetActive(true);
        else
            _ = HandOverAsync(handOver);
        if (active)
        {
            // The native side kept every picture and state while the Avalonia Launcher had the screen (the host
            // retains and replays them), and anything that changed meanwhile has a new key, so only that is pictured
            // again. Re-picturing every tile here cost ~0.3 s of CPU each time a sheet or naming closed (M45 H4 polish).
            // Focus and tracks are re-sent: they are small and the selector may have moved.
            _focusSent = null;
            // Big Type (M47 B9): the ring lands where the focus is now (a tile put down after it was carried, a folder made), rather than
            // travelling from where it was when this Launcher last had the screen, as the Avalonia ring landed there (the mock's 'back').
            _landFocus = HandheldTokens.IsBigType;
            _tracksSent = null;
            Schedule();
        }
    }

    private void OnListExitEnded()
    {
        _motion.ScreenHoldEnded -= OnListExitEnded;
        UpdateActive();
    }

    // Counts hand-overs, so one overtaken by the next (a sheet closed before this view drew) does nothing.
    private int _handOver;

    /// <summary>
    /// The Avalonia Launcher takes the screen (a sheet, the menu, Settings, a panel): the native Launcher stays in front until this
    /// view has drawn a frame of its own. Hiding it at once showed the second display's Avalonia surface before it had drawn anything
    /// since it was made visible: two or three white frames each time a sheet opened (seen on the emulator, both looks; M47 B7).
    /// </summary>
    private async System.Threading.Tasks.Task HandOverAsync(int handOver)
    {
        try
        {
            if (Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(_view)?.Compositor is { } compositor)
            {
                // The frame that lays out and draws what just showed, then the one after it: the first is on screen by then.
                for (var frame = 0; frame < 2; frame++)
                {
                    await compositor.RequestCompositionBatchCommitAsync().Rendered;
                    if (handOver != _handOver)
                        return;
                }
            }
        }
        catch (Exception)
        {
            // A view torn down meanwhile: hand over at once, as before.
        }
        if (handOver == _handOver && !_active)
            _host.SetActive(false);
    }

    /// <summary>
    /// The Launcher moved to the other canvas (M45 H5): every sprite sits somewhere new and home has a new shape, so
    /// everything sent goes and the next sync pictures it all again, the layout first.
    /// </summary>
    private void Repicture()
    {
        _repicturePending = true;
        Schedule();
    }

    // Set by a change of shape; the next sync (laid out for the new canvas) pictures everything again.
    private bool _repicturePending;

    /// <summary>
    /// Raised after the first sync since this Launcher was shown again or changed shape: its native screen has every
    /// piece (a swap waits for it before sliding that screen in).
    /// </summary>
    public event Action? CaughtUp;

    // ---- Changes ----

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HandheldShellViewModel.IsLauncherOverlayOpen):
                UpdateActive();
                break;
            case nameof(HandheldShellViewModel.XSwaps):
                Schedule();
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
            // Big Type (M47 B8): the game ended and the companion's parts have left: the shelf comes back.
            case nameof(HandheldShellViewModel.Playing) when _shell?.Playing is null && _playSent:
                _playSent = false;
                if (_active && !IsPaused)
                    _host.Post(new LauncherPlayCommand(false, Animate: _shell?.HoldsStill != true));
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
        // Big Type (M47 B8): the focused tile flies up through the hinge, and the grid goes as the Stage's takeover starts.
        if (HandheldTokens.IsBigType)
        {
            // A launch while the Avalonia Launcher has the screen (a sheet's rows leaving) is sent when the native one has it again.
            if (_active && !IsPaused)
                SendPlay();
            return;
        }
        _launchPending = true;
        Schedule();
    }

    // Whether the native side was told a game launched (it is told when the shelf comes back).
    private bool _playSent;

    // The launch (M47 B8): the focused tile flies, the grid goes as the takeover starts.
    private void SendPlay()
    {
        if (_playSent || _shell is not { Playing: { IsEnding: false } })
            return;
        if (_focusSent?.SpriteId is { } tile)
            _host.Post(new LauncherLaunchCommand(tile, Downward: IsWide, BigType: true));
        _host.Post(new LauncherPlayCommand(true, Animate: _shell.HoldsStill != true));
        _playSent = true;
    }

    // The folder's name whose letters were sent (M47 B8).
    private string? _lettersSent;

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Schedule();

    // A press or a touch wakes Big Type's resting motion and restarts the idle hop's wait (M47 B5). Sent while the Avalonia
    // Launcher has the screen too, so the native one wakes in step when it comes back.
    private void OnInput()
    {
        if (HandheldTokens.IsBigType && !IsPaused && !_failed)
            _host.Post(new LauncherInputCommand());
    }

    private void OnBumped(HandheldDirection direction)
    {
        if (!_active || IsPaused)
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
        if (!_active || IsPaused || _screen is not { } screen)
            return;
        var at = screen.Grid.PositionOf(folder.HomeIndex);
        var (id, origin) = HomeTile(at.Page, at.Slot);
        _host.Post(new LauncherZoomCommand(true, id, (float)origin.X, (float)origin.Y));
        _host.Post(new LauncherFolderCommand(true));
        _folderOpenSent = true;
        _folderOpenedAt = Stopwatch.GetTimestamp();
        _lettersSent = null;
    }

    // When the last folder opened on this Launcher (its name's letters land only then, not on a catch-up).
    private long? _folderOpenedAt;

    private void OnFolderClosing(HandheldFolderViewModel folder)
    {
        if (!_active || IsPaused || _screen is not { } screen)
            return;
        var at = screen.Grid.PositionOf(folder.HomeIndex);
        var (id, origin) = HomeTile(at.Page, at.Slot);
        _host.Post(new LauncherZoomCommand(false, id, (float)origin.X, (float)origin.Y));
        _host.Post(new LauncherFolderCommand(false));
        _folderOpenSent = false;
        if (_lettersSent is not null)
        {
            _lettersSent = null;
            _host.Post(new LauncherLettersCommand("f-back", null, 0f, Animate: false));
        }
    }

    // What the native side was last told about an open folder (a hidden Launcher catches up without the zoom).
    private bool? _folderOpenSent;

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
        if (!_active || _syncPending || IsPaused)
            return;
        _syncPending = true;
        Dispatcher.UIThread.Post(Sync, DispatcherPriority.Loaded);
    }

    private void Sync()
    {
        _syncPending = false;
        _retryRequested = false;
        // Never picture a view that is between windows (a swap detaches it before attaching it to the other display):
        // its pieces would be pictured from stale or stand-alone layout. Attaching lays it out, which syncs again.
        if (!_active || _shell is not { } shell || _screen is not { } screen || IsPaused || !_view.IsAttachedToVisualTree())
            return;
        var repictured = _repicturePending;
        if (repictured)
        {
            // Everything goes and comes back in this one batch, so the native side never draws an empty frame.
            _repicturePending = false;
            foreach (var id in _sent.Keys)
                _host.Post(new LauncherSpriteCommand(id, null));
            _sent.Clear();
            _homeFaces.Clear();
            _folderStates.Clear();
            _focusSent = null;
            _tracksSent = null;
        }
        var present = new HashSet<string>(StringComparer.Ordinal);
        _anchors.Clear();

        // How the Launcher moves comes before any sprite (M47 B5): Big Type's motion, held still with the device.
        var motion = new LauncherMotionCommand(HandheldTokens.IsBigType, shell.HoldsStill);
        if (motion != _motionSent)
        {
            _host.Post(motion);
            _motionSent = motion;
        }
        SendLayout();
        if (HandheldTokens.IsClassic)
        {
            Static("bg", PageWidth.ToString(System.Globalization.CultureInfo.InvariantCulture), present, () => Background());
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
        }
        else
        {
            // Taking the screen back under Big Type (M47 B9), the ground is already the colour the Avalonia Launcher flooded to.
            SyncGround(shell, instant: repictured || _catchUpPending || _landFocus);
            // The Swap corner only while X swaps (M47 B9): during a launch it goes, as on the Avalonia Launcher.
            if (shell.XSwaps)
                Static("bt-swap", "1", present, () => Sprite("bt-swap", HandheldStageShots.Shot(_btSwap, _canvas), LauncherGroup.Dock));
            if (screen.IsPageCountShown)
                Static("bt-page", screen.PageCountText, present, () => Text(Sprite("bt-page", HandheldStageShots.Shot(_btPage, _canvas), LauncherGroup.Dock), _btPage));
        }

        SyncHome(shell, screen, present);
        SyncDots(screen, present);
        SyncFolder(shell, present);
        SyncDock(present);
        SyncTitle(shell, present);

        foreach (var gone in _sent.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _sent.Remove(gone);
            _host.Post(new LauncherSpriteCommand(gone, null));
        }

        var folderOpen = shell.OpenFolder is not null;
        if (_folderOpenSent != folderOpen)
        {
            _host.Post(new LauncherFolderCommand(folderOpen));
            _folderOpenSent = folderOpen;
        }
        var tracks = (screen.CurrentPage?.Index ?? 0, shell.OpenFolder?.FirstColumnFor(IsWide) ?? 0);
        // Big Type's ring lands on another home page and pops in rather than stretching across the screen (M47 B5).
        var paged = _tracksSent is { } before && before.Page != tracks.Item1 && shell.OpenFolder is null;
        if (_tracksSent != tracks)
        {
            var columnJump = _tracksSent is { } previous && Math.Abs(previous.Column - tracks.Item2) > 1;
            _host.Post(new LauncherTracksCommand(tracks.Item1, tracks.Item2, SnapPage: _tracksSent is null,
                SnapColumn: _tracksSent is null || columnJump || shell.OpenFolder?.IsResorting == true));
            _tracksSent = tracks;
        }
        // Big Type (M47 B8): a launch that came while the Avalonia Launcher had the screen; a shelf that came back meanwhile.
        if (HandheldTokens.IsBigType)
        {
            if (shell.Playing is not null)
                SendPlay();
            else if (_playSent)
            {
                _playSent = false;
                _host.Post(new LauncherPlayCommand(false, Animate: false));
            }
        }
        // Catching up (shown again for a swap, or a new shape): the selector and the lift are already where the focus
        // is when the screen comes into view, rather than gliding from where they were before it was hidden.
        SyncFocus(instant: repictured || _catchUpPending || _landFocus, pop: paged && HandheldTokens.IsBigType && !_landFocus);
        _landFocus = false;
        if (_launchPending && present.Contains("t-cover"))
        {
            _launchPending = false;
            // On the 16:9 screen the Launcher is the top one: the cover leaves downward, toward the Stage.
            _host.Post(new LauncherLaunchCommand("t-cover", Downward: IsWide));
        }
        if (repictured || _catchUpPending)
        {
            _catchUpPending = false;
            CaughtUp?.Invoke();
        }
        if (!_retryRequested)
            _retries = 0;
    }

    private LauncherLayoutCommand? _layoutSent;

    // The geometry goes over whenever it changes (a swap changes all of it). The folder page is hidden, so unmeasured,
    // until a folder first opens; meanwhile its clip is the area above the dock it will fill, from its own margin.
    // (Waiting for it left the native Launcher on its default 1240 canvas until a folder opened: stretched across the
    // 16:9 screen once swapped.)
    private void SendLayout()
    {
        if (Rect(_homeClip) is not { } home)
            return;
        var margin = _folderPage.Margin;
        var folder = Rect(_folderPage) ?? new StageRect((float)margin.Left, (float)margin.Top,
            (float)(_canvas.Bounds.Width - margin.Left - margin.Right), (float)(_canvas.Bounds.Height - DockHeight - margin.Top - margin.Bottom));
        var layout = new LauncherLayoutCommand((float)_canvas.Bounds.Width, (float)_canvas.Bounds.Height, home, folder, PageWidth,
            ColumnPitch, DotPitch, DotsShift);
        if (layout == _layoutSent)
            return;
        _host.Post(layout);
        _layoutSent = layout;
    }

    // The canvas's dock row (the Grid's second row, 144 px).
    private const double DockHeight = 144;

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

    // A count's sprite carries where its characters sit, so a new picture of it rolls the ones that changed (M47 B5).
    private LauncherSprite? Text(LauncherSprite? sprite, Control text) =>
        sprite is null ? null : sprite with { Text = HandheldTextCells.Of(text, _canvas) };

    /// <summary>
    /// Big Type's ground (M47 B3): the native Launcher clears to the shell's colour for what is focused, the same value the
    /// Avalonia Launcher's ground draws, so the hand-off to a sheet and back shows no change. A new colour floods in from the focused
    /// tile (M47 B6, move 3); the first, a catch-up and holding still land it at once.
    /// </summary>
    private void SyncGround(HandheldShellViewModel shell, bool instant)
    {
        var colour = shell.LauncherGround;
        if (_groundSent == colour && !instant)
            return;
        var flood = _groundSent is not null && !instant && !shell.HoldsStill;
        _groundSent = colour;
        // The compositor writes sRGB values straight out, as the view's brush does. The shelf coming back after a game floods from the
        // middle (M47 B8, the mock's endPlay).
        _host.Post(new LauncherGroundCommand(colour.R / 255f, colour.G / 255f, colour.B / 255f, flood, FromMiddle: shell.MiddleFloodGround == colour));
    }

    /// <summary>The ring for the look: today's three bands, or Big Type's one white band of <c>shape.RingStroke</c>.</summary>
    private static LauncherRingBands? RingBands(Control anchor)
    {
        if (HandheldTokens.IsClassic || HandheldTokens.RingBand is not Avalonia.Media.ISolidColorBrush { Color: var band })
            return null;
        return LauncherRingBands.One((float)HandheldSelectorAnchor.BigTypeStroke(anchor), band.R / 255f, band.G / 255f, band.B / 255f);
    }

    private LauncherSprite Background()
    {
        var plain = new Border
        {
            Width = PageWidth,
            Height = 1080,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(HandheldTokens.ClassicGroundTop, 0), new GradientStop(HandheldTokens.ClassicGroundBottom, 1) },
            },
        };
        return new LauncherSprite("bg", HandheldStageShots.Picture(plain, 0, 0, PageWidth, 1080), LauncherGroup.Background,
            LauncherTrack.None, false, 0, 0);
    }

    // A liftable piece: its wrapper is pictured; it lifts about its focusable panel's centre.
    private void Liftable(string id, string key, Control wrapper, LauncherGroup group, LauncherTrack track, float columnShift,
        HashSet<string> present, StageImage? liftShadow = null, HandheldFolderArt? folderArt = null, int z = 0, float lift = 0f,
        LauncherCell? cell = null, bool? homeFace = null)
    {
        if (wrapper is not Decorator { Child: Control panel } || !wrapper.IsVisible)
            return;
        present.Add(id);
        _anchors[panel] = (id, track, columnShift);
        if (_sent.TryGetValue(id, out var sent) && sent == key)
            return;
        if (HandheldTokens.IsClassic)
            foreach (var art in wrapper.GetVisualDescendants().OfType<HandheldFolderArt>())
                art.Classes.Set("frozen", true);
        // A folder's art is sent as parts that glide (its flap peeks on focus): the tile is pictured without it.
        FolderArt? parts = null;
        StageImage? image;
        if (folderArt is not null)
        {
            parts = HandheldFolderArtShots.Capture(folderArt, _canvas);
            if (parts is not { Parts.Count: > 0 })
            {
                // The art can't be pictured just now (hidden, or not laid out yet). The tile without it is an empty
                // picture, and once its key is marked sent nothing pictures it again until the tile's look changes:
                // it would sit blank on home until focused. Keep what the native side has and try again shortly.
                RetrySoon();
                return;
            }
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
        else if (homeFace is { } focused && _homeFaces.TryGetValue((id, focused), out var cached) && cached.Key == key)
        {
            image = cached.Image;
        }
        else
        {
            image = HandheldStageShots.Shot(wrapper, _canvas);
            if (homeFace is { } face && image is not null)
            {
                _homeFaces[(id, face)] = (key, image);
                HandheldProbeLog.Write?.Invoke($"home tile {id} captured, focused={face}");
            }
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
            (float)centre.X + columnShift, (float)centre.Y, shadow, parts, z, lift, cell) { CacheFace = homeFace.HasValue }));
    }

    // A piece that could not be pictured asks for another sync: a later layout pass usually brings one anyway, but
    // nothing guarantees it. A few tries a short while apart, then the next change of any kind tries again.
    private const int MaxRetries = 10;
    private int _retries;
    private bool _retryPending;
    private bool _retryRequested;

    private void RetrySoon()
    {
        _retryRequested = true;
        if (_retryPending || _retries >= MaxRetries)
            return;
        _retryPending = true;
        _retries++;
        DispatcherTimer.RunOnce(() =>
        {
            _retryPending = false;
            Schedule();
        }, TimeSpan.FromMilliseconds(100));
    }

    private void SyncHome(HandheldShellViewModel shell, HandheldHomeScreenViewModel screen, HashSet<string> present)
    {
        if (_facePage != screen.CurrentPage?.Index)
        {
            _homeFaces.Clear();
            _facePage = screen.CurrentPage?.Index;
        }
        // Slots replaced by an edit or a move-mode step are not watched any longer.
        var current = screen.Pages.SelectMany(page => page.Slots).ToHashSet();
        foreach (var gone in _watched.OfType<HandheldHomeSlotViewModel>().Where(slot => !current.Contains(slot)).ToList())
        {
            gone.PropertyChanged -= OnWatchedChanged;
            _watched.Remove(gone);
        }
        var folderAt = shell.OpenFolder is { } openFolder ? screen.Grid.PositionOf(openFolder.HomeIndex) : (HandheldHomePosition?)null;
        foreach (var page in screen.Pages)
        {
            var slots = (_pageTrack.ContainerFromIndex(page.Index) as Control)?.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault();
            foreach (var slot in page.Slots)
            {
                Watch(slot);
                if ((slots?.ContainerFromIndex(slot.Index) as ContentPresenter)?.Child is not Control wrapper)
                    continue;
                var id = $"h:{page.Index}:{slot.Index}";
                var art = slot.IsFolder && HandheldTokens.IsClassic ? wrapper.GetVisualDescendants().OfType<HandheldFolderArt>().FirstOrDefault() : null;
                var key = HandheldTokens.IsClassic ? $"{slot.PictureKey}|{Ref(slot.PeekA)}|{Ref(slot.PeekB)}|{Ref(slot.PeekC)}" : slot.PictureKey;
                // Big Type's flat tiles and empty slots change with focus (white with the colour as ink; a plus): their
                // picture does too. A game's cover does not.
                var changesFace = HandheldTokens.IsBigType && (slot.IsFlatTile || slot.IsEmpty);
                if (changesFace)
                    key += $"|{slot.IsFocused}";
                // Big Type's tiles know their place, so neighbours make room and the idle hop runs along the diagonal (M47 B5).
                LauncherCell? cell = HandheldTokens.IsBigType
                    ? new LauncherCell(LauncherGroup.Home, page.Index, slot.Index % screen.Grid.Columns, slot.Index / screen.Grid.Columns)
                    : null;
                Liftable(id, key, wrapper, LauncherGroup.Home, LauncherTrack.Page, 0, present, folderArt: art, lift: LiftOf(HandheldTokens.FocusScale),
                    cell: cell, homeFace: changesFace && page.Index == _facePage ? slot.IsFocused : null);
                // No flap state for a tile the native side doesn't have yet (its picture waits for a retry): it would be
                // dropped there, and this side would think it sent.
                if (art is null || !_sent.ContainsKey(id))
                    continue;
                // The flap peeks while the folder is focused and swings open with its folder.
                var open = folderAt is { } at && at.Slot == slot.Index && at.Page == page.Index;
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

    // Today's sprites take the compositor's own lift (0 = 1.13); Big Type's give theirs.
    private static float LiftOf(double scale) => HandheldTokens.IsClassic ? 0f : (float)scale;

    private void SyncDots(HandheldHomeScreenViewModel shell, HashSet<string> present)
    {
        // Big Type has no dots, edge hints or page name: the page is the dock row's "1 / 2".
        if (HandheldTokens.IsBigType)
            return;
        // The edge hints fade with home, like the dots.
        if (shell.CanPageLeft)
            Static("edge-l", "1", present, () => Sprite("edge-l", HandheldStageShots.Shot(_edgeLeft, _canvas), LauncherGroup.Dots));
        if (shell.CanPageRight)
            Static("edge-r", "1", present, () => Sprite("edge-r", HandheldStageShots.Shot(_edgeRight, _canvas), LauncherGroup.Dots));
        // The 16:9 screen names the page in its spare height (M45 H5).
        if (shell.IsWide)
            Static("page-label", shell.PageLabel, present, () => Sprite("page-label", HandheldStageShots.Shot(_pageLabel, _canvas), LauncherGroup.Dots));
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
        if (HandheldTokens.IsClassic)
        {
            Liftable("f-back", "1", _backShot, LauncherGroup.Folder, LauncherTrack.None, 0, present);
            Liftable("f-sort", folder.SortLabel, _sortShot, LauncherGroup.Folder, LauncherTrack.None, 0, present);
            Static("f-title", $"{folder.Name}|{folder.CountText}", present,
                () => Sprite("f-title", HandheldStageShots.Shot(_folderTitle, _canvas), LauncherGroup.Folder));
        }
        else
        {
            // The name is Back's place and the sort's value sits in the dock row's corner: the ring frames them, nothing lifts.
            // Keyed by the size it is drawn at as well as its text (M47 B10a): a name set smaller to keep HeaderGap from its count is
            // pictured, and its letters sent, at that size.
            var nameKey = FormattableString.Invariant($"{folder.Name}|{_btFolderName.Layout?.Size}");
            Liftable("f-back", nameKey, _btBackShot, LauncherGroup.Folder, LauncherTrack.None, 0, present, lift: 1f);
            // Its letters land as it opens (M47 B8): the native side draws them in the name's place until they have.
            if (_lettersSent != nameKey && _btFolderName.Layout is not null &&
                HandheldStageShots.ToCanvas(_btFolderName, default, _canvas) is { } nameAt)
            {
                _lettersSent = nameKey;
                _host.Post(new LauncherLettersCommand("f-back", _btFolderName.Letters(nameAt), (float)HandheldTokens.HeadingInFromEm,
                    Animate: !shell.HoldsStill && _folderOpenedAt is { } opened && Stopwatch.GetElapsedTime(opened).TotalMilliseconds < 1000));
            }
            Static("f-count", folder.CountNumber, present,
                () => Text(Sprite("f-count", HandheldStageShots.Shot(_btFolderCount, _canvas), LauncherGroup.Folder), _btFolderCount));
            if (folder.CanSort)
                Liftable("f-sort", folder.SortLabel, _btSortShot, LauncherGroup.Dock, LauncherTrack.None, 0, present, lift: 1f);
        }
        if (folder.IsEmpty)
            Static("f-empty", "1", present, () => Sprite("f-empty", HandheldStageShots.Shot(_folderEmpty, _canvas), LauncherGroup.Folder));

        var shift = folder.WindowFirstColumn * ColumnPitch;
        if (HandheldTokens.IsClassic)
            _liftShadow ??= LiftShadow();
        for (var i = 0; i < folder.VisibleTiles.Count; i++)
        {
            var tile = folder.VisibleTiles[i];
            Watch(tile);
            if ((_folderTiles.ContainerFromIndex(i) as ContentPresenter)?.Child is not Control wrapper || wrapper.Bounds.Width <= 0)
                continue;
            var key = $"{Ref(tile.Cover)}|{tile.Title}|{tile.IsUnavailable}|{tile.IsSettings}|{tile.IsActivity}";
            if (HandheldTokens.IsBigType && tile.IsApp)
                key += $"|{tile.IsFocused}";
            LauncherCell? cell = HandheldTokens.IsBigType
                ? new LauncherCell(LauncherGroup.Folder, 0, tile.Column, tile.Index % HandheldFolderNavigation.Rows)
                : null;
            Liftable($"f:{tile.Index}", key, wrapper, LauncherGroup.Folder, LauncherTrack.Column, shift, present,
                HandheldTokens.IsClassic ? _liftShadow : null, lift: LiftOf(HandheldTokens.FocusScale), cell: cell);
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
            // Big Type's All apps and menu buttons turn white with the ground as ink while focused.
            if (HandheldTokens.IsBigType && !item.IsApp)
            {
                var ink = item.IsFocused && item.Ground is Avalonia.Media.ISolidColorBrush solid ? solid.Color.ToString() : "-";
                key += $"|{item.IsFocused}|{ink}";
            }
            Liftable($"d:{i}", key, wrapper, LauncherGroup.Dock, LauncherTrack.None, 0, present, lift: LiftOf(HandheldTokens.DockFocusScale));
        }
    }

    // ---- The Title page: drawn over everything while it is up ----

    private void SyncTitle(HandheldShellViewModel shell, HashSet<string> present)
    {
        // Big Type has no Title page to draw (M47 B8): the page is only the launch's state.
        if (!shell.ShowsTitlePage || shell.TitlePage is not { } page)
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
        var background = new Panel { Width = PageWidth, Height = 1080 };
        background.Children.Add(new Border
        {
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(HandheldTokens.ClassicGroundTop, 0), new GradientStop(HandheldTokens.ClassicGroundBottom, 1) },
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
                GradientStops = { new GradientStop(HandheldTokens.ClassicWashOpaque, 0), new GradientStop(HandheldTokens.ClassicWashClear, 0.7) },
            },
        });
        return new LauncherSprite("t-bg", HandheldStageShots.Picture(background, 0, 0, PageWidth, 1080), LauncherGroup.Title,
            LauncherTrack.None, false, 0, 0, Z: 0);
    }

    // Set when this Launcher takes the screen back under Big Type: the next focus lands.
    private bool _landFocus;

    private void SyncFocus(bool instant, bool pop = false)
    {
        LauncherFocusCommand focus;
        if (_motion.FocusedAnchor() is { } anchor && _anchors.TryGetValue(anchor, out var known))
        {
            focus = new LauncherFocusCommand(known.Id, Target(anchor, known.Track, known.ColumnShift), instant, pop);
        }
        else
        {
            focus = new LauncherFocusCommand(null, null, instant);
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
        // Today's ring grows with the lift; Big Type's stays at its outset from the resting tile (M47 B3).
        var lift = anchor.Classes.Contains("focusable") ? HandheldTokens.RingLift : 1;
        var cx = anchor.Bounds.Width / 2;
        var cy = anchor.Bounds.Height / 2;
        var lifted = new Rect(cx + ((local.X - cx) * lift), cy + ((local.Y - cy) * lift), local.Width * lift, local.Height * lift);
        if (HandheldStageShots.ToCanvas(anchor, lifted.TopLeft, _canvas) is not { } at)
            return null;
        var radius = (ring?.CornerRadius.TopLeft ?? 32) * lift;
        return new LauncherSelectorTarget((float)at.X + columnShift, (float)at.Y, (float)lifted.Width, (float)lifted.Height,
            (float)radius, track, HandheldTokens.IsClassic ? Glow(lifted.Width, lifted.Height, radius) : null, RingBands(anchor));
    }

    private StageImage Glow(double width, double height, double radius)
    {
        var key = ((int)Math.Round(width), (int)Math.Round(height), (int)Math.Round(radius));
        if (_glows.TryGetValue(key, out var glow))
            return glow;
        var shadow = new HandheldShadow
        {
            Blur = GlowBlur,
            Color = HandheldTokens.ClassicRingGlow,
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
        var side = HandheldTokens.TileFolder + (2 * HandheldTokens.ClassicLiftShadowBlur);
        var shadow = new HandheldShadow
        {
            Radius = HandheldTokens.ClassicFolderTileShadowRadius, Blur = HandheldTokens.ClassicLiftShadowBlur,
            Color = HandheldTokens.ClassicLiftShadow, Width = side, Height = side, Filled = true,
        };
        return HandheldStageShots.Picture(shadow, 0, 0, (float)side, (float)side);
    }

    private void Watch(INotifyPropertyChanged item)
    {
        if (_watched.Add(item))
            item.PropertyChanged += OnWatchedChanged;
    }

    private static string Ref(object? value) => value is null ? "-" : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
