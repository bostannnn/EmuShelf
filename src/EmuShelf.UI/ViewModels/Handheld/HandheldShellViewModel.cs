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

    public HandheldShellViewModel(IHandheldLibrary library, IDeviceStatusService? deviceStatus = null, TimeProvider? clock = null)
    {
        _library = library;
        DeviceStatus = deviceStatus ?? UnknownDeviceStatusService.Instance;
        _clock = clock ?? TimeProvider.System;
        DockItems.Add(new HandheldDockItemViewModel(HandheldDockKind.AllApps));
        DockItems.Add(new HandheldDockItemViewModel(HandheldDockKind.Menu));
        MenuItems.Add(new HandheldMenuItemViewModel("Switch to Gamepad mode", () => _library.SwitchModeAsync(InterfaceMode.Gamepad)));
        MenuItems.Add(new HandheldMenuItemViewModel("Quit EmuShelf", () => { _library.Quit(); return Task.CompletedTask; }));
        RefreshStatus();
    }

    public ObservableCollection<HandheldHomePageViewModel> Pages { get; } = [];
    public ObservableCollection<HandheldPageDotViewModel> PageDots { get; } = [];
    public ObservableCollection<HandheldDockItemViewModel> DockItems { get; } = [];
    public ObservableCollection<HandheldMenuItemViewModel> MenuItems { get; } = [];
    public ObservableCollection<HandheldHint> Hints { get; } = [];
    public HandheldStageViewModel Stage { get; } = new();

    [ObservableProperty]
    public partial HandheldHomePageViewModel? CurrentPage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultiplePages))]
    public partial int PageCount { get; private set; }

    public bool HasMultiplePages => PageCount > 1;

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

    /// <summary>Mirrors the app's status line; it clears itself when the app's message is dismissed.</summary>
    public void ShowStatus(string? message) => StatusMessage = message;

    /// <summary>Raised when a move runs into a wall, for the view's edge bump.</summary>
    public event Action<HandheldDirection>? Bumped;

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

    /// <summary>Builds home from the library: one platform folder per platform with games, in navigation order.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Library refreshes can overlap (an import landing during the startup availability pass); only the
        // newest read may rebuild home, so a slower older one never overwrites it.
        var generation = ++_initializeGeneration;
        var platforms = await _library.GetPlatformsAsync(cancellationToken);
        if (_disposed || generation != _initializeGeneration)
            return;
        _platforms.Clear();
        foreach (var platform in platforms)
            _platforms[platform.SystemId] = platform;
        _layout = HandheldHomeLayout.CreateDefault(platforms.Select(platform => platform.SystemId));
        BuildPages();

        // Keep the selection where it was if it still exists (a library refresh), else start at the top.
        if (_home.Page >= _layout.Pages.Count)
            _home = new HandheldHomePosition(0, 0);
        if (OpenFolder is { } folder && !_platforms.ContainsKey(folder.Platform.SystemId))
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
            case GamepadAction.ResetRotation:
                RecentreMedium();
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

    private void Move(HandheldDirection direction)
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
        var (position, outcome) = HandheldHomeNavigation.Move(_home, direction, _layout.Pages.Count);
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
            case HandheldDirection.Right when folder.FocusedHeaderItem == HandheldHeaderItem.Back:
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
        if (page < 0 || page >= _layout.Pages.Count)
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

        var slot = _layout.SlotAt(_home.Page, _home.Slot);
        if (slot.Kind == HandheldSlotKind.PlatformFolder && slot.SystemId is { } systemId &&
            _platforms.TryGetValue(systemId, out var platform))
        {
            _ = OpenFolderAsync(platform, _home.Slot);
        }
        // A on an empty slot makes a folder there — that is H4. Until then it does nothing.
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
    public async Task OpenFolderAsync(HandheldPlatform platform, int homeSlot)
    {
        CloseFolder();
        var folder = new HandheldFolderViewModel(platform, homeSlot);
        OpenFolder = folder;
        Zone = HandheldZone.Grid;
        UpdateFocus();

        var cancellation = new CancellationTokenSource();
        _folderLoad = cancellation;
        IReadOnlyList<HandheldGameEntry> games;
        try
        {
            games = await _library.GetGamesAsync(platform.SystemId, cancellation.Token);
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

        folder.Source = games;
        ApplySort(folder, HandheldFolderSort.Manual, selectFirst: true);
        folder.IsLoaded = true;
        if (!folder.HasGames && Zone == HandheldZone.Grid)
        {
            Zone = HandheldZone.Header;
            folder.FocusedHeaderItem = HandheldHeaderItem.Back;
        }
        UpdateFocus();
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
            games = await _library.GetGamesAsync(folder.Platform.SystemId);
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
        var previous = folder.Source;
        folder.Source = games;
        _stageGameId = -1; // the Stage re-reads the selected game's facts
        var firstColumn = folder.FirstVisibleColumn;
        ApplySort(folder, folder.Sort, keepColumn: firstColumn);
        OnPropertyChanged(nameof(OpenFolder));
        _library.ReleaseGames(previous);
    }

    public void CloseFolder()
    {
        if (OpenFolder is not { } folder)
            return;
        // A Title page belongs to its folder's entries, which are released below.
        CloseTitle();
        _folderLoad?.Cancel();
        _folderLoad?.Dispose();
        _folderLoad = null;
        foreach (var tile in folder.Items)
            ReleaseCover(tile);
        folder.VisibleTiles.Clear();
        _library.ReleaseGames(folder.Source);
        OpenFolder = null;
        if (Zone == HandheldZone.Header)
            Zone = HandheldZone.Grid;
        _home = _home with { Slot = folder.HomeSlot };
        UpdateFocus();
    }

    private void ApplySort(HandheldFolderViewModel folder, HandheldFolderSort sort, bool selectFirst = false, int keepColumn = 0)
    {
        var selectedId = folder.SelectedTile?.Entry.Id;
        foreach (var tile in folder.Items)
            ReleaseCover(tile);
        folder.VisibleTiles.Clear();

        var sorted = HandheldFolderSorting.Apply(
            folder.Source,
            sort,
            game => game.Title,
            game => game.LastPlayedAt,
            game => game.PlayCount);
        folder.Items = sorted.Select((entry, index) => new HandheldGameTileViewModel(index, entry)).ToList();
        folder.Sort = sort;
        // A re-read keeps the scroll window where it was (clamped by the selection); a re-sort starts at the left.
        folder.FirstVisibleColumn = keepColumn;
        // Re-sorting keeps the same game selected; the first open starts at the first game.
        var index = selectFirst || selectedId is null
            ? 0
            : Math.Max(0, folder.Items.ToList().FindIndex(tile => tile.Entry.Id == selectedId));
        folder.SelectedIndex = -1;
        if (folder.Items.Count > 0)
            SelectInFolder(folder, index, updateFocus: false);
        OnPropertyChanged(nameof(OpenFolder));
        UpdateFocus();
    }

    private void SelectInFolder(HandheldFolderViewModel folder, int index, bool updateFocus = true)
    {
        folder.SelectedIndex = index;
        var column = index / HandheldFolderNavigation.Rows;
        var first = HandheldFolderNavigation.FirstVisibleColumn(column, folder.FirstVisibleColumn, folder.Items.Count);
        var windowChanged = first != folder.FirstVisibleColumn || folder.VisibleTiles.Count == 0;
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
        var firstIndex = folder.FirstVisibleColumn * rows;
        var lastIndex = Math.Min(folder.Items.Count, (folder.FirstVisibleColumn + HandheldFolderNavigation.VisibleColumns + 1) * rows);
        folder.VisibleTiles.Clear();
        for (var i = firstIndex; i < lastIndex; i++)
            folder.VisibleTiles.Add(folder.Items[i]);

        var keepFrom = Math.Max(0, folder.FirstVisibleColumn - CoverKeepMarginColumns) * rows;
        var keepTo = Math.Min(folder.Items.Count,
            (folder.FirstVisibleColumn + HandheldFolderNavigation.VisibleColumns + 1 + CoverKeepMarginColumns) * rows);
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

    private void BuildPages()
    {
        Pages.Clear();
        PageDots.Clear();
        for (var page = 0; page < _layout.Pages.Count; page++)
        {
            var slots = _layout.Pages[page].Slots
                .Select((slot, index) => new HandheldHomeSlotViewModel(
                    index,
                    slot,
                    slot.SystemId is { } id && _platforms.TryGetValue(id, out var platform) ? platform : null))
                .ToList();
            Pages.Add(new HandheldHomePageViewModel(page, slots));
            PageDots.Add(new HandheldPageDotViewModel(page));
        }
        PageCount = Pages.Count;
    }

    private void ShowPage(int page)
    {
        CurrentPage = Pages.Count > page ? Pages[page] : null;
        foreach (var dot in PageDots)
            dot.IsCurrent = dot.Index == page;
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
        var page = CurrentPage;
        if (page is not null)
        {
            foreach (var slot in page.Slots)
                slot.IsFocused = OpenFolder is null && Zone == HandheldZone.Grid && slot.Index == _home.Slot;
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

        UpdateStage();
        UpdateHints();
    }

    private void UpdateStage()
    {
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
            ShowFolderStage(folder.Platform, folder.IsLoaded ? folder.CountText : "Loading…");
            return;
        }

        if (!IsLoaded)
        {
            ShowStage(HandheldStageKind.None, string.Empty, string.Empty);
            return;
        }

        var slot = _layout.SlotAt(_home.Page, _home.Slot);
        if (slot.SystemId is { } systemId && _platforms.TryGetValue(systemId, out var platform))
            ShowFolderStage(platform, $"Folder · {HandheldText.GameCount(platform.GameCount)}");
        else
            ShowStage(HandheldStageKind.EmptySlot, "Empty space",
                _platforms.Count == 0 ? "Add games from the menu in Gamepad mode" : "Folders you make will go here");
    }

    private void ShowStage(HandheldStageKind kind, string title, string subtitle, bool keepArt = false)
    {
        CancelStageArt(keepArt);
        Stage.Kind = kind;
        Stage.Title = title;
        Stage.Subtitle = subtitle;
        Stage.Monogram = string.Empty;
        Stage.AchievementText = null;
        Stage.AppIcon = null;
        Stage.Tint = Brushes.Gray;
        Stage.Medium = null;
        Stage.Glyph = kind switch
        {
            HandheldStageKind.AllApps => HandheldGlyph.AllApps,
            HandheldStageKind.Menu => HandheldGlyph.Menu,
            HandheldStageKind.EmptySlot => HandheldGlyph.Plus,
            HandheldStageKind.Header => OpenFolder?.IsSortFocused == true ? HandheldGlyph.Sort : HandheldGlyph.Back,
            _ => HandheldGlyph.None,
        };
    }

    private void ShowFolderStage(HandheldPlatform platform, string subtitle)
    {
        ShowStage(HandheldStageKind.Folder, platform.Name, subtitle);
        Stage.Monogram = platform.ShortName;
        Stage.AccentBrush = Color.TryParse(platform.AccentColor, out var color) ? new SolidColorBrush(color) : Brushes.Gray;
    }

    private long _stageGameId = -1;

    private void ShowGameStage(HandheldGameEntry game, Bitmap? interimCover = null)
    {
        if (Stage.Kind == HandheldStageKind.Game && _stageGameId == game.Id)
            return;
        // Game to game keeps the label up until the next one is ready, rather than flashing it blank.
        ShowStage(HandheldStageKind.Game, game.Title, GameSubtitle(game), keepArt: Stage.Kind == HandheldStageKind.Game);
        _stageGameId = game.Id;
        Stage.AchievementText = game.AchievementText;
        if (_platforms.TryGetValue(game.SystemId, out var platform) && Color.TryParse(platform.AccentColor, out var accent))
        {
            Stage.MediumAccent = accent;
            Stage.Tint = new SolidColorBrush(accent);
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
        ShowStage(HandheldStageKind.Hero, game.Title, HeroSubtitle(game));
        _stageGameId = game.Id;
        Stage.AchievementText = game.AchievementText;
        if (_platforms.TryGetValue(game.SystemId, out var platform) && Color.TryParse(platform.AccentColor, out var accent))
            Stage.Tint = new SolidColorBrush(accent);
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
            if (hero && cover is not null && await PickTintAsync(cover) is { } tint && Current())
                Stage.Tint = tint;
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

    /// <summary>The cover's strongest hue as a brush (the artwork palette engine's accent), or null for grey art.</summary>
    private static async Task<IBrush?> PickTintAsync(Bitmap cover)
    {
        // Copying the pixels touches the live bitmap, so it stays on this (UI) thread; the analysis does not.
        if (ArtworkPaletteExtractor.CopyPixels(cover) is not { } pixels)
            return null;
        var palette = await Task.Run(() => ArtworkPaletteExtractor.FromBgraPixels(pixels, previousIsDark: false));
        return palette is not null && Color.TryParse(palette.Accent, out var accent) ? new SolidColorBrush(accent) : null;
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

    private void UpdateHints()
    {
        Hints.Clear();
        if (IsMenuOpen)
        {
            Hints.Add(new HandheldHint("A", "Select"));
            Hints.Add(new HandheldHint("B", "Close"));
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
                Hints.Add(new HandheldHint("A", "Open"));
                Hints.Add(new HandheldHint("B", "Back"));
                return;
            case HandheldZone.Header:
                Hints.Add(new HandheldHint("A", OpenFolder?.IsSortFocused == true ? "Change" : "Back"));
                Hints.Add(new HandheldHint("B", "Close"));
                return;
        }
        if (OpenFolder is { } folder)
        {
            if (folder.SelectedTile is not null)
                Hints.Add(new HandheldHint("A", "Play"));
            Hints.Add(new HandheldHint("B", "Back"));
            return;
        }
        if (!_layout.SlotAt(_home.Page, _home.Slot).IsEmpty)
            Hints.Add(new HandheldHint("A", "Open"));
        Hints.Add(new HandheldHint(string.Empty, "Menu", HandheldGlyph.Menu));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopStatusUpdates();
        StopMediumTimer();
        CloseTitle();
        CloseFolder();
        CancelStageArt();
    }
}
