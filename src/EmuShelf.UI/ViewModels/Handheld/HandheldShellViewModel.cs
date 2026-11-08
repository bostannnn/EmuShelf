using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The Handheld mode shell (M45). Owns every piece of browsing state — the home layout, the open folder,
/// the selection and which zone it is in (grid, folder header, dock), the menu — and decides what the
/// Stage shows. <c>HandheldStageView</c> (top screen) and <c>HandheldLauncherView</c> (bottom screen)
/// are pure views onto it. Pad input from either display arrives through
/// <see cref="DispatchGamepadAction"/>.
/// </summary>
public sealed partial class HandheldShellViewModel : ObservableObject, IDisposable
{
    /// <summary>Tile covers decode at about the tile's on-screen size (≈200 px on the Thor's bottom panel).</summary>
    public const int TileDecodeWidth = 220;

    /// <summary>The Stage's cover decodes from the full cover at roughly its on-screen height.</summary>
    public const int StageDecodeWidth = 640;

    /// <summary>The Title page's cover on the Launcher (about 400 px wide on the 1240×1080 canvas).</summary>
    public const int TitleCoverDecodeWidth = 480;

    // One column either side of the realized window keeps its covers, so a step back is instant.
    private const int CoverKeepMarginColumns = 1;

    private readonly IHandheldLibrary _library;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, HandheldPlatform> _platforms = new(StringComparer.Ordinal);
    private HandheldHomeLayout _layout = HandheldHomeLayout.CreateDefault([]);
    private HandheldHomePosition _home = new(0, 0);
    private CancellationTokenSource? _folderLoad;
    private CancellationTokenSource? _stageArtLoad;
    private CancellationTokenSource? _titleLoad;
    private DispatcherTimer? _statusTimer;
    private SecondScreenViewModel? _companion;
    private bool _disposed;
    private int _initializeGeneration;

    public HandheldShellViewModel(IHandheldLibrary library, IDeviceStatusService? deviceStatus = null, TimeProvider? clock = null,
        IHandheldHomeLayoutStore? layoutStore = null, bool screensSwapped = false, Action<bool>? saveScreensSwapped = null)
    {
        _library = library;
        _saveScreensSwapped = saveScreensSwapped;
        _store = layoutStore ?? new InMemoryHandheldHomeLayoutStore();
        DeviceStatus = deviceStatus ?? UnknownDeviceStatusService.Instance;
        _clock = clock ?? TimeProvider.System;
        _heldDirection = new HandheldHeldDirection(_clock);
        DockItems.Add(new HandheldDockItemViewModel(HandheldDockKind.AllApps));
        DockItems.Add(new HandheldDockItemViewModel(HandheldDockKind.Menu));
        // A stop-gap until Handheld mode has its own Settings (M45 H6a): Gamepad mode's Settings, on the main display.
        MenuItems.Add(new HandheldMenuItemViewModel("Settings", () => _library.OpenSettingsAsync()));
        MenuItems.Add(new HandheldMenuItemViewModel("Switch to Gamepad mode", () => _library.SwitchModeAsync(InterfaceMode.Gamepad)));
        MenuItems.Add(new HandheldMenuItemViewModel("Quit EmuShelf", () => { _library.Quit(); return Task.CompletedTask; }));
        RefreshStatus();
        NarrowHome.PropertyChanged += OnScreenChanged;
        WideHome.PropertyChanged += OnScreenChanged;
        // The remembered side (M45 H5), once everything it touches exists.
        SetSwapped(screensSwapped);
    }

    /// <summary>Home on the screen the Launcher is on now (the other shape is kept current too, M45 H5).</summary>
    public ObservableCollection<HandheldHomePageViewModel> Pages => Home.Pages;
    public ObservableCollection<HandheldPageDotViewModel> PageDots => Home.PageDots;
    public ObservableCollection<HandheldDockItemViewModel> DockItems { get; } = [];
    public ObservableCollection<HandheldMenuItemViewModel> MenuItems { get; } = [];
    public ObservableCollection<HandheldHint> Hints { get; } = [];
    public HandheldStageViewModel Stage { get; } = new();

    public HandheldHomePageViewModel? CurrentPage => Home.CurrentPage;

    public int PageCount => Home.PageCount;

    public bool HasMultiplePages => Home.HasMultiplePages;

    [ObservableProperty]
    public partial HandheldZone Zone { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderOpen))]
    [NotifyPropertyChangedFor(nameof(IsHomeVisible))]
    public partial HandheldFolderViewModel? OpenFolder { get; private set; }

    public bool IsFolderOpen => OpenFolder is not null;
    public bool IsHomeVisible => OpenFolder is null;

    /// <summary>The open Title page (A on a game), drawn over the grid it was opened from; null while browsing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTitleOpen))]
    [NotifyPropertyChangedFor(nameof(TitleGame))]
    public partial HandheldTitlePageViewModel? TitlePage { get; private set; }

    public bool IsTitleOpen => TitlePage is not null;

    /// <summary>The Title page's game as the rest of the app knows it (the companion's achievements follow it).</summary>
    public GameViewModel? TitleGame => TitlePage?.Entry.ViewModel;

    [ObservableProperty]
    public partial int DockIndex { get; private set; }

    [ObservableProperty]
    public partial bool IsMenuOpen { get; private set; }

    [ObservableProperty]
    public partial int MenuIndex { get; private set; }

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    // Status pill.
    [ObservableProperty]
    public partial string TimeText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBattery))]
    public partial string? BatteryText { get; private set; }

    public bool HasBattery => BatteryText is not null;

    [ObservableProperty]
    public partial bool IsCharging { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWifiState))]
    [NotifyPropertyChangedFor(nameof(WifiOpacity))]
    public partial bool? IsWifiConnected { get; private set; }

    public bool HasWifiState => IsWifiConnected is not null;
    public double WifiOpacity => IsWifiConnected == true ? 1 : 0.3;

    /// <summary>The battery/Wi-Fi source for the status pill; replaceable once the host knows the device.</summary>
    public IDeviceStatusService DeviceStatus { get; set; }

    /// <summary>The app's current status message (a launch in progress, a failure), shown on the Stage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    public partial string? StatusMessage { get; private set; }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    /// <summary>Raised when a move runs into a wall, for the view's edge bump.</summary>
    public event Action<HandheldDirection>? Bumped;

    /// <summary>
    /// Raised just before the Stage moves on to a different thing (the view keeps a picture of the outgoing
    /// art for the centre pop), and right after, once the new thing is in place.
    /// </summary>
    public event Action? StageChanging;

    /// <inheritdoc cref="StageChanging"/>
    public event Action? StageChanged;

    /// <summary>Raised before an open folder is torn down, while its tiles are still on screen (the close pictures the page).</summary>
    public event Action<HandheldFolderViewModel>? FolderClosing;

    /// <summary>Raised when Play starts a launch from the Title page, before the lead-in (the cover's flight).</summary>
    public event Action<HandheldTitlePageViewModel>? LaunchStarting;

    /// <summary>
    /// How long a launch waits after Play for its motion (the cover lifting off the Launcher and landing on
    /// the Stage) before the emulator starts. The host sets <see cref="HandheldMotion.LaunchLead"/>; zero in tests.
    /// </summary>
    public TimeSpan LaunchLead { get; set; } = TimeSpan.Zero;

    /// <summary>The home position the selection is on (or returns to from a folder or the dock).</summary>
    public HandheldHomePosition HomePosition => _home;

    /// <summary>
    /// The companion's view model, whose five pinned dock slots this dock shows and whose drawer All apps
    /// opens. Set by the Android head once Screen-2's presentation exists; null elsewhere.
    /// </summary>
    public SecondScreenViewModel? Companion
    {
        get => _companion;
        set
        {
            if (ReferenceEquals(_companion, value))
                return;
            _companion = value;
            RebuildDock();
            // The Title page's Achievements button depends on a companion to open in.
            if (TitlePage is { } title)
            {
                BuildTitleButtons(title);
                UpdateHints();
            }
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Builds home from the saved layout (the default on a first start: Recent, then one platform folder per
    /// platform with games, in navigation order), reconciled with the library.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Library refreshes can overlap (an import landing during the startup availability pass); only the
        // newest read may rebuild home, so a slower older one never overwrites it.
        var generation = ++_initializeGeneration;
        var platforms = await _library.GetPlatformsAsync(cancellationToken);
        var gameIds = await _library.GetGameIdsAsync(cancellationToken);
        var recent = await _library.GetRecentGameIdsAsync(RecentCount, cancellationToken);
        if (_disposed || generation != _initializeGeneration)
            return;
        _platforms.Clear();
        foreach (var platform in platforms)
            _platforms[platform.SystemId] = platform;
        _recentIds = recent;
        // A carried tile and an open sheet were arranged against the layout being replaced: they go. A name being
        // typed stays (Done finds a free slot if its own was taken meanwhile).
        if (_move is not null)
        {
            _move = null;
            IsMoving = false;
        }
        Sheet = null;
        _layout = LoadLayout(platforms, gameIds);
        // A library change may have changed which games come first; the peek covers are read again.
        ReleasePeekCovers();
        // Pins are read again too, so their availability (the dimming) and play data are current.
        PrunePins(all: true);
        BuildPages();
        _ = EnsurePinsAsync();

        // Keep the selection where it was if it still exists (a library refresh), else start at the top.
        if (_home.Page >= ViewPageCount)
            _home = new HandheldHomePosition(0, 0);
        if (OpenFolder is { } folder && !FolderStillExists(folder.Source))
            CloseFolder();
        ShowPage(_home.Page);
        IsLoaded = true;
        UpdateFocus();
    }

    /// <summary>Starts the clock/battery/Wi-Fi refresh for the status pill while the mode is on screen.</summary>
    public void StartStatusUpdates()
    {
        RefreshStatus();
        if (_statusTimer is not null)
            return;
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
    }

    public void StopStatusUpdates()
    {
        _statusTimer?.Stop();
        _statusTimer = null;
    }

    public void RefreshStatus()
    {
        TimeText = _clock.GetLocalNow().ToString("t", CultureInfo.CurrentCulture);
        var status = DeviceStatus.GetStatus();
        BatteryText = status.BatteryPercent is { } percent ? $"{percent}%" : null;
        IsCharging = status.IsCharging;
        IsWifiConnected = status.IsWifiConnected;
    }

    /// <summary>The single entry point for pad input, from either display. Returns whether it was consumed.</summary>
    public bool DispatchGamepadAction(GamepadAction action)
    {
        // The companion's drawer (All apps) is open over Screen-2: it owns the pad until it closes.
        if (_companion is { } companion && companion.Overlay != SecondScreenOverlayKind.None)
            return companion.DispatchGamepadAction(action) || action is GamepadAction.Cancel;

        if (IsMenuOpen)
            return DispatchMenuAction(action);

        if (Naming is { } naming)
            return DispatchNamingAction(naming, action);

        if (Sheet is { } sheet)
            return DispatchSheetAction(sheet, action);

        if (_move is { } move)
            return DispatchMoveAction(move, action);

        if (TitlePage is { } title)
            return DispatchTitleAction(title, action);

        switch (action)
        {
            case GamepadAction.NavigateUp:
                Move(HandheldDirection.Up);
                return true;
            case GamepadAction.NavigateDown:
                Move(HandheldDirection.Down);
                return true;
            case GamepadAction.NavigateLeft:
                Move(HandheldDirection.Left);
                return true;
            case GamepadAction.NavigateRight:
                Move(HandheldDirection.Right);
                return true;
            case GamepadAction.Confirm:
                Activate();
                return true;
            case GamepadAction.Cancel:
                Back();
                // B is always the shell's, even with nothing to back out of, so it never falls through to
                // the platform as a system Back.
                return true;
            case GamepadAction.Menu:
                OpenMenu();
                return true;
            case GamepadAction.Actions:
                OpenActions();
                return true;
            case GamepadAction.ResetRotation:
                RecentreMedium();
                return true;
            case GamepadAction.Search:
                // X swaps the screens (M45 H5); the selection, an open folder and its scroll go along.
                _ = SwapScreensAsync();
                return true;
            case GamepadAction.NextPlatform when OpenFolder is null:
                TurnPage(1);
                return true;
            case GamepadAction.PreviousPlatform when OpenFolder is null:
                TurnPage(-1);
                return true;
            default:
                // Every other button is swallowed so nothing reaches the hidden Gamepad shell.
                return true;
        }
    }

    /// <summary>
    /// The system Back button. Closes whatever is open (menu, drawer, folder, dock/header focus) and returns
    /// true; with nothing open it returns false so the platform can leave the app, as Gamepad mode does.
    /// </summary>
    public bool DispatchBack()
    {
        if (_companion is { } companion && companion.Overlay != SecondScreenOverlayKind.None)
            return companion.DispatchGamepadAction(GamepadAction.Cancel);
        if (!IsMenuOpen && (Naming is not null || Sheet is not null || _move is not null))
            return DispatchGamepadAction(GamepadAction.Cancel);
        if (!IsMenuOpen && TitlePage is { } title)
        {
            BackOnTitle(title);
            return true;
        }
        if (IsMenuOpen || OpenFolder is not null || Zone != HandheldZone.Grid)
        {
            if (IsMenuOpen)
                CloseMenu();
            else
                Back();
            return true;
        }
        return false;
    }

    private bool FolderStillExists(HandheldFolderSource source) => source.Kind switch
    {
        HandheldFolderKind.Platform => source.Platform is { } platform && _platforms.ContainsKey(platform.SystemId),
        HandheldFolderKind.User => _layout.FindFolder(source.FolderId) is not null,
        _ => true,
    };

    private void Move(HandheldDirection direction)
    {
        // The top screen changes once per move: a held direction waits until it settles (Motion spec).
        _stageHold = null;
        _stageDefer = _heldDirection.Press() ? HandheldMotion.HeldDirectionSettle : null;
        try
        {
            MoveCore(direction);
        }
        finally
        {
            _stageDefer = null;
        }
    }

    private void MoveCore(HandheldDirection direction)
    {
        switch (Zone)
        {
            case HandheldZone.Dock:
                MoveInDock(direction);
                break;
            case HandheldZone.Header:
                MoveInHeader(direction);
                break;
            default:
                if (OpenFolder is { } folder)
                    MoveInFolder(folder, direction);
                else
                    MoveOnHome(direction);
                break;
        }
    }

    private void MoveOnHome(HandheldDirection direction)
    {
        var (position, outcome) = HandheldHomeNavigation.Move(_home, direction, ViewPageCount, HomeGrid);
        switch (outcome)
        {
            case HandheldMoveOutcome.Moved:
                _home = position;
                if (position.Page != CurrentPage?.Index)
                    ShowPage(position.Page);
                UpdateFocus();
                break;
            case HandheldMoveOutcome.ExitDown:
                EnterDock();
                break;
            default:
                Bumped?.Invoke(direction);
                break;
        }
    }

    private void MoveInFolder(HandheldFolderViewModel folder, HandheldDirection direction)
    {
        var (index, outcome) = HandheldFolderNavigation.Move(folder.SelectedIndex, folder.Items.Count, direction);
        switch (outcome)
        {
            case HandheldMoveOutcome.Moved:
                SelectInFolder(folder, index);
                break;
            case HandheldMoveOutcome.ExitUp:
                Zone = HandheldZone.Header;
                folder.FocusedHeaderItem = HandheldHeaderItem.Back;
                UpdateFocus();
                break;
            case HandheldMoveOutcome.ExitDown:
                EnterDock();
                break;
            default:
                Bumped?.Invoke(direction);
                break;
        }
    }

    private void MoveInHeader(HandheldDirection direction)
    {
        if (OpenFolder is not { } folder)
            return;
        switch (direction)
        {
            case HandheldDirection.Left when folder.FocusedHeaderItem == HandheldHeaderItem.Sort:
                folder.FocusedHeaderItem = HandheldHeaderItem.Back;
                UpdateFocus();
                break;
            case HandheldDirection.Right when folder.FocusedHeaderItem == HandheldHeaderItem.Back && folder.CanSort:
                folder.FocusedHeaderItem = HandheldHeaderItem.Sort;
                UpdateFocus();
                break;
            case HandheldDirection.Down when folder.HasGames:
                folder.FocusedHeaderItem = null;
                Zone = HandheldZone.Grid;
                // Back into the grid at the top of the leftmost visible column.
                SelectInFolder(folder, Math.Min(folder.FirstVisibleColumn * HandheldFolderNavigation.Rows, folder.Items.Count - 1));
                break;
            case HandheldDirection.Down:
                folder.FocusedHeaderItem = null;
                EnterDock();
                break;
            default:
                Bumped?.Invoke(direction);
                break;
        }
    }

    private void MoveInDock(HandheldDirection direction)
    {
        switch (direction)
        {
            case HandheldDirection.Left when DockIndex > 0:
                DockIndex--;
                UpdateFocus();
                break;
            case HandheldDirection.Right when DockIndex < DockItems.Count - 1:
                DockIndex++;
                UpdateFocus();
                break;
            case HandheldDirection.Up:
                // Back to where the selection came from: the folder's grid (or its header if it is empty),
                // else the home slot.
                if (OpenFolder is { } folder && !folder.HasGames)
                {
                    Zone = HandheldZone.Header;
                    folder.FocusedHeaderItem = HandheldHeaderItem.Back;
                }
                else
                {
                    Zone = HandheldZone.Grid;
                }
                UpdateFocus();
                break;
            default:
                Bumped?.Invoke(direction);
                break;
        }
    }

    private void EnterDock()
    {
        Zone = HandheldZone.Dock;
        UpdateFocus();
    }

    private void TurnPage(int delta)
    {
        var page = _home.Page + delta;
        if (page < 0 || page >= ViewPageCount)
        {
            Bumped?.Invoke(delta > 0 ? HandheldDirection.Right : HandheldDirection.Left);
            return;
        }
        _home = _home with { Page = page };
        Zone = HandheldZone.Grid;
        ShowPage(page);
        UpdateFocus();
    }

    private void Activate()
    {
        switch (Zone)
        {
            case HandheldZone.Dock:
                ActivateDockItem(DockItems[DockIndex]);
                return;
            case HandheldZone.Header when OpenFolder is { } headerFolder:
                if (headerFolder.FocusedHeaderItem == HandheldHeaderItem.Sort)
                    ApplySort(headerFolder, HandheldFolderSorting.Next(headerFolder.Sort));
                else
                    CloseFolder();
                return;
        }

        if (OpenFolder is { } folder)
        {
            // A on a game opens its Title page; Play there is the second press (spec decision 13).
            if (folder.SelectedTile is { } tile)
                OpenTitle(tile.Entry);
            return;
        }

        var slot = HomeSlotAt(HomeIndex);
        switch (slot.Kind)
        {
            case HandheldSlotKind.Empty:
                // A on an empty slot makes a folder there, the 3DS gesture (decision 11).
                StartNaming(isNew: true, HomeIndex, [], null, -1);
                return;
            case HandheldSlotKind.Game:
                if (_pins.TryGetValue(slot.GameId, out var pinned))
                    OpenTitle(pinned);
                return;
        }
        if (FolderSourceFor(slot) is { } source)
            _ = OpenFolderAsync(source, _home.Slot);
    }

    private void Back()
    {
        if (Zone == HandheldZone.Dock || Zone == HandheldZone.Header)
        {
            if (OpenFolder is { } folder)
            {
                folder.FocusedHeaderItem = null;
                // B from the header or dock inside a folder closes the folder, as Back does.
                CloseFolder();
                return;
            }
            Zone = HandheldZone.Grid;
            UpdateFocus();
            return;
        }
        if (OpenFolder is not null)
            CloseFolder();
    }

    /// <summary>Opens a platform folder: loads its games, then selects the first.</summary>
    public Task OpenFolderAsync(HandheldPlatform platform, int homeSlot) =>
        OpenFolderAsync(HandheldFolderSource.ForPlatform(platform), homeSlot);

    /// <summary>Opens a folder (a platform's, yours, Recent): loads its games, then selects the first.</summary>
    public async Task OpenFolderAsync(HandheldFolderSource source, int homeSlot)
    {
        CloseFolder();
        // Opening a folder takes its "New" ribbon off; the tile is redrawn when the folder closes, so the page
        // the folder zooms out of stays the one on screen.
        var index = (_home.Page * SlotsPerPage) + homeSlot;
        var folder = new HandheldFolderViewModel(source, homeSlot)
        {
            HomeIndex = index,
            RibbonIndex = index < _layout.SlotCount && _layout.SlotAt(index).IsNew ? index : -1,
        };
        OpenFolder = folder;
        Zone = HandheldZone.Grid;
        // The top screen stays on the open folder while the page zooms in, then moves on to the first one.
        _stageHold = _clock.GetTimestamp();
        UpdateFocus();

        var cancellation = new CancellationTokenSource();
        _folderLoad = cancellation;
        IReadOnlyList<HandheldGameEntry> games;
        try
        {
            games = await LoadFolderGamesAsync(source, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cancellation.IsCancellationRequested || !ReferenceEquals(OpenFolder, folder))
        {
            _library.ReleaseGames(games);
            return;
        }

        folder.Games = games;
        ApplySort(folder, source.IsRecent ? HandheldFolderSort.Manual : source.Sort, selectFirst: true);
        folder.IsLoaded = true;
        if (!folder.HasGames && Zone == HandheldZone.Grid)
        {
            Zone = HandheldZone.Header;
            folder.FocusedHeaderItem = HandheldHeaderItem.Back;
        }
        UpdateFocus();
        _stageHold = null;
    }

    /// <summary>
    /// Re-reads the open folder's games (after a play was recorded, so last played, play time and the sorts
    /// are current), keeping the selected game. Skipped while a Title page is up; it closes into a fresh read.
    /// </summary>
    public async Task RefreshOpenFolderAsync()
    {
        if (OpenFolder is not { IsLoaded: true } folder || TitlePage is not null)
            return;
        IReadOnlyList<HandheldGameEntry> games;
        try
        {
            games = await LoadFolderGamesAsync(folder.Source, default);
        }
        catch (Exception)
        {
            return; // the folder keeps its last read
        }
        if (!ReferenceEquals(OpenFolder, folder) || TitlePage is not null)
        {
            _library.ReleaseGames(games);
            return;
        }
        var previous = folder.Games;
        folder.Games = games;
        _stageGameId = -1; // the Stage re-reads the selected game's facts
        var firstColumn = folder.FirstVisibleColumn;
        ApplySort(folder, folder.Sort, keepColumn: firstColumn);
        OnPropertyChanged(nameof(OpenFolder));
        _library.ReleaseGames(previous);
    }

    /// <summary>A folder's games, in its own order: a platform's library order, yours as added, Recent newest first.</summary>
    private Task<IReadOnlyList<HandheldGameEntry>> LoadFolderGamesAsync(HandheldFolderSource source, CancellationToken token) =>
        source.Kind switch
        {
            HandheldFolderKind.Platform when source.Platform is { } platform => _library.GetGamesAsync(platform.SystemId, token),
            HandheldFolderKind.Recent => _library.GetGamesByIdAsync(_recentIds, token),
            _ => _library.GetGamesByIdAsync(source.GameIds ?? [], token),
        };

    public void CloseFolder()
    {
        if (OpenFolder is not { } folder)
            return;
        // A Title page belongs to its folder's entries, which are released below.
        CloseTitle();
        _stageHold = null;
        FolderClosing?.Invoke(folder);
        _folderLoad?.Cancel();
        _folderLoad?.Dispose();
        _folderLoad = null;
        foreach (var tile in folder.Items)
            ReleaseCover(tile);
        folder.VisibleTiles.Clear();
        _library.ReleaseGames(folder.Games);
        OpenFolder = null;
        if (Zone == HandheldZone.Header)
            Zone = HandheldZone.Grid;
        _home = PositionOf(folder.HomeIndex);
        if (folder.RibbonIndex >= 0 && folder.RibbonIndex < _layout.SlotCount && _layout.SlotAt(folder.RibbonIndex).IsNew)
        {
            ApplyLayout(_layout.WithRibbonCleared(folder.RibbonIndex));
            return;
        }
        UpdateFocus();
    }

    private void ApplySort(HandheldFolderViewModel folder, HandheldFolderSort sort, bool selectFirst = false, int keepColumn = 0)
    {
        var selectedId = folder.SelectedTile?.Entry.Id;
        foreach (var tile in folder.Items)
            ReleaseCover(tile);
        folder.VisibleTiles.Clear();

        var sorted = HandheldFolderSorting.Apply(
            folder.Games,
            sort,
            game => game.Title,
            game => game.LastPlayedAt,
            game => game.PlayCount);
        folder.Items = sorted.Select((entry, index) => new HandheldGameTileViewModel(index, entry)).ToList();
        // Your folders remember the sort chip they were left on.
        if (folder.Source is { IsUser: true, FolderId: { } folderId } && folder.Sort != sort &&
            _layout.FindFolder(folderId) is { } saved && saved.Sort != sort)
        {
            _layout = _layout.WithFolder(saved with { Sort = sort });
            _store.Save(_layout);
        }
        folder.Sort = sort;
        folder.IsResorting = true;
        // A re-read keeps the scroll window where it was (clamped by the selection); a re-sort starts at the left.
        folder.FirstVisibleColumn = keepColumn;
        folder.NarrowFirstColumn = keepColumn;
        folder.WideFirstColumn = keepColumn;
        // Re-sorting keeps the same game selected; the first open starts at the first game.
        var index = selectFirst || selectedId is null
            ? 0
            : Math.Max(0, folder.Items.ToList().FindIndex(tile => tile.Entry.Id == selectedId));
        folder.SelectedIndex = -1;
        if (folder.Items.Count > 0)
            SelectInFolder(folder, index, updateFocus: false);
        folder.IsResorting = false;
        OnPropertyChanged(nameof(OpenFolder));
        UpdateFocus();
    }

    private void SelectInFolder(HandheldFolderViewModel folder, int index, bool updateFocus = true)
    {
        folder.SelectedIndex = index;
        var column = index / HandheldFolderNavigation.Rows;
        // Both screens' scroll follow the selection; the one in use is FirstVisibleColumn.
        var narrow = HandheldFolderNavigation.FirstVisibleColumn(column, folder.NarrowFirstColumn, folder.Items.Count,
            NarrowHome.FolderVisibleColumns);
        var wide = HandheldFolderNavigation.FirstVisibleColumn(column, folder.WideFirstColumn, folder.Items.Count,
            WideHome.FolderVisibleColumns);
        var first = IsSwapped ? wide : narrow;
        var windowChanged = narrow != folder.NarrowFirstColumn || wide != folder.WideFirstColumn || folder.VisibleTiles.Count == 0;
        folder.NarrowFirstColumn = narrow;
        folder.WideFirstColumn = wide;
        folder.FirstVisibleColumn = first;
        if (windowChanged)
            RealizeWindow(folder);
        if (updateFocus)
            UpdateFocus();
    }

    /// <summary>
    /// Realizes only the columns on screen (four plus the peeking one) and decodes their covers; covers more
    /// than a column outside the window are released. A thousand-game folder never holds more than a screen
    /// of tiles and a few columns of bitmaps.
    /// </summary>
    private void RealizeWindow(HandheldFolderViewModel folder)
    {
        const int rows = HandheldFolderNavigation.Rows;
        // One column behind the window stays realized too, so a column scroll slides the leaving column
        // out instead of dropping it. The window covers what both screens show (M45 H5), so the Launcher on the other
        // screen is ready for a swap.
        var showFrom = Math.Min(folder.NarrowFirstColumn, folder.WideFirstColumn);
        var showTo = Math.Max(folder.NarrowFirstColumn + NarrowHome.FolderVisibleColumns, folder.WideFirstColumn + WideHome.FolderVisibleColumns);
        folder.WindowFirstColumn = Math.Max(0, showFrom - 1);
        var firstIndex = folder.WindowFirstColumn * rows;
        var lastIndex = Math.Min(folder.Items.Count, (showTo + 1) * rows);
        SlideWindow(folder.VisibleTiles, folder.Items, firstIndex, lastIndex);

        var keepFrom = Math.Max(0, showFrom - CoverKeepMarginColumns) * rows;
        var keepTo = Math.Min(folder.Items.Count, (showTo + 1 + CoverKeepMarginColumns) * rows);
        for (var i = 0; i < folder.Items.Count; i++)
        {
            var tile = folder.Items[i];
            if (i >= keepFrom && i < keepTo)
            {
                if (tile.Cover is null && tile.CoverLoad is null)
                    _ = LoadTileCoverAsync(tile);
            }
            else
            {
                ReleaseCover(tile);
            }
        }
    }

    /// <summary>
    /// Makes <paramref name="visible"/> hold <paramref name="items"/>[first, last) by removing and adding only at
    /// the ends, so a column scroll touches the column that leaves and the one that arrives. Clearing and
    /// re-adding the window made the Launcher rebuild all 18 tile controls on every press (about 25 ms of UI
    /// thread a press on the Thor, and the fans with it). A window that does not overlap is replaced outright.
    /// </summary>
    internal static void SlideWindow(IList<HandheldGameTileViewModel> visible, IReadOnlyList<HandheldGameTileViewModel> items, int first, int last)
    {
        var currentFirst = visible.Count > 0 ? visible[0].Index : -1;
        var currentLast = visible.Count > 0 ? visible[^1].Index + 1 : -1;
        var contiguous = visible.Count > 0 && currentLast - currentFirst == visible.Count &&
            ReferenceEquals(visible[0], items.ElementAtOrDefault(currentFirst));
        if (!contiguous || last <= currentFirst || first >= currentLast || first >= last)
        {
            visible.Clear();
            for (var i = first; i < last; i++)
                visible.Add(items[i]);
            return;
        }
        while (visible.Count > 0 && visible[0].Index < first)
            visible.RemoveAt(0);
        while (visible.Count > 0 && visible[^1].Index >= last)
            visible.RemoveAt(visible.Count - 1);
        for (var i = Math.Min(currentFirst, last) - 1; i >= first; i--)
            visible.Insert(0, items[i]);
        for (var i = Math.Max(currentLast, first); i < last; i++)
            visible.Add(items[i]);
    }

    private async Task LoadTileCoverAsync(HandheldGameTileViewModel tile)
    {
        var cancellation = new CancellationTokenSource();
        tile.CoverLoad = cancellation;
        try
        {
            var bitmap = await _library.LoadCoverAsync(tile.Entry, TileDecodeWidth, fullSize: false, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                bitmap?.Dispose();
                return;
            }
            tile.Cover = bitmap;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(tile.CoverLoad, cancellation))
                tile.CoverLoad = null;
            cancellation.Dispose();
        }
    }

    private void ReleaseCover(HandheldGameTileViewModel tile)
    {
        tile.CoverLoad?.Cancel();
        tile.CoverLoad = null;
        if (tile.Cover is { } cover)
        {
            // The Stage may be borrowing this cover as its interim label: take it off the Stage first.
            if (_stageArtBorrowed && ReferenceEquals(Stage.Art, cover))
                ReplaceStageArt(null, borrowed: false);
            tile.Cover = null;
            cover.Dispose();
        }
    }

    private void ActivateDockItem(HandheldDockItemViewModel item)
    {
        switch (item.Kind)
        {
            case HandheldDockKind.Menu:
                OpenMenu();
                break;
            case HandheldDockKind.AllApps:
                _companion?.ToggleDrawerCommand.Execute(null);
                break;
            case HandheldDockKind.App when item.Slot is { } slot:
                _companion?.ActivateSlotCommand.Execute(slot);
                break;
        }
    }

    private void OpenMenu()
    {
        IsMenuOpen = true;
        MenuIndex = 0;
        UpdateMenuFocus();
        UpdateHints();
    }

    private void CloseMenu()
    {
        IsMenuOpen = false;
        UpdateHints();
    }

    private bool DispatchMenuAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp when MenuIndex > 0:
                MenuIndex--;
                UpdateMenuFocus();
                break;
            case GamepadAction.NavigateDown when MenuIndex < MenuItems.Count - 1:
                MenuIndex++;
                UpdateMenuFocus();
                break;
            case GamepadAction.Confirm:
                var item = MenuItems[MenuIndex];
                CloseMenu();
                _ = item.Invoke();
                break;
            case GamepadAction.Cancel:
            case GamepadAction.Menu:
                CloseMenu();
                break;
        }
        return true;
    }

    private void UpdateMenuFocus()
    {
        for (var i = 0; i < MenuItems.Count; i++)
            MenuItems[i].IsFocused = i == MenuIndex;
    }

    /// <summary>There is a page to the left / right of the one on screen (the edge hints).</summary>
    public bool CanPageLeft => Home.CanPageLeft;

    public bool CanPageRight => Home.CanPageRight;

    partial void OnOpenFolderChanged(HandheldFolderViewModel? value)
    {
        NarrowHome.IsFolderOpen = value is not null;
        WideHome.IsFolderOpen = value is not null;
    }

    /// <summary>
    /// Shows <paramref name="page"/> on the screen in use, and on the other shape the page holding the same slot of the
    /// saved layout, so that Launcher is on the right page when a swap brings it in.
    /// </summary>
    private void ShowPage(int page)
    {
        Home.ShowPage(page);
        ShowOtherPage();
    }

    private void ShowOtherPage()
    {
        var other = OtherHome;
        var page = other.Grid.PositionOf(HomeIndex).Page;
        if (other.CurrentPage?.Index != page || other.CurrentPage is null)
            other.ShowPage(page);
    }

    private void RebuildDock()
    {
        var focusedKind = DockItems.Count > DockIndex ? DockItems[DockIndex].Kind : HandheldDockKind.AllApps;
        DockItems.Clear();
        DockItems.Add(new HandheldDockItemViewModel(HandheldDockKind.AllApps));
        if (_companion is { } companion)
        {
            foreach (var slot in companion.Dock)
                DockItems.Add(new HandheldDockItemViewModel(HandheldDockKind.App, slot));
        }
        DockItems.Add(new HandheldDockItemViewModel(HandheldDockKind.Menu));
        DockIndex = focusedKind == HandheldDockKind.Menu ? DockItems.Count - 1 : 0;
        UpdateFocus();
    }

    /// <summary>Marks the one focused element and points the Stage at it.</summary>
    private void UpdateFocus()
    {
        // Both shapes mark the same slot of the saved layout (the other one is the Launcher a swap brings in), each on
        // the page that holds it.
        ShowOtherPage();
        var homeIndex = HomeIndex;
        foreach (var screen in Screens)
        {
            if (screen.CurrentPage is not { } page)
                continue;
            var at = screen.Grid.PositionOf(homeIndex);
            foreach (var slot in page.Slots)
                slot.IsFocused = OpenFolder is null && Zone == HandheldZone.Grid && page.Index == at.Page && slot.Index == at.Slot;
        }
        if (OpenFolder is { } folder)
        {
            foreach (var tile in folder.VisibleTiles)
                tile.IsFocused = Zone == HandheldZone.Grid && tile.Index == folder.SelectedIndex;
            if (Zone != HandheldZone.Header)
                folder.FocusedHeaderItem = null;
        }
        for (var i = 0; i < DockItems.Count; i++)
            DockItems[i].IsFocused = Zone == HandheldZone.Dock && i == DockIndex;

        UpdateHomePeek();
        if (StageDelay() is { } delay)
            ScheduleStage(delay);
        else
            UpdateStage();
        UpdateHints();
    }

    // ---- The top screen's timing: once per move, and the open folder held while its page zooms in ----

    private readonly HandheldHeldDirection _heldDirection;
    private TimeSpan? _stageDefer;
    private long? _stageHold;
    private DispatcherTimer? _stageTimer;

    /// <summary>The Stage is waiting for a held direction to settle (or for a folder's page to land).</summary>
    public bool IsStagePending { get; private set; }

    /// <summary>Brings the Stage up to date now. The settle timer calls it; tests call it instead of waiting.</summary>
    public void SettleStage()
    {
        if (IsStagePending)
            UpdateStage();
    }

    private TimeSpan? StageDelay()
    {
        if (_stageDefer is { } defer)
            return defer;
        if (_stageHold is { } since && OpenFolder is { SelectedTile: not null } && Zone == HandheldZone.Grid && TitlePage is null)
        {
            var remaining = HandheldMotion.FolderOpenStageHold - _clock.GetElapsedTime(since);
            if (remaining > TimeSpan.Zero)
                return remaining;
        }
        return null;
    }

    private void ScheduleStage(TimeSpan delay)
    {
        IsStagePending = true;
        if (_disposed)
            return;
        if (_stageTimer is null)
        {
            _stageTimer = new DispatcherTimer(DispatcherPriority.Input);
            _stageTimer.Tick += (_, _) =>
            {
                _stageTimer?.Stop();
                SettleStage();
            };
        }
        _stageTimer.Stop();
        _stageTimer.Interval = delay;
        _stageTimer.Start();
    }

    private void UpdateStage()
    {
        IsStagePending = false;
        _stageTimer?.Stop();
        UpdateStageCore();
        RaiseStageChanged();
    }

    private void UpdateStageCore()
    {
        if (Naming is { } naming)
        {
            ShowNamingStage(naming);
            return;
        }
        if (_move is { } move)
        {
            ShowMoveStage(move);
            return;
        }
        if (TitlePage is { } title)
        {
            ShowHeroStage(title.Entry);
            return;
        }

        if (Zone == HandheldZone.Dock && DockItems.Count > DockIndex)
        {
            var item = DockItems[DockIndex];
            switch (item.Kind)
            {
                case HandheldDockKind.AllApps:
                    ShowStage(HandheldStageKind.AllApps, "All apps", "Every app on this device");
                    break;
                case HandheldDockKind.Menu:
                    ShowStage(HandheldStageKind.Menu, "Menu", "Switch mode, quit");
                    break;
                default:
                    var label = item.Slot?.Label;
                    ShowStage(HandheldStageKind.App, label ?? "Empty dock slot",
                        label is null ? "Pin an app from All apps" : "Android app");
                    Stage.AppIcon = item.Slot?.Icon;
                    break;
            }
            return;
        }

        if (OpenFolder is { } folder)
        {
            if (Zone == HandheldZone.Header)
            {
                ShowStage(HandheldStageKind.Header,
                    folder.IsSortFocused ? $"Sort: {folder.SortLabel}" : "Back to home",
                    folder.IsSortFocused ? "Press A to change the order" : folder.Name);
                return;
            }
            if (folder.SelectedTile is { } tile)
            {
                ShowGameStage(tile.Entry, tile.Cover);
                return;
            }
            ShowFolderStage(folder.Source, folder.IsLoaded ? folder.CountText : "Loading…");
            return;
        }

        if (!IsLoaded)
        {
            ShowStage(HandheldStageKind.None, string.Empty, string.Empty);
            return;
        }

        var slot = HomeSlotAt(HomeIndex);
        if (slot.Kind == HandheldSlotKind.Game)
        {
            if (_pins.TryGetValue(slot.GameId, out var pinned))
                ShowGameStage(pinned, _pinCovers.GetValueOrDefault(pinned.Id));
            else
                ShowStage(HandheldStageKind.None, string.Empty, string.Empty);
            return;
        }
        if (FolderSourceFor(slot) is { } source)
            ShowFolderStage(source, slot.IsNew ? $"New · {source.Description}" : source.Description);
        else
            ShowStage(HandheldStageKind.EmptySlot, "Empty space",
                _platforms.Count == 0 ? "Add games from the menu in Gamepad mode" : "Press A to make a folder here");
    }

    private void ShowStage(HandheldStageKind kind, string title, string subtitle, bool keepArt = false,
        HandheldBackdrop? backdrop = null)
    {
        // A different thing on the Stage gets the centre pop; a refresh of the same thing does not.
        var changing = kind != Stage.Kind || !string.Equals(title, Stage.Title, StringComparison.Ordinal);
        if (changing)
            StageChanging?.Invoke();
        try
        {
            ShowStageCore(kind, title, subtitle, keepArt, backdrop);
        }
        finally
        {
            if (changing)
                _stageChangedPending = true;
        }
    }

    // StageChanged is raised once the whole update is in place (art, medium, tint), not halfway through it.
    private bool _stageChangedPending;

    private void RaiseStageChanged()
    {
        if (!_stageChangedPending)
            return;
        _stageChangedPending = false;
        StageChanged?.Invoke();
    }

    private void ShowStageCore(HandheldStageKind kind, string title, string subtitle, bool keepArt, HandheldBackdrop? backdrop)
    {
        CancelStageArt(keepArt);
        Stage.Kind = kind;
        Stage.Title = title;
        Stage.Subtitle = subtitle;
        Stage.Monogram = string.Empty;
        Stage.FolderGlyph = HandheldGlyph.None;
        Stage.AchievementText = null;
        Stage.AppIcon = null;
        // Set once per change, never cleared first: the view crossfades only when colour or era really change.
        Stage.Backdrop = backdrop;
        Stage.Medium = null;
        Stage.PeekA = Stage.PeekB = Stage.PeekC = null;
        Stage.Glyph = kind switch
        {
            HandheldStageKind.AllApps => HandheldGlyph.AllApps,
            HandheldStageKind.Menu => HandheldGlyph.Menu,
            HandheldStageKind.EmptySlot => HandheldGlyph.Plus,
            HandheldStageKind.Header => OpenFolder?.IsSortFocused == true ? HandheldGlyph.Sort : HandheldGlyph.Back,
            _ => HandheldGlyph.None,
        };
    }

    private void ShowFolderStage(HandheldFolderSource source, string subtitle)
    {
        // A platform's folder sits on its own backdrop; yours and Recent on the soft tile grid in their colour.
        ShowStage(HandheldStageKind.Folder, source.Name, subtitle,
            backdrop: source.Platform is { } platform ? BackdropFor(platform) : FolderBackdrop(source.AccentColor));
        Stage.Monogram = source.Monogram;
        Stage.FolderGlyph = source.Kind switch
        {
            HandheldFolderKind.User => HandheldGlyph.Star,
            HandheldFolderKind.Recent => HandheldGlyph.Clock,
            _ => HandheldGlyph.None,
        };
        Stage.AccentBrush = Color.TryParse(source.AccentColor, out var color) ? new SolidColorBrush(color) : Brushes.Gray;
        ApplyPeek(source, Stage);
    }

    private long _stageGameId = -1;

    /// <summary>The backdrop behind your folders and Recent: the tile grid, in the folder's colour (as in the mockup).</summary>
    internal static HandheldBackdrop? FolderBackdrop(string color) =>
        Color.TryParse(color, out var parsed) ? new HandheldBackdrop(parsed, EmuShelf.Core.Systems.SystemEra.Cartridge) : null;

    /// <summary>The era motif for a system's folder and games, in its colour; null for an unknown system.</summary>
    internal static HandheldBackdrop? BackdropFor(HandheldPlatform? platform) =>
        platform is not null && Color.TryParse(platform.AccentColor, out var color) ? new HandheldBackdrop(color, platform.Era, platform.Backdrops) : null;

    private void ShowGameStage(HandheldGameEntry game, Bitmap? interimCover = null)
    {
        if (Stage.Kind == HandheldStageKind.Game && _stageGameId == game.Id)
            return;
        // Game to game keeps the label up until the next one is ready, rather than flashing it blank.
        _platforms.TryGetValue(game.SystemId, out var platform);
        ShowStage(HandheldStageKind.Game, game.Title, GameSubtitle(game), keepArt: Stage.Kind == HandheldStageKind.Game,
            backdrop: BackdropFor(platform));
        _stageGameId = game.Id;
        Stage.AchievementText = game.AchievementText;
        if (platform is not null && Color.TryParse(platform.AccentColor, out var accent))
        {
            Stage.MediumAccent = accent;
        }
        Stage.Medium = game.ViewModel?.ShelfMediaShell;
        // The tile's cover is already decoded: print it on the medium now, so the label changes with the
        // selection instead of going blank until the Stage's sharper decode lands.
        if (interimCover is not null)
            ReplaceStageArt(interimCover, borrowed: true);
        // One continuous pose across games: the spring is retargeted at rest from what is on screen.
        _medium.Recentre();
        PushMediumPose();
        UpdateMediumTimer();
        _ = LoadStageArtAsync(game, hero: false);
    }

    /// <summary>
    /// The Title page's hero: the fan art full-bleed with the logo and a meta line, or — for a game with no
    /// fan art — the cover large on a backdrop tinted from it, with the name plate.
    /// </summary>
    private void ShowHeroStage(HandheldGameEntry game)
    {
        if (Stage.Kind == HandheldStageKind.Hero && _stageGameId == game.Id)
            return;
        _platforms.TryGetValue(game.SystemId, out var platform);
        ShowStage(HandheldStageKind.Hero, game.Title, HeroSubtitle(game), backdrop: BackdropFor(platform));
        _stageGameId = game.Id;
        Stage.AchievementText = game.AchievementText;
        _ = LoadStageArtAsync(game, hero: true);
    }

    private string HeroSubtitle(HandheldGameEntry game)
    {
        var now = _clock.GetLocalNow();
        var played = game.PlayCount == 0
            ? "Not played yet"
            : game.Playtime.TotalMinutes >= 1
                ? $"{HandheldText.Duration(game.Playtime)} played · {HandheldText.LastPlayed(game.LastPlayedAt, now)}"
                : $"Played {HandheldText.LastPlayed(game.LastPlayedAt, now).ToLowerInvariant()}";
        return $"{game.SystemName} · {played}";
    }

    private static string GameSubtitle(HandheldGameEntry game)
    {
        var played = game.PlayCount == 0
            ? "Not played yet"
            : game.Playtime.TotalMinutes >= 1 ? HandheldText.Playtime(game.Playtime) : "Played";
        var unavailable = game.IsAvailable ? string.Empty : " · Unavailable";
        return $"{game.SystemName} · {played}{unavailable}";
    }

    /// <summary>
    /// Loads what the Stage draws for a game, one decode at a time and only while that game is still on the
    /// Stage. Browsing: the cover, then the tiny fan art for the soft backdrop. Hero: the fan art and logo;
    /// the cover (and the tint picked from it) only when there is no fan art to show.
    /// </summary>
    private async Task LoadStageArtAsync(HandheldGameEntry game, bool hero)
    {
        var cancellation = new CancellationTokenSource();
        _stageArtLoad?.Dispose();
        _stageArtLoad = cancellation;
        var token = cancellation.Token;
        bool Current() => !token.IsCancellationRequested && _stageGameId == game.Id;
        try
        {
            if (hero)
            {
                var fanart = await LoadArtSafeAsync(game, HandheldArtKind.Fanart, token);
                if (!Assign(fanart, bitmap => Stage.Fanart = bitmap))
                    return;
                if (fanart is not null)
                {
                    var logo = await LoadArtSafeAsync(game, HandheldArtKind.Logo, token);
                    Assign(logo, bitmap => Stage.Logo = bitmap);
                    return;
                }
            }

            var cover = await _library.LoadCoverAsync(game, StageDecodeWidth, fullSize: true, token);
            if (!Current())
            {
                cover?.Dispose();
                return;
            }
            // A game with no cover still replaces the borrowed interim label (with nothing).
            ReplaceStageArt(cover, borrowed: false);
            // A Title page without fan art takes the cover's colour; the system's era stays.
            if (hero && cover is not null && await PickTintAsync(cover) is { } tint && Current() && Stage.Backdrop is { } backdrop)
                Stage.Backdrop = backdrop with { Color = tint };
        }
        catch (OperationCanceledException)
        {
        }

        // Hands a decoded bitmap to the Stage if the game is still showing; otherwise drops it and stops.
        bool Assign(Bitmap? bitmap, Action<Bitmap?> assign)
        {
            if (!Current())
            {
                bitmap?.Dispose();
                return false;
            }
            assign(bitmap);
            return true;
        }
    }

    // Fan art and logos are decoration: a broken file leaves the cover fallback, never a fault.
    private async Task<Bitmap?> LoadArtSafeAsync(HandheldGameEntry game, HandheldArtKind kind, CancellationToken token)
    {
        try
        {
            return await _library.LoadArtAsync(game, kind, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The cover's strongest hue (the artwork palette engine's accent), or null for grey art.</summary>
    private static async Task<Color?> PickTintAsync(Bitmap cover)
    {
        // Copying the pixels touches the live bitmap, so it stays on this (UI) thread; the analysis does not.
        if (ArtworkPaletteExtractor.CopyPixels(cover) is not { } pixels)
            return null;
        var palette = await Task.Run(() => ArtworkPaletteExtractor.FromBgraPixels(pixels, previousIsDark: false));
        return palette is not null && Color.TryParse(palette.Accent, out var accent) ? accent : null;
    }

    // Stage.Art is either the Stage's own decode (disposed when replaced) or a tile's cover borrowed for
    // the moment between a selection and that decode (owned by the tile, never disposed here).
    private bool _stageArtBorrowed;

    private void ReplaceStageArt(Bitmap? bitmap, bool borrowed)
    {
        var previous = Stage.Art;
        var previousBorrowed = _stageArtBorrowed;
        // Off the Stage first, then disposed, so no frame can draw a disposed image.
        Stage.Art = bitmap;
        _stageArtBorrowed = bitmap is not null && borrowed;
        if (previous is not null && !previousBorrowed && !ReferenceEquals(previous, bitmap))
            previous.Dispose();
    }

    private void CancelStageArt(bool keepArt = false)
    {
        _stageArtLoad?.Cancel();
        _stageArtLoad?.Dispose();
        _stageArtLoad = null;
        _stageGameId = -1;
        if (!keepArt)
            ReplaceStageArt(null, borrowed: false);
        // Take each bitmap off the Stage before disposing it, so no frame can draw a disposed image.
        var released = new[] { Stage.Fanart, Stage.Logo };
        Stage.Fanart = null;
        Stage.Logo = null;
        foreach (var bitmap in released)
            bitmap?.Dispose();
    }

    // The hints the current state wants, built fresh on every update and copied into Hints only when they differ:
    // most moves keep the same hints, and rebuilding the pill's items each time cost a layout pass for nothing.
    private readonly List<HandheldHint> _nextHints = [];

    private void UpdateHints()
    {
        _nextHints.Clear();
        BuildHints();
        if (_nextHints.SequenceEqual(Hints))
            return;
        Hints.Clear();
        foreach (var hint in _nextHints)
            Hints.Add(hint);
    }

    private void BuildHints()
    {
        if (IsMenuOpen)
        {
            _nextHints.Add(new HandheldHint("A", "Select"));
            _nextHints.Add(new HandheldHint("B", "Close"));
            return;
        }
        if (Naming is not null || Sheet is not null || _move is not null)
        {
            AddArrangeHints();
            return;
        }
        if (TitlePage is { } title)
        {
            AddTitleHints(title);
            return;
        }
        switch (Zone)
        {
            case HandheldZone.Dock:
                _nextHints.Add(new HandheldHint("A", "Open"));
                _nextHints.Add(new HandheldHint("B", "Back"));
                return;
            case HandheldZone.Header:
                _nextHints.Add(new HandheldHint("A", OpenFolder?.IsSortFocused == true ? "Change" : "Back"));
                _nextHints.Add(new HandheldHint("B", "Close"));
                return;
        }
        if (OpenFolder is { } folder)
        {
            if (folder.SelectedTile is not null)
            {
                _nextHints.Add(new HandheldHint("A", "Play"));
                _nextHints.Add(new HandheldHint("Y", "More"));
            }
            _nextHints.Add(new HandheldHint("B", "Back"));
            return;
        }
        var focused = HomeSlotAt(HomeIndex);
        _nextHints.Add(new HandheldHint("A", focused.IsEmpty ? "New folder" : "Open"));
        _nextHints.Add(new HandheldHint("Y", "More"));
        _nextHints.Add(new HandheldHint(string.Empty, "Menu", HandheldGlyph.Menu));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopStatusUpdates();
        StopMediumTimer();
        _mediumTimer?.Dispose();
        _mediumTimer = null;
        _stageTimer?.Stop();
        _noticeTimer?.Stop();
        CloseTitle();
        CloseFolder();
        CancelStageArt();
        ReleasePeekCovers();
        PrunePins(all: true);
    }
}
