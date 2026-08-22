using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Rendering;
using EmuShelf.App.Diagnostics;
using EmuShelf.App.Services;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Input;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Shell;
using EmuShelf.Core.Storage;
using EmuShelf.Core.Systems;
using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Systems;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Android;
using EmuShelf.Integrations.Emulators.Rpcs3;

namespace EmuShelf.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const int SearchDebounceMs = 250;
    // Every platform/scope/layout/sort/column change rewrites the whole settings.json (temp-write +
    // rename). Browsing platform-to-platform at 500 ms coalescing meant dozens of full rewrites a minute
    // — needless flash wear on a handheld, and on Android it also fed MediaStore/FUSE scan churn. A few
    // seconds coalesces active browsing into a handful of writes; the resting selection is what matters,
    // and it's still captured. Nothing is lost on app close: the shell flushes any pending save on
    // background/close (see FlushPendingLibraryViewStateSave).
    private const int ViewStateSaveDebounceMs = 2500;
    // Fast LB/RB cycling changes the selected platform many times a second; each change used to run a
    // full clear-and-rebuild of the grid (BeginScopeChange + a fresh DB query + hundreds of new
    // GameViewModels), which is what blanked covers, dropped the selector and reset focus mid-scroll.
    // Coalesce a burst into one reload of the platform the user settles on.
    private const int PlatformReloadDebounceMs = 180;
    private static readonly TimeSpan GamepadReturnInputGuard = TimeSpan.FromMilliseconds(500);
    private static readonly StringComparer PathComparer = FilePathComparison.Comparer;

    /// <summary>How long a completed action's result stays on screen before dismissing itself.</summary>
    private static readonly TimeSpan InfoStatusLifetime = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Failures get noticeably longer than a result does. The toast is the only place a failed
    /// import or launch is reported, so it has to outlast a glance away from the screen — but it
    /// still clears itself, because a stale error on a working library is its own kind of wrong.
    /// </summary>
    private static readonly TimeSpan ErrorStatusLifetime = TimeSpan.FromSeconds(15);

    private readonly IGameLibrary _library;
    private readonly IFolderScanner _scanner;
    private readonly IGameImportRules _importRules;
    private readonly IAvailabilityChecker _availabilityChecker;
    private readonly IDialogService _dialogs;
    // EmuShelf's data root, surfaced in Settings so the user can reveal it. Null in design/tests.
    private readonly string? _dataDirectory;
    private readonly IEmulatorLaunchService _launchService;
    private readonly IFileRevealService _fileReveal;
    private readonly IEmulatorConfigurationStore _emulatorConfigurations;
    private readonly IReadOnlyList<EmulatorDefinition> _emulators;
    private readonly IGameCoverService _covers;
    private readonly IGameDetailsStore? _gameDetails;
    private readonly IAppThemeService _themeService;
    private readonly IInterfaceModeService? _interfaceModeService;
    private readonly IApplicationLifetimeService? _applicationLifetime;
    private readonly AppUpdateCoordinator? _updates;
    private readonly IOnScreenKeyboardService _onScreenKeyboard;
    private readonly IGameMetadataService _metadataService;
    private readonly IGameMetadataStore? _metadataStore;
    private readonly IMetadataPreferencesService _metadataPreferences;
    private readonly IRetroAchievementsIdentificationService? _retroAchievements;
    private readonly IRetroAchievementsReadStore? _retroAchievementsRead;
    private readonly IRetroAchievementsAccountService? _retroAccount;
    private readonly IScreenScraperAccountService? _screenScraperAccount;
    private readonly IScreenScraperPreviewService? _screenScraperPreview;
    private readonly IGameScrapeApplicationService? _scrapeApply;
    private readonly IScreenScraperBatchService? _scrapeBatch;
    private readonly IRemoteArtworkDownloader? _artworkDownloader;
    private readonly IGameArtworkSearchProvider? _artworkSearch;
    private readonly ISettingsService? _settingsService;
    private readonly IRetroAchievementsMatchingService? _retroMatching;
    private readonly IRetroAchievementsProgressService? _retroProgress;
    private readonly IRetroAchievementsDetailsService? _retroDetails;
    private readonly IRetroAchievementsRefreshService? _retroRefresh;
    private readonly IRetroAchievementsBadgeCache? _retroBadges;
    // Coordinates the full identify → match → progress sequence. Individual services also
    // serialize their own work, but this prevents an import finishing halfway through a connect
    // and leaving newly hashed games unmatched.
    private readonly SemaphoreSlim _retroAchievementsPipeline = new(1, 1);
    // Cover loads run with AllowConcurrentExecutions, so a screenful of tiles realizing at once (or a
    // startup rebuild) would otherwise launch dozens of decode+thumbnail Task.Run items simultaneously
    // and starve the thread pool — covers popped in staggered and the whole app felt choppy. Cap the
    // fleet to a few in-flight decodes; the rest queue and finish smoothly. Scaled to the machine so a
    // dual-core box is not over-subscribed and a Deck (or bigger) still fills quickly.
    private readonly SemaphoreSlim _coverDecodeGate =
        new(Math.Clamp(Environment.ProcessorCount / 2, 2, 4));
    private readonly IAppLogger _logger;
    private readonly IReadOnlyDictionary<string, GameSystem> _systemsById;

    private readonly DispatcherTimer _searchDebounce;
    private readonly DispatcherTimer _statusDismiss;
    private readonly DispatcherTimer _viewStateSave;
    private readonly DispatcherTimer _platformReloadDebounce;
    private readonly DispatcherTimer _shelfMotionTimer;
    private readonly PhysicalShelfMotionModel _shelfMotion = new();
    private long _shelfMotionTimestamp;
    private readonly DispatcherTimer _shelfLaunchTimer;
    private readonly PhysicalShelfLaunchTransitionModel _shelfLaunchTransition = new();
    private long _shelfLaunchTimestamp;
    private TaskCompletionSource? _shelfLaunchCompletion;
    private TaskCompletionSource? _platformReloadCompletion;
    private readonly ILibraryViewStateService _libraryViewState;
    private bool _isRestoringViewState;
    private readonly List<GameViewModel> _systemGames = [];
    private readonly HashSet<long> _deferredCoverLoads = [];
    private GameViewModel? _selectionAnchor;
    private HashSet<GameViewModel>? _marqueeBaseSelection;
    // The selected count last broadcast to the per-game context-menu strings. Those strings depend
    // only on the count, so the O(N) broadcast is skipped while a rubber-band drag leaves it unchanged.
    private int _broadcastSelectionCount = -1;
    private bool _isFrontendSuspended;
    private DateTimeOffset _gamepadInputGuardUntil;
    private string _appliedSearchText = string.Empty;
    private string? _displayedScopeKey;
    private readonly Dictionary<string, long> _focusedGameByScope = new(StringComparer.Ordinal);

    // "Match colours to artwork": as focus settles on a game its cover drives a live palette. Debounced
    // so fast scrolling does not thrash, cached per cover so re-focus is instant, and the last dark/light
    // reading is carried forward so the factory's hysteresis stops the whole UI strobing between covers.
    private readonly DispatcherTimer _ambientThemeDebounce;
    private readonly Dictionary<string, ArtworkPalette> _ambientPaletteCache = new(StringComparer.Ordinal);
    private GameViewModel? _ambientPendingGame;
    private bool? _ambientLastIsDark;

    // The spotlight hero shows the focused game's fan art + rating. Only one game's fan-art bitmap is
    // ever decoded at a time (released as focus moves) so a long list never accumulates full-size
    // images. A generation counter discards a decode that finished after focus moved on.
    private int _spotlightHeroGeneration;

    // Built GameViewModel lists per scope key (system:{id} / AllGames / RecentlyAdded). Navigating to
    // an already-visited scope reuses its list instantly instead of re-querying the DB and rebuilding
    // hundreds of view models, which is what made fast LB/RB cycling thrash. The cache owns these view
    // models; it is dropped wholesale (forceRebuild) whenever the underlying library data changes
    // (add/remove/rename/rescan, availability and achievements passes), so a rebuild always reflects
    // the DB. Covers stay warm across switches because the same view models are reused.
    private readonly Dictionary<string, List<GameViewModel>> _scopeCache = new(StringComparer.Ordinal);

    // Scope keys whose displayed view models have had their scraped-metadata projection loaded. The
    // bulk read only runs for the Desktop list view (M40 item 2), so grid/gamepad scopes skip it and
    // load lazily if the user later switches to the list. Cache reuse keeps the same VM instances, so
    // a scope already in this set still has its projections.
    private readonly HashSet<string> _scopesWithProjections = new(StringComparer.Ordinal);

    // Bumped on every reload so a slow load that finishes after a newer one is discarded,
    // keeping the shown games in sync with the current selection.
    private int _loadGeneration;
    private Task _selectedSystemLoad = Task.CompletedTask;

    public ObservableCollection<GameSystem> Systems { get; }
    public ObservableCollection<GameSystem> NavigationSystems { get; }
    public ObservableCollection<GamepadPlatformTabViewModel> GamepadPlatforms { get; }
    public BulkObservableCollection<GameViewModel> Games { get; } = [];

    /// <summary>
    /// Whether the effect-on tube renderer can bring up GL this session. Latches off once ruled out:
    /// a context that failed to come up will not come up later.
    /// </summary>
    /// <remarks>
    /// Kept separate from the effect-off in-place host's support (<see cref="InlineSceneSupported"/>)
    /// so one path's GL failure never disables the other. Collapsing the two onto one flag is the bug
    /// that turned an effect-off failure into a whole shelf of flat covers for the session, tube
    /// included — see DECISIONS 2026-08-16.
    /// </remarks>
    private bool _shelfHeroSupported = true;

    /// <summary>
    /// Whether the effect-off in-place host can bring up GL this session. Independent of the tube:
    /// when it fails, the effect-off shelf falls back to the tube drawn flat, not to flat covers.
    /// </summary>
    private bool _inlineShelfSupported = true;

    /// <summary>The effect-on/full-bleed tube renderer's own GL support (its host's IsSceneSupported).</summary>
    public bool TubeSceneSupported => _shelfHeroSupported;

    /// <summary>The effect-off in-place host's own GL support (its host's IsSceneSupported).</summary>
    public bool InlineSceneSupported => _inlineShelfSupported;

    /// <summary>
    /// Whether a 3D renderer draws the shelf in the current mode; the flat 2D fallback shows when it
    /// does not. Mode-aware: effect-on needs the tube, effect-off takes the in-place host or, if that
    /// could not start, the tube drawn flat.
    /// </summary>
    public bool ShelfSceneSupported =>
        CrtScreenEffect ? _shelfHeroSupported : (_inlineShelfSupported || _shelfHeroSupported);

    /// <summary>
    /// Rules out the effect-on tube renderer for the session.
    /// </summary>
    /// <remarks>
    /// Called by the view when the tube's <c>MediaShelf3DControl</c> reports it could not bring up a
    /// GL context. Games realized later pick this up through <see cref="ApplyShelfHeroSupport"/>, so a
    /// scope switch after the failure does not quietly re-enable a hero that cannot render. Does not
    /// touch the in-place host's support — if only the tube is gone, the effect-off shelf can still
    /// render 3D through the in-place host.
    /// </remarks>
    /// <param name="reason">Why the GPU path is unavailable, for the log. A silent revert to flat
    /// covers is indistinguishable from the feature never having been built, and on a machine we
    /// cannot test on this exception — a driver's shader info log, most likely — is the whole
    /// diagnosis.</param>
    public void DisableShelfHero(Exception? reason = null)
    {
        if (!_shelfHeroSupported)
        {
            return;
        }

        _logger.Warning(
            "The couch shelf's effect-on tube could not start; falling back for that mode.", reason);

        _shelfHeroSupported = false;
        OnPropertyChanged(nameof(ShelfSceneSupported));
        OnPropertyChanged(nameof(TubeSceneSupported));
        OnPropertyChanged(nameof(ShowShelfFlatBackdrop));
        // Only games flip to flat covers, and only when the current mode has no 3D renderer left; the
        // per-game flag itself is keyed on the tube, which is correct because the flat strip only
        // shows once the tube is gone (effect-on) or both renderers are gone (effect-off).
        foreach (var game in Games)
        {
            game.ShelfHeroSupported = false;
        }
    }

    /// <summary>
    /// Rules out the effect-off in-place host for the session, leaving the tube untouched.
    /// </summary>
    /// <remarks>
    /// Called by the view when the in-place host reports it could not bring up a GL context — the
    /// case that, collapsed onto the tube's flag, produced the Steam Deck blackout. The effect-off
    /// shelf now falls back to the tube drawn flat (<see cref="ShowCouchScene"/> takes the shelf when
    /// the in-place host is out), so the models stay 3D; the tube, and therefore the effect-on mode,
    /// is never disabled by this.
    /// </remarks>
    public void DisableInlineShelf(Exception? reason = null)
    {
        if (!_inlineShelfSupported)
        {
            return;
        }

        _logger.Warning(
            "The couch shelf's effect-off in-place scene could not start; using the tube drawn flat.",
            reason);

        _inlineShelfSupported = false;
        OnPropertyChanged(nameof(InlineSceneSupported));
        OnPropertyChanged(nameof(ShowInlineShelfScene));
        OnPropertyChanged(nameof(ShowCouchScene));
        OnPropertyChanged(nameof(IsShelfTubeActive));
        OnPropertyChanged(nameof(ShelfSceneSupported));
        OnPropertyChanged(nameof(ShowShelfFlatBackdrop));
    }

    /// <summary>Applies the session's hero support to a freshly built game list.</summary>
    private void ApplyShelfHeroSupport(IEnumerable<GameViewModel> games)
    {
        if (_shelfHeroSupported)
        {
            return;
        }

        foreach (var game in games)
        {
            game.ShelfHeroSupported = false;
        }
    }

    // Row projection of Games for the gamepad grid. The grid is rendered as a virtualized ListBox with
    // one row per line, so only the ~5 visible rows realize (mature couch-UI pattern) — vastly cheaper
    // than laying out every tile, and it avoids the phantom-cell defect of a virtualized UniformGrid.
    // Each row holds exactly GamepadColumnCount games, so the rendered column count is guaranteed to
    // equal the value navigation uses. Navigation still runs on the flat Games list + index%columns.
    public BulkObservableCollection<IReadOnlyList<GameViewModel>> GamepadRows { get; } = [];

    // Row projection of the focused game's visible achievements for the gamepad achievements grid.
    // Same row-virtualized ListBox pattern as GamepadRows: only the on-screen rows realize, and it
    // avoids the phantom-cell / top-left-hole defect a virtualized UniformGridLayout produced, which
    // dropped achievement tiles out of the grid entirely. Each row holds exactly
    // GamepadAchievementColumnCount tiles, so the rendered column count always equals the navigation
    // stride. Navigation still runs on the flat VisibleAchievements list + index%columns.
    public BulkObservableCollection<IReadOnlyList<AchievementRowViewModel>> GamepadAchievementRows { get; } = [];

    public ObservableCollection<GamepadOverlayOptionViewModel> GamepadOverlayOptions { get; } = [];

    [ObservableProperty]
    public partial GameSystem? SelectedSystem { get; set; }

    /// <summary>
    /// Ids of the systems that lead their manufacturer group in <see cref="NavigationSystems"/> —
    /// the first visible system of each manufacturer, in list order. The Desktop console list shows
    /// a manufacturer header above exactly these rows. A fresh set instance is assigned whenever the
    /// visible set changes so the header bindings re-evaluate.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlySet<string> GroupLeaderSystemIds { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    [ObservableProperty]
    public partial LibraryScope CurrentLibraryScope { get; set; } = LibraryScope.System;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsGridView { get; set; } = true;

    /// <summary>Gamepad (couch) mode only: which couch layout is on screen — the cover grid, the
    /// spotlight (list + fanart hero), or the physical-media shelf. Chosen from the system-menu picker
    /// and remembered across launches.</summary>
    [ObservableProperty]
    public partial GamepadLibraryLayout GamepadLayout { get; set; } = GamepadLibraryLayout.Grid;

    /// <summary>The spotlight layout is on screen. Kept as a computed alias over
    /// <see cref="GamepadLayout"/> so the many spotlight-only checks and XAML bindings are unchanged.</summary>
    public bool IsGamepadSpotlightView => GamepadLayout == GamepadLibraryLayout.Spotlight;

    /// <summary>The physical-media shelf layout is on screen.</summary>
    public bool IsGamepadShelfView => GamepadLayout == GamepadLibraryLayout.Shelf;

    /// <summary>The cover grid is the active layout. Drives the focused-game dock, which the grid
    /// shows and the other two layouts (each carrying their own title/hero) hide.</summary>
    public bool IsGamepadGridLayout => GamepadLayout == GamepadLibraryLayout.Grid;

    /// <summary>In the spotlight hero, whether the Achievements action is armed (so A opens it)
    /// instead of Play, the default. Left/Right move between the two; it resets to Play whenever the
    /// focused game changes or the couch layout toggles.</summary>
    [ObservableProperty]
    public partial bool IsSpotlightAchievementsFocused { get; set; }

    /// <summary>Play is armed when the spotlight is showing and Achievements isn't. Drives its ring.</summary>
    public bool IsSpotlightPlayFocused => IsGamepadSpotlightView && !IsSpotlightAchievementsFocused;

    partial void OnIsSpotlightAchievementsFocusedChanged(bool value) =>
        OnPropertyChanged(nameof(IsSpotlightPlayFocused));

    [ObservableProperty]
    public partial LibrarySortColumn SortColumn { get; set; } = LibrarySortColumn.Title;

    [ObservableProperty]
    public partial bool SortDescending { get; set; }

    // Per-column arrow shown in the list header (empty unless that column is the active sort).
    public string TitleSortGlyph => SortGlyph(LibrarySortColumn.Title);
    public string ConsoleSortGlyph => SortGlyph(LibrarySortColumn.Console);
    public string FormatSortGlyph => SortGlyph(LibrarySortColumn.Format);
    public string AchievementsSortGlyph => SortGlyph(LibrarySortColumn.Achievements);
    public string TexturesSortGlyph => SortGlyph(LibrarySortColumn.Textures);
    public string StatusSortGlyph => SortGlyph(LibrarySortColumn.Status);

    private string SortGlyph(LibrarySortColumn column) =>
        SortColumn == column ? (SortDescending ? "▼" : "▲") : string.Empty;

    partial void OnSortColumnChanged(LibrarySortColumn value)
    {
        NotifySortGlyphs();
        NotifyGamepadSortSelection();
        NotifyGamepadSortDirection();
        ScheduleLibraryViewStateSave();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        NotifySortGlyphs();
        NotifyGamepadSortDirection();
        ScheduleLibraryViewStateSave();
    }

    partial void OnIsGridViewChanged(bool value)
    {
        ScheduleLibraryViewStateSave();
        // Switching to the list view: load the scraped-metadata projection if this scope skipped it
        // while the grid was showing (M40 item 2).
        if (!value)
            EnsureDetailsProjectionsLoaded();
    }

    partial void OnGamepadLayoutChanged(GamepadLibraryLayout value)
    {
        PerfTrace.Event($"EVENT layout->{value}");
        ScheduleLibraryViewStateSave();
        OnPropertyChanged(nameof(IsGamepadSpotlightView));
        OnPropertyChanged(nameof(IsGamepadShelfView));
        OnPropertyChanged(nameof(IsGamepadGridLayout));
        OnPropertyChanged(nameof(ShowGamepadGrid));
        OnPropertyChanged(nameof(ShowGamepadSpotlight));
        OnPropertyChanged(nameof(ShowGamepadShelf));
        OnPropertyChanged(nameof(ShowShelfTube));
        OnPropertyChanged(nameof(ShowCouchScene));
        OnPropertyChanged(nameof(ShowInlineShelfScene));
        OnPropertyChanged(nameof(ShowShelfFlatBackdrop));
        OnPropertyChanged(nameof(IsShelfTubeActive));
        OnPropertyChanged(nameof(ShelfSceneItems));
        NotifySaveSyncPresentationChanged();

        // Leaving the shelf layout: no launch pose should be left mid-flight for the next visit.
        if (value != GamepadLibraryLayout.Shelf)
            AbandonOrphanedShelfLaunchTransition();

        OnPropertyChanged(nameof(IsGridViewModeSelected));
        OnPropertyChanged(nameof(IsListViewModeSelected));
        OnPropertyChanged(nameof(IsShelfViewModeSelected));
        IsSpotlightAchievementsFocused = false; // the hero always opens on Play
        OnPropertyChanged(nameof(IsSpotlightPlayFocused));
        if (value == GamepadLibraryLayout.Spotlight)
            LoadSpotlightHero(FocusedGame);
        else
            ClearSpotlightHero();

        if (value == GamepadLibraryLayout.Shelf)
        {
            SnapShelfToFocusedGame();
            if (FocusedGame is { } shelfGame)
                PrefetchCoversAroundFocus(shelfGame);
        }
        else
            _shelfMotionTimer.Stop();
    }

    /// <summary>Flips the couch layout between the cover grid and the spotlight list + hero. The
    /// shelf is reached from the picker, not this toggle, which stays a binary grid⇄spotlight flip.</summary>
    [RelayCommand]
    private void ToggleGamepadView()
    {
        if (IsGamepadMode)
            GamepadLayout = IsGamepadSpotlightView ? GamepadLibraryLayout.Grid : GamepadLibraryLayout.Spotlight;
    }

    /// <summary>The couch layout picker shown at the top of the system menu — the three tiles light
    /// their active one and re-raise when the layout changes.</summary>
    public bool IsGridViewModeSelected => GamepadLayout == GamepadLibraryLayout.Grid;
    public bool IsListViewModeSelected => GamepadLayout == GamepadLibraryLayout.Spotlight;
    public bool IsShelfViewModeSelected => GamepadLayout == GamepadLibraryLayout.Shelf;

    /// <summary>Which region of the system menu owns the focus ring: the view-mode row, the sort row, or
    /// the option list below them. Up/Down walk between the regions; Left/Right pick within a row and
    /// apply live; A is inert on either row. Was a single bool before the sort row was added.</summary>
    [ObservableProperty]
    public partial GamepadMenuFocusRegion MenuFocusRegion { get; set; } = GamepadMenuFocusRegion.Options;

    /// <summary>Focus ring is on the view-mode row. Computed alias over <see cref="MenuFocusRegion"/>,
    /// kept so existing XAML bindings and tests keep working.</summary>
    public bool IsGamepadViewModeRowFocused => MenuFocusRegion == GamepadMenuFocusRegion.ViewMode;

    /// <summary>Focus ring is on the sort row.</summary>
    public bool IsGamepadSortRowFocused => MenuFocusRegion == GamepadMenuFocusRegion.Sort;

    partial void OnMenuFocusRegionChanged(GamepadMenuFocusRegion value)
    {
        OnPropertyChanged(nameof(IsGamepadViewModeRowFocused));
        OnPropertyChanged(nameof(IsGamepadSortRowFocused));
        UpdateGamepadOverlayOptionFocus();
    }

    /// <summary>The couch layouts in the picker's Left→Right tile order. D-pad Left/Right steps this
    /// list (clamped); each tile also sets its layout directly on click.</summary>
    private static readonly GamepadLibraryLayout[] GamepadLayoutOrder =
        [GamepadLibraryLayout.Grid, GamepadLibraryLayout.Spotlight, GamepadLibraryLayout.Shelf];

    /// <summary>Selects the cover-grid couch layout. Bound to the Grid tile.</summary>
    [RelayCommand]
    private void SelectGridViewMode()
    {
        if (IsGamepadMode)
            GamepadLayout = GamepadLibraryLayout.Grid;
    }

    /// <summary>Selects the spotlight list couch layout. Bound to the List tile.</summary>
    [RelayCommand]
    private void SelectListViewMode()
    {
        if (IsGamepadMode)
            GamepadLayout = GamepadLibraryLayout.Spotlight;
    }

    /// <summary>Selects the physical-media shelf couch layout. Bound to the Shelf tile.</summary>
    [RelayCommand]
    private void SelectShelfViewMode()
    {
        if (IsGamepadMode)
            GamepadLayout = GamepadLibraryLayout.Shelf;
    }

    /// <summary>Steps the view-mode picker one tile Left (-1) or Right (+1), clamped at the ends.
    /// Drives the D-pad on the focused view-mode row.</summary>
    private void MoveGamepadViewModeSelection(int delta)
    {
        if (!IsGamepadMode)
            return;
        var index = Array.IndexOf(GamepadLayoutOrder, GamepadLayout);
        if (index < 0)
            index = 0;
        GamepadLayout = GamepadLayoutOrder[Math.Clamp(index + delta, 0, GamepadLayoutOrder.Length - 1)];
    }

    // ---- Gamepad "Sort by" row (Start menu). Reuses the view-mode card component and drives the same
    // global SortColumn/SortDescending the desktop uses, so couch and desktop stay in sync and the choice
    // persists across restarts for free. ----

    // The four couch sort options, in Left/Right order, each an existing sort column.
    private static readonly LibrarySortColumn[] GamepadSortColumns =
        [LibrarySortColumn.LastPlayed, LibrarySortColumn.DateAdded, LibrarySortColumn.Title, LibrarySortColumn.Rating];

    public bool IsGamepadSortRecentlyPlayedSelected => SortColumn == LibrarySortColumn.LastPlayed;
    public bool IsGamepadSortRecentlyAddedSelected => SortColumn == LibrarySortColumn.DateAdded;
    public bool IsGamepadSortTitleSelected => SortColumn == LibrarySortColumn.Title;
    public bool IsGamepadSortRatingSelected => SortColumn == LibrarySortColumn.Rating;

    private void NotifyGamepadSortSelection()
    {
        OnPropertyChanged(nameof(IsGamepadSortRecentlyPlayedSelected));
        OnPropertyChanged(nameof(IsGamepadSortRecentlyAddedSelected));
        OnPropertyChanged(nameof(IsGamepadSortTitleSelected));
        OnPropertyChanged(nameof(IsGamepadSortRatingSelected));
    }

    /// <summary>Applies one of the couch sort options. Sets the shared sort state DIRECTLY — not via the
    /// list-header <see cref="SortByCommand"/>, whose toggle semantics would force ascending and float
    /// never-played / unrated games to the top — then re-sorts. Each option carries its own direction:
    /// recency and rating are newest / highest first, title is A–Z.</summary>
    [RelayCommand]
    private void SelectGamepadSort(LibrarySortColumn column)
    {
        if (!IsGamepadMode)
            return;
        SortColumn = column;
        SortDescending = column is not LibrarySortColumn.Title;
        ApplyFilter();
    }

    // Left/Right on the sort row steps through GamepadSortColumns, clamped at the ends like the grid.
    private void MoveGamepadSortSelection(int delta)
    {
        var current = Array.IndexOf(GamepadSortColumns, SortColumn);
        if (current < 0)
            current = 0; // current sort isn't a couch option (e.g. set on desktop): step from the first
        SelectGamepadSort(GamepadSortColumns[Math.Clamp(current + delta, 0, GamepadSortColumns.Length - 1)]);
    }

    /// <summary>Reverses the current couch sort's direction. Invoked on A (Confirm) while the sort row is
    /// focused — the desktop list's ▲/▼ toggle has no other couch analogue. The header arrow reflects it.</summary>
    private void ToggleGamepadSortDirection()
    {
        if (!IsGamepadMode)
            return;
        SortDescending = !SortDescending;
        ApplyFilter();
    }

    /// <summary>Direction arrow shown in the couch sort header: down = descending, up = ascending.</summary>
    public string GamepadSortDirectionArrow => SortDescending ? "↓" : "↑";

    /// <summary>Plain-language direction for the couch sort header, phrased per field.</summary>
    public string GamepadSortDirectionSummary => (SortColumn, SortDescending) switch
    {
        (LibrarySortColumn.Title, false) => "A to Z",
        (LibrarySortColumn.Title, true) => "Z to A",
        (LibrarySortColumn.Rating, false) => "Lowest first",
        (LibrarySortColumn.Rating, true) => "Highest first",
        (_, false) => "Oldest first",
        (_, true) => "Newest first",
    };

    private void NotifyGamepadSortDirection()
    {
        OnPropertyChanged(nameof(GamepadSortDirectionArrow));
        OnPropertyChanged(nameof(GamepadSortDirectionSummary));
    }

    private void NotifySortGlyphs()
    {
        OnPropertyChanged(nameof(TitleSortGlyph));
        OnPropertyChanged(nameof(ConsoleSortGlyph));
        OnPropertyChanged(nameof(FormatSortGlyph));
        OnPropertyChanged(nameof(AchievementsSortGlyph));
        OnPropertyChanged(nameof(TexturesSortGlyph));
        OnPropertyChanged(nameof(StatusSortGlyph));
        UpdateColumnSortGlyphs();
    }

    // ---- M40: configurable Desktop list-view columns ------------------------------------------

    // The ListBox item padding (12 each side); the row-scroller viewport already excludes the
    // vertical scrollbar, so this is the only chrome the fallback estimate has to guess.
    private const double ListRowPadding = 12 + 12;

    private bool _columnsInitialized;

    /// <summary>Width actually available for a row's cells — the ListBox's content-viewport width
    /// (which already excludes the vertical scrollbar, and is 0-width on macOS overlay scrollbars)
    /// minus the item padding, reported by the view. Drives the flex (Title) column so it fills the
    /// row exactly with no permanent right gap. The grid scroller that feeds
    /// <see cref="LibraryViewportWidth"/> is collapsed in list mode, hence a separate value.</summary>
    [ObservableProperty]
    public partial double ListViewportWidth { get; set; }

    partial void OnListViewportWidthChanged(double value) => RecomputeColumnWidths();

    /// <summary>Every list-view column in display order — the source for the column picker and for
    /// persistence. See DECISIONS 2026-08-08.</summary>
    public ObservableCollection<LibraryColumn> Columns { get; } = [];

    /// <summary>The visible columns in order, bound by the list header and every row so hiding,
    /// reordering, or resizing a column is a data change rather than a control-internal one.</summary>
    public ObservableCollection<LibraryColumn> VisibleColumns { get; } = [];

    private void InitializeColumns()
    {
        if (_columnsInitialized)
            return;

        foreach (var column in LibraryColumnCatalog.CreateDefault())
        {
            column.PropertyChanged += OnLibraryColumnPropertyChanged;
            Columns.Add(column);
        }

        _columnsInitialized = true;
        RebuildVisibleColumns();
        RecomputeColumnWidths();
        UpdateColumnSortGlyphs();
    }

    private void OnLibraryColumnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Resizing a fixed column shrinks/grows the flex column so the row still fills exactly; the
        // flex column's own width is the computed result, so ignore it to avoid recomputing forever.
        if (e.PropertyName == nameof(LibraryColumn.Width))
        {
            if (sender is LibraryColumn { IsFlex: false })
            {
                RecomputeColumnWidths();
                ScheduleLibraryViewStateSave();
            }
            return;
        }

        if (e.PropertyName != nameof(LibraryColumn.IsVisible))
            return;

        // A hidden Title would leave the table with no identifying column; the picker disables that
        // toggle, but guard here too so a bad persisted state can never blank the view.
        if (sender is LibraryColumn { IsFlex: true, IsVisible: false } flex)
        {
            flex.IsVisible = true;
            return;
        }

        RebuildVisibleColumns();
        RecomputeColumnWidths();
        ScheduleLibraryViewStateSave();
    }

    // Reconciles VisibleColumns in place with minimal Insert/RemoveAt/Move rather than Clear+Add, so
    // toggling or reordering one column only touches that column's cell in every realized row — the
    // others (and their already-decoded covers) survive instead of being torn down and reloaded.
    private void RebuildVisibleColumns()
    {
        var target = Columns.Where(column => column.IsVisible).ToList();

        for (var index = VisibleColumns.Count - 1; index >= 0; index--)
            if (!target.Contains(VisibleColumns[index]))
                VisibleColumns.RemoveAt(index);

        for (var index = 0; index < target.Count; index++)
        {
            var column = target[index];
            var current = VisibleColumns.IndexOf(column);
            if (current < 0)
                VisibleColumns.Insert(index, column);
            else if (current != index)
                VisibleColumns.Move(current, index);
        }
    }

    /// <summary>Recomputes the flex (Title) column's pixel width so the visible columns fill the row
    /// exactly. Fixed columns keep their own (optionally resized) width; only the flex column moves.</summary>
    private void RecomputeColumnWidths()
    {
        if (Columns.FirstOrDefault(column => column.IsFlex) is not { } flex)
            return;

        var others = Columns
            .Where(column => column.IsVisible && !column.IsFlex)
            .Sum(column => column.Width);
        flex.Width = Math.Max(flex.MinWidth, ListViewportWidth - others);
    }

    private void UpdateColumnSortGlyphs()
    {
        if (!_columnsInitialized)
            return;

        foreach (var column in Columns)
        {
            column.SortGlyph = column.SortColumn == SortColumn
                ? (SortDescending ? "▼" : "▲")
                : string.Empty;
        }
    }

    /// <summary>Applies a persisted column layout over the default set: reorders to the saved order,
    /// restores visibility and fixed-column widths, and tolerates unknown keys (dropped) and new
    /// columns (appended in catalog order). Called during view-state restore, so saves are suppressed.</summary>
    private void ApplyPersistedColumns(IReadOnlyList<LibraryColumnSetting> persisted)
    {
        if (persisted.Count == 0)
            return;

        var byKey = Columns.ToDictionary(column => column.Key);
        var ordered = new List<LibraryColumn>(Columns.Count);
        foreach (var setting in persisted)
        {
            if (!Enum.TryParse<LibraryColumnKey>(setting.Key, out var key) ||
                !byKey.TryGetValue(key, out var column) ||
                ordered.Contains(column))
            {
                continue;
            }

            if (column.CanHide)
                column.IsVisible = setting.IsVisible;
            if (!column.IsFlex && setting.Width > 0)
                column.Width = Math.Clamp(setting.Width, column.MinWidth, column.MaxWidth);
            ordered.Add(column);
        }

        // Columns added since the layout was saved keep their catalog position at the end.
        foreach (var column in Columns)
            if (!ordered.Contains(column))
                ordered.Add(column);

        for (var target = 0; target < ordered.Count; target++)
        {
            var current = Columns.IndexOf(ordered[target]);
            if (current != target)
                Columns.Move(current, target);
        }

        RebuildVisibleColumns();
        RecomputeColumnWidths();
    }

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Drives both how long the toast lives and the colour of its leading dot.</summary>
    [ObservableProperty]
    public partial StatusSeverity StatusSeverity { get; set; } = StatusSeverity.Info;

    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    [ObservableProperty]
    public partial string LibraryCountText { get; set; } = "0 games";

    [ObservableProperty]
    public partial bool ShowEmptyPlatforms { get; set; }

    /// <summary>True when the current filter yields at least one game (drives the views).</summary>
    [ObservableProperty]
    public partial bool HasGames { get; set; }

    /// <summary>The couch cover grid is on screen: gamepad mode, games present, grid layout.</summary>
    public bool ShowGamepadGrid => IsGamepadMode && HasGames && GamepadLayout == GamepadLibraryLayout.Grid;

    /// <summary>The couch spotlight (list + fanart hero) is on screen: gamepad mode, games present,
    /// spotlight layout.</summary>
    public bool ShowGamepadSpotlight => IsGamepadMode && HasGames && GamepadLayout == GamepadLibraryLayout.Spotlight;

    /// <summary>The couch physical-media shelf is on screen: gamepad mode, games present, shelf layout.</summary>
    public bool ShowGamepadShelf => IsGamepadMode && HasGames && GamepadLayout == GamepadLibraryLayout.Shelf;

    /// <summary>
    /// The CRT tube is on screen. Deliberately NOT gated on <see cref="HasGames"/>.
    /// </summary>
    /// <remarks>
    /// Stepping platforms with LB/RB empties the collection and refills it, so anything gated on
    /// games being present blinks off in between. For the shelf's contents that is invisible; for
    /// the tube it meant the whole effect dropped out for a frame and the bare, un-warped couch UI
    /// showed through — and worse, the GL scene was detached and rebuilt on every single platform
    /// step. The tube stays up across the gap and simply shows an empty shelf, which is also the
    /// right answer for a platform that genuinely has no games: the empty state belongs inside the
    /// television, not beside it.
    /// </remarks>
    public bool ShowShelfTube => IsGamepadMode && GamepadLayout == GamepadLibraryLayout.Shelf;

    partial void OnHasGamesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGamepadGrid));
        OnPropertyChanged(nameof(ShowGamepadSpotlight));
        OnPropertyChanged(nameof(ShowGamepadShelf));
        OnPropertyChanged(nameof(ShowShelfTube));
        OnPropertyChanged(nameof(ShowCouchScene));
        OnPropertyChanged(nameof(ShowInlineShelfScene));
        OnPropertyChanged(nameof(ShowShelfFlatBackdrop));
        OnPropertyChanged(nameof(IsShelfTubeActive));
        OnPropertyChanged(nameof(ShelfSceneItems));
        NotifySaveSyncPresentationChanged();
    }

    /// <summary>True only when the selected system has no games at all — drives the "add your first game" prompt.</summary>
    [ObservableProperty]
    public partial bool IsLibraryEmpty { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSearchEmpty { get; set; }

    /// <summary>True between a scope change and its games arriving; suppresses the empty states.</summary>
    [ObservableProperty]
    public partial bool IsLibraryLoading { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    // True only while a launch/exit cloud save sync is actually running. Physical-shelf mode keeps
    // the scene visible and uses its progress toast in both phases.
    [ObservableProperty]
    public partial bool IsSyncingSavesForLaunch { get; set; }

    // True only for the pre-launch phase of that sync — the one that must finish before the emulator
    // starts, so it holds the grid behind the large centered panel. The post-exit phase leaves this
    // false: the player is back in the library and free to browse while the upload finishes in the
    // background, with only the ordinary status toast to say so.
    [ObservableProperty]
    public partial bool IsBlockingLaunchSaveSync { get; set; }

    [ObservableProperty]
    public partial ThemePreference CurrentTheme { get; set; }

    /// <summary>When true, the couch UI recolours from the focused game's artwork; the chosen theme is
    /// the fallback for artwork with no usable colour. Offered next to the theme gallery in both modes.</summary>
    [ObservableProperty]
    public partial bool AmbientThemeFromArtwork { get; set; }

    /// <summary>Whether the couch shelf is presented through a simulated CRT tube.</summary>
    [ObservableProperty]
    public partial bool CrtScreenEffect { get; set; }

    /// <summary>
    /// The presentation the one couch renderer is handed: the tube when the effect is on, a flat
    /// compositor when it is off.
    /// </summary>
    /// <remarks>
    /// The individual parameters are fixed at the shipped defaults; this property is only the on/off
    /// switch over the top of them. If per-parameter controls are ever wanted, this is where a tuned
    /// presentation would come from instead.
    ///
    /// Off maps to <see cref="CrtPresentation.Flat"/>, not <see cref="CrtPresentation.Off"/>: the
    /// shelf keeps a single GL renderer across the toggle rather than standing up a second one, so an
    /// effect-off shelf still has to composite the captured rail, title and overlays over the media —
    /// which <see cref="CrtPresentation.Flat"/> does (an exact composite with every curve and scanline
    /// at zero) and <see cref="CrtPresentation.Off"/>'s bare resolve blit does not. Reusing the one
    /// context is what survives a driver (the Steam Deck's) that refuses to bring up a second — see
    /// DECISIONS 2026-08-16.
    /// </remarks>
    public CrtPresentation CouchCrt =>
        CrtScreenEffect ? CrtPresentation.Default : CrtPresentation.Flat;

    /// <summary>
    /// The full-bleed capture tube is on screen.
    /// </summary>
    /// <remarks>
    /// The tube is up over every couch layout when the effect is on, because the CRT is a property of
    /// the television the couch UI is shown on, not of one layout inside it — the grid and the spotlight
    /// are just as much "a console menu on a TV" as the shelf is. Desktop mode is deliberately
    /// excluded: it is a mouse-driven library window, and a warped, scanned one would be unusable.
    ///
    /// With the effect off the tube is normally idle — the effect-off shelf is drawn by the in-place
    /// host (<see cref="ShowInlineShelfScene"/>), which keeps the rail, overlays and toasts as live
    /// Avalonia around it. The one exception is the fallback: if that in-place host cannot bring up GL
    /// (the Steam Deck), the tube takes the effect-off shelf drawn flat (<see cref="CouchCrt"/>), so
    /// the models stay 3D instead of dropping to flat covers. See DECISIONS 2026-08-16.
    /// </remarks>
    public bool ShowCouchScene =>
        IsGamepadMode && (CrtScreenEffect || (IsGamepadShelfView && !_inlineShelfSupported));

    /// <summary>
    /// The in-place 3D shelf is on screen: the preferred effect-off renderer.
    /// </summary>
    /// <remarks>
    /// It sits inside the shelf's own slot rather than covering the window, so the couch UI stacks
    /// around it — rail above, title below, any overlay over it — as live Avalonia, with no capture and
    /// a redraw only when the shelf itself moves. That is why the effect-off shelf prefers it over the
    /// tube drawn flat. If it cannot bring up GL its support latches off (<see cref="DisableInlineShelf"/>)
    /// and <see cref="ShowCouchScene"/> takes over with the tube.
    /// </remarks>
    public bool ShowInlineShelfScene =>
        IsGamepadMode && IsGamepadShelfView && !CrtScreenEffect && _inlineShelfSupported;

    /// <summary>The presentation the in-place shelf is handed: a flat, opaque compositor — no capture,
    /// no distortion — so the effect-off shelf is a genuine hard-off rather than a quieter tube.</summary>
    public CrtPresentation InlineShelfCrt => CrtPresentation.Flat;

    /// <summary>
    /// How much larger than the desktop composition the 3D shelf media is framed. The desktop couch is
    /// tuned for the Steam Deck at 1.0; a handheld held at arm's length wants the media much larger, so
    /// Android raises it. Kept as a single knob here so it is easy to tune.
    /// </summary>
    public double ShelfFillScale => OperatingSystem.IsAndroid() ? 1.5 : 1.0;

    /// <summary>
    /// True on Android, where the view drops GPU-expensive per-tile decoration (blurred drop shadows and
    /// their overdraw) from the grid. Those effects recomposite every frame while the library scrolls; on
    /// a handheld GPU that is the dominant grid cost (the fan-on-scroll investigation), and desktop keeps
    /// them. Bound as a style class on the couch root, so it is a one-line reach for any effect to gate.
    /// </summary>
    public bool IsReducedEffectsPlatform => OperatingSystem.IsAndroid();

    /// <summary>
    /// The games the 3D scene draws, or nothing outside the shelf layout.
    /// </summary>
    /// <remarks>
    /// The tube runs over every couch layout, but only the shelf has physical media in it. Handing
    /// the scene an empty list on the grid and spotlight leaves it compositing the captured UI and
    /// nothing else, instead of drawing a row of cartridges on top of the cover grid. The in-place
    /// scene shares this, and is only ever attached in the shelf layout to begin with.
    /// </remarks>
    public IReadOnlyList<GameViewModel>? ShelfSceneItems => IsGamepadShelfView ? Games : null;

    /// <summary>
    /// The full-bleed tube is drawing over the shelf.
    /// </summary>
    /// <remarks>
    /// Gates the couch root's transparent background: the tube captures that root and composites it
    /// back over the 3D media, so the root must stop painting its own opaque fill or the capture
    /// returns a solid sheet with the shelf hidden behind it. True on the shelf when the effect is on,
    /// and in the effect-off fallback where the tube stands in for the in-place host. False for the
    /// in-place host, which sits in the slot and does not capture the root — there the root keeps its
    /// ordinary opaque background, which is also what the flat fallback draws on.
    /// </remarks>
    public bool IsShelfTubeActive =>
        IsGamepadMode && IsGamepadShelfView && (CrtScreenEffect || !_inlineShelfSupported);

    /// <summary>The shelf paints its own flat backdrop only when the current mode has no 3D renderer.
    /// When one is up — the tube, or the in-place host — it resolves the couch backdrop itself and
    /// draws opaque over it, and the couch root's own library fill covers the bands around the media.</summary>
    public bool ShowShelfFlatBackdrop => !ShelfSceneSupported;

    /// <summary>The library size, cached on the UI thread from <see cref="Games"/>'s CollectionChanged so
    /// the pool-thread perf sampler never reads the UI-owned collection. Volatile for cross-thread reads.</summary>
    private volatile int _perfGamesCount;

    /// <summary>
    /// A one-line snapshot of the couch state for the log-based perf sampler (<see cref="PerfTrace"/>):
    /// current layout, CRT toggle, the active render path, the selected platform/scope, and the visible
    /// library size. Read off the UI thread by the sampler, so it only reads primitives and cached values
    /// (never the UI-owned <see cref="Games"/> collection directly).
    /// </summary>
    public string PerfStateSnapshot =>
        $"layout={GamepadLayout} crt={(CrtScreenEffect ? "on" : "off")} path={PerfRenderPath} " +
        $"sys={SelectedSystem?.Name ?? CurrentLibraryScope.ToString()} games={_perfGamesCount}";

    private string PerfRenderPath => GamepadLayout switch
    {
        GamepadLibraryLayout.Grid => "grid",
        GamepadLibraryLayout.Spotlight => "spotlight",
        GamepadLibraryLayout.Shelf => ShowInlineShelfScene ? "shelf-inline-gl"
            : ShowCouchScene ? "shelf-tube"
            : "shelf-flat",
        _ => "?",
    };

    partial void OnCrtScreenEffectChanged(bool value)
    {
        PerfTrace.Event($"EVENT crt->{(value ? "on" : "off")}");
        OnPropertyChanged(nameof(CouchCrt));
        OnPropertyChanged(nameof(ShowCouchScene));
        OnPropertyChanged(nameof(ShowInlineShelfScene));
        OnPropertyChanged(nameof(IsShelfTubeActive));
        OnPropertyChanged(nameof(ShelfSceneSupported));
        OnPropertyChanged(nameof(ShowShelfFlatBackdrop));
        _ = _themeService.SetCrtScreenEffectAsync(value);
    }

    /// <summary>Follows the CRT flag when the service is changed by any path, not just this one.</summary>
    private void OnThemeServiceCrtScreenEffectChanged(object? sender, EventArgs e)
    {
        if (CrtScreenEffect != _themeService.CrtScreenEffect)
        {
            CrtScreenEffect = _themeService.CrtScreenEffect;
        }
    }

    /// <summary>Every built-in appearance, offered in Desktop Settings. The controller
    /// theme gallery projects the same instances so both modes stay in lock-step.</summary>
    public IReadOnlyList<ThemeChoiceViewModel> ThemeChoices { get; }

    [ObservableProperty]
    public partial bool IsNavigationCollapsed { get; set; }

    [ObservableProperty]
    public partial bool IsGamepadMode { get; set; }

    // Drives the `.controller-input` visual state. Mouse input is disabled outright in Gamepad mode
    // (the cursor is hidden and the gamepad surface is non-hit-testable), so controller input is
    // always the active modality and this stays true throughout a Gamepad session.
    [ObservableProperty]
    public partial bool IsGamepadControllerInputActive { get; set; } = true;

    [ObservableProperty]
    public partial GameViewModel? FocusedGame { get; set; }

    [ObservableProperty]
    public partial bool IsGameActionsOpen { get; set; }

    [ObservableProperty]
    public partial GamepadOverlayKind GamepadOverlay { get; set; }

    // Guards RequestSettingsFromGamepadAsync against a re-entrant open while its database read awaits.
    private bool _openingGamepadSettings;

    // The desktop-mode confirmation is reachable from two places — the System Menu and the "Set cover"
    // handoff overlay — so B has to return to whichever one opened it rather than always the menu.
    private GamepadOverlayKind _desktopModeConfirmationParent = GamepadOverlayKind.SystemMenu;

    // The folder chosen in the gamepad import flow, held between the OS folder pick and the system
    // choice made in the ImportSystem overlay. Cleared once the import runs or the overlay is cancelled.
    private string? _pendingImportFolder;

    [ObservableProperty]
    public partial GamepadSettingsViewModel? GamepadSettings { get; set; }

    [ObservableProperty]
    public partial int GamepadOverlaySelectionIndex { get; set; }

    [ObservableProperty]
    public partial AchievementDetailsViewModel? GamepadAchievementDetails { get; set; }

    [ObservableProperty]
    public partial AchievementRowViewModel? FocusedGamepadAchievement { get; set; }

    [ObservableProperty]
    public partial GamepadScraperViewModel? GamepadScraperDetails { get; set; }

    [ObservableProperty]
    public partial GamepadCoverSearchViewModel? GamepadCoverSearchDetails { get; set; }

    [ObservableProperty]
    public partial GamepadBatchScraperViewModel? GamepadBatchScraperDetails { get; set; }

    [ObservableProperty]
    public partial GamepadHotkeysViewModel? GamepadHotkeys { get; set; }

    public bool HasGamepadOverlay => GamepadOverlay != GamepadOverlayKind.None;
    public bool GamepadOverlayOwnsTextInput => GamepadOverlay is GamepadOverlayKind.Search or GamepadOverlayKind.Rename ||
        IsGamepadSettingsOpen && GamepadSettings?.IsTextEntryOpen == true;
    public bool IsGamepadAchievementsOpen => GamepadOverlay == GamepadOverlayKind.Achievements;
    public bool IsGamepadSearchOpen => GamepadOverlay == GamepadOverlayKind.Search;
    public bool IsGamepadRenameOpen => GamepadOverlay == GamepadOverlayKind.Rename;
    public bool IsGamepadRemoveOpen => GamepadOverlay == GamepadOverlayKind.RemoveConfirmation;
    public bool IsGamepadCoverHandoffOpen => GamepadOverlay == GamepadOverlayKind.CoverDesktopHandoff;
    public bool IsGamepadCoverSearchOpen => GamepadOverlay == GamepadOverlayKind.CoverSearch;
    public bool IsGamepadScraperOpen => GamepadOverlay == GamepadOverlayKind.Scraper;
    public bool IsGamepadBatchScraperOpen => GamepadOverlay == GamepadOverlayKind.BatchScraper;
    public bool IsGamepadSystemMenuOpen => GamepadOverlay == GamepadOverlayKind.SystemMenu;
    public bool IsGamepadSettingsOpen => GamepadOverlay == GamepadOverlayKind.Settings;
    public bool IsGamepadHotkeysOpen => GamepadOverlay == GamepadOverlayKind.Hotkeys;
    public bool IsGamepadSettingsTextEntryOpen => IsGamepadSettingsOpen && GamepadSettings?.IsTextEntryOpen == true;
    public bool IsGamepadSettingsConfirmationOpen => IsGamepadSettingsOpen && GamepadSettings?.IsConfirmationOpen == true;
    public bool IsGamepadSettingsChoicePickerOpen =>
        IsGamepadSettingsOpen && GamepadSettings?.IsChoicePickerOpen == true;
    /// <summary>Settings overlay open in its normal (non-modal) state, so the footer shows the
    /// navigation hints; entry, confirmation, and choice modals swap in their own legends.</summary>
    public bool IsGamepadSettingsNormal =>
        IsGamepadSettingsOpen && GamepadSettings?.IsNormal == true;
    public int GamepadSettingsFocusRevision => GamepadSettings?.FocusRevision ?? 0;
    public bool IsGamepadDesktopModeConfirmationOpen => GamepadOverlay == GamepadOverlayKind.DesktopModeConfirmation;
    public bool IsGamepadQuitConfirmationOpen => GamepadOverlay == GamepadOverlayKind.QuitConfirmation;
    /// <summary>The three yes/no confirmations (Remove, Desktop-mode switch, Quit). They share one
    /// standard two-button layout — an explanatory body over a right-aligned [Cancel] [action] row that
    /// Left/Right walk — instead of the vertical option list the picker overlays use.</summary>
    public bool IsGamepadConfirmationOverlay => GamepadOverlay is
        GamepadOverlayKind.RemoveConfirmation or
        GamepadOverlayKind.DesktopModeConfirmation or
        GamepadOverlayKind.QuitConfirmation;
    /// <summary>Confirmations render their Cancel/action pair in a dedicated horizontal row, so the
    /// shared vertical option list is hidden for them (see <see cref="ShowsGamepadOverlayOptions"/>).</summary>
    public bool ShowsGamepadConfirmationActions => IsGamepadConfirmationOverlay;
    public bool AreGamepadOverlayOptionsTopAligned => GamepadOverlay is
        GamepadOverlayKind.Actions or
        GamepadOverlayKind.DiscSelection or GamepadOverlayKind.SystemMenu or
        GamepadOverlayKind.ImportSystem;
    /// <summary>
    /// A floor, in DIP, for the option-list scroll region — <b>Android only</b>. The overlay Border is
    /// vertically centred and sizes to its content; on the Thor the option ScrollViewer contributes no
    /// height to that measure, so an option-list overlay <em>without</em> the system-menu picker header to
    /// prop it open (the import/actions/disc choosers) collapses its list to zero and shows nothing to pick
    /// — reproduced only on device, never in desktop headless (the plan's Milestone S chooser bug). This
    /// forces a real viewport so the list is visible and scrollable. Zero on the desktop targets, where the
    /// list already measures far taller and the pinned pixel-height snapshots must not move.
    /// </summary>
    public double GamepadOverlayOptionsMinHeight =>
        OperatingSystem.IsAndroid() && ShowsGamepadOverlayOptions ? 240 : 0;
    // The Achievements, Settings, Scraper, BatchScraper and Hotkeys overlays render their own bespoke
    // bodies and footers, so the shared option-button list and default hint legend are hidden for them.
    // (Hotkeys keeps the chrome title — it just needs its own body and hints, not a fresh header.)
    // Confirmations also swap the default legend for their own "A Confirm / B Cancel" one.
    public bool UsesGamepadDefaultOverlayHints => GamepadOverlay is not
        (GamepadOverlayKind.Achievements or GamepadOverlayKind.Search or
         GamepadOverlayKind.Rename or GamepadOverlayKind.Scraper or GamepadOverlayKind.BatchScraper or
         GamepadOverlayKind.CoverSearch or GamepadOverlayKind.Settings or GamepadOverlayKind.Hotkeys or
         GamepadOverlayKind.RemoveConfirmation or GamepadOverlayKind.DesktopModeConfirmation or
         GamepadOverlayKind.QuitConfirmation);
    public bool ShowsGamepadOverlayOptions => GamepadOverlay is not
        (GamepadOverlayKind.Achievements or GamepadOverlayKind.Search or GamepadOverlayKind.Rename or
         GamepadOverlayKind.Settings or GamepadOverlayKind.Scraper or GamepadOverlayKind.BatchScraper or
         GamepadOverlayKind.CoverSearch or GamepadOverlayKind.Hotkeys or GamepadOverlayKind.RemoveConfirmation or
         GamepadOverlayKind.DesktopModeConfirmation or GamepadOverlayKind.QuitConfirmation);
    // Confirmations render their own centred title inside the dialog card, so the chrome header title
    // is suppressed for them (it would otherwise pin a second title to the top-left of the sheet).
    public bool ShowsGamepadOverlayChromeTitle => GamepadOverlay is not
        (GamepadOverlayKind.Achievements or GamepadOverlayKind.Settings or GamepadOverlayKind.Scraper or
         GamepadOverlayKind.BatchScraper or GamepadOverlayKind.RemoveConfirmation or
         GamepadOverlayKind.DesktopModeConfirmation or GamepadOverlayKind.QuitConfirmation);
    public string GamepadOverlayTitle => GamepadOverlay switch
    {
        GamepadOverlayKind.Actions => FocusedGame is null ? "Game actions" : $"{FocusedGame.DisplayTitle} actions",
        GamepadOverlayKind.Search => "Search",
        GamepadOverlayKind.Rename => "Rename game",
        GamepadOverlayKind.DiscSelection => FocusedGame is null ? "Select disc" : $"{FocusedGame.DisplayTitle} — select disc",
        GamepadOverlayKind.ImportSystem => "Add games — choose system",
        GamepadOverlayKind.RemoveConfirmation => "Remove game?",
        GamepadOverlayKind.CoverDesktopHandoff => "Set cover",
        GamepadOverlayKind.CoverSearch => FocusedGame is null ? "Set cover" : $"Set cover — {FocusedGame.DisplayTitle}",
        GamepadOverlayKind.Scraper => "Scrape with ScreenScraper",
        GamepadOverlayKind.BatchScraper => "Scrape games with ScreenScraper",
        GamepadOverlayKind.SystemMenu => "Menu",
        GamepadOverlayKind.Settings => "Settings",
        GamepadOverlayKind.Hotkeys => "Hotkeys",
        GamepadOverlayKind.DesktopModeConfirmation => "Switch to Desktop mode?",
        GamepadOverlayKind.QuitConfirmation => "Quit EmuShelf?",
        _ => string.Empty,
    };
    [ObservableProperty]
    public partial double GamepadViewportWidth { get; set; }

    private int _gamepadColumnCount = 1;
    // Observable so the gamepad grid's UniformGrid binds its Columns to it: the rendered column count
    // is then guaranteed to equal the value navigation uses, so index%columns can never disagree with
    // the layout.
    public int GamepadColumnCount
    {
        get => _gamepadColumnCount;
        private set
        {
            if (SetProperty(ref _gamepadColumnCount, value))
                BuildGamepadRows(); // re-group into rows of the new width (e.g. on resize)
        }
    }

    // Slice the flat Games list into rows of GamepadColumnCount for the virtualized row list. Called
    // whenever Games or the column count changes; cheap (it allocates small arrays, not view models).
    private void BuildGamepadRows()
    {
        if (!IsGamepadMode)
        {
            if (GamepadRows.Count > 0)
                GamepadRows.Clear();
            return;
        }

        var columns = Math.Max(1, GamepadColumnCount);
        var rows = new List<IReadOnlyList<GameViewModel>>((Games.Count + columns - 1) / columns);
        for (var start = 0; start < Games.Count; start += columns)
        {
            var take = Math.Min(columns, Games.Count - start);
            var row = new GameViewModel[take];
            for (var offset = 0; offset < take; offset++)
                row[offset] = Games[start + offset];
            rows.Add(row);
        }

        GamepadRows.ReplaceAll(rows);
    }
    private int _gamepadAchievementColumnCount = 1;
    // Derived purely by width arithmetic (UpdateGamepadAchievementColumnCount) using the same tile
    // width + spacing the grid renders, so it always equals the rendered column count without reading
    // the visual tree. An earlier design read it back from realized tile bounds; during a refresh or
    // filter change the repeater is mid-recycle and those bounds are stale (tiles collapse into one Y
    // row, or only a partial row is realized), which produced a garbage count and, with the virtualized
    // UniformGridLayout, dropped tiles out of the grid. The arithmetic value cannot race.
    public int GamepadAchievementColumnCount
    {
        get => _gamepadAchievementColumnCount;
        private set
        {
            if (SetProperty(ref _gamepadAchievementColumnCount, value))
                BuildGamepadAchievementRows(); // re-slice into rows of the new width
        }
    }

    // Width of the achievements grid viewport (the row ListBox), reported by the view's SizeChanged.
    [ObservableProperty]
    public partial double GamepadAchievementViewportWidth { get; set; }

    partial void OnGamepadAchievementViewportWidthChanged(double value)
    {
        if (value > 0)
            UpdateGamepadAchievementColumnCount();
    }

    private void UpdateGamepadAchievementColumnCount()
    {
        if (GamepadAchievementViewportWidth <= 0)
            return;
        GamepadAchievementColumnCount = ColumnsThatFit(
            Math.Max(0, GamepadAchievementViewportWidth - GamepadAchievementGridHorizontalPadding),
            AchievementTileWidth,
            AchievementTileSpacing);
    }

    // Slice the focused game's visible achievements into rows of GamepadAchievementColumnCount for the
    // virtualized row list. Called when the visible set or the column count changes; cheap (it slices
    // references, not view models).
    private void BuildGamepadAchievementRows()
    {
        if (!IsGamepadMode || !IsGamepadAchievementsOpen ||
            GamepadAchievementDetails?.VisibleAchievements is not { Count: > 0 } achievements)
        {
            if (GamepadAchievementRows.Count > 0)
                GamepadAchievementRows.Clear();
            return;
        }

        var columns = Math.Max(1, GamepadAchievementColumnCount);
        var rows = new List<IReadOnlyList<AchievementRowViewModel>>(
            (achievements.Count + columns - 1) / columns);
        for (var start = 0; start < achievements.Count; start += columns)
        {
            var take = Math.Min(columns, achievements.Count - start);
            var row = new AchievementRowViewModel[take];
            for (var offset = 0; offset < take; offset++)
                row[offset] = achievements[start + offset];
            rows.Add(row);
        }

        GamepadAchievementRows.ReplaceAll(rows);
    }

    public int GamepadAchievementLayoutRevision { get; private set; }
    public bool HasFocusedGamepadAchievement => FocusedGamepadAchievement is not null;

    /// <summary>Width of the console/collections rail: a full label column when expanded, a
    /// narrow icon rail when collapsed so the library grid reclaims the freed horizontal space.</summary>
    public double NavigationWidth => IsNavigationCollapsed ? 72 : 246;

    /// <summary>True when the rail shows labels; the positive form keeps element-name XAML
    /// bindings simple (no negation inside a cast path).</summary>
    public bool IsNavigationExpanded => !IsNavigationCollapsed;

    partial void OnIsNavigationCollapsedChanged(bool value)
    {
        OnPropertyChanged(nameof(NavigationWidth));
        OnPropertyChanged(nameof(IsNavigationExpanded));
        ScheduleLibraryViewStateSave();
    }

    // Grid cover sizing: covers grow from a 188px floor up to a cap so a whole number of columns
    // fills the library width (no lopsided right gutter) as the window or sidebar resizes.
    private const double MinCoverWidth = 188;
    private const double MaxCoverWidth = 232;
    private const double CoverColumnSpacing = 28;    // matches UniformGridLayout MinColumnSpacing

    // Rows either side of the focused row whose covers are warmed ahead of the scroll (see
    // PrefetchCoversAroundFocus). A held d-pad steps ~one row per 110ms, so a few rows of lead lets the
    // off-thread decode stay ahead of the glide and the incoming tile is already painted.
    private const int GamepadCoverPrefetchRows = 3;
    private const int ShelfNeighbourPrefetchRadius = 3;

    // On-disk cover thumbnails are generated at this max width (mirrors GameCoverService.ThumbnailWidth).
    // A cover is decoded to the displayed pixel size, capped here so it is never upscaled past the source.
    private const int CoverThumbnailNativeWidth = 300;

    // Gamepad achievements grid: fixed 100px badge tiles with 12px gutters (mirrors the
    // Border.gamepad-achievement style and the old UniformGridLayout). The column count is derived
    // from the row ListBox width by the same arithmetic; the horizontal padding reserves the scroll
    // gutter and the row's own side padding so a row of that many tiles never overflows the viewport
    // and clips its right-hand tile.
    private const double AchievementTileWidth = 100;
    private const double AchievementTileSpacing = 12;
    private const double GamepadAchievementGridHorizontalPadding = 28;

    // Each mode measures a different element, so each has its own inset. Desktop measures the
    // ScrollViewer, and the ItemsRepeater inside it carries Margin 32/28 that the measurement
    // still includes. Gamepad measures its own ScrollViewer, whose Margin is already excluded from
    // its arranged size; its repeater carries a deliberate side gutter (GamepadGridSideGutter each
    // side) so the focused tile's accent glow — which blurs ~30px past the cover — is never shaved
    // by the scroller's clip on the edge columns. The column arithmetic subtracts both gutters so a
    // whole number of covers fills the region between them with no lopsided edge.
    private const double DesktopGridHorizontalPadding = 60;
    // The reserved gutter on each side of the gamepad grid, mirrored by the ItemsRepeater's Margin
    // in MainWindow.axaml. Must exceed the EmuFocusGlow blur radius (~30px) so edge-column focus
    // never clips. The view reads it via GamepadGridSideGutterPixels to place the selector overlay.
    internal const double GamepadGridSideGutter = 40;
    private const double GamepadGridHorizontalPadding = 2 * GamepadGridSideGutter;

    /// <summary>The per-side gutter (logical px) reserved inside the gamepad grid scroller, so the
    /// view's deterministic selector/reveal geometry uses the same value the layout is sized from.</summary>
    internal static double GamepadGridSideGutterPixels => GamepadGridSideGutter;

    /// <summary>Current width of the desktop library grid area.</summary>
    [ObservableProperty]
    public partial double LibraryViewportWidth { get; set; }

    /// <summary>Cover width computed for the active mode's viewport. The grid layout uses it as
    /// the uniform cell width (MinItemWidth) so a whole number of columns fills the row.</summary>
    [ObservableProperty]
    public partial double GridCoverWidth { get; set; }

    /// <summary>The window's render scaling (device pixels per logical pixel), pushed in by the view.
    /// Covers are decoded to their displayed pixel size, which needs this to stay crisp on a HiDPI
    /// display; it defaults to 1 so a decode before the view reports scaling is still valid.</summary>
    public double CoverRenderScale { get; set; } = 1.0;

    // Only one mode is on screen at a time, so exactly one viewport is authoritative. Reading the
    // active one — rather than letting whichever view last raised SizeChanged win — is what keeps
    // a mode switch from sizing tiles for the mode that is no longer visible.
    private double ActiveViewportWidth => IsGamepadMode ? GamepadViewportWidth : LibraryViewportWidth;

    private double ActiveGridHorizontalPadding =>
        IsGamepadMode ? GamepadGridHorizontalPadding : DesktopGridHorizontalPadding;

    partial void OnLibraryViewportWidthChanged(double value) => UpdateCoverLayout();

    public bool IsAllGamesSelected => CurrentLibraryScope == LibraryScope.AllGames;
    public bool IsRecentlyAddedSelected => CurrentLibraryScope == LibraryScope.RecentlyAdded;
    public bool IsRecentlyPlayedSelected => CurrentLibraryScope == LibraryScope.RecentlyPlayed;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusText);

    /// <summary>Drives the Gamepad corner toast. Only the blocking pre-launch sync trades the toast
    /// for the large centered panel, and only off the physical shelf — the shelf keeps the toast so
    /// the cartridge choreography stays visible, and the non-blocking post-exit sync keeps it too so
    /// the player can browse while the upload finishes.</summary>
    public bool ShowGamepadStatusToast =>
        HasStatusMessage && (!IsBlockingLaunchSaveSync || ShowGamepadShelf);

    public bool ShowBlockingLaunchSaveSync => IsBlockingLaunchSaveSync && !ShowGamepadShelf;

    /// <summary>Lets the toast mark a failure without the text having to say "failed".</summary>
    public bool IsStatusError => StatusSeverity == StatusSeverity.Error;
    public bool IsStatusProgress => StatusSeverity == StatusSeverity.Progress;
    public bool IsStatusInfo => StatusSeverity == StatusSeverity.Info;

    /// <summary>In-app auto-update state, bound by the update banner. Null in design-time and tests.</summary>
    public AppUpdateCoordinator? Updates => _updates;
    /// <summary>
    /// True while an emulator owns the game session, plus a short return guard that absorbs the
    /// controller/key used to close it. Input services poll this directly, so it remains accurate
    /// without a timer or UI notification.
    /// </summary>
    public bool IsGamepadInputSuspended =>
        _isFrontendSuspended || DateTimeOffset.UtcNow < _gamepadInputGuardUntil;
    public int SelectedGameCount => Games.Count(game => game.IsSelected);
    public bool HasSelectedGames => SelectedGameCount > 0;
    public string SelectionSummaryText => SelectedGameCount == 1 ? "1 game selected" : $"{SelectedGameCount} games selected";
    public string SelectionRemovalText => SelectedGameCount <= 1
        ? "Remove from library…"
        : $"Remove {SelectedGameCount} selected games…";
    public string LibraryTitle => CurrentLibraryScope switch
    {
        LibraryScope.AllGames => "All Games",
        LibraryScope.RecentlyAdded => "Recently Added",
        LibraryScope.RecentlyPlayed => "Recently Played",
        _ => SelectedSystem?.Name ?? "Library",
    };
    public string EmptyLibraryTitle => CurrentLibraryScope switch
    {
        LibraryScope.AllGames => "Your game library is empty",
        LibraryScope.RecentlyAdded => "No recently added games",
        LibraryScope.RecentlyPlayed => "No recently played games",
        _ => $"Your {SelectedSystem?.Name ?? "game"} library is empty",
    };
    public string EmptyLibraryDescription => CurrentLibraryScope switch
    {
        LibraryScope.RecentlyAdded => "Games you import will appear here in newest-first order.",
        LibraryScope.RecentlyPlayed => "Games you launch will appear here, most recently played first.",
        _ => SelectedSystem?.Id == "playstation3"
            ? "Sync the explicitly selected RPCS3 library from Settings to add PlayStation 3 games."
            : "Add game files or a dedicated folder to begin building this shelf.",
    };

    /// <summary>
    /// Whether this platform has a Desktop window shell to switch to. False on Android, where the
    /// gamepad shell is the whole app; used to hide every "switch to Desktop" affordance and to word
    /// the desktop-only handoffs honestly. Defaults to true when no mode service is injected, so
    /// design-time and existing desktop tests are unchanged. Constant for the session (the platform
    /// does not change), so it needs no change notification.
    /// </summary>
    public bool SupportsDesktopMode => _interfaceModeService?.SupportsDesktopMode ?? true;

    /// <summary>Gamepad empty-library prompt, worded for whether a Desktop mode exists to import from.</summary>
    public string GamepadEmptyLibraryPrompt => SupportsDesktopMode
        ? "No games are available in this view. Use Menu to switch to Desktop mode and add games."
        : "No games are available in this view. Press Menu, then Add games, to pick a folder to import.";

    /// <summary>Title of the Set-cover handoff overlay: a route to Desktop, or an honest not-here.</summary>
    public string GamepadCoverHandoffTitle => SupportsDesktopMode
        ? "Set cover in Desktop mode"
        : "Set cover unavailable here";

    /// <summary>Body of the Set-cover handoff overlay, matching <see cref="GamepadCoverHandoffTitle"/>.</summary>
    public string GamepadCoverHandoffDescription => SupportsDesktopMode
        ? "Choosing an image needs the platform file picker, which is not controller-safe in Gamepad mode. Continue only if you want to leave Gamepad mode."
        : "Choosing a cover image from a file isn't available on this device yet. Turn on web image search in Settings to set covers with the controller.";

    /// <summary>Design-time / fallback constructor. The real app injects services.</summary>
    private readonly CloudSaveSyncCoordinator? _cloudSaveSync;
    private readonly IGameSaveSyncService? _gameSaveSync;
    private readonly TexturePackCoordinator? _texturePacks;
    private readonly HotkeyCoordinator? _hotkeys;

    public MainViewModel()
        : this(
            new EmptyGameLibrary(),
            new NullFolderScanner(),
            new NoImportRules(),
            new AlwaysAvailableChecker(),
            new NullDialogService(),
            KnownSystems.All)
    {
    }

    public MainViewModel(
        IGameLibrary library,
        IFolderScanner scanner,
        IGameImportRules importRules,
        IAvailabilityChecker availabilityChecker,
        IDialogService dialogs,
        IReadOnlyList<GameSystem> systems,
        IEmulatorLaunchService? launchService = null,
        IEmulatorConfigurationStore? emulatorConfigurations = null,
        IReadOnlyList<EmulatorDefinition>? emulators = null,
        IGameCoverService? covers = null,
        IAppThemeService? themeService = null,
        IGameMetadataService? metadataService = null,
        IMetadataPreferencesService? metadataPreferences = null,
        IAppLogger? logger = null,
        IRetroAchievementsIdentificationService? retroAchievements = null,
        IRetroAchievementsReadStore? retroAchievementsRead = null,
        IRetroAchievementsAccountService? retroAccount = null,
        IRetroAchievementsMatchingService? retroMatching = null,
        IRetroAchievementsProgressService? retroProgress = null,
        IRetroAchievementsDetailsService? retroDetails = null,
        IRetroAchievementsRefreshService? retroRefresh = null,
        IGameMetadataStore? metadataStore = null,
        IInterfaceModeService? interfaceModeService = null,
        IRetroAchievementsBadgeCache? retroBadges = null,
        CloudSaveSyncCoordinator? cloudSaveSync = null,
        IGameSaveSyncService? gameSaveSync = null,
        IApplicationLifetimeService? applicationLifetime = null,
        TexturePackCoordinator? texturePacks = null,
        ILibraryViewStateService? libraryViewState = null,
        IScreenScraperAccountService? screenScraperAccount = null,
        IScreenScraperPreviewService? screenScraperPreview = null,
        IGameScrapeApplicationService? scrapeApply = null,
        IScreenScraperBatchService? scrapeBatch = null,
        IRemoteArtworkDownloader? artworkDownloader = null,
        IGameArtworkSearchProvider? artworkSearch = null,
        ISettingsService? settingsService = null,
        IOnScreenKeyboardService? onScreenKeyboard = null,
        IGameDetailsStore? gameDetails = null,
        IAppPaths? appPaths = null,
        AppUpdateCoordinator? updates = null,
        IFileRevealService? fileReveal = null,
        HotkeyCoordinator? hotkeys = null)
    {
        _dataDirectory = appPaths?.BaseDirectory;
        _updates = updates;
        _libraryViewState = libraryViewState ?? new NullLibraryViewStateService();
        _screenScraperAccount = screenScraperAccount;
        _screenScraperPreview = screenScraperPreview;
        _scrapeApply = scrapeApply;
        _scrapeBatch = scrapeBatch;
        _artworkDownloader = artworkDownloader;
        _artworkSearch = artworkSearch;
        _settingsService = settingsService;
        _library = library;
        _scanner = scanner;
        _importRules = importRules;
        _availabilityChecker = availabilityChecker;
        _dialogs = dialogs;
        _launchService = launchService ?? new NullEmulatorLaunchService();
        _fileReveal = fileReveal ?? new NullFileRevealService();
        _emulatorConfigurations = emulatorConfigurations ?? new NullEmulatorConfigurationStore();
        _emulators = emulators ?? KnownEmulators.All;
        _covers = covers ?? new NullGameCoverService();
        _gameDetails = gameDetails;
        _themeService = themeService ?? new NullAppThemeService();
        _interfaceModeService = interfaceModeService;
        _applicationLifetime = applicationLifetime;
        _onScreenKeyboard = onScreenKeyboard ?? UnsupportedOnScreenKeyboardService.Instance;
        IsGamepadMode = interfaceModeService?.Current == InterfaceMode.Gamepad;
        if (_interfaceModeService is not null)
        {
            _interfaceModeService.ModeChanged += (_, mode) =>
            {
                IsGamepadMode = mode == InterfaceMode.Gamepad;
                // Gamepad mode renders tiles, so it forces a grid. Returning to Desktop puts the
                // user's own view choice back: leaving the forced grid in place would both strand
                // a list-view user in a grid and, on their next unrelated change, persist that
                // grid over the preference they never changed.
                IsGridView = IsGamepadMode || _libraryViewState.Current.IsGridView;
                if (IsGamepadMode)
                    CoerceCouchLibraryState();
                else
                    ClearSpotlightHero(); // release the hero bitmap when leaving couch mode
            };
        }
        _metadataService = metadataService ?? new NullGameMetadataService();
        _metadataStore = metadataStore;
        _metadataPreferences = metadataPreferences ?? new NullMetadataPreferencesService();
        _retroAchievements = retroAchievements;
        _retroAchievementsRead = retroAchievementsRead;
        _retroAccount = retroAccount;
        _retroMatching = retroMatching;
        _retroProgress = retroProgress;
        _retroDetails = retroDetails;
        // A details refresh (opening the achievements overlay, or the post-exit refresh) writes the
        // account's new unlock count to the progress store, but only a full reload re-applied it to a
        // tile. So the focused-game dock widget kept showing the pre-unlock count ("0/9") after an
        // unlock the overlay already reflected. Re-apply the affected tiles' display when fresh
        // details arrive so the widget and grid mark update without a reload.
        if (_retroDetails is not null)
            _retroDetails.DetailsRefreshed += OnAchievementDetailsRefreshed;
        _retroRefresh = retroRefresh;
        _retroBadges = retroBadges;
        _cloudSaveSync = cloudSaveSync;
        _texturePacks = texturePacks;
        _hotkeys = hotkeys;
        _gameSaveSync = gameSaveSync ?? cloudSaveSync;
        _logger = logger ?? NullAppLogger.Instance;
        // Build the theme choices before assigning CurrentTheme: the generated setter fires
        // OnCurrentThemeChanged, which reads ThemeChoices, whenever the saved theme differs from the
        // System default.
        ThemeChoices = ThemeCatalog.All
            .Select(theme => new ThemeChoiceViewModel(theme, SetThemeAsync))
            .ToArray();
        CurrentTheme = _themeService.Current;
        foreach (var choice in ThemeChoices)
            choice.IsSelected = choice.Id == CurrentTheme;

        _ambientThemeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _ambientThemeDebounce.Tick += (_, _) =>
        {
            _ambientThemeDebounce.Stop();
            ApplyAmbientThemeForPendingGame();
        };

        _shelfMotionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _shelfMotionTimer.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = _shelfMotionTimestamp == 0
                ? _shelfMotionTimer.Interval.TotalMilliseconds
                : Stopwatch.GetElapsedTime(_shelfMotionTimestamp, now).TotalMilliseconds;
            _shelfMotionTimestamp = now;
            AdvanceShelfMotion(elapsed);
        };
        _shelfLaunchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _shelfLaunchTimer.Tick += (_, _) =>
        {
            var now = Stopwatch.GetTimestamp();
            var elapsed = _shelfLaunchTimestamp == 0
                ? _shelfLaunchTimer.Interval.TotalMilliseconds
                : Stopwatch.GetElapsedTime(_shelfLaunchTimestamp, now).TotalMilliseconds;
            _shelfLaunchTimestamp = now;
            AdvanceShelfLaunchTransition(elapsed);
        };
        // Assigned after the timer exists: a persisted "on" fires OnAmbientThemeFromArtworkChanged.
        AmbientThemeFromArtwork = _themeService.AmbientFromArtwork;
        CrtScreenEffect = _themeService.CrtScreenEffect;
        // The theme service is the single source of truth for the CRT flag. Both settings surfaces
        // reach it through this view model, but nothing had it read the service back — so a change made
        // any other way (a settings-file restore, a future caller) left the bound toggle and the actual
        // presentation out of step, which is exactly how "the switch does nothing" happens. Mirror the
        // service here; the setter re-persists through it, and the service no-ops on an unchanged value,
        // so this cannot loop. The service outlives this view model, so no unsubscribe is needed.
        _themeService.CrtScreenEffectChanged += OnThemeServiceCrtScreenEffectChanged;

        Systems = new ObservableCollection<GameSystem>(systems);
        _systemsById = systems.ToDictionary(system => system.Id, StringComparer.Ordinal);
        ShowEmptyPlatforms = _libraryViewState.Current.ShowEmptyPlatforms;
        var populatedSystemIds = ReadPopulatedSystemIds();
        var navigationSystems = systems
            .Where(system => ShowEmptyPlatforms || populatedSystemIds.Contains(system.Id))
            .ToArray();
        NavigationSystems = new ObservableCollection<GameSystem>(navigationSystems);
        GamepadPlatforms = new ObservableCollection<GamepadPlatformTabViewModel>(
            navigationSystems.Select(system => new GamepadPlatformTabViewModel(system)));
        GroupLeaderSystemIds = ComputeGroupLeaders(navigationSystems);

        // Keep the gamepad row projection in lockstep with Games no matter how Games is changed
        // (reload, filter, or a direct test mutation), so the virtualized row grid never goes stale.
        // Also cache the count for the perf sampler, which runs on a pool thread and must not read the
        // UI-owned collection directly (see PerfStateSnapshot).
        Games.CollectionChanged += (_, _) =>
        {
            _perfGamesCount = Games.Count;
            BuildGamepadRows();
        };
        _perfGamesCount = Games.Count;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ApplyFilter();
        };

        _statusDismiss = new DispatcherTimer();
        _statusDismiss.Tick += (_, _) =>
        {
            _statusDismiss.Stop();
            StatusText = string.Empty;
        };

        _platformReloadDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PlatformReloadDebounceMs),
        };
        _platformReloadDebounce.Tick += async (_, _) =>
        {
            _platformReloadDebounce.Stop();
            var completion = _platformReloadCompletion;
            _platformReloadCompletion = null;
            try
            {
                // Navigation hot path: reuse the cached scope if we have visited it before.
                await ReloadGamesAsync(useCache: true);
            }
            finally
            {
                // ReloadGamesAsync swallows its own errors, but complete the awaiter no matter what
                // so a caller that awaited the selection change can never hang.
                completion?.TrySetResult();
            }
        };

        // Selecting a platform moves several of these properties at once. Coalesce them into one
        // write so a click on the sidebar is not three settings-file round trips.
        _viewStateSave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ViewStateSaveDebounceMs) };
        _viewStateSave.Tick += (_, _) =>
        {
            _viewStateSave.Stop();
            _ = _libraryViewState.SaveAsync(BuildLibraryViewState());
        };

        InitializeColumns();
        RestoreLibraryViewState();
    }

    /// <summary>
    /// Puts the library back the way it was left. Restoring assigns the same properties the user
    /// normally drives, so the save is suppressed for the duration — otherwise the first launch
    /// after an upgrade would write defaults over a perfectly good remembered view.
    /// </summary>
    private void RestoreLibraryViewState()
    {
        var state = _libraryViewState.Current;
        _isRestoringViewState = true;
        try
        {
            IsGridView = state.IsGridView;
            // Couch-only preference, independent of the desktop grid/list choice above. Prefer the named
            // layout; fall back to the legacy spotlight bool (so a pre-Shelf settings file still opens
            // into spotlight), then to the grid.
            var layout = Enum.TryParse<GamepadLibraryLayout>(state.GamepadLayout, out var parsedLayout)
                && Enum.IsDefined(typeof(GamepadLibraryLayout), parsedLayout)
                ? parsedLayout
                : GamepadLibraryLayout.Grid;
            if (layout == GamepadLibraryLayout.Grid && state.GamepadSpotlightView)
                layout = GamepadLibraryLayout.Spotlight;
            GamepadLayout = layout;
            SortColumn = Enum.TryParse<LibrarySortColumn>(state.SortColumn, out var column)
                ? column
                : LibrarySortColumn.Title;
            SortDescending = state.SortDescending;
            // Couch offers only four sort orders; restoring into gamepad mode with any other falls back to a
            // couch default (the reload below applies it). No-op in desktop mode.
            CoerceCouchSort();
            IsNavigationCollapsed = state.IsNavigationCollapsed;
            ApplyPersistedColumns(state.ListColumns);

            var scope = Enum.TryParse<LibraryScope>(state.Scope, out var parsed)
                ? parsed
                : LibraryScope.System;
            if (scope == LibraryScope.System)
            {
                // A system id can disappear between launches; fall back rather than open empty.
                SelectedSystem = state.SelectedSystemId is { } id &&
                    NavigationSystems.FirstOrDefault(candidate => candidate.Id == id) is { } system
                    ? system
                    : NavigationSystems.FirstOrDefault();
                if (SelectedSystem is null)
                    CurrentLibraryScope = LibraryScope.AllGames;
            }
            else
            {
                // Couch mode has no Recently-* place; fall back to All Games there so the rail highlights
                // a stop and the Sort row is not a no-op.
                CurrentLibraryScope = IsGamepadMode && scope is (LibraryScope.RecentlyAdded or LibraryScope.RecentlyPlayed)
                    ? LibraryScope.AllGames
                    : scope;
            }

            // One immediate initial load for whatever scope the restore settled on. The SelectedSystem
            // setter's debounce is suppressed during restore, so this is the sole first build — no
            // 180 ms dead time before the first DB read, and nothing left pending to race the
            // post-open refresh passes.
            _selectedSystemLoad = ReloadGamesAsync();
        }
        finally
        {
            _isRestoringViewState = false;
        }
    }

    /// <summary>The snapshot the debounced save writes. Internal so tests can assert it directly.</summary>
    internal LibraryViewSettings BuildLibraryViewState() => new()
    {
        // Gamepad mode forces a grid to render its tiles, which is not a statement about what the
        // user wants on the desktop. Keep the stored desktop preference while that mode is active.
        IsGridView = IsGamepadMode ? _libraryViewState.Current.IsGridView : IsGridView,
        GamepadLayout = GamepadLayout.ToString(),
        // Kept in sync so an older build (which only reads this bool) still restores grid-vs-spotlight.
        GamepadSpotlightView = IsGamepadSpotlightView,
        SortColumn = SortColumn.ToString(),
        SortDescending = SortDescending,
        IsNavigationCollapsed = IsNavigationCollapsed,
        ShowEmptyPlatforms = ShowEmptyPlatforms,
        Scope = CurrentLibraryScope.ToString(),
        SelectedSystemId = SelectedSystem?.Id,
        ListColumns = Columns
            .Select(column => new LibraryColumnSetting
            {
                Key = column.Key.ToString(),
                IsVisible = column.IsVisible,
                // The flex column's width is always recomputed from the viewport, so persist 0.
                Width = column.IsFlex ? 0 : column.Width,
            })
            .ToList(),
    };

    private void ScheduleLibraryViewStateSave()
    {
        if (_isRestoringViewState)
            return;

        _viewStateSave.Stop();
        _viewStateSave.Start();
    }

    private HashSet<string> ReadPopulatedSystemIds()
    {
        try
        {
            // Presence in EmuShelf's library is authoritative. IsAvailable is deliberately not
            // consulted: disconnecting a Steam Deck SD card must not erase its platforms from nav.
            return _library.GetPopulatedSystemIds().ToHashSet(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not determine populated platforms: {ex.Message}");
            return [];
        }
    }

    private void RefreshNavigationSystems(IReadOnlySet<string> populatedSystemIds)
    {
        var visible = Systems
            .Where(system => ShowEmptyPlatforms || populatedSystemIds.Contains(system.Id))
            .ToArray();
        if (!NavigationSystems.SequenceEqual(visible))
        {
            NavigationSystems.Clear();
            foreach (var system in visible)
                NavigationSystems.Add(system);

            GamepadPlatforms.Clear();
            foreach (var system in visible)
                GamepadPlatforms.Add(new GamepadPlatformTabViewModel(system));

            GroupLeaderSystemIds = ComputeGroupLeaders(visible);
        }

        UpdateGamepadPlatformState();
    }

    /// <summary>
    /// The id of the first system of each manufacturer in <paramref name="orderedVisibleSystems"/>,
    /// which the Desktop console list uses to place a single manufacturer header per group. Systems
    /// with no manufacturer are skipped (they carry no header). Order-dependent, so the caller passes
    /// the systems in navigation order.
    /// </summary>
    private static IReadOnlySet<string> ComputeGroupLeaders(IEnumerable<GameSystem> orderedVisibleSystems)
    {
        var manufacturersSeen = new HashSet<string>(StringComparer.Ordinal);
        var leaders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var system in orderedVisibleSystems)
        {
            if (!string.IsNullOrEmpty(system.Manufacturer) && manufacturersSeen.Add(system.Manufacturer))
                leaders.Add(system.Id);
        }

        return leaders;
    }

    private async Task SetShowEmptyPlatformsAsync(bool show)
    {
        ShowEmptyPlatforms = show;
        var populatedSystemIds = await Task.Run(ReadPopulatedSystemIds);
        RefreshNavigationSystems(populatedSystemIds);

        if (!show && CurrentLibraryScope == LibraryScope.System &&
            SelectedSystem is { } selected && !populatedSystemIds.Contains(selected.Id))
        {
            await ShowCollectionAsync(LibraryScope.AllGames);
        }

        await _libraryViewState.SaveAsync(BuildLibraryViewState());
    }

    /// <summary>
    /// Writes a pending view change immediately instead of waiting out the debounce. Called as the
    /// window closes: switching to list view and quitting straight away is well under the debounce
    /// interval, and without this the change the user just made is the one change never saved.
    /// </summary>
    internal void FlushPendingLibraryViewStateSave()
    {
        if (!_viewStateSave.IsEnabled)
            return;

        _viewStateSave.Stop();
        _libraryViewState.Save(BuildLibraryViewState());
    }

    partial void OnSelectedSystemChanged(GameSystem? value)
    {
        PerfTrace.Event($"EVENT platform->{value?.Name ?? "(scope)"}");
        if (value is not null)
            CurrentLibraryScope = LibraryScope.System;
        NotifyLibraryPresentationChanged();
        UpdateGamepadPlatformState();
        ScheduleLibraryViewStateSave();
        // During startup restore the setter only establishes the rail/title; RestoreLibraryViewState
        // kicks the single initial load itself (immediately, without the debounce below). Debouncing
        // is for live LB/RB cycling: the highlight and title move at once, but the heavy grid reload is
        // coalesced so holding/tapping does not rebuild the library on every press.
        if (!_isRestoringViewState)
            _selectedSystemLoad = RequestLibraryReload();
    }

    partial void OnCurrentLibraryScopeChanged(LibraryScope value)
    {
        OnPropertyChanged(nameof(IsAllGamesSelected));
        OnPropertyChanged(nameof(IsRecentlyAddedSelected));
        OnPropertyChanged(nameof(IsRecentlyPlayedSelected));
        NotifyLibraryPresentationChanged();
        UpdateGamepadPlatformState();
        ScheduleLibraryViewStateSave();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    partial void OnStatusSeverityChanged(StatusSeverity value)
    {
        OnPropertyChanged(nameof(IsStatusError));
        OnPropertyChanged(nameof(IsStatusProgress));
        OnPropertyChanged(nameof(IsStatusInfo));
    }

    partial void OnStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatusMessage));
        OnPropertyChanged(nameof(ShowGamepadStatusToast));
        ScheduleStatusDismiss();
    }

    partial void OnIsSyncingSavesForLaunchChanged(bool value) =>
        NotifySaveSyncPresentationChanged();

    partial void OnIsBlockingLaunchSaveSyncChanged(bool value) =>
        NotifySaveSyncPresentationChanged();

    private void NotifySaveSyncPresentationChanged()
    {
        OnPropertyChanged(nameof(ShowGamepadStatusToast));
        OnPropertyChanged(nameof(ShowBlockingLaunchSaveSync));
    }

    /// <summary>
    /// The single entry point for the library toast. Severity is set first so the dismiss timer
    /// that <see cref="OnStatusTextChanged"/> starts is already looking at the new message's kind.
    /// </summary>
    private void SetStatus(string text, StatusSeverity severity = StatusSeverity.Info)
    {
        StatusSeverity = severity;
        StatusText = text;
    }

    /// <summary>
    /// How long the current message has left before it dismisses itself, or <see cref="TimeSpan.Zero"/>
    /// if it never will. Progress messages get no countdown at all: the operation producing them
    /// replaces the text with its own result (or an error) when it finishes, and a scan that goes
    /// quiet for five seconds must not look like it stopped.
    /// </summary>
    internal TimeSpan StatusDismissDelay => !HasStatusMessage
        ? TimeSpan.Zero
        : StatusSeverity switch
        {
            StatusSeverity.Progress => TimeSpan.Zero,
            StatusSeverity.Error => ErrorStatusLifetime,
            _ => InfoStatusLifetime,
        };

    private void ScheduleStatusDismiss()
    {
        _statusDismiss.Stop();
        var lifetime = StatusDismissDelay;
        if (lifetime <= TimeSpan.Zero)
            return;

        _statusDismiss.Interval = lifetime;
        _statusDismiss.Start();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        _searchDebounce.Stop();
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearStatus() => StatusText = string.Empty;

    [RelayCommand]
    private void ToggleNavigation() => IsNavigationCollapsed = !IsNavigationCollapsed;

    [RelayCommand]
    private Task ShowAllGamesAsync() => ShowCollectionAsync(LibraryScope.AllGames);

    [RelayCommand]
    private Task ShowRecentlyAddedAsync() => ShowCollectionAsync(LibraryScope.RecentlyAdded);

    [RelayCommand]
    private Task ShowRecentlyPlayedAsync() => ShowCollectionAsync(LibraryScope.RecentlyPlayed);

    // Couch mode has no Recently-* place, and its Sort row offers only the four GamepadSortColumns. When we
    // enter gamepad mode holding a scope or sort the couch can't represent (e.g. restored from a desktop
    // session), fall back to couch defaults so the rail highlights a stop, a sort card is selected, and the
    // Sort header stays honest.
    private void CoerceCouchLibraryState()
    {
        if (!IsGamepadMode)
            return;

        var sortChanged = CoerceCouchSort();

        if (CurrentLibraryScope is LibraryScope.RecentlyAdded or LibraryScope.RecentlyPlayed)
            _ = ShowAllGamesAsync();  // reloads and re-sorts with the (possibly coerced) sort
        else if (sortChanged)
            ApplyFilter();            // no scope change, so re-sort the current view in place
    }

    // The couch Sort row only offers GamepadSortColumns; any other column (set on the desktop) falls back to
    // Recently played so a card is always selected. Returns whether it changed the sort. No-op on desktop.
    private bool CoerceCouchSort()
    {
        if (!IsGamepadMode || Array.IndexOf(GamepadSortColumns, SortColumn) >= 0)
            return false;
        SortColumn = LibrarySortColumn.LastPlayed;
        SortDescending = true;
        return true;
    }

    [RelayCommand]
    private async Task PreviousPlatformAsync() => await MovePlatformAsync(-1);

    [RelayCommand]
    private async Task NextPlatformAsync() => await MovePlatformAsync(1);

    [RelayCommand]
    private void SelectPlatform(GameSystem? system)
    {
        if (system is not null)
            SelectedSystem = system;
    }

    // LB/RB cycle one ordered list the rail mirrors — All Games, then each system — and wrap at
    // both ends. Recently Added / Recently Played are couch sort orders (Start menu → Sort), not
    // places, so they are not stops on this cycle.
    private async Task MovePlatformAsync(int direction)
    {
        var count = NavigationSystems.Count + 1; // [All Games, systems…]
        if (count <= 1)
            return; // Only All Games exists; nothing to cycle to.

        var current = CurrentPlatformCycleIndex();
        // From an off-list scope (e.g. Recently Added) the first press returns to All Games rather
        // than stepping past it, so a controller can never dead-end away from the cycle.
        var target = current < 0 ? 0 : ((current + direction) % count + count) % count;

        if (target == 0)
            await ShowAllGamesAsync();
        else
            SelectedSystem = NavigationSystems[target - 1];
    }

    // Position in the LB/RB cycle for the current scope, or -1 when the scope is not on the cycle.
    private int CurrentPlatformCycleIndex()
    {
        if (CurrentLibraryScope == LibraryScope.AllGames)
            return 0;
        if (CurrentLibraryScope == LibraryScope.System && SelectedSystem is not null &&
            NavigationSystems.IndexOf(SelectedSystem) is >= 0 and var systemIndex)
        {
            return systemIndex + 1;
        }
        return -1;
    }

    [RelayCommand]
    private void FocusNextGame() => MoveFocusedGame(1);

    [RelayCommand]
    private void FocusPreviousGame() => MoveFocusedGame(-1);

    [RelayCommand]
    private void FocusGame(GameViewModel? game)
    {
        if (IsGamepadMode && game is not null && Games.Contains(game))
            FocusedGame = game;
    }

    [RelayCommand]
    private Task LaunchFocusedGameAsync() => LaunchGameAsync(FocusedGame);

    [RelayCommand]
    private void OpenFocusedDiscSelection()
    {
        if (FocusedGame?.IsMultiDisc == true)
            OpenGamepadOverlay(GamepadOverlayKind.DiscSelection);
    }

    [RelayCommand]
    private Task OpenFocusedAchievementsAsync() => OpenAchievementDetailsAsync(FocusedGame);

    [RelayCommand]
    private void EditFocusedTitle()
    {
        if (FocusedGame is not null)
        {
            FocusedGame.DraftTitle = FocusedGame.Title;
            if (IsGamepadMode)
            {
                // Retain the existing edit lifecycle; the desktop popup is hidden with its tree.
                FocusedGame.IsEditingTitle = true;
                OpenGamepadOverlay(GamepadOverlayKind.Rename);
            }
            else
                FocusedGame.IsEditingTitle = true;
        }
    }

    [RelayCommand]
    private Task SetFocusedCoverAsync()
    {
        if (IsGamepadMode)
        {
            // Web cover search is controller-native; a local-file pick still needs the OS picker, so it
            // hands off to Desktop. When web search is off (or unavailable) there is nothing to search,
            // so go straight to the handoff.
            if (FocusedGame is { } game && CanGamepadCoverSearch)
                return OpenGamepadCoverSearchAsync(game);

            OpenGamepadOverlay(GamepadOverlayKind.CoverDesktopHandoff);
            return Task.CompletedTask;
        }

        return SetGameCoverAsync(FocusedGame);
    }

    /// <summary>Whether the controller-native web cover search can open: a search provider and
    /// downloader are wired, and the user has left web image search on in Settings.</summary>
    private bool CanGamepadCoverSearch =>
        _artworkSearch is not null &&
        _artworkDownloader is not null &&
        (_settingsService?.Load().Scraping.WebImageSearchEnabled ?? true);

    private Task OpenGamepadCoverSearchAsync(GameViewModel game)
    {
        if (_artworkSearch is null || _artworkDownloader is null)
        {
            OpenGamepadOverlay(GamepadOverlayKind.CoverDesktopHandoff);
            return Task.CompletedTask;
        }

        var preferredAspectRatio = _systemsById.TryGetValue(game.SystemId, out var system)
            ? system.CoverAspectRatio
            : game.CoverAspectRatio;
        // The local-file pick is not passed through here: choosing a file needs the OS picker, so the
        // overlay's "Choose a file" target hands off to Desktop instead of calling this.
        var search = new CoverSearchViewModel(
            new GameCoverPickerContext(game.DisplayTitle, game.SystemName, preferredAspectRatio),
            _artworkSearch,
            _artworkDownloader,
            () => Task.FromResult<string?>(null),
            _logger);
        search.CloseRequested += picked => OnGamepadCoverPicked(game, picked);
        var details = new GamepadCoverSearchViewModel(
            search,
            () => OpenGamepadOverlay(GamepadOverlayKind.CoverDesktopHandoff));

        OpenGamepadOverlay(GamepadOverlayKind.CoverSearch);
        GamepadCoverSearchDetails = details;
        return details.LoadAsync();
    }

    // Raised when the wrapped picker resolves — either a downloaded web cover (import it) or a cancel
    // (null). Closing the overlay disposes the picker; the downloaded staging file it handed us
    // survives until ImportPickedCoverAsync consumes and deletes it.
    private void OnGamepadCoverPicked(GameViewModel game, PickedGameCover? picked)
    {
        CloseGamepadOverlay();
        if (picked is not null)
            _ = ImportPickedCoverAsync(game, picked);
    }

    [RelayCommand]
    private Task RemoveFocusedGameAsync()
    {
        if (IsGamepadMode && FocusedGame is not null)
        {
            OpenGamepadOverlay(GamepadOverlayKind.RemoveConfirmation);
            return Task.CompletedTask;
        }

        return RemoveGameAsync(FocusedGame);
    }

    [RelayCommand]
    private Task ScrapeFocusedGameAsync()
    {
        if (FocusedGame is null)
            return Task.CompletedTask;

        // Gamepad mode scrapes in a controller-native overlay that reuses the shared scraper view
        // model — no Desktop handoff. Desktop keeps its own window.
        if (IsGamepadMode)
            return OpenGamepadScraperAsync(FocusedGame);

        return ScrapeGameAsync(FocusedGame);
    }

    private Task OpenGamepadScraperAsync(GameViewModel game)
    {
        if (_screenScraperPreview is null || _scrapeApply is null ||
            _screenScraperAccount is null || _settingsService is null)
        {
            SetStatus("ScreenScraper is unavailable right now.", StatusSeverity.Error);
            return Task.CompletedTask;
        }

        var settings = _settingsService.Load().Scraping.ScreenScraper;
        var scraper = new GameScraperViewModel(
            game.Id, game.Title, _screenScraperPreview, _scrapeApply, _screenScraperAccount,
            settings, _artworkDownloader, _logger);
        var details = new GamepadScraperViewModel(scraper);

        OpenGamepadOverlay(GamepadOverlayKind.Scraper);
        GamepadScraperDetails = details;
        return scraper.LoadAsync();
    }

    /// <summary>Whether a controller-native batch scrape can be started over the games now in view.</summary>
    public bool CanScrapeAllInView => _scrapeBatch is not null && _settingsService is not null && HasGames;

    /// <summary>
    /// Opens the controller-native batch scraper over every game in the current view, giving Gamepad
    /// mode the determinate multi-game progress bar Desktop reaches through multi-select. Hash/serial
    /// only — unmatched games are reported, never guessed.
    /// </summary>
    [RelayCommand]
    private void ScrapeAllInView()
    {
        if (!IsGamepadMode || _scrapeBatch is null || _settingsService is null)
            return;

        var gameIds = Games.Select(game => game.Id).Distinct().ToList();
        if (gameIds.Count == 0)
            return;

        // A single-platform view names the console; a mixed view (All Games and the like) just says
        // "selected", matching how the Desktop batch window titles a mixed selection.
        var systemName = Games.Select(game => game.SystemName).Distinct().Count() == 1
            ? Games[0].SystemName
            : "selected";

        var settings = _settingsService.Load().Scraping.ScreenScraper;
        var batch = new GameBatchScraperViewModel(gameIds, systemName, _scrapeBatch, settings, _logger);
        batch.CloseRequested += CloseGamepadOverlay;
        var details = new GamepadBatchScraperViewModel(batch);

        OpenGamepadOverlay(GamepadOverlayKind.BatchScraper);
        GamepadBatchScraperDetails = details;
    }

    [RelayCommand]
    private void OpenFocusedGameActions()
    {
        if (FocusedGame is not null)
            OpenGamepadOverlay(GamepadOverlayKind.Actions);
    }

    [RelayCommand]
    private void OpenGamepadSearch() => OpenGamepadOverlay(GamepadOverlayKind.Search);

    [RelayCommand]
    private void OpenGamepadMenu()
    {
        if (IsGamepadSystemMenuOpen)
            CloseGamepadOverlay();
        else
            OpenGamepadOverlay(GamepadOverlayKind.SystemMenu);
    }

    [RelayCommand]
    private void RequestDesktopModeFromGamepad()
    {
        // Remember the overlay we came from (System Menu or the Set-cover handoff) so B backs out to it.
        _desktopModeConfirmationParent = GamepadOverlay is GamepadOverlayKind.CoverDesktopHandoff
            ? GamepadOverlayKind.CoverDesktopHandoff
            : GamepadOverlayKind.SystemMenu;
        OpenGamepadOverlay(GamepadOverlayKind.DesktopModeConfirmation);
    }

    [RelayCommand]
    private async Task RequestSettingsFromGamepadAsync()
    {
        // Building the projection awaits a database read, so a second press before it completes would
        // otherwise start an overlapping open and race on GamepadSettings. One in-flight open at a time.
        if (!IsGamepadMode || IsBusy || _openingGamepadSettings)
            return;

        _openingGamepadSettings = true;
        try
        {
            CloseGamepadSettingsProjection();
            var settings = await CreateSettingsViewModelAsync();
            GamepadSettings = new GamepadSettingsViewModel(
                settings,
                _onScreenKeyboard,
                ThemeChoices,
                SetThemeAsync,
                OpenGamepadHotkeysFromSettings,
                androidEmulatorChoices: OperatingSystem.IsAndroid()
                    ? AndroidEmulatorChoiceCatalog.BySystem
                    : null);
            OpenGamepadOverlay(GamepadOverlayKind.Settings);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not open Gamepad settings.", ex);
            SetStatus($"Could not open Settings: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            _openingGamepadSettings = false;
        }
    }

    /// <summary>
    /// Opens the controller-native Hotkeys overlay from the gamepad Settings General row. It wraps the
    /// same <see cref="EmulatorSettingsViewModel"/> the Settings projection is already showing — so no
    /// config files are re-read and no Desktop window is ever involved — and B returns to Settings,
    /// whose projection <see cref="OpenGamepadOverlay"/> deliberately leaves intact.
    /// </summary>
    private Task OpenGamepadHotkeysFromSettings()
    {
        if (!IsGamepadMode || GamepadSettings?.Settings is not { HasHotkeys: true } settings)
            return Task.CompletedTask;

        var details = new GamepadHotkeysViewModel(settings);
        OpenGamepadOverlay(GamepadOverlayKind.Hotkeys);
        GamepadHotkeys = details;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void RequestQuitFromGamepad() =>
        OpenGamepadOverlay(GamepadOverlayKind.QuitConfirmation);

    [RelayCommand]
    private void MoveGamepadOverlayUp()
    {
        if (IsGamepadAchievementsOpen)
        {
            MoveFocusedAchievementVertical(-1);
            return;
        }

        // Two selector rows sit above the option list. Up walks Options(top) → Sort → View mode → stay.
        if (IsGamepadSystemMenuOpen)
        {
            switch (MenuFocusRegion)
            {
                case GamepadMenuFocusRegion.ViewMode:
                    return; // already on the top row
                case GamepadMenuFocusRegion.Sort:
                    MenuFocusRegion = GamepadMenuFocusRegion.ViewMode;
                    return;
                case GamepadMenuFocusRegion.Options when GamepadOverlaySelectionIndex == 0:
                    MenuFocusRegion = GamepadMenuFocusRegion.Sort;
                    return;
            }
        }

        MoveGamepadOverlaySelection(-1);
    }

    [RelayCommand]
    private void MoveGamepadOverlayDown()
    {
        if (IsGamepadAchievementsOpen)
        {
            MoveFocusedAchievementVertical(1);
            return;
        }

        // Down walks View mode → Sort → option list (top entry).
        if (IsGamepadSystemMenuOpen && MenuFocusRegion != GamepadMenuFocusRegion.Options)
        {
            if (MenuFocusRegion == GamepadMenuFocusRegion.ViewMode)
            {
                MenuFocusRegion = GamepadMenuFocusRegion.Sort;
                return;
            }
            GamepadOverlaySelectionIndex = 0;
            MenuFocusRegion = GamepadMenuFocusRegion.Options; // refreshes the option ring onto the top entry
            return;
        }

        MoveGamepadOverlaySelection(1);
    }

    [RelayCommand]
    private void CycleGamepadAchievementSort()
    {
        if (!IsGamepadAchievementsOpen || GamepadAchievementDetails is not { } details)
            return;

        // Sorting a controller grid should keep the selector in the same physical slot. Following
        // the old achievement id makes the ring jump across the screen as that badge moves.
        var focusedIndex = FocusedGamepadAchievement is { } focused
            ? details.VisibleAchievements.IndexOf(focused)
            : 0;
        focusedIndex = Math.Max(0, focusedIndex);

        details.CycleSortCommand.Execute(null);

        FocusedGamepadAchievement = details.VisibleAchievements.Count == 0
            ? null
            : details.VisibleAchievements[Math.Min(focusedIndex, details.VisibleAchievements.Count - 1)];

    }

    [RelayCommand]
    private void ActivateGamepadOverlay()
    {
        // On the sort row, A reverses the current sort's direction — Left/Right already picked the field.
        if (IsGamepadSystemMenuOpen && IsGamepadSortRowFocused)
        {
            ToggleGamepadSortDirection();
            return;
        }
        // On the view-mode row, Left/Right already applied the choice live, so A is inert rather than
        // firing whichever option index sits selected underneath.
        if (IsGamepadSystemMenuOpen && MenuFocusRegion != GamepadMenuFocusRegion.Options)
            return;
        if (GamepadOverlayOptions.Count == 0)
            return;
        GamepadOverlayOptions[GamepadOverlaySelectionIndex].Command.Execute(null);
    }

    [RelayCommand]
    private void CloseGamepadOverlay()
    {
        var closingOverlay = GamepadOverlay;
        if (closingOverlay == GamepadOverlayKind.Rename && FocusedGame is not null)
        {
            FocusedGame.DraftTitle = FocusedGame.Title;
            FocusedGame.IsEditingTitle = false;
        }
        DisposeGamepadAchievementDetails();
        DisposeGamepadScraperDetails();
        DisposeGamepadCoverSearchDetails();
        DisposeGamepadBatchScraperDetails();
        DisposeGamepadHotkeysDetails();
        if (closingOverlay == GamepadOverlayKind.Settings)
            CloseGamepadSettingsProjection();
        FocusedGamepadAchievement = null;
        if (closingOverlay == GamepadOverlayKind.ImportSystem)
            _pendingImportFolder = null; // a cancelled import must not leave a stale folder pending
        GamepadOverlayOptions.Clear();
        GamepadOverlay = GamepadOverlayKind.None;
        IsGameActionsOpen = false;
        RestoreFocusedGame();
    }

    [RelayCommand]
    private void BackFromGamepadOverlay()
    {
        if (GamepadOverlay == GamepadOverlayKind.Scraper)
        {
            // Once a scrape reaches a terminal message (applied / failed / unsupported), B returns
            // to the library; while it is still working, B steps back to the game's Actions menu.
            if (GamepadScraperDetails?.Scraper.State is
                GameScraperState.Applied or GameScraperState.Failure or GameScraperState.Unsupported)
            {
                CloseGamepadOverlay();
            }
            else
            {
                OpenGamepadOverlay(GamepadOverlayKind.Actions);
            }
            return;
        }

        if (GamepadOverlay == GamepadOverlayKind.BatchScraper)
        {
            // A running batch is left alone (Cancel stops it via A on the focused button); before it
            // starts or after it finishes, B closes back to the library.
            if (GamepadBatchScraperDetails?.Batch.IsRunning != true)
                CloseGamepadOverlay();
            return;
        }

        if (GamepadOverlay == GamepadOverlayKind.Hotkeys)
        {
            // Opened from the gamepad Settings General row, whose projection survived the open, so B
            // steps back into Settings. Only if that projection is gone (defensive) do we close out.
            if (GamepadSettings is not null)
                OpenGamepadOverlay(GamepadOverlayKind.Settings);
            else
                CloseGamepadOverlay();
            return;
        }

        if (IsGamepadSettingsOpen)
        {
            OnGamepadSettingsCloseRequested(false);
            return;
        }

        var returnOverlay = GamepadOverlay switch
        {
            GamepadOverlayKind.Rename or
            GamepadOverlayKind.DiscSelection or
            GamepadOverlayKind.RemoveConfirmation or
            GamepadOverlayKind.CoverDesktopHandoff or
            GamepadOverlayKind.CoverSearch => GamepadOverlayKind.Actions,
            // Desktop-mode confirm returns to whichever overlay opened it, not always the System Menu.
            GamepadOverlayKind.DesktopModeConfirmation => _desktopModeConfirmationParent,
            GamepadOverlayKind.QuitConfirmation => GamepadOverlayKind.SystemMenu,
            _ => GamepadOverlayKind.None,
        };

        if (returnOverlay == GamepadOverlayKind.None)
            CloseGamepadOverlay();
        else
            OpenGamepadOverlay(returnOverlay);
    }

    [RelayCommand]
    private async Task SaveGamepadTitleAsync()
    {
        var game = FocusedGame;
        await SaveGameTitleAsync(game);
        if (game is null || !game.IsEditingTitle)
            CloseGamepadOverlay();
    }

    [RelayCommand]
    private async Task ConfirmGamepadRemoveAsync()
    {
        if (FocusedGame is not { } game)
            return;

        await RemoveGameCoreAsync(game);
        CloseGamepadOverlay();
    }

    [RelayCommand]
    private async Task SetInterfaceModeAsync(InterfaceMode mode)
    {
        if (_interfaceModeService is not null)
            await _interfaceModeService.SetModeAsync(mode);
    }

    /// <summary>
    /// Single routing entry point for controller commands, shared by native pad input
    /// (<see cref="GamepadInputService"/>) and Steam-Input keyboard mapping (the MainWindow key
    /// handler), so both input paths behave identically. Returns whether the action was consumed,
    /// letting the key handler mark the event handled.
    /// </summary>
    private readonly MediaRotationModel _shelfHeroRotation = new();

    /// <summary>
    /// Continuous selection coordinate consumed by the shared shelf scene. It is an integer only
    /// at rest; during navigation the old and next media occupy intermediate world positions.
    /// </summary>
    public double ShelfPosition => _shelfMotion.Position;

    /// <summary>The shelf hero's yaw, in radians, bound straight to the 3D control.</summary>
    public double ShelfHeroYaw => _shelfHeroRotation.Yaw;

    /// <summary>The shelf hero's pitch, in radians.</summary>
    public double ShelfHeroPitch => _shelfHeroRotation.Pitch;

    /// <summary>The focused item's launch-only transform, or null during ordinary browsing.</summary>
    public PhysicalShelfLaunchPose? ShelfLaunchPose =>
        _shelfLaunchTransition.IsIdle ? null : _shelfLaunchTransition.Pose;

    /// <summary>
    /// Pose captured from the game that just left focus. The renderer blends it back to the
    /// neighbour pose while that cartridge physically travels away from the centre.
    /// </summary>
    public PhysicalShelfDeparturePose? ShelfDeparturePose { get; private set; }

    /// <summary>
    /// Advances the shelf hero's pose from one tick of right-stick input.
    /// </summary>
    /// <remarks>
    /// Gated on the hero actually being on screen, so the model neither accumulates rotation the
    /// player cannot see nor asks for a redraw while the shelf is showing flat covers. Returns
    /// without notifying when the stick is centred, which is the common case every tick.
    /// </remarks>
    public void ApplyRightStickRotation(float rightStickX, float rightStickY, double deltaMilliseconds)
    {
        if (!IsGamepadMode || IsGamepadInputSuspended || !ShowGamepadShelf || HasGamepadOverlay)
            return;

        if (_shelfHeroRotation.Update(rightStickX, rightStickY, deltaMilliseconds))
            NotifyShelfHeroPose();
    }

    /// <summary>Returns the hero to face-on. Driven by R3 and by any change of focus.</summary>
    public void RecentreShelfHero()
    {
        if (_shelfHeroRotation.Recentre())
            NotifyShelfHeroPose();
    }

    private void NotifyShelfHeroPose()
    {
        OnPropertyChanged(nameof(ShelfHeroYaw));
        OnPropertyChanged(nameof(ShelfHeroPitch));
    }

    /// <summary>Advances shelf travel; public so headless tests exercise the timer's exact path.</summary>
    public bool AdvanceShelfMotion(double deltaMilliseconds)
    {
        if (!_shelfMotion.Update(deltaMilliseconds))
        {
            if (_shelfMotion.IsSettled)
            {
                _shelfMotionTimer.Stop();
                _shelfMotionTimestamp = 0;
            }
            return false;
        }

        OnPropertyChanged(nameof(ShelfPosition));
        if (_shelfMotion.IsSettled)
        {
            _shelfMotionTimer.Stop();
            _shelfMotionTimestamp = 0;
        }
        return true;
    }

    /// <summary>Advances launch choreography; public so tests exercise the timer's exact path.</summary>
    public bool AdvanceShelfLaunchTransition(double deltaMilliseconds)
    {
        var changed = _shelfLaunchTransition.Update(deltaMilliseconds);
        if (changed)
        {
            OnPropertyChanged(nameof(ShelfLaunchPose));
        }

        if (_shelfLaunchTransition.IsCommitted || _shelfLaunchTransition.IsIdle)
        {
            _shelfLaunchTimer.Stop();
            _shelfLaunchTimestamp = 0;
            _shelfLaunchCompletion?.TrySetResult();
            _shelfLaunchCompletion = null;
        }

        return changed;
    }

    /// <summary>
    /// Drops an in-flight launch pose that no launch is managing, when the shelf leaves the screen.
    /// </summary>
    /// <remarks>
    /// The choreography clears itself through commit → return inside the launch flow, and that is safe
    /// today only because input is frozen for the whole launch, so layout, mode and platform cannot
    /// change under it. This is the belt for that brace: if the shelf stops being shown while the
    /// transition is somehow still mid-flight, reset it so a stale pose cannot strand a cartridge on a
    /// later shelf. Guarded on <see cref="IsBusy"/> so it never cuts a launch that is genuinely running
    /// its course — a committed medium sitting out an emulator session holds <see cref="IsBusy"/>, and
    /// its own finally owns the return.
    /// </remarks>
    private void AbandonOrphanedShelfLaunchTransition()
    {
        if (IsBusy || _shelfLaunchTransition.IsIdle)
        {
            return;
        }

        _shelfLaunchTransition.Reset();
        _shelfLaunchTimer.Stop();
        _shelfLaunchTimestamp = 0;
        _shelfLaunchCompletion?.TrySetResult();
        _shelfLaunchCompletion = null;
        OnPropertyChanged(nameof(ShelfLaunchPose));
    }

    private void MoveShelfToFocusedGame(GameViewModel? oldValue, GameViewModel? newValue)
    {
        if (newValue is null)
        {
            _shelfMotionTimer.Stop();
            return;
        }

        var target = Games.IndexOf(newValue);
        if (target < 0)
        {
            return;
        }

        var previous = oldValue is null ? -1 : Games.IndexOf(oldValue);
        var snap = !ShowGamepadShelf || previous < 0 || Math.Abs(target - previous) > 4;
        if (snap)
        {
            _shelfMotion.SnapTo(target);
            _shelfMotionTimer.Stop();
            _shelfMotionTimestamp = 0;
            OnPropertyChanged(nameof(ShelfPosition));
            return;
        }

        if (_shelfMotion.MoveTo(target))
        {
            _shelfMotionTimestamp = Stopwatch.GetTimestamp();
            _shelfMotionTimer.Start();
        }
    }

    private void SnapShelfToFocusedGame()
    {
        var index = FocusedGame is null ? -1 : Games.IndexOf(FocusedGame);
        if (index < 0)
        {
            return;
        }

        _shelfMotion.SnapTo(index);
        _shelfMotionTimer.Stop();
        _shelfMotionTimestamp = 0;
        OnPropertyChanged(nameof(ShelfPosition));
    }

    public bool DispatchGamepadAction(GamepadAction action)
    {
        if (!IsGamepadMode)
            return false;

        IsGamepadControllerInputActive = true;

        // Consume late Steam-Input keyboard events as well as native-pad input while a tracked
        // game is active/returning. In particular, B/Escape must not turn a game return into a
        // Desktop-mode switch.
        if (IsGamepadInputSuspended)
            return true;

        // Recentring is about the hero, not about whatever pane has focus, so it is answered here
        // rather than in each view's routing. Harmless when the shelf is not showing.
        if (action == GamepadAction.ResetRotation)
        {
            RecentreShelfHero();
            return true;
        }

        if (IsGamepadSettingsOpen && GamepadSettings is { } settings)
            return settings.Dispatch(action);

        if (GamepadOverlayOwnsTextInput)
            return DispatchTextOverlayAction(action);

        if (IsGamepadScraperOpen)
            return DispatchScraperOverlayAction(action);

        if (IsGamepadCoverSearchOpen)
            return DispatchCoverSearchOverlayAction(action);

        if (IsGamepadBatchScraperOpen)
            return DispatchBatchScraperOverlayAction(action);

        if (IsGamepadHotkeysOpen)
            return DispatchHotkeysOverlayAction(action);

        return HasGamepadOverlay
            ? DispatchOverlayAction(action)
            : DispatchLibraryAction(action);
    }

    /// <summary>
    /// Answers a platform Back request (the Android system Back button / gesture). Back closes an open
    /// couch overlay or menu — behaving exactly like B/Cancel — but at the root library it does
    /// <em>nothing here</em> and returns false so the platform can act on it (on Android, exit the app).
    /// This is the one place Back must diverge from <see cref="DispatchGamepadAction"/>: the library-level
    /// Cancel deliberately swallows B/Escape (so it can't bubble), which would otherwise trap Back and make
    /// the app impossible to leave. Returns true only when a modal was actually closed.
    /// </summary>
    public bool DispatchBackButton()
    {
        if (!IsGamepadMode)
            return false;

        // Nothing open to back out of → let the platform handle Back (Android exits). A soft keyboard, when
        // showing, consumes Back before it reaches the activity, so dismissing the IME stays the OS's job.
        if (!HasGamepadOverlay)
            return false;

        return DispatchGamepadAction(GamepadAction.Cancel);
    }

    private bool DispatchHotkeysOverlayAction(GamepadAction action)
    {
        // Modal like the scraper: Up/Down move the ring through the global actions and the per-emulator
        // Apply / Revert buttons, A activates the focused one, B backs out to Settings. Every other
        // action is swallowed so it cannot leak to the library beneath (e.g. LB/RB switching platforms).
        switch (action)
        {
            case GamepadAction.NavigateUp:
                GamepadHotkeys?.MoveFocus(-1);
                return true;
            case GamepadAction.NavigateDown:
                GamepadHotkeys?.MoveFocus(1);
                return true;
            case GamepadAction.Confirm:
                GamepadHotkeys?.Activate();
                return true;
            case GamepadAction.Cancel:
                BackFromGamepadOverlayCommand.Execute(null);
                return true;
            default:
                return true;
        }
    }

    private bool DispatchBatchScraperOverlayAction(GamepadAction action)
    {
        // Modal like the single-game scraper: Up/Down move the ring, A activates, B backs out. Every
        // other action is swallowed so it cannot leak to the library beneath (e.g. LB/RB switching
        // platforms mid-scrape).
        switch (action)
        {
            case GamepadAction.NavigateUp:
                GamepadBatchScraperDetails?.MoveFocus(-1);
                return true;
            case GamepadAction.NavigateDown:
                GamepadBatchScraperDetails?.MoveFocus(1);
                return true;
            case GamepadAction.Confirm:
                GamepadBatchScraperDetails?.Activate();
                return true;
            case GamepadAction.Cancel:
                BackFromGamepadOverlayCommand.Execute(null);
                return true;
            default:
                return true;
        }
    }

    private bool DispatchScraperOverlayAction(GamepadAction action)
    {
        // The scraper overlay is modal and owns its own D-pad focus: only Up/Down move the ring,
        // A activates the focused control, and B backs out. Every other action is swallowed so it
        // cannot leak to the library beneath (e.g. LB/RB switching platforms mid-scrape).
        switch (action)
        {
            case GamepadAction.NavigateUp:
                GamepadScraperDetails?.MoveFocus(-1);
                return true;
            case GamepadAction.NavigateDown:
                GamepadScraperDetails?.MoveFocus(1);
                return true;
            case GamepadAction.Confirm:
                GamepadScraperDetails?.Activate();
                return true;
            case GamepadAction.Cancel:
                BackFromGamepadOverlayCommand.Execute(null);
                return true;
            default:
                return true;
        }
    }

    private bool DispatchCoverSearchOverlayAction(GamepadAction action)
    {
        // Modal like the scraper: Up/Down move the ring across the query field, Search, the cover
        // tiles, and "Choose a file"; A activates the focused one; B backs out. The query field takes
        // real keyboard focus (via the view) so the Steam/OS on-screen keyboard types into it. Every
        // other action is swallowed so it cannot leak to the library beneath.
        switch (action)
        {
            case GamepadAction.NavigateUp:
                GamepadCoverSearchDetails?.MoveFocus(-1);
                return true;
            case GamepadAction.NavigateDown:
                GamepadCoverSearchDetails?.MoveFocus(1);
                return true;
            case GamepadAction.Confirm:
                GamepadCoverSearchDetails?.Activate();
                return true;
            case GamepadAction.Cancel:
                BackFromGamepadOverlayCommand.Execute(null);
                return true;
            default:
                return true;
        }
    }

    private bool DispatchTextOverlayAction(GamepadAction action)
    {
        // A Search/Rename overlay owns text entry (typed via the Steam/OS on-screen keyboard); the
        // controller may only confirm or dismiss it.
        switch (action)
        {
            case GamepadAction.Cancel:
                BackFromGamepadOverlayCommand.Execute(null);
                return true;
            case GamepadAction.Menu:
                OpenGamepadMenuCommand.Execute(null);
                return true;
            case GamepadAction.Confirm when IsGamepadRenameOpen:
                SaveGamepadTitleCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private bool DispatchOverlayAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.Cancel:
                BackFromGamepadOverlayCommand.Execute(null);
                return true;
            case GamepadAction.Menu:
                OpenGamepadMenuCommand.Execute(null);
                return true;
            case GamepadAction.PreviousPlatform when IsGamepadAchievementsOpen:
                GamepadAchievementDetails?.CycleFilterCommand.Execute(-1);
                return true;
            case GamepadAction.NextPlatform when IsGamepadAchievementsOpen:
                GamepadAchievementDetails?.CycleFilterCommand.Execute(1);
                return true;
            case GamepadAction.Search when IsGamepadAchievementsOpen:
                GamepadAchievementDetails?.RefreshCommand.Execute(null);
                return true;
            case GamepadAction.Actions when IsGamepadAchievementsOpen:
                CycleGamepadAchievementSortCommand.Execute(null);
                return true;
            case GamepadAction.NavigateLeft when IsGamepadAchievementsOpen:
                MoveFocusedAchievementHorizontal(-1);
                return true;
            case GamepadAction.NavigateRight when IsGamepadAchievementsOpen:
                MoveFocusedAchievementHorizontal(1);
                return true;
            case GamepadAction.NavigateLeft when IsGamepadConfirmationOverlay:
                MoveGamepadOverlaySelection(-1); // step onto Cancel (left button)
                return true;
            case GamepadAction.NavigateRight when IsGamepadConfirmationOverlay:
                MoveGamepadOverlaySelection(1); // step onto the action (right button)
                return true;
            case GamepadAction.NavigateLeft when IsGamepadSystemMenuOpen && IsGamepadViewModeRowFocused:
                MoveGamepadViewModeSelection(-1);
                return true;
            case GamepadAction.NavigateRight when IsGamepadSystemMenuOpen && IsGamepadViewModeRowFocused:
                MoveGamepadViewModeSelection(1);
                return true;
            case GamepadAction.NavigateLeft when IsGamepadSystemMenuOpen && IsGamepadSortRowFocused:
                MoveGamepadSortSelection(-1);
                return true;
            case GamepadAction.NavigateRight when IsGamepadSystemMenuOpen && IsGamepadSortRowFocused:
                MoveGamepadSortSelection(1);
                return true;
            case GamepadAction.NavigateUp:
                MoveGamepadOverlayUpCommand.Execute(null);
                return true;
            case GamepadAction.NavigateDown:
                MoveGamepadOverlayDownCommand.Execute(null);
                return true;
            case GamepadAction.Confirm when !IsGamepadAchievementsOpen:
                ActivateGamepadOverlayCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    private bool DispatchLibraryAction(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.PreviousPlatform:
                PreviousPlatformCommand.Execute(null);
                return true;
            case GamepadAction.NextPlatform:
                NextPlatformCommand.Execute(null);
                return true;
            case GamepadAction.Confirm:
                // In the spotlight, A fires whichever hero action is armed (Play by default, or the
                // Achievements widget when Left has selected it); the grid always launches.
                if (IsGamepadSpotlightView && IsSpotlightAchievementsFocused &&
                    FocusedGame?.ShowAchievementMark == true)
                    OpenFocusedAchievementsCommand.Execute(null);
                else
                    LaunchFocusedGameCommand.Execute(null);
                return true;
            case GamepadAction.Cancel:
                // Nothing to back out of at the top level; swallow B/Escape so it can't bubble.
                return true;
            case GamepadAction.Search:
                OpenGamepadSearchCommand.Execute(null);
                return true;
            case GamepadAction.Actions:
                OpenFocusedGameActionsCommand.Execute(null);
                return true;
            case GamepadAction.Menu:
                OpenGamepadMenuCommand.Execute(null);
                return true;
            // Each layout reads the d-pad differently. Spotlight is a single-column list: Up/Down step
            // one game, Left/Right move the hero action ring (Left arms Achievements when the game has a
            // set, Right arms Play). The shelf is a single horizontal row: Left/Right step one game and
            // Up/Down are inert. The cover grid keeps 2-D movement (Up/Down span a full row).
            case GamepadAction.NavigateLeft:
                switch (GamepadLayout)
                {
                    case GamepadLibraryLayout.Spotlight:
                        if (FocusedGame?.ShowAchievementMark == true)
                            IsSpotlightAchievementsFocused = true;
                        break;
                    case GamepadLibraryLayout.Shelf:
                        FocusPreviousGameCommand.Execute(null);
                        break;
                    default:
                        MoveGamepadFocusLeftCommand.Execute(null);
                        break;
                }
                return true;
            case GamepadAction.NavigateRight:
                switch (GamepadLayout)
                {
                    case GamepadLibraryLayout.Spotlight:
                        IsSpotlightAchievementsFocused = false;
                        break;
                    case GamepadLibraryLayout.Shelf:
                        FocusNextGameCommand.Execute(null);
                        break;
                    default:
                        MoveGamepadFocusRightCommand.Execute(null);
                        break;
                }
                return true;
            case GamepadAction.NavigateUp:
                switch (GamepadLayout)
                {
                    case GamepadLibraryLayout.Spotlight:
                        FocusPreviousGameCommand.Execute(null);
                        break;
                    case GamepadLibraryLayout.Shelf:
                        break; // a horizontal row has nothing above/below
                    default:
                        MoveGamepadFocusUpCommand.Execute(null);
                        break;
                }
                return true;
            case GamepadAction.NavigateDown:
                switch (GamepadLayout)
                {
                    case GamepadLibraryLayout.Spotlight:
                        FocusNextGameCommand.Execute(null);
                        break;
                    case GamepadLibraryLayout.Shelf:
                        break;
                    default:
                        MoveGamepadFocusDownCommand.Execute(null);
                        break;
                }
                return true;
            default:
                return false;
        }
    }

    private void OpenGamepadOverlay(GamepadOverlayKind overlay)
    {
        if (!IsGamepadMode)
            return;

        DisposeGamepadAchievementDetails();
        DisposeGamepadScraperDetails();
        DisposeGamepadCoverSearchDetails();
        DisposeGamepadBatchScraperDetails();
        DisposeGamepadHotkeysDetails();
        FocusedGamepadAchievement = null;
        // A pending import folder is only meaningful while the ImportSystem chooser is up. Transitioning
        // to any other overlay (e.g. Menu from the chooser) abandons the import, so drop it here — the
        // one place every overlay transition funnels through — not only on CloseGamepadOverlay.
        if (overlay != GamepadOverlayKind.ImportSystem)
            _pendingImportFolder = null;
        GamepadOverlayOptions.Clear();
        MenuFocusRegion = GamepadMenuFocusRegion.Options; // every open lands on the option list, not a selector row
        GamepadOverlay = overlay;
        IsGameActionsOpen = overlay == GamepadOverlayKind.Actions; // compatibility for existing bindings/tests

        switch (overlay)
        {
            case GamepadOverlayKind.Actions:
                AddGameActions();
                break;
            case GamepadOverlayKind.Search:
                break;
            case GamepadOverlayKind.Rename:
                break;
            case GamepadOverlayKind.DiscSelection:
                AddDiscSelectionOptions();
                break;
            case GamepadOverlayKind.ImportSystem:
                AddImportSystemOptions();
                break;
            case GamepadOverlayKind.RemoveConfirmation:
                AddOption("Cancel", BackFromGamepadOverlayCommand, isCancel: true);
                AddOption("Remove", ConfirmGamepadRemoveCommand, true);
                break;
            case GamepadOverlayKind.CoverDesktopHandoff:
                // Where Desktop exists, offer the route to it; where it does not (Android), the overlay
                // is a plain acknowledgement of "not available here" and A/B just closes it.
                if (SupportsDesktopMode)
                    AddOption("Continue to Desktop mode", RequestDesktopModeFromGamepadCommand);
                else
                    AddOption("OK", BackFromGamepadOverlayCommand, isCancel: true);
                break;
            case GamepadOverlayKind.Achievements:
                FocusFirstAchievement();
                break;
            case GamepadOverlayKind.Scraper:
            case GamepadOverlayKind.BatchScraper:
            case GamepadOverlayKind.CoverSearch:
                // These overlays render their own body and own their D-pad focus; no option list.
                break;
            case GamepadOverlayKind.SystemMenu:
                // The couch layout picker is the view-mode row at the top of the menu, not an option here.
                AddOption("Search", OpenGamepadSearchCommand);
                // Where Desktop mode is unreachable (Android), importing is controller-native from the
                // menu; on desktop couch, "Switch to Desktop" below is the import route, so it is not
                // duplicated here.
                if (!SupportsDesktopMode)
                    AddOption("Add games", AddFolderFromGamepadCommand);
                if (CanScrapeAllInView)
                    AddOption("Scrape all in view", ScrapeAllInViewCommand);
                AddOption("Settings", RequestSettingsFromGamepadCommand);
                if (SupportsDesktopMode)
                    AddOption("Switch to Desktop mode", RequestDesktopModeFromGamepadCommand);
                AddOption("Quit EmuShelf", RequestQuitFromGamepadCommand, true);
                break;
            case GamepadOverlayKind.Settings:
                break;
            case GamepadOverlayKind.DesktopModeConfirmation:
                AddOption("Cancel", BackFromGamepadOverlayCommand, isCancel: true);
                AddOption("Switch", SwitchToDesktopModeCommand);
                break;
            case GamepadOverlayKind.QuitConfirmation:
                AddOption("Cancel", BackFromGamepadOverlayCommand, isCancel: true);
                AddOption("Quit", ConfirmQuitGamepadCommand, true);
                break;
        }

        GamepadOverlaySelectionIndex = overlay == GamepadOverlayKind.DiscSelection && FocusedGame is { } selectedGame
            ? Math.Max(0, selectedGame.Discs.ToList().FindIndex(disc => disc.Game.Id == selectedGame.LaunchModel.Id))
            // Confirmations lay out [Cancel, action]; land on the action (index 1, the right button) so a
            // deliberate menu pick still confirms with a single A, and Left steps back onto Cancel.
            : IsGamepadConfirmationOverlay ? 1
            : 0;
        UpdateGamepadOverlayOptionFocus();
        NotifyGamepadOverlayState();
    }

    private void AddGameActions()
    {
        AddOption("Launch", LaunchFromGamepadOverlayCommand);
        if (FocusedGame?.IsMultiDisc == true)
            AddOption("Select disc", OpenFocusedDiscSelectionCommand);
        if (FocusedGame?.CanOpenAchievementDetails == true)
            AddOption("Achievements", OpenFocusedAchievementsCommand);
        AddOption("Edit title", EditFocusedTitleCommand);
        AddOption("Set cover", SetFocusedCoverCommand);
        AddOption("Scrape with ScreenScraper", ScrapeFocusedGameCommand);
        AddOption("Remove", RemoveFocusedGameCommand, true);
    }

    private void AddDiscSelectionOptions()
    {
        if (FocusedGame is { } game)
        {
            foreach (var disc in game.Discs)
            {
                var current = disc.Game.Id == game.LaunchModel.Id ? " (current)" : string.Empty;
                AddOption(
                    $"Disc {disc.Number}{current}",
                    new AsyncRelayCommand(() => SelectDiscFromGamepadAsync(disc)));
            }
        }
    }

    private void AddOption(string label, ICommand command, bool isDestructive = false, bool isCancel = false) =>
        GamepadOverlayOptions.Add(new GamepadOverlayOptionViewModel(label, command, isDestructive, isCancel));

    private void MoveGamepadOverlaySelection(int delta)
    {
        if (GamepadOverlayOptions.Count == 0)
            return;
        GamepadOverlaySelectionIndex = Math.Clamp(
            GamepadOverlaySelectionIndex + delta,
            0,
            GamepadOverlayOptions.Count - 1);
    }

    private void UpdateGamepadOverlayOptionFocus()
    {
        // No option carries the ring while either system-menu selector row owns focus.
        var rowFocused = IsGamepadSystemMenuOpen && MenuFocusRegion != GamepadMenuFocusRegion.Options;
        for (var index = 0; index < GamepadOverlayOptions.Count; index++)
            GamepadOverlayOptions[index].IsFocused = !rowFocused && index == GamepadOverlaySelectionIndex;
    }

    private void FocusFirstAchievement()
    {
        FocusedGamepadAchievement = GamepadAchievementDetails?.VisibleAchievements.FirstOrDefault();
    }

    private void MoveFocusedAchievementHorizontal(int direction)
    {
        var rows = GamepadAchievementDetails?.VisibleAchievements;
        if (rows is not { Count: > 0 })
            return;
        var index = FocusedGamepadAchievement is null ? 0 : rows.IndexOf(FocusedGamepadAchievement);
        if (index < 0)
            index = 0;
        var column = index % GamepadAchievementColumnCount;
        var target = index + Math.Sign(direction);
        if (target >= 0 && target < rows.Count &&
            (direction < 0 ? column > 0 : column < GamepadAchievementColumnCount - 1))
        {
            FocusedGamepadAchievement = rows[target];
        }
    }

    private void MoveFocusedAchievementVertical(int direction)
    {
        var rows = GamepadAchievementDetails?.VisibleAchievements;
        if (rows is not { Count: > 0 })
            return;
        var index = FocusedGamepadAchievement is null ? 0 : rows.IndexOf(FocusedGamepadAchievement);
        if (index < 0)
            index = 0;
        var target = index + Math.Sign(direction) * GamepadAchievementColumnCount;
        if (target >= 0 && target < rows.Count)
            FocusedGamepadAchievement = rows[target];
    }

    private void HandleGamepadAchievementDetailsPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!IsGamepadAchievementsOpen ||
            e.PropertyName != nameof(AchievementDetailsViewModel.VisibleAchievements) ||
            GamepadAchievementDetails is not { } details)
        {
            return;
        }

        var focusedId = FocusedGamepadAchievement?.AchievementId;
        FocusedGamepadAchievement = focusedId is { } achievementId
            ? details.VisibleAchievements.FirstOrDefault(row => row.AchievementId == achievementId) ??
              details.VisibleAchievements.FirstOrDefault()
            : details.VisibleAchievements.FirstOrDefault();
        BuildGamepadAchievementRows(); // re-slice the replaced visible set into rows
        GamepadAchievementLayoutRevision++;
        OnPropertyChanged(nameof(GamepadAchievementLayoutRevision));
    }

    private void DisposeGamepadAchievementDetails()
    {
        if (GamepadAchievementDetails is not { } details)
            return;

        // Clearing the property unsubscribes the change handler and empties the row list
        // (OnGamepadAchievementDetailsChanged) before the view model is disposed.
        GamepadAchievementDetails = null;
        details.Dispose();
    }

    private void DisposeGamepadScraperDetails()
    {
        if (GamepadScraperDetails is not { } details)
            return;

        // A successful apply projected a new cover and metadata: refresh the library so the focused
        // tile reflects it, mirroring the desktop scraper's post-apply reload.
        var reload = details.HasAppliedChanges;
        details.Dispose();
        GamepadScraperDetails = null;
        if (reload)
            _ = ReloadGamesAsync();
    }

    private void DisposeGamepadCoverSearchDetails()
    {
        if (GamepadCoverSearchDetails is not { } details)
            return;

        // The picked cover is imported by OnGamepadCoverPicked (which drives the grid tile update
        // itself), so there is nothing to reload here — just tear down the wrapped picker.
        details.Dispose();
        GamepadCoverSearchDetails = null;
    }

    private void DisposeGamepadBatchScraperDetails()
    {
        if (GamepadBatchScraperDetails is not { } details)
            return;

        // A batch that wrote at least one field or image changed the visible tiles; reload so the
        // library reflects it, matching the Desktop batch window's post-apply reload.
        var reload = details.Batch.AppliedChanges;
        details.Dispose();
        GamepadBatchScraperDetails = null;
        if (reload)
            _ = ReloadGamesAsync();
    }

    private void DisposeGamepadHotkeysDetails()
    {
        if (GamepadHotkeys is not { } details)
            return;

        // Unhooks the shared settings view model and clears the focus ring off the reused rows; the
        // settings projection itself is owned by GamepadSettings, so it is not disposed here.
        details.Dispose();
        GamepadHotkeys = null;
    }

    private void NotifyGamepadOverlayState()
    {
        OnPropertyChanged(nameof(HasGamepadOverlay));
        OnPropertyChanged(nameof(GamepadOverlayOwnsTextInput));
        OnPropertyChanged(nameof(IsGamepadAchievementsOpen));
        OnPropertyChanged(nameof(IsGamepadSearchOpen));
        OnPropertyChanged(nameof(IsGamepadRenameOpen));
        OnPropertyChanged(nameof(IsGamepadRemoveOpen));
        OnPropertyChanged(nameof(IsGamepadCoverHandoffOpen));
        OnPropertyChanged(nameof(IsGamepadCoverSearchOpen));
        OnPropertyChanged(nameof(IsGamepadScraperOpen));
        OnPropertyChanged(nameof(IsGamepadBatchScraperOpen));
        OnPropertyChanged(nameof(IsGamepadSystemMenuOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsOpen));
        OnPropertyChanged(nameof(IsGamepadHotkeysOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsTextEntryOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsConfirmationOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsNormal));
        OnPropertyChanged(nameof(GamepadSettingsFocusRevision));
        OnPropertyChanged(nameof(IsGamepadDesktopModeConfirmationOpen));
        OnPropertyChanged(nameof(IsGamepadQuitConfirmationOpen));
        OnPropertyChanged(nameof(IsGamepadConfirmationOverlay));
        OnPropertyChanged(nameof(ShowsGamepadConfirmationActions));
        OnPropertyChanged(nameof(AreGamepadOverlayOptionsTopAligned));
        OnPropertyChanged(nameof(UsesGamepadDefaultOverlayHints));
        OnPropertyChanged(nameof(ShowsGamepadOverlayOptions));
        OnPropertyChanged(nameof(GamepadOverlayOptionsMinHeight));
        OnPropertyChanged(nameof(ShowsGamepadOverlayChromeTitle));
        OnPropertyChanged(nameof(GamepadOverlayTitle));
    }

    [RelayCommand]
    private async Task LaunchFromGamepadOverlayAsync()
    {
        CloseGamepadOverlay();
        await LaunchFocusedGameAsync();
    }

    private async Task SelectDiscFromGamepadAsync(GameDisc disc)
    {
        var game = FocusedGame;
        CloseGamepadOverlay();
        if (game is not null)
            await SelectDiscFromLibraryAsync(game, disc);
    }

    [RelayCommand]
    private async Task SwitchToDesktopModeAsync()
    {
        CloseGamepadOverlay();
        await SetInterfaceModeAsync(InterfaceMode.Desktop);
    }

    [RelayCommand]
    private void ConfirmQuitGamepad()
    {
        CloseGamepadOverlay();
        _applicationLifetime?.Shutdown();
    }

    private async Task ShowCollectionAsync(LibraryScope scope)
    {
        CurrentLibraryScope = scope;
        if (SelectedSystem is not null)
        {
            SelectedSystem = null;
            await _selectedSystemLoad;
        }
        else
        {
            // Reached All Games / Recently Added directly (a menu, or an LB/RB stop that did not pass
            // through a system). Debounce it the same way so cycling across this stop does not thrash.
            await RequestLibraryReload();
        }
    }

    private void NotifyLibraryPresentationChanged()
    {
        OnPropertyChanged(nameof(LibraryTitle));
        OnPropertyChanged(nameof(EmptyLibraryTitle));
        OnPropertyChanged(nameof(EmptyLibraryDescription));
    }

    private void MoveFocusedGame(int delta)
    {
        if (!IsGamepadMode || Games.Count == 0)
            return;
        var index = FocusedGame is null ? 0 : Games.IndexOf(FocusedGame);
        if (index < 0)
            index = 0;
        FocusedGame = Games[Math.Clamp(index + delta, 0, Games.Count - 1)];
    }

    // The d-pad/stick move only inside the cover grid; platforms are switched by LB/RB. Each
    // direction clamps at the grid edge rather than escaping into the rail or wrapping rows.
    [RelayCommand]
    private void MoveGamepadFocusLeft()
    {
        if (FocusedGame is not { } focused)
            return;
        var index = Games.IndexOf(focused);
        if (index > 0 && index % GamepadColumnCount != 0)
            FocusedGame = Games[index - 1];
    }

    [RelayCommand]
    private void MoveGamepadFocusRight()
    {
        if (FocusedGame is not { } focused)
            return;
        var index = Games.IndexOf(focused);
        if (index >= 0 && index + 1 < Games.Count && index % GamepadColumnCount < GamepadColumnCount - 1)
            FocusedGame = Games[index + 1];
    }

    [RelayCommand]
    private void MoveGamepadFocusUp()
    {
        if (!IsGamepadMode || Games.Count == 0)
            return;

        var index = FocusedGame is null ? 0 : Math.Max(0, Games.IndexOf(FocusedGame));
        if (index < GamepadColumnCount)
            return; // Top row: stay put. Platforms are reached with LB/RB, not by moving up.

        FocusedGame = Games[index - GamepadColumnCount];
    }

    [RelayCommand]
    private void MoveGamepadFocusDown()
    {
        if (!IsGamepadMode || Games.Count == 0)
            return;

        var index = FocusedGame is null ? 0 : Math.Max(0, Games.IndexOf(FocusedGame));
        var target = index + GamepadColumnCount;
        if (target < Games.Count)
            FocusedGame = Games[target];
    }

    private string FocusScopeKey() => CurrentLibraryScope switch
    {
        LibraryScope.AllGames => "all",
        LibraryScope.RecentlyAdded => "recent",
        LibraryScope.RecentlyPlayed => "played",
        _ => "system:" + (SelectedSystem?.Id ?? string.Empty),
    };

    private void RestoreFocusedGame()
    {
        if (!IsGamepadMode)
            return;
        var restored = _focusedGameByScope.TryGetValue(FocusScopeKey(), out var id)
            ? Games.FirstOrDefault(game => game.Id == id)
            : null;
        FocusedGame = restored ?? Games.FirstOrDefault();
    }

    partial void OnGamepadViewportWidthChanged(double value)
    {
        if (value <= 0)
            return;

        // Deliberately does not write LibraryViewportWidth: that field is the desktop's, and
        // overwriting it made the desktop grid inherit the gamepad viewport after a mode switch.
        UpdateCoverLayout();
    }

    // The rail is a passive indicator of the current scope: at most one platform tab is active,
    // and the All Games tab tracks CurrentLibraryScope on its own.
    private void UpdateGamepadPlatformState()
    {
        foreach (var platform in GamepadPlatforms)
            platform.IsActive = CurrentLibraryScope == LibraryScope.System &&
                                string.Equals(platform.System.Id, SelectedSystem?.Id, StringComparison.Ordinal);
    }

    partial void OnCurrentThemeChanged(ThemePreference value)
    {
        foreach (var choice in ThemeChoices)
            choice.IsSelected = choice.Id == value;
    }

    partial void OnFocusedGameChanged(GameViewModel? oldValue, GameViewModel? newValue)
    {
        ShelfDeparturePose = oldValue is null
            ? null
            : new PhysicalShelfDeparturePose(
                oldValue.Id,
                (float)_shelfHeroRotation.Yaw,
                (float)_shelfHeroRotation.Pitch);
        OnPropertyChanged(nameof(ShelfDeparturePose));

        if (oldValue is not null)
        {
            oldValue.IsFocused = false;
            // Only the current hero keeps decoded backdrop/logo bitmaps, so scrolling a long list
            // never accumulates full-size images. The path/rating stay cached for an instant re-focus.
            oldValue.FanartImage = null;
            oldValue.WheelImage = null;
        }
        if (newValue is not null)
        {
            newValue.IsFocused = true;
            _focusedGameByScope[FocusScopeKey()] = newValue.Id;
            PrefetchCoversAroundFocus(newValue);
        }

        MoveShelfToFocusedGame(oldValue, newValue);

        // A new game arrives face-on. Carrying the previous game's angle over would present the
        // next cover already turned away, which reads as the shelf being broken rather than as
        // the pose being preserved.
        RecentreShelfHero();

        // Picking a new game re-arms Play, so A always launches the freshly focused game by default.
        IsSpotlightAchievementsFocused = false;

        ScheduleAmbientThemeUpdate(newValue);
        if (IsGamepadSpotlightView)
            LoadSpotlightHero(newValue);
    }

    // Warm the covers a few rows either side of the focused tile so the grid glides over already-loaded
    // artwork instead of blank frames that pop in a beat late. Cover loads are gated on tile realization
    // (OnGameCoverAttached), which is one row too late for a smooth scroll; this decouples the load from
    // realization and runs slightly ahead of it. Loading a cover already loaded/loading is a synchronous
    // no-op, and covers persist on the view model, so re-warming the same window each step is cheap.
    private void PrefetchCoversAroundFocus(GameViewModel focused)
    {
        if (!IsGamepadMode || Games.Count == 0)
            return;

        var index = Games.IndexOf(focused);
        if (index < 0)
            return;

        if (ShowGamepadShelf)
        {
            var shelfStart = Math.Max(0, index - ShelfNeighbourPrefetchRadius);
            var shelfEnd = Math.Min(Games.Count - 1, index + ShelfNeighbourPrefetchRadius);
            for (var shelfIndex = shelfStart; shelfIndex <= shelfEnd; shelfIndex++)
            {
                var shelfGame = Games[shelfIndex];
                if (shelfGame.LoadCoverCommand.CanExecute(shelfGame))
                    shelfGame.LoadCoverCommand.Execute(shelfGame);
            }
            return;
        }

        var columns = Math.Max(1, GamepadColumnCount);
        var rowIndex = index / columns;
        var startRow = Math.Max(0, rowIndex - GamepadCoverPrefetchRows);
        var start = startRow * columns;
        var end = Math.Min(Games.Count - 1, (rowIndex + GamepadCoverPrefetchRows + 1) * columns - 1);
        for (var i = start; i <= end; i++)
        {
            var game = Games[i];
            if (game.LoadCoverCommand.CanExecute(game))
                game.LoadCoverCommand.Execute(game);
        }
    }

    private void ScheduleAmbientThemeUpdate(GameViewModel? game)
    {
        if (!IsGamepadMode || !AmbientThemeFromArtwork)
            return;

        _ambientPendingGame = game;
        _ambientThemeDebounce.Stop();
        _ambientThemeDebounce.Start();
    }

    partial void OnAmbientThemeFromArtworkChanged(bool value)
    {
        _ = _themeService.SetAmbientFromArtworkAsync(value);
        if (value)
        {
            _ambientPendingGame = FocusedGame;
            ApplyAmbientThemeForPendingGame();
        }
        else
        {
            _ambientThemeDebounce?.Stop();
            _themeService.ClearArtworkPalette();
        }
    }

    private void ApplyAmbientThemeForPendingGame()
    {
        if (!AmbientThemeFromArtwork || !IsGamepadMode)
            return;

        var game = _ambientPendingGame;
        if (game is null)
            return;

        // The spotlight's fan-art backdrop fills the screen, so recolour from it there; the cover
        // drives the grid. Until the fan art has decoded, fall back to the cover so the tint still
        // moves with focus (LoadSpotlightHero re-triggers this once the backdrop is ready).
        var useFanart = IsGamepadSpotlightView && game.FanartImage is not null;
        var key = useFanart ? game.FanartPath : game.CoverPath;
        if (string.IsNullOrEmpty(key))
        {
            // No art for this game → show the chosen theme rather than the previous game's colour.
            _themeService.ClearArtworkPalette();
            return;
        }

        if (_ambientPaletteCache.TryGetValue(key, out var cached))
        {
            _ambientLastIsDark = cached.IsDark;
            _themeService.ApplyArtworkPalette(cached);
            return;
        }

        var image = useFanart ? game.FanartImage : game.CoverImage;
        if (image is null)
            return; // art not decoded yet; keep the current palette until it loads

        var pixels = ArtworkPaletteExtractor.CopyPixels(image);
        if (pixels is null)
            return;

        _ = AnalyzeAndApplyAmbientAsync(game, key, pixels, _ambientLastIsDark);
    }

    private async Task AnalyzeAndApplyAmbientAsync(
        GameViewModel game,
        string key,
        byte[] pixels,
        bool? previousIsDark)
    {
        var palette = await Task.Run(() => ArtworkPaletteExtractor.FromBgraPixels(pixels, previousIsDark));

        // Focus or mode may have changed while extracting; only apply if this is still the pending game.
        if (!AmbientThemeFromArtwork || !IsGamepadMode || !ReferenceEquals(_ambientPendingGame, game))
            return;

        if (palette is null)
        {
            _themeService.ClearArtworkPalette();
            return;
        }

        _ambientPaletteCache[key] = palette;
        _ambientLastIsDark = palette.IsDark;
        _themeService.ApplyArtworkPalette(palette);
    }

    // Fan art (typically ~1920×1080) is decoded to fit this box for the hero; one bitmap exists at a
    // time, so this is a safe upper bound on the spotlight's image memory.
    private const int SpotlightFanartMaxWidth = 1920;
    private const int SpotlightFanartMaxHeight = 1080;
    private const int SpotlightWheelMaxWidth = 900;
    private const int SpotlightWheelMaxHeight = 400;

    /// <summary>Resolves the focused game's fan art + rating for the spotlight hero. Its scraped
    /// details are read once per game (off the UI thread) and cached on the view model, then the
    /// fan-art bitmap is decoded — only for the current hero, and only while focus has not moved on.</summary>
    private void LoadSpotlightHero(GameViewModel? game)
    {
        if (!IsGamepadMode || !IsGamepadSpotlightView || game is null)
            return;

        // The cover grid is hidden in spotlight mode, so its tile-attach path never realizes the
        // focused cover. The spotlight itself doesn't show the cover (the no-fanart backdrop is a
        // themed gradient), so decode it only when the ambient palette needs a source before the
        // fan art has loaded.
        if (AmbientThemeFromArtwork && game.CoverImage is null && game.LoadCoverCommand.CanExecute(game))
            game.LoadCoverCommand.Execute(game);

        var generation = ++_spotlightHeroGeneration;
        _ = LoadSpotlightHeroAsync(game, generation);
    }

    private async Task LoadSpotlightHeroAsync(GameViewModel game, int generation)
    {
        try
        {
            if (!game.AreSpotlightDetailsLoaded)
            {
                if (_gameDetails is not null)
                {
                    var resolved = await Task.Run(() => ResolveSpotlightDetails(game.Id));
                    if (generation != _spotlightHeroGeneration)
                        return; // focus moved on while the details were being read
                    game.ApplySpotlightDetails(resolved.FanartPath, resolved.WheelPath, resolved.RatingText, resolved.Facts);
                }
                else
                {
                    // No details store (a degraded/headless config): there is no art to resolve, so
                    // mark the hero resolved with none. That engages the title fallback rather than
                    // leaving the hero with neither a logo nor a name.
                    game.ApplySpotlightDetails(null, null, null, []);
                }
            }

            await LoadSpotlightBitmapAsync(
                game, generation,
                () => game.FanartPath,
                () => game.FanartImage is not null,
                image => game.FanartImage = image,
                SpotlightFanartMaxWidth, SpotlightFanartMaxHeight);

            // The cover-based tint was a placeholder while the backdrop decoded; recolour from the
            // fan art now that it exists so the accent matches what fills the screen.
            if (game.FanartImage is not null && generation == _spotlightHeroGeneration)
                ScheduleAmbientThemeUpdate(game);

            await LoadSpotlightBitmapAsync(
                game, generation,
                () => game.WheelPath,
                () => game.WheelImage is not null,
                image => game.WheelImage = image,
                SpotlightWheelMaxWidth, SpotlightWheelMaxHeight);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not load the spotlight hero art for {game.Title}: {ex.Message}");
        }
    }

    // Decodes one hero asset (fan art or logo) off the UI thread and assigns it only if this game is
    // still the focused hero for this generation, so a fast scroll never leaks a bitmap onto a stale VM.
    private async Task LoadSpotlightBitmapAsync(
        GameViewModel game,
        int generation,
        Func<string?> path,
        Func<bool> alreadyLoaded,
        Action<Bitmap> assign,
        int maxWidth,
        int maxHeight)
    {
        if (alreadyLoaded() || path() is not { Length: > 0 } source || !File.Exists(source))
            return;

        var image = await Task.Run(() => SafeImageDecoder.DecodeToFit(source, maxWidth, maxHeight));
        if (generation != _spotlightHeroGeneration || !ReferenceEquals(FocusedGame, game))
        {
            image.Dispose();
            return;
        }

        assign(image);
    }

    private (string? FanartPath, string? WheelPath, string? RatingText, IReadOnlyList<string> Facts) ResolveSpotlightDetails(long gameId)
    {
        if (_gameDetails is null)
            return (null, null, null, []);

        var details = _gameDetails.GetDetails(gameId);
        return (
            SelectSpotlightMedia(details.Media, GameMediaKind.Fanart),
            SelectSpotlightMedia(details.Media, GameMediaKind.Wheel),
            FormatSpotlightRating(details.Metadata),
            ComposeSpotlightInfo(details.Metadata));
    }

    // The spotlight hero's metadata chips: genre, year, players, developer, publisher, from the scraped
    // metadata — one entry per field that is present. Publisher is dropped when it merely repeats the
    // developer (common for first-party titles). The launch filename is shown separately as a caption.
    internal static IReadOnlyList<string> ComposeSpotlightInfo(IReadOnlyList<GameMetadataValue> metadata)
    {
        string? Field(GameMetadataField field) =>
            metadata.FirstOrDefault(value => value.Field == field)?.Value is { Length: > 0 } v ? v : null;

        var year = Field(GameMetadataField.ReleaseDate) is { } date && date.Length >= 4 ? date[..4] : null;
        var players = Field(GameMetadataField.Players) is { } count
            ? (count == "1" ? "1 player" : $"{count} players")
            : null;

        var developer = Field(GameMetadataField.Developer);
        var publisher = Field(GameMetadataField.Publisher);
        if (publisher is not null && string.Equals(publisher, developer, StringComparison.OrdinalIgnoreCase))
            publisher = null;

        return new[] { Field(GameMetadataField.Genre), year, players, developer, publisher }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!)
            .ToArray();
    }

    private static string? SelectSpotlightMedia(IReadOnlyList<GameMediaAsset> media, GameMediaKind kind) =>
        media
            .Where(asset => asset.Kind == kind)
            .OrderByDescending(asset => asset.IsSelected)
            .Select(asset => asset.LocalPath)
            .FirstOrDefault(path => !string.IsNullOrEmpty(path) && File.Exists(path));

    private static string? FormatSpotlightRating(IReadOnlyList<GameMetadataValue> metadata)
    {
        var raw = metadata.FirstOrDefault(value => value.Field == GameMetadataField.Rating)?.Value;
        if (raw is null ||
            !double.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var provider))
            return null;

        // ScreenScraper stores its rating on a 0–20 scale; present it as the 0–10 star score the
        // spotlight hero shows (e.g. 14/20 → "7.0").
        var score = Math.Clamp(provider / 2.0, 0, 10);
        return score.ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>Releases the hero's decoded fan art (leaving couch mode) so it is not held while the
    /// spotlight is off screen; the cheap path/rating cache is kept for an instant re-focus.</summary>
    private void ClearSpotlightHero()
    {
        _spotlightHeroGeneration++;
        if (FocusedGame is { } game)
        {
            game.FanartImage = null;
            game.WheelImage = null;
        }
    }

    partial void OnGamepadOverlayChanged(GamepadOverlayKind value)
    {
        NotifyGamepadOverlayState();
        // Details may have been assigned before the overlay opened (row build is deferred while it is
        // closed), so slice the rows now that it is showing. SizeChanged corrects the column count.
        if (value == GamepadOverlayKind.Achievements)
            BuildGamepadAchievementRows();
    }

    partial void OnGamepadOverlaySelectionIndexChanged(int value) => UpdateGamepadOverlayOptionFocus();

    partial void OnFocusedGamepadAchievementChanged(
        AchievementRowViewModel? oldValue,
        AchievementRowViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsFocused = false;
        if (newValue is not null)
            newValue.IsFocused = true;
        OnPropertyChanged(nameof(HasFocusedGamepadAchievement));
    }

    partial void OnGamepadAchievementDetailsChanged(
        AchievementDetailsViewModel? oldValue,
        AchievementDetailsViewModel? newValue)
    {
        // Wire the row projection to whichever details are current, whether assigned by the open
        // command or directly (as tests do). The handler rebuilds rows when the visible set changes
        // (filter/sort/refresh); this initial build covers the set the constructor already populated.
        if (oldValue is not null)
            oldValue.PropertyChanged -= HandleGamepadAchievementDetailsPropertyChanged;
        if (newValue is not null)
            newValue.PropertyChanged += HandleGamepadAchievementDetailsPropertyChanged;
        BuildGamepadAchievementRows();
    }

    partial void OnIsGamepadModeChanged(bool value)
    {
        // Entering Gamepad mode before its grid has ever been measured would leave GamepadColumnCount
        // at its default of 1, so row-wise Up/Down would step a single tile (behaving like Left/Right)
        // and the reveal could strand the selector off-screen. Seed the gamepad viewport from the
        // desktop's so a real column count exists on entry; the gamepad grid's own SizeChanged still
        // corrects it once it lays out.
        if (value && GamepadViewportWidth <= 0 && LibraryViewportWidth > 0)
            GamepadViewportWidth = LibraryViewportWidth;

        // The newly visible mode has its own viewport and inset, so the covers have to be re-sized
        // for it here. Waiting for that view's SizeChanged is not enough: if its width has not
        // changed since it was last shown, the event never comes and the tiles keep the other
        // mode's dimensions.
        UpdateCoverLayout();

        OnPropertyChanged(nameof(ShowGamepadGrid));
        OnPropertyChanged(nameof(ShowGamepadSpotlight));
        OnPropertyChanged(nameof(ShowGamepadShelf));
        OnPropertyChanged(nameof(ShowShelfTube));
        OnPropertyChanged(nameof(ShowCouchScene));
        OnPropertyChanged(nameof(ShowInlineShelfScene));
        OnPropertyChanged(nameof(ShowShelfFlatBackdrop));
        OnPropertyChanged(nameof(IsShelfTubeActive));
        OnPropertyChanged(nameof(ShelfSceneItems));
        NotifySaveSyncPresentationChanged();

        // Leaving couch mode entirely: drop any launch pose so it cannot resurface next session.
        if (!value)
            AbandonOrphanedShelfLaunchTransition();

        if (value)
        {
            IsGamepadControllerInputActive = true;
            IsGridView = true;
            BuildGamepadRows(); // populate the row list for the grid we're about to show
            RestoreFocusedGame();
            if (AmbientThemeFromArtwork)
            {
                _ambientPendingGame = FocusedGame;
                ApplyAmbientThemeForPendingGame();
            }
            if (IsGamepadSpotlightView)
                LoadSpotlightHero(FocusedGame); // decode the hero for the restored focus
        }
        else
        {
            CloseGamepadOverlay();
            GamepadRows.Clear(); // drop the row tiles' view models while the gamepad grid is hidden
            // Desktop keeps the chosen theme; the artwork palette is a couch-mode effect.
            _ambientThemeDebounce?.Stop();
            if (AmbientThemeFromArtwork)
                _themeService.ClearArtworkPalette();
            // Back on the desktop: if we land in the list view, make sure its scraped columns have
            // their projection (gamepad builds skip the read).
            EnsureDetailsProjectionsLoaded();
        }
    }

    /// <summary>
    /// Applies a library item gesture. The view only reports modifier keys; selection state and
    /// its range anchor remain shared between the grid and list representations. Ctrl/Cmd+Shift
    /// extends a range, while Shift alone replaces the previous selection with that range.
    /// </summary>
    public void SelectGame(GameViewModel game, bool toggle = false, bool selectRange = false)
    {
        if (IsBusy || !Games.Contains(game))
            return;

        if (selectRange && _selectionAnchor is not null && Games.Contains(_selectionAnchor))
        {
            var start = Games.IndexOf(_selectionAnchor);
            var end = Games.IndexOf(game);
            if (!toggle)
                DeselectAllGames();
            for (var index = Math.Min(start, end); index <= Math.Max(start, end); index++)
                Games[index].IsSelected = true;
        }
        else if (toggle)
        {
            game.IsSelected = !game.IsSelected;
            _selectionAnchor = game;
        }
        else
        {
            DeselectAllGames();
            game.IsSelected = true;
            _selectionAnchor = game;
        }

        // Shift without an existing anchor behaves like an ordinary click and establishes one.
        if (_selectionAnchor is null || !Games.Contains(_selectionAnchor))
            _selectionAnchor = game;
        NotifySelectionChanged();
    }

    // Rubber-band (marquee) selection. The view drives the geometry — it hit-tests realized tiles
    // against the drawn box and reports which games are inside — while this owns the selection state
    // so grid and list share one model with click/Shift/Ctrl selection. A non-additive drag replaces
    // the selection; Ctrl/Cmd keeps the pre-drag selection as an immutable base and adds to it.
    public void BeginMarqueeSelection(bool additive)
    {
        if (IsBusy)
            return;

        if (additive)
        {
            _marqueeBaseSelection = Games.Where(game => game.IsSelected).ToHashSet();
        }
        else
        {
            _marqueeBaseSelection = [];
            DeselectAllGames();
            NotifySelectionChanged();
        }
    }

    // Only realized (on-screen) tiles are reported; off-screen games are left untouched, so a game
    // the box already claimed keeps its selection when it scrolls out of view.
    public void UpdateMarqueeSelection(
        IReadOnlyCollection<GameViewModel> realizedGames,
        IReadOnlyCollection<GameViewModel> gamesInBox)
    {
        if (_marqueeBaseSelection is null)
            return;

        var inBox = gamesInBox as ISet<GameViewModel> ?? gamesInBox.ToHashSet();
        foreach (var game in realizedGames)
        {
            var shouldSelect = _marqueeBaseSelection.Contains(game) || inBox.Contains(game);
            if (game.IsSelected != shouldSelect)
                game.IsSelected = shouldSelect;
        }

        NotifySelectionChanged();
    }

    public void EndMarqueeSelection()
    {
        if (_marqueeBaseSelection is null)
            return;

        _marqueeBaseSelection = null;
        _selectionAnchor = Games.FirstOrDefault(game => game.IsSelected);
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void SelectAllGames()
    {
        if (IsBusy)
            return;

        foreach (var game in Games)
            game.IsSelected = true;

        _selectionAnchor = Games.FirstOrDefault();
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        DeselectAllGames();

        _selectionAnchor = null;
        NotifySelectionChanged();
    }

    private void DeselectAllGames()
    {
        foreach (var game in _systemGames.Concat(Games).Distinct())
            game.IsSelected = false;
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedGameCount));
        OnPropertyChanged(nameof(HasSelectedGames));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(SelectionRemovalText));

        // This runs on every pointer-move and auto-scroll tick during a rubber-band drag, but the
        // per-game strings below are a pure function of the selected count, so re-broadcasting them
        // to every tile when the count has not moved is wasted work. Skip it unless the count changed.
        // New tiles only enter the library while the selection is cleared (a scope load resets it
        // first), and their defaults already match the count≤1 wording, so none miss an update.
        var count = SelectedGameCount;
        if (count != _broadcastSelectionCount)
        {
            _broadcastSelectionCount = count;
            var removalText = SelectionRemovalText;
            var canScrapeSelection = count > 1;
            var scrapeText = $"Scrape {count} selected with ScreenScraper…";
            foreach (var game in _systemGames.Concat(Games).Distinct())
            {
                game.SelectionRemovalText = removalText;
                game.SelectionScrapeText = scrapeText;
                game.CanScrapeSelection = canScrapeSelection;
            }
        }

        RemoveSelectedGamesCommand.NotifyCanExecuteChanged();
        ScrapeSelectedGamesCommand.NotifyCanExecuteChanged();
    }

    // Recompute the cover width for the current viewport so a whole number of columns fills the
    // row (no lopsided right gutter), then push it and the shared shelf height to every tile. The
    // shelf height is the tallest cover in the view so a mixed collection stays baseline-aligned.
    private void UpdateCoverLayout(bool applyVisibleShelf = true)
    {
        var coverWidth = MinCoverWidth;
        var available = ActiveViewportWidth - ActiveGridHorizontalPadding;
        if (available >= MinCoverWidth)
        {
            var columns = ColumnsThatFit(available, MinCoverWidth);
            coverWidth = Math.Floor((available - (columns - 1) * CoverColumnSpacing) / columns);
            coverWidth = Math.Clamp(coverWidth, MinCoverWidth, MaxCoverWidth);
        }

        // Drives the layout's cell width; the view sets UniformGridLayout.MinItemWidth from it.
        GridCoverWidth = coverWidth;

        // D-pad up/down steps a whole row, so this must be the number of columns the layout
        // actually renders. It is derived from the same width and inset the cells are sized from:
        // when the two disagreed by one, Up/Down landed on the wrong tile and the reveal scrolled
        // to it, which read as the grid jumping and games vanishing.
        if (IsGamepadMode && GamepadViewportWidth > 0)
        {
            GamepadColumnCount = ColumnsThatFit(
                Math.Max(0, GamepadViewportWidth - GamepadGridHorizontalPadding),
                coverWidth);
        }

        if (_systemGames.Count == 0)
            return;

        var shelfCoverHeight = _systemGames.Max(
            game => Math.Round(coverWidth / game.CoverAspectRatio));
        var gamepadCoverHeight = GamepadCoverHeightFor(_systemGames, coverWidth);
        foreach (var game in _systemGames)
            game.ApplyCoverLayout(coverWidth, shelfCoverHeight, gamepadCoverHeight);

        if (applyVisibleShelf)
            ApplyVisibleCoverShelf(coverWidth);
    }

    /// <summary>
    /// How many cells of <paramref name="itemWidth"/> fit in <paramref name="available"/>, matching
    /// UniformGridLayout's own arithmetic so the view model and the layout never disagree.
    /// </summary>
    private static int ColumnsThatFit(double available, double itemWidth) =>
        ColumnsThatFit(available, itemWidth, CoverColumnSpacing);

    private static int ColumnsThatFit(double available, double itemWidth, double spacing) => Math.Max(
        1,
        (int)((available + spacing) / (itemWidth + spacing)));

    private void ApplyVisibleCoverShelf(double coverWidth)
    {
        if (Games.Count == 0)
            return;

        var shelfCoverHeight = Games.Max(
            game => Math.Round(coverWidth / game.CoverAspectRatio));
        var gamepadCoverHeight = GamepadCoverHeightFor(Games, coverWidth);
        foreach (var game in Games)
            game.ApplyCoverLayout(coverWidth, shelfCoverHeight, gamepadCoverHeight);
    }

    // The gamepad grid unifies tile heights ONLY when a view mixes platforms. A single-platform view
    // (any System scope, or a collection that happens to hold one system) keeps that platform's true
    // cover shape, so its covers fill the frame with no letterbox bars — only a genuinely mixed view,
    // which would otherwise be a ragged skyline of covers at different heights, is flattened onto one
    // frame (covers cropped to fill). Returns the gamepad frame height every tile in this view uses.
    internal static double GamepadCoverHeightFor(IReadOnlyList<GameViewModel> games, double coverWidth)
    {
        if (games.Count == 0)
            return 0;

        var firstSystem = games[0].SystemId;
        var mixed = false;
        for (var i = 1; i < games.Count; i++)
        {
            if (!string.Equals(games[i].SystemId, firstSystem, StringComparison.Ordinal))
            {
                mixed = true;
                break;
            }
        }

        return mixed
            ? Math.Round(coverWidth / GameViewModel.GamepadMixedCoverAspectRatio)
            : Math.Round(coverWidth / games[0].CoverAspectRatio);
    }

    // Entry point for reloads driven by a selection change (LB/RB platform cycling, or a scope switch).
    // Returns a task that completes once the games for the new scope are on screen, so the few callers
    // that set the selection and then await the load still observe the finished grid. Direct callers
    // (rescan, add/remove, availability passes) keep calling ReloadGamesAsync so their reloads rebuild.
    private Task RequestLibraryReload()
    {
        var scopeKey = DescribeScope(CurrentLibraryScope, SelectedSystem);

        // Already built and still valid: swap it in synchronously so the correct games appear instantly
        // — no blank frame, no debounce. ReloadGamesAsync's cache fast path returns without awaiting.
        if (_scopeCache.ContainsKey(scopeKey))
        {
            _platformReloadDebounce.Stop();
            var pending = _platformReloadCompletion;
            _platformReloadCompletion = null;
            var swap = ReloadGamesAsync(useCache: true);
            pending?.TrySetResult();
            return swap;
        }

        // Not built yet: clear the outgoing tiles now so one platform's library can't sit under
        // another platform's title, then debounce the heavy build so cycling through several unvisited
        // platforms only builds the one the user settles on.
        if (!string.Equals(scopeKey, _displayedScopeKey, StringComparison.Ordinal))
            BeginScopeChange();
        _platformReloadCompletion ??=
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _platformReloadDebounce.Stop();
        _platformReloadDebounce.Start();
        return _platformReloadCompletion.Task;
    }

    // Default (useCache: false) is the safe, data-changing reload used by every mutation and refresh
    // path: it drops the whole scope cache so the rebuild reflects the DB. Only the navigation hot path
    // (platform cycling) passes useCache: true to reuse a previously built scope. Defaulting to the
    // safe behavior means a caller that forgets the flag rebuilds (correct, just not cached) rather
    // than serving stale tiles.
    internal async Task ReloadGamesAsync(bool useCache = false)
    {
        var system = SelectedSystem;
        var scope = CurrentLibraryScope;
        if (scope == LibraryScope.System && system is null)
            return;

        var scopeKey = DescribeScope(scope, system);

        // A data-changing reload: drop every cached scope so each rebuilds from the DB. Scopes that are
        // not on screen are disposed now; the on-screen scope's view models stay alive until their
        // freshly built replacement is ready (handled below), so the grid never shows disposed tiles.
        if (!useCache)
        {
            foreach (var (key, list) in _scopeCache)
            {
                if (!string.Equals(key, _displayedScopeKey, StringComparison.Ordinal))
                    foreach (var vm in list)
                        vm.Dispose();
            }
            _scopeCache.Clear();
        }

        // Fast path: this scope has been built before and nothing has invalidated it. Reuse its view
        // models instantly — no DB read, no rebuild, no dispose, covers already loaded. Synchronous, so
        // it cannot be pre-empted by a competing reload.
        if (useCache && _scopeCache.TryGetValue(scopeKey, out var cachedGames))
        {
            // A cache hit reuses the very same GameViewModel instances, so any leftover IsSelected
            // flags (and the anchor/focus that point at them) would ride back in when the user
            // returns to a scope. The slow path resets this via BeginScopeChange; mirror it here so
            // switching to a cached scope clears the selection exactly like switching to an unbuilt
            // one. Only reset on an actual scope change so re-reading the on-screen scope is a no-op.
            if (!string.Equals(scopeKey, _displayedScopeKey, StringComparison.Ordinal))
            {
                ClearSelection();
                FocusedGame = null;
            }

            // Cancel any slow reload still in flight so it cannot land after us and overwrite the
            // scope we just switched to.
            ++_loadGeneration;
            _systemGames.Clear();
            _systemGames.AddRange(cachedGames);
            UpdateCoverLayout(applyVisibleShelf: false);
            ApplyFilter();
            _displayedScopeKey = scopeKey;
            IsLibraryLoading = false;
            // Cached view models keep any projection they were built with; load it now only if this
            // scope has never had one and the list view is showing.
            EnsureDetailsProjectionsLoaded();
            return;
        }

        var generation = ++_loadGeneration;

        // The scraped-metadata columns only show in the Desktop list view, so build their projection
        // during the load only when that view is active; grid/gamepad scopes skip the read and load
        // lazily if the user later switches to the list (M40 item 2). Captured on the UI thread here.
        var listActive = !IsGridView && !IsGamepadMode;

        // The rail, the title and the count all move to the new platform the instant the selection
        // changes, but the games behind them only arrive two awaits later. Drop the outgoing
        // platform's tiles now so that gap shows an empty grid rather than one platform's library
        // sitting under another platform's name. A reload of the scope already on screen (an
        // availability pass, a rescan) keeps its tiles, so refreshes do not flash.
        if (!string.Equals(scopeKey, _displayedScopeKey, StringComparison.Ordinal))
            BeginScopeChange();

        try
        {
            var populatedSystemIds = await Task.Run(ReadPopulatedSystemIds);
            if (generation != _loadGeneration)
                return;
            RefreshNavigationSystems(populatedSystemIds);
            if (!ShowEmptyPlatforms && scope == LibraryScope.System && system is not null &&
                !populatedSystemIds.Contains(system.Id))
            {
                await ShowCollectionAsync(LibraryScope.AllGames);
                return;
            }

            var artworkBySystem = (scope == LibraryScope.System && system is not null
                    ? [system.Id]
                    : _systemsById.Keys)
                .ToDictionary(
                    systemId => systemId,
                    PlatformArtwork.ForSystem,
                    StringComparer.Ordinal);
            var games = await Task.Run(() =>
            {
                var loaded = scope switch
                {
                    // Group before limiting. Limiting raw rows first can split a title when one of
                    // its discs falls just outside the newest 30 imported/played files.
                    LibraryScope.RecentlyAdded => _library.GetGames(),
                    LibraryScope.RecentlyPlayed => _library.GetGames(),
                    LibraryScope.System => _library.GetGames(system!.Id),
                    _ => _library.GetGames(),
                };

                var titleSets = GameDiscSetBuilder.Build(loaded, _library.GetDiscSelections());
                if (scope == LibraryScope.RecentlyAdded)
                {
                    titleSets = titleSets
                        .OrderByDescending(titleSet => titleSet.Discs.Max(disc => disc.Game.DateAdded))
                        .ThenBy(titleSet => titleSet.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                        .Take(30)
                        .ToArray();
                }
                else if (scope == LibraryScope.RecentlyPlayed)
                {
                    // A set surfaces by its most recently played disc; never-played sets (every disc
                    // LastPlayedAt == null) are excluded so the collection only shows games you've launched.
                    titleSets = titleSets
                        .Select(titleSet => (titleSet, lastPlayed: titleSet.Discs.Max(disc => disc.Game.LastPlayedAt)))
                        .Where(entry => entry.lastPlayed is not null)
                        .OrderByDescending(entry => entry.lastPlayed)
                        .ThenBy(entry => entry.titleSet.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                        .Take(30)
                        .Select(entry => entry.titleSet)
                        .ToArray();
                }
                var viewModels = new List<GameViewModel>(titleSets.Count);
                foreach (var titleSet in titleSets)
                {
                    var game = titleSet.DisplayGame;
                    if (!_systemsById.TryGetValue(game.SystemId, out var gameSystem))
                        continue;

                    artworkBySystem.TryGetValue(game.SystemId, out var artwork);
                    var viewModel = new GameViewModel(
                        game,
                        gameSystem.Name,
                        gameSystem.ShortName,
                        gameSystem.AccentColor,
                        LaunchGameCommand,
                        SaveGameTitleCommand,
                        SetGameCoverCommand,
                        LoadGameCoverCommand,
                        artwork,
                        gameSystem.CoverAspectRatio,
                        OpenAchievementDetailsCommand,
                        RemoveSelectedGamesCommand,
                        ScrapeSelectedGamesCommand,
                        titleSet.Discs,
                        titleSet.SelectedDisc,
                        titleSet.DisplayTitle,
                        titleSet.SelectionKey,
                        LaunchSelectedDiscFromLibraryAsync,
                        ScrapeGameCommand,
                        ShowGameInFolderCommand,
                        OpenTextureFolderCommand,
                        TexturePackProviderRegistry.Find(game.SystemId) is not null);
                    viewModels.Add(viewModel);
                }

                ApplyAchievementDisplays(viewModels);
                ApplyTexturePackDisplays(viewModels);
                ApplyScrapedTitles(viewModels);
                ApplyPhysicalMediaTexturePaths(viewModels);
                if (listActive)
                    ApplyDetailsProjections(viewModels);
                return viewModels;
            });

            // A newer reload (system switch, or the post-availability refresh) started while we
            // were reading — discard this stale result so it can't overwrite the current view.
            if (generation != _loadGeneration)
            {
                foreach (var game in games)
                    game.Dispose();
                return;
            }

            ClearSelection();

            // Dispose the outgoing on-screen view models only if the cache is not keeping them. On a
            // scope switch the previous scope stays cached, so its tiles must survive; on a forced
            // rebuild its cache entry was dropped above, so now that the replacement is built they can
            // be released here.
            var outgoingRetained = _displayedScopeKey is { } outgoingKey && _scopeCache.ContainsKey(outgoingKey);
            if (!outgoingRetained)
                foreach (var existingGame in _systemGames)
                    existingGame.Dispose();

            _systemGames.Clear();
            _systemGames.AddRange(games);
            _scopeCache[scopeKey] = games;
            // Games still contains the previous scope here. ApplyFilter replaces it immediately
            // afterward and performs the authoritative visible-shelf pass.
            UpdateCoverLayout(applyVisibleShelf: false);
            ApplyFilter();
            _displayedScopeKey = scopeKey;
            IsLibraryLoading = false;
            // These are freshly built view models: they carry a projection only if it was applied in
            // the worker above (list view active). Track that so a later switch to the list can tell
            // whether it still needs to load one.
            if (listActive)
                _scopesWithProjections.Add(scopeKey);
            else
                _scopesWithProjections.Remove(scopeKey);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not load the current library view.", ex);
            SetStatus($"Could not load library: {ex.Message}", StatusSeverity.Error);
            // Only the newest load may lower the flag; an older one failing must not un-blank a
            // view that a newer load is still filling.
            if (generation == _loadGeneration)
                IsLibraryLoading = false;
        }
    }

    /// <summary>
    /// Identifies what the library is showing, so a reload can tell "the user moved to a different
    /// platform" from "re-read the platform already on screen".
    /// </summary>
    private static string DescribeScope(LibraryScope scope, GameSystem? system) =>
        scope == LibraryScope.System ? $"system:{system?.Id}" : scope.ToString();

    /// <summary>
    /// Empties the visible grid for an incoming scope. The empty-library and no-results panels are
    /// suppressed meanwhile: nothing is known yet, and claiming the platform is empty before it has
    /// been read is its own wrong answer. <see cref="ApplyFilter"/> restores all of it.
    /// </summary>
    private void BeginScopeChange()
    {
        IsLibraryLoading = true;
        ClearSelection();
        FocusedGame = null;
        Games.Clear();
        HasGames = false;
        IsLibraryEmpty = false;
        IsSearchEmpty = false;
    }

    // A just-refreshed detail carries the account's current unlocks. Re-apply the display for every
    // loaded tile linked to that RA game so the focused-game widget and grid mark reflect a new
    // unlock immediately, whether the refresh came from the post-exit pass or from opening the
    // achievements overlay. This never touches the network: it re-reads the local stores the reload
    // path already uses. Marshaled to the UI thread because the details service raises this from the
    // request continuation.
    private void OnAchievementDetailsRefreshed(RetroAchievementsDetailsSnapshot snapshot)
    {
        if (_retroAchievementsRead is null)
            return;

        if (Dispatcher.UIThread.CheckAccess())
            ApplyRefreshedAchievementDisplay(snapshot.Details.GameId);
        else
            Dispatcher.UIThread.Post(() => ApplyRefreshedAchievementDisplay(snapshot.Details.GameId));
    }

    private void ApplyRefreshedAchievementDisplay(int retroAchievementsGameId)
    {
        if (_retroAchievementsRead is null)
            return;

        try
        {
            var affected = _systemGames
                .Where(game => game.RetroAchievementsGameId == retroAchievementsGameId)
                .ToArray();
            if (affected.Length == 0)
                return;

            var links = _retroAchievementsRead.GetAllLinks();
            var progress = _retroAchievementsRead.GetAllProgress();
            var connected = _retroAccount?.IsConnected ?? false;
            foreach (var game in affected)
            {
                links.TryGetValue(game.Id, out var link);
                RetroAchievementsProgressSnapshot? snapshot = null;
                if (link?.RetroAchievementsGameId is { } raGameId)
                    progress.TryGetValue(raGameId, out snapshot);
                game.ApplyAchievementsDisplay(
                    RetroAchievementsDisplay.For(game.SystemId, connected, link, snapshot));
            }
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not apply refreshed RetroAchievements progress to the library.", ex);
        }
    }

    // Resolves each game's achievement presentation from the cached links + progress and the
    // current connection state, so the grid mark and list column render from local data alone
    // (no network). Runs on the load worker before the view models reach the bound collection.
    private void ApplyAchievementDisplays(IReadOnlyList<GameViewModel> viewModels)
    {
        if (_retroAchievementsRead is null || viewModels.Count == 0)
            return;

        try
        {
            var links = _retroAchievementsRead.GetAllLinks();
            var progress = _retroAchievementsRead.GetAllProgress();
            var connected = _retroAccount?.IsConnected ?? false;
            foreach (var viewModel in viewModels)
            {
                links.TryGetValue(viewModel.Id, out var link);
                RetroAchievementsProgressSnapshot? snapshot = null;
                if (link?.RetroAchievementsGameId is { } raGameId)
                    progress.TryGetValue(raGameId, out snapshot);
                viewModel.ApplyAchievementsDisplay(
                    RetroAchievementsDisplay.For(viewModel.SystemId, connected, link, snapshot));
                viewModel.ApplyAchievementLink(
                    link is { HasAchievements: true, RetroAchievementsGameId: { } linkedGameId }
                        ? linkedGameId
                        : null);
            }
        }
        catch (Exception ex)
        {
            // The library must still render if the achievement tables are unreadable.
            _logger.Warning("Could not resolve RetroAchievements display state.", ex);
        }
    }

    /// <summary>
    /// Reads the cached texture inventory after the UI paints and applies the resulting marks.
    /// Deliberately the cached path: startup must never walk every installed pack.
    /// </summary>
    public async Task LoadTexturePacksAtStartupAsync(CancellationToken cancellationToken = default)
    {
        if (_texturePacks is null)
            return;

        try
        {
            await _texturePacks.LoadCachedAsync(cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => ApplyTexturePackDisplays(_systemGames));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not load the cached texture-pack inventory.", ex);
        }
    }

    /// <summary>
    /// Rescans every configured texture root, then refreshes the marks. This is the explicit
    /// Rescan action — the only thing that walks the texture directories.
    /// </summary>
    public async Task<TexturePackInventoryResult> RefreshTexturePacksAsync(
        CancellationToken cancellationToken = default)
    {
        if (_texturePacks is null)
            return TexturePackInventoryResult.Empty;

        var result = await _texturePacks.RefreshAsync(cancellationToken);
        // Reapply here rather than waiting for the next collection load. Settings drives this, and
        // without it the already-rendered rows keep their old marks until the user happens to
        // switch platforms — which reads as the scan having done nothing.
        await Dispatcher.UIThread.InvokeAsync(() => ApplyTexturePackDisplays(_systemGames));
        return result;
    }

    // Every library game's display title, keyed by id, for surfaces that must name a game outside
    // the visible collection. Falls back to the loaded collection if the library read fails, so
    // Settings degrades to partial titles rather than throwing.
    private IReadOnlyDictionary<long, string> BuildLibraryTitleLookup()
    {
        try
        {
            var titles = new Dictionary<long, string>();
            foreach (var game in _library.GetGames())
                titles[game.Id] = game.Title;
            return titles;
        }
        catch (Exception ex)
        {
            _logger.Warning("Could not read library titles for the texture-pack list.", ex);
            return _systemGames.ToDictionary(game => game.Id, game => game.Title);
        }
    }

    // Overlays the normalized scraped title onto each game for display (grid, list, and spotlight),
    // in one bulk read. Runs on the build worker before the view models are bound, so no cross-thread
    // notify occurs. DisplayTitle keeps a user rename ahead of the scraped name.
    private void ApplyScrapedTitles(IReadOnlyList<GameViewModel> viewModels)
    {
        if (_metadataStore is null || viewModels.Count == 0)
            return;

        try
        {
            var titles = _metadataStore.GetProviderTitles();
            if (titles.Count == 0)
                return;

            foreach (var viewModel in viewModels)
                if (titles.TryGetValue(viewModel.Id, out var scraped))
                    viewModel.ApplyScrapedTitle(scraped);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not load scraped titles for the library: {ex.Message}");
        }
    }

    // Fills the scraped-metadata list columns (completeness, artwork/description presence, rating,
    // genre/year/players/dev/pub) from one bulk read on the load worker, so a column never triggers a
    // per-row GetDetails on the UI thread (the N+1 the M11 work removed). See DECISIONS 2026-08-08.
    private void ApplyDetailsProjections(IReadOnlyList<GameViewModel> viewModels)
    {
        if (_gameDetails is null || viewModels.Count == 0)
            return;

        try
        {
            var projections = _gameDetails.GetAllDetailsProjections();
            foreach (var viewModel in viewModels)
                viewModel.ApplyDetailsProjection(
                    projections.TryGetValue(viewModel.Id, out var projection) ? projection : null);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not load metadata projections for the list columns: {ex.Message}");
        }
    }

    // The 3D shelf needs only the selected artwork paths, not every metadata/media row. One
    // bulk read keeps gamepad/grid scope construction free of the per-game GetDetails N+1 that the
    // details projections deliberately avoid. Decoding remains lazy inside the bounded shelf control.
    private void ApplyPhysicalMediaTexturePaths(IReadOnlyList<GameViewModel> viewModels)
    {
        if (_gameDetails is null || viewModels.Count == 0)
            return;

        try
        {
            var textures = _gameDetails.GetSelectedMediaPaths(GameMediaKind.PhysicalMediaTexture);
            // A keep case wears three scraped faces, not one. All three are read in the same bulk
            // pass, for the same reason the texture always was: per-game reads here would be an
            // N+1 across the whole scope, and the shelf only decodes the visible window anyway.
            var backs = _gameDetails.GetSelectedMediaPaths(GameMediaKind.BoxBack);
            var spines = _gameDetails.GetSelectedMediaPaths(GameMediaKind.BoxSpine);
            foreach (var viewModel in viewModels)
            {
                viewModel.ApplyPhysicalMediaTexturePath(
                    textures.TryGetValue(viewModel.Id, out var texture) ? texture : null);
                viewModel.ApplyBoxBackPath(
                    backs.TryGetValue(viewModel.Id, out var back) ? back : null);
                viewModel.ApplyBoxSpinePath(
                    spines.TryGetValue(viewModel.Id, out var spine) ? spine : null);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not load physical-media artwork paths for the shelf: {ex.Message}");
        }
    }

    /// <summary>Loads the scraped-metadata projection for the current scope on demand — used when the
    /// user switches to the Desktop list view (or returns to it) for a scope that skipped the read in
    /// grid/gamepad mode. No-ops when the list isn't showing or the scope already has its projection.
    /// Runs the read on a worker and applies on the UI thread, guarded against a scope change.</summary>
    private void EnsureDetailsProjectionsLoaded()
    {
        if (_gameDetails is null || IsGamepadMode || IsGridView)
            return;
        if (_displayedScopeKey is not { } scopeKey || _scopesWithProjections.Contains(scopeKey))
            return;

        var games = _systemGames.ToArray();
        if (games.Length == 0)
        {
            _scopesWithProjections.Add(scopeKey);
            return;
        }

        var generation = _loadGeneration;
        _ = Task.Run(() =>
        {
            IReadOnlyDictionary<long, GameDetailsProjection> projections;
            try
            {
                projections = _gameDetails.GetAllDetailsProjections();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not load metadata projections for the list columns: {ex.Message}");
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                // A scope switch (or reload) since we started owns its own projection load; don't
                // stamp stale data onto whatever is now on screen.
                if (generation != _loadGeneration)
                    return;
                foreach (var game in games)
                    game.ApplyDetailsProjection(
                        projections.TryGetValue(game.Id, out var projection) ? projection : null);
                _scopesWithProjections.Add(scopeKey);
            });
        });
    }

    // Resolves each game's texture-pack presentation from the last completed inventory pass. Like
    // the achievement pass this is local-only and runs on the load worker: the map was already
    // built in one background pass, so a row never reads a directory or the database itself.
    private void ApplyTexturePackDisplays(IReadOnlyList<GameViewModel> viewModels)
    {
        if (_texturePacks is null || viewModels.Count == 0)
            return;

        try
        {
            var result = _texturePacks.Current;
            var scanned = _texturePacks.HasScanned;
            var loadingBySystem = result.Platforms.ToDictionary(
                platform => platform.SystemId,
                platform => platform.Loading,
                StringComparer.Ordinal);

            foreach (var viewModel in viewModels)
            {
                if (TexturePackProviderRegistry.Find(viewModel.SystemId) is null)
                {
                    viewModel.ApplyTexturePackDisplay(TexturePackDisplay.Unsupported);
                    continue;
                }

                if (!scanned)
                {
                    viewModel.ApplyTexturePackDisplay(TexturePackDisplay.NotScanned);
                    continue;
                }

                // A multi-disc title is one card over several library rows, and these emulators key
                // a pack on one disc's identifier, so the whole set is matched when any disc is.
                var matches = result.Map.GetMatches(viewModel.Discs.Select(disc => disc.Game.Id));
                loadingBySystem.TryGetValue(viewModel.SystemId, out var loading);
                viewModel.ApplyTexturePackDisplay(TexturePackDisplay.For(
                    matches,
                    loading,
                    TexturePackProviderRegistry.DescribeEmulator));
            }
        }
        catch (Exception ex)
        {
            // The library must still render if the texture inventory is unusable.
            _logger.Warning("Could not resolve texture-pack display state.", ex);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadGameCoverAsync(GameViewModel? game)
    {
        if (game is null || game.CoverPath is null || game.HasCoverImage || game.IsCoverLoading)
            return;

        if (_isFrontendSuspended)
        {
            _deferredCoverLoads.Add(game.Id);
            return;
        }

        var generation = _loadGeneration;
        var coverPath = game.CoverPath;
        var coverRevision = game.CoverRevision;
        game.IsCoverLoading = true;
        // Bound how many covers decode at once. Acquired after the cheap guards above so queued tiles
        // still show their loading state while they wait their turn.
        await _coverDecodeGate.WaitAsync();
        try
        {
            var thumbnailPath = await _covers.GetThumbnailAsync(game.Id, coverPath);
            if (thumbnailPath is null)
                return;

            // Decode to the tile's displayed pixel size rather than the full thumbnail: the grid never
            // renders a cover wider than MaxCoverWidth, so decoding to that (× render scale, capped at the
            // source width so it is never upscaled) yields a crisp tile with a smaller bitmap — a lighter
            // GPU upload and less memory, which matters most when a run of covers lands in one scroll.
            var decodeWidth = Math.Clamp(
                (int)Math.Ceiling(MaxCoverWidth * CoverRenderScale),
                1,
                CoverThumbnailNativeWidth);
            var image = await Task.Run(() =>
            {
                using var stream = File.OpenRead(thumbnailPath);
                return Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality);
            });
            if (generation == _loadGeneration &&
                coverRevision == game.CoverRevision &&
                _systemGames.Contains(game) &&
                !_isFrontendSuspended)
            {
                game.CoverImage = image;
            }
            else
            {
                image.Dispose();
                if (_isFrontendSuspended)
                    _deferredCoverLoads.Add(game.Id);
            }
        }
        catch (Exception ex)
        {
            if (generation == _loadGeneration &&
                coverRevision == game.CoverRevision &&
                _systemGames.Contains(game))
            {
                if (_isFrontendSuspended)
                {
                    _deferredCoverLoads.Add(game.Id);
                    return;
                }
                _logger.Warning($"Could not load the cover for game id {game.Id}.", ex);
                SetStatus($"Could not load cover for {game.DisplayTitle}: {ex.Message}", StatusSeverity.Error);
            }
        }
        finally
        {
            _coverDecodeGate.Release();
            game.IsCoverLoading = false;
        }
    }

    // Sets the sort column, toggling ascending/descending when the same column is chosen again.
    [RelayCommand]
    private void SortBy(LibrarySortColumn column)
    {
        if (SortColumn == column)
            SortDescending = !SortDescending;
        else
        {
            SortColumn = column;
            SortDescending = false;
        }
        ApplyFilter();
    }

    // The Recently Added / Recently Played collections define their own order — newest activity
    // first — when the load worker builds them. Applying the column sort on top would override that
    // and make a "Recently …" collection read like an A–Z list, so these scopes keep their load order.
    private bool IsRecencyOrderedScope =>
        CurrentLibraryScope is LibraryScope.RecentlyAdded or LibraryScope.RecentlyPlayed;

    private IEnumerable<GameViewModel> SortGames(IEnumerable<GameViewModel> games)
    {
        if (IsRecencyOrderedScope)
            return games;

        var text = StringComparer.OrdinalIgnoreCase;
        IOrderedEnumerable<GameViewModel> By<TKey>(
            Func<GameViewModel, TKey> key, IComparer<TKey>? comparer = null) =>
            SortDescending ? games.OrderByDescending(key, comparer) : games.OrderBy(key, comparer);

        var ordered = SortColumn switch
        {
            LibrarySortColumn.Console => By(g => g.SystemName, text),
            LibrarySortColumn.Format => By(g => g.FormatLabel, text),
            LibrarySortColumn.Achievements => By(g => g.AchievementSortKey),
            LibrarySortColumn.HardcoreAchievements => By(g => g.HardcoreSortKey),
            LibrarySortColumn.Textures => By(g => g.TextureSortKey),
            LibrarySortColumn.Status => By(g => g.AvailabilityText, text),
            LibrarySortColumn.LastPlayed => By(g => g.LastPlayedSortKey),
            LibrarySortColumn.Playtime => By(g => g.PlaytimeSortKey),
            LibrarySortColumn.PlayCount => By(g => g.PlayCountSortKey),
            LibrarySortColumn.DateAdded => By(g => g.DateAddedSortKey),
            LibrarySortColumn.MetadataCompleteness => By(g => g.MetadataCompletenessSortKey),
            LibrarySortColumn.ArtworkCover => By(g => g.HasScrapedCover),
            LibrarySortColumn.Screenshot => By(g => g.HasScrapedScreenshot),
            LibrarySortColumn.Fanart => By(g => g.HasScrapedFanart),
            LibrarySortColumn.Logo => By(g => g.HasScrapedLogo),
            LibrarySortColumn.Description => By(g => g.HasScrapedDescription),
            LibrarySortColumn.TitleScreen => By(g => g.HasScrapedTitleScreen),
            LibrarySortColumn.BoxBack => By(g => g.HasScrapedBoxBack),
            LibrarySortColumn.BoxSpine => By(g => g.HasScrapedBoxSpine),
            LibrarySortColumn.PhysicalMedia => By(g => g.HasScrapedPhysicalMedia),
            LibrarySortColumn.PhysicalMediaTexture => By(g => g.HasScrapedPhysicalMediaTexture),
            LibrarySortColumn.Rating => By(g => g.RatingSortKey),
            LibrarySortColumn.Genre => By(g => g.GenreColumnText, text),
            LibrarySortColumn.Year => By(g => g.YearSortKey),
            LibrarySortColumn.Players => By(g => g.PlayersColumnText, text),
            LibrarySortColumn.Developer => By(g => g.DeveloperColumnText, text),
            LibrarySortColumn.Publisher => By(g => g.PublisherColumnText, text),
            _ => By(g => g.DisplayTitle, text),
        };
        // The displayed title is the stable secondary key so equal rows keep a deterministic order.
        return ordered.ThenBy(g => g.DisplayTitle, text);
    }

    internal void ApplyFilter()
    {
        var query = SearchText.Trim();
        if (!string.Equals(query, _appliedSearchText, StringComparison.Ordinal))
            ClearSelection();
        _appliedSearchText = query;
        IEnumerable<GameViewModel> filtered = _systemGames;
        if (query.Length > 0)
            filtered = _systemGames.Where(g =>
                g.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                g.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

        Games.ReplaceAll(SortGames(filtered));
        ApplyShelfHeroSupport(Games);
        ApplyVisibleCoverShelf(GridCoverWidth > 0 ? GridCoverWidth : MinCoverWidth);

        HasGames = Games.Count > 0;
        IsLibraryEmpty = _systemGames.Count == 0;
        IsSearchEmpty = _systemGames.Count > 0 && Games.Count == 0;
        LibraryCountText = _systemGames.Count == 1 ? "1 game" : $"{_systemGames.Count} games";
        NotifyLibraryPresentationChanged();
        RestoreFocusedGame();
    }

    [RelayCommand]
    private async Task AddGamesAsync()
    {
        if (IsBusy)
            return;

        var paths = await _dialogs.PickGameFilesAsync();
        if (paths.Count == 0)
            return;

        var previousStatus = StatusText;
        var previousSeverity = StatusSeverity;
        IsBusy = true;
        try
        {
            SetStatus(
                paths.Count == 1 ? "Inspecting game…" : $"Inspecting {paths.Count} files…",
                StatusSeverity.Progress);
            var analyses = await Task.Run(() => paths
                .Select(_importRules.AnalyzeFile)
                .ToList());

            var suggested = analyses
                .SelectMany(analysis => analysis.SuggestedSystems)
                .GroupBy(system => system.Id)
                .OrderByDescending(group => group.Count())
                .Select(group => group.First())
                .FirstOrDefault() ?? SelectedSystem;

            var system = await _dialogs.PickSystemAsync(Systems, suggested);
            if (system is null)
            {
                SetStatus(previousStatus, previousSeverity);
                return;
            }

            if (system.Id == "playstation3")
            {
                SetStatus("PlayStation 3 games are imported only from RPCS3. Use Settings to sync its library.");
                return;
            }

            var accepted = analyses
                .Where(analysis => analysis.MatchFor(system.Id) is
                    GameFileMatch.Compatible or GameFileMatch.Unrecognized)
                .Select(analysis => analysis.Path)
                .ToList();
            var incompatible = analyses.Count(analysis =>
                analysis.MatchFor(system.Id) == GameFileMatch.Incompatible);
            var unsupported = analyses.Count(analysis =>
                analysis.MatchFor(system.Id) == GameFileMatch.Unsupported);
            var confirmedUnrecognized = analyses.Count(analysis =>
                analysis.MatchFor(system.Id) == GameFileMatch.Unrecognized);

            var selection = await Task.Run(() => _importRules.SelectGameEntries(accepted, system));
            var importResult = await ReconcileImportAsync(system, selection);
            await ShowSystemAsync(system);
            SetStatus(BuildAddGamesStatus(
                importResult.AddedCount,
                incompatible,
                unsupported,
                confirmedUnrecognized,
                system.Name));
            await MaybeStartMetadataForImportAsync(importResult.AddedGameIds);
        }
        catch (Exception ex)
        {
            _logger.Error("Game import failed.", ex);
            SetStatus($"Import failed: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildAddGamesStatus(
        int added,
        int incompatible,
        int unsupported,
        int confirmedUnrecognized,
        string systemName)
    {
        var status = added == 1 ? "Added 1 game" : $"Added {added} games";
        var skipped = incompatible + unsupported;
        if (incompatible > 0 && unsupported == 0)
        {
            status += incompatible == 1
                ? $" — skipped 1 file not recognized as {systemName}"
                : $" — skipped {incompatible} files not recognized as {systemName}";
        }
        else if (unsupported > 0 && incompatible == 0)
        {
            status += unsupported == 1
                ? " — skipped 1 unsupported file"
                : $" — skipped {unsupported} unsupported files";
        }
        else if (skipped > 0)
        {
            status += $" — skipped {skipped} files " +
                $"({incompatible} not recognized as {systemName}, {unsupported} unsupported)";
        }

        if (confirmedUnrecognized > 0)
        {
            status += confirmedUnrecognized == 1
                ? $"; used confirmed {systemName} system for 1 unrecognized file"
                : $"; used confirmed {systemName} system for {confirmedUnrecognized} unrecognized files";
        }

        return status;
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (IsBusy)
            return;

        var folder = await _dialogs.PickFolderAsync();
        if (folder is null)
            return;

        var system = await _dialogs.PickSystemAsync(Systems, SelectedSystem);
        if (system is null)
            return;

        await ImportFolderForSystemAsync(folder, system);
    }

    /// <summary>
    /// Gamepad-native folder import: the desktop <see cref="AddFolderAsync"/> flow with the two modal
    /// pickers replaced by controller-native steps. The OS folder picker still runs (it is the one
    /// step that genuinely needs the platform picker — a SAF tree on Android), but the system choice
    /// is a Gamepad overlay rather than a dialog <see cref="Window"/>. Bound into the couch system menu
    /// only where Desktop mode is absent (Android); on desktop couch, "Switch to Desktop" is the import
    /// route. See the plan's A1 gamepad-native import item.
    /// </summary>
    [RelayCommand]
    private async Task AddFolderFromGamepadAsync()
    {
        if (IsBusy)
            return;

        var folder = await _dialogs.PickFolderAsync();
        if (folder is null)
        {
            // The picker was cancelled or (on Android without all-files access) returned no usable
            // path; drop back to the shelf rather than opening an empty system chooser.
            CloseGamepadOverlay();
            return;
        }

        _pendingImportFolder = folder;
        OpenGamepadOverlay(GamepadOverlayKind.ImportSystem);
    }

    private void AddImportSystemOptions()
    {
        // The scanner imports a folder *for a chosen system*, so list every importable console (PS3 is
        // RPCS3-sync-only and cannot be folder-scanned). Selecting one runs the scan; B cancels.
        foreach (var system in Systems.Where(system => system.Id != "playstation3"))
            AddOption(system.Name, new AsyncRelayCommand(() => ImportPendingFolderForSystemAsync(system)));
        AddOption("Cancel", BackFromGamepadOverlayCommand, isCancel: true);
    }

    private async Task ImportPendingFolderForSystemAsync(GameSystem system)
    {
        var folder = _pendingImportFolder;
        _pendingImportFolder = null;
        CloseGamepadOverlay();
        if (folder is not null)
            await ImportFolderForSystemAsync(folder, system);
    }

    private async Task ImportFolderForSystemAsync(string folder, GameSystem system)
    {
        if (system.Id == "playstation3")
        {
            SetStatus("PlayStation 3 games are imported only from RPCS3. Use Settings to sync its library.");
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<ScanProgress>(p =>
                SetStatus($"Scanning {system.Name}… {p.CandidatesFound} found", StatusSeverity.Progress));

            var selection = await _scanner.ScanAsync(folder, system, progress);
            await Task.Run(() => _library.AddLibraryFolder(system.Id, folder));

            var importResult = await ReconcileImportAsync(system, selection);
            await ShowSystemAsync(system);
            SetStatus(importResult.AddedCount == 1
                ? "Added 1 game from folder"
                : $"Added {importResult.AddedCount} games from folder");
            await MaybeStartMetadataForImportAsync(importResult.AddedGameIds);
        }
        catch (Exception ex)
        {
            _logger.Error($"Folder scan failed for system {system.Id}.", ex);
            SetStatus($"Scan failed: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task RescanSystemAsync()
    {
        if (SelectedSystem?.Id == "playstation3")
        {
            SetStatus("Use Settings to sync the explicitly selected RPCS3 library.");
            return Task.CompletedTask;
        }

        return SelectedSystem is { } system ? RescanAsync([system], system) : Task.CompletedTask;
    }

    [RelayCommand]
    private Task RescanAllAsync() => RescanAsync(NonRpcs3Systems(), SelectedSystem);

    private async Task<string> RescanSystemFromSettingsAsync(string systemId, IProgress<string> progress)
    {
        var system = Systems.FirstOrDefault(candidate => candidate.Id == systemId);
        if (system is null)
            return "That console is no longer available.";

        if (system.Id == "playstation3")
            return "Use Sync RPCS3 library to refresh PlayStation 3 games.";

        await RescanAsync([system], SelectedSystem, progress);
        return StatusText;
    }

    private async Task<string> RescanAllFromSettingsAsync(IProgress<string> progress)
    {
        await RescanAsync(NonRpcs3Systems(), SelectedSystem, progress);
        return StatusText;
    }

    private IReadOnlyList<LibraryFolder> GetLibraryFoldersForSettings(string systemId) =>
        _library.GetLibraryFolders(systemId);

    // Every system's remembered folders in one query/connection. Opening Settings seeds each row from
    // this instead of reading the database once per system on the UI thread (a cold-open freeze on a
    // portable/external drive, where every unpooled connection open is a fresh file open).
    private IReadOnlyList<LibraryFolder> GetAllLibraryFoldersForSettings() =>
        _library.GetLibraryFolders();

    private async Task<string> AddLibraryFolderFromSettingsAsync(string systemId, string folderPath)
    {
        if (IsBusy)
            return "Library work is already in progress.";
        var system = Systems.FirstOrDefault(candidate => candidate.Id == systemId);
        if (system is null || system.Id == "playstation3")
            return "That platform does not use remembered folders.";

        IsBusy = true;
        try
        {
            var selection = await _scanner.ScanAsync(folderPath, system);
            await Task.Run(() => _library.AddLibraryFolder(systemId, folderPath));
            var imported = await ReconcileImportAsync(system, selection);
            await UpdateAvailabilityAsync();
            await ReloadGamesAsync();
            await MaybeStartMetadataForImportAsync(imported.AddedGameIds);
            return imported.AddedCount == 1
                ? "Folder remembered — added 1 game."
                : $"Folder remembered — added {imported.AddedCount} games.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not add a remembered folder for {systemId}.", ex);
            return $"Could not add folder: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<string> ChangeLibraryFolderFromSettingsAsync(
        string systemId,
        long folderId,
        string replacementPath)
    {
        if (IsBusy)
            return "Library work is already in progress.";
        var system = Systems.FirstOrDefault(candidate => candidate.Id == systemId);
        if (system is null || system.Id == "playstation3")
            return "That platform does not use remembered folders.";

        IsBusy = true;
        try
        {
            // Scan first: an unreadable or invalid replacement must leave both the remembered root
            // and every existing game path unchanged.
            var selection = await _scanner.ScanAsync(replacementPath, system);
            var preparedEntries = await PrepareImportEntriesAsync(system, selection.EntryPaths);
            var verifiedGamePaths = await FindVerifiedRelocationsAsync(
                folderId,
                system,
                replacementPath,
                preparedEntries);
            var changed = await Task.Run(() => _library.ReplaceLibraryFolder(
                folderId,
                systemId,
                replacementPath,
                verifiedGamePaths));
            var imported = await ReconcileImportAsync(system, selection, preparedEntries);
            await UpdateAvailabilityAsync();
            await ReloadGamesAsync();
            await MaybeStartMetadataForImportAsync(imported.AddedGameIds);

            var details = new List<string>();
            if (changed.RebasedGameCount > 0)
                details.Add($"{changed.RebasedGameCount} existing game path(s) updated");
            if (imported.AddedCount > 0)
                details.Add($"{imported.AddedCount} new game(s) added");
            return details.Count == 0
                ? "Folder changed — no library entries changed."
                : "Folder changed — " + string.Join(", ", details) + ".";
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not change a remembered folder for {systemId}.", ex);
            return $"Could not change folder: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<string> ForgetLibraryFolderFromSettingsAsync(string systemId, long folderId)
    {
        try
        {
            await Task.Run(() => _library.RemoveLibraryFolder(folderId, systemId));
            return "Folder forgotten. Existing games and files were not removed.";
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not forget a remembered folder for {systemId}.", ex);
            return $"Could not forget folder: {ex.Message}";
        }
    }

    private IEnumerable<GameSystem> NonRpcs3Systems() =>
        Systems.Where(system => system.Id != "playstation3");

    private async Task<string> SyncRpcs3LibraryFromSettingsAsync()
    {
        if (IsBusy)
            return "Library work is already in progress.";

        // RPCS3 keeps games.yml in its configuration root beside or under the configured
        // executable. Reuse that folder and only prompt when the list is not found there.
        var configurationDirectory = await Task.Run(() =>
            Rpcs3LibrarySource.LocateConfigurationDirectory(
                _emulatorConfigurations.Get("playstation3")?.ExecutablePath));
        configurationDirectory ??= await _dialogs.PickRpcs3ConfigurationDirectoryAsync();
        if (configurationDirectory is null)
            return "RPCS3 library sync cancelled.";

        IsBusy = true;
        try
        {
            SetStatus("Reading the RPCS3 game list…", StatusSeverity.Progress);
            var source = new Rpcs3LibrarySource(configurationDirectory);
            var result = await new ExternalLibrarySyncService(_library).SyncAsync(source);
            var playStation3 = Systems.FirstOrDefault(system => system.Id == "playstation3");
            if (playStation3 is not null)
                await ShowSystemAsync(playStation3);

            SetStatus(BuildRpcs3SyncStatus(result));
            var syncStatus = StatusText;

            // Newly synced RPCS3 games take the same opt-in metadata/cover path as every other
            // import. This sync is the only route PlayStation 3 games enter the library — all four
            // file/folder import paths deliberately reserve PS3 for it — so without this call they
            // would never be enriched and would show no cover. A re-sync reports existing entries as
            // updated (not added), so AddedGameIds is empty and nothing already present is refetched.
            await MaybeStartMetadataForImportAsync(result.AddedGameIds);
            return syncStatus;
        }
        catch (Rpcs3LibraryFormatException ex)
        {
            _logger.Warning($"RPCS3 library sync was rejected: {ex.Message}");
            SetStatus($"RPCS3 library sync failed: {ex.Message}", StatusSeverity.Error);
            return StatusText;
        }
        catch (ExternalLibrarySourceConflictException ex)
        {
            // Expected, recoverable condition: an entry collides with another game's path. The
            // reconciliation left the library unchanged, so surface the actionable message plainly.
            _logger.Warning($"RPCS3 library sync stopped on a path conflict: {ex.Message}");
            SetStatus($"RPCS3 library sync stopped: {ex.Message}", StatusSeverity.Error);
            return StatusText;
        }
        catch (Exception ex)
        {
            _logger.Error("RPCS3 library sync failed.", ex);
            SetStatus($"RPCS3 library sync failed: {ex.Message}", StatusSeverity.Error);
            return StatusText;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildRpcs3SyncStatus(ExternalLibraryImportResult result)
    {
        var changes = new List<string>();
        if (result.AddedCount > 0)
            changes.Add(result.AddedCount == 1 ? "1 added" : $"{result.AddedCount} added");
        if (result.UpdatedCount > 0)
            changes.Add(result.UpdatedCount == 1 ? "1 updated" : $"{result.UpdatedCount} updated");
        if (result.MarkedSourceMissingCount > 0)
        {
            changes.Add(result.MarkedSourceMissingCount == 1
                ? "1 source-missing"
                : $"{result.MarkedSourceMissingCount} source-missing");
        }

        return changes.Count == 0
            ? "RPCS3 library sync complete — no changes"
            : $"RPCS3 library sync complete — {string.Join(", ", changes)}";
    }

    private async Task RescanAsync(
        IEnumerable<GameSystem> systems,
        GameSystem? systemToShow,
        IProgress<string>? statusProgress = null)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var total = 0;
            var addedIds = new List<long>();
            foreach (var system in systems.Where(system => system.Id != "playstation3"))
            {
                var folders = await Task.Run(() => _library.GetLibraryFolders(system.Id));
                // Paths already in the library for this system. The scan walk still enumerates every
                // folder (needed so descriptor/playlist collapse stays correct), but reading each
                // candidate's embedded evidence opens the file, so restrict that to genuinely new
                // entries — a steady-state rescan of an unchanged library then does no file reads.
                var knownPaths = await Task.Run(() => _library.GetGames(system.Id)
                    .Select(game => Path.GetFullPath(game.Path))
                    .ToHashSet(PathComparer));
                foreach (var folder in folders)
                {
                    // The main-window toast is hidden behind the Settings modal, so mirror the same
                    // live count to statusProgress when a rescan was launched from Settings — that is
                    // what surfaces "Rescanning {system}… {n} found" in the modal (and Gamepad pill).
                    var progress = new Progress<ScanProgress>(p =>
                    {
                        var message = $"Rescanning {system.Name}… {p.CandidatesFound} found";
                        SetStatus(message, StatusSeverity.Progress);
                        statusProgress?.Report(message);
                    });
                    var selection = await _scanner.ScanAsync(folder.Path, system, progress);
                    var newSelection = SelectUnimportedEntries(selection, knownPaths);
                    var importResult = await ReconcileImportAsync(system, newSelection);
                    // Overlapping folders shouldn't re-read an entry the previous folder just added.
                    foreach (var path in newSelection.EntryPaths)
                        knownPaths.Add(Path.GetFullPath(path));
                    total += importResult.AddedCount;
                    addedIds.AddRange(importResult.AddedGameIds);
                }
            }

            await UpdateAvailabilityAsync();
            if (systemToShow is not null)
                await ShowSystemAsync(systemToShow);
            else
                await ReloadGamesAsync();
            SetStatus(total == 0 ? "Rescan complete — no new games" : $"Rescan added {total} game(s)");
            if (addedIds.Count > 0)
            {
                // A remembered-folder rescan is another import path. Only its newly discovered
                // rows join the existing account-gated pipeline; unchanged ROMs keep their
                // fingerprinted identification result and are not opened again.
                if (_retroAchievements is not null && _retroAccount?.IsConnected == true)
                    _ = SynchronizeImportedRetroAchievementsAsync(addedIds);

                // Preserve rescan's existing metadata behavior: cover/title fetching remains an
                // independent automatic preference and does not show the first-import prompt.
                if (_metadataPreferences.AutomaticallyFetchAfterImport)
                    _ = EnrichImportedGamesAsync(addedIds);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Library rescan failed.", ex);
            SetStatus($"Rescan failed: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Drops entries already in the library so a rescan only reads embedded evidence for new files.
    /// Suppressed paths are preserved: descriptor/playlist collapse still removes stale components
    /// even when the descriptor itself was imported on an earlier scan.
    /// </summary>
    private static GameEntrySelection SelectUnimportedEntries(
        GameEntrySelection selection,
        IReadOnlySet<string> knownFullPaths)
    {
        if (selection.EntryPaths.Count == 0)
            return selection;

        var newEntries = selection.EntryPaths
            .Where(path => !knownFullPaths.Contains(Path.GetFullPath(path)))
            .ToArray();

        return newEntries.Length == selection.EntryPaths.Count
            ? selection
            : selection with { EntryPaths = newEntries };
    }

    private async Task<GameImportResult> ReconcileImportAsync(
        GameSystem system,
        GameEntrySelection selection)
    {
        var preparedEntries = await PrepareImportEntriesAsync(system, selection.EntryPaths);
        return await ReconcileImportAsync(system, selection, preparedEntries);
    }

    private async Task<GameImportResult> ReconcileImportAsync(
        GameSystem system,
        GameEntrySelection selection,
        IReadOnlyList<PreparedImportEntry> preparedEntries)
    {
        if (selection.EntryPaths.Count == 0 && selection.SuppressedPaths.Count == 0)
            return GameImportResult.Empty;

        var now = DateTimeOffset.Now;
        var games = preparedEntries.Select(entry => new Game
        {
            SystemId = system.Id,
            Path = entry.Path,
            Title = entry.Metadata.EmbeddedTitle ?? System.IO.Path.GetFileNameWithoutExtension(entry.Path),
            TitleOrigin = entry.Metadata.EmbeddedTitle is null
                ? GameTitleOrigin.Filename
                : GameTitleOrigin.Embedded,
            IsAvailable = true,
            DateAdded = now,
        }).ToArray();

        var result = await Task.Run(() =>
            _library.ReconcileImport(system.Id, games, selection.SuppressedPaths));
        await PersistImportEvidenceAsync(system.Id, preparedEntries);

        return result;
    }

    private Task<PreparedImportEntry[]> PrepareImportEntriesAsync(
        GameSystem system,
        IReadOnlyList<string> entryPaths) =>
        Task.Run(() => entryPaths
            .Select(path => new PreparedImportEntry(
                path,
                _importRules.ReadImportMetadata(path, system)))
            .ToArray());

    private Task<IReadOnlyDictionary<long, string>> FindVerifiedRelocationsAsync(
        long folderId,
        GameSystem system,
        string replacementPath,
        IReadOnlyList<PreparedImportEntry> preparedEntries) => Task.Run(() =>
    {
        if (_metadataStore is null)
            return (IReadOnlyDictionary<long, string>)new Dictionary<long, string>();

        var originalRoot = _library.GetLibraryFolders(system.Id)
            .Single(folder => folder.Id == folderId)
            .Path;
        var replacementRoot = Path.GetFullPath(replacementPath);
        var replacements = preparedEntries.ToDictionary(
            entry => Path.GetFullPath(entry.Path),
            entry => entry,
            PathComparer);
        var verified = new Dictionary<long, string>();

        foreach (var game in _library.GetGames(system.Id))
        {
            if (!TryGetRelativePath(originalRoot, game.Path, out var relativePath))
                continue;
            var candidate = Path.GetFullPath(Path.Combine(replacementRoot, relativePath));
            if (!replacements.TryGetValue(candidate, out var replacement) ||
                !IdentifiersMatch(_metadataStore.GetIdentifiers(game.Id), replacement.Metadata.Identifiers))
            {
                continue;
            }
            verified[game.Id] = candidate;
        }

        return verified;
    });

    private static bool IdentifiersMatch(
        IReadOnlyList<GameIdentifier> stored,
        IReadOnlyList<GameIdentifier> replacement) =>
        stored.Any(left => replacement.Any(right =>
            left.Kind == right.Kind &&
            string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase)));

    private static bool TryGetRelativePath(string root, string path, out string relativePath)
    {
        relativePath = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return relativePath != ".." &&
               !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relativePath);
    }

    private async Task PersistImportEvidenceAsync(
        string systemId,
        IReadOnlyList<PreparedImportEntry> preparedEntries)
    {
        if (_metadataStore is null)
            return;

        var metadataByPath = preparedEntries
            .Where(entry => entry.Metadata.Identifiers.Count > 0)
            .ToDictionary(
                entry => entry.Path,
                entry => entry.Metadata,
                StringComparer.OrdinalIgnoreCase);
        if (metadataByPath.Count == 0)
            return;

        // Revisit matching existing rows too. The game insert and evidence write use separate
        // persistence interfaces, so this makes a transient evidence-write failure recoverable
        // the next time the same game is imported without overwriting other identifier sources.
        var importedGames = await Task.Run(() => _library.GetGames(systemId));
        await Task.Run(() =>
        {
            foreach (var game in importedGames)
            {
                if (metadataByPath.TryGetValue(game.Path, out var metadata) &&
                    _metadataStore.GetIdentifiers(game.Id).Count == 0)
                {
                    _metadataStore.ReplaceIdentifiers(game.Id, metadata.Identifiers);
                }
            }
        });
    }

    private sealed record PreparedImportEntry(string Path, GameImportMetadata Metadata);

    private async Task ShowSystemAsync(GameSystem system)
    {
        if (SelectedSystem?.Id == system.Id)
            await ReloadGamesAsync();
        else
        {
            SelectedSystem = system;
            await _selectedSystemLoad;
        }
    }

    /// <summary>
    /// The post-open background passes, run in a controlled order so they stop stampeding the initial
    /// library load. Previously all four were launched at once from <c>MainWindow.Opened</c>: two of
    /// them (availability, RetroAchievements) each trigger a full <see cref="ReloadGamesAsync"/> that
    /// disposes and rebuilds every tile, so with the initial load still in flight the grid was built,
    /// discarded and rebuilt two or three times over — the visible flicker and "background races" at
    /// startup. Here the two grid-rebuilding passes wait for the first load to settle and then run one
    /// after another, so the library is built once and refreshed at most twice, in sequence. Texture
    /// marks and the update check never rebuild the grid, so they run alongside.
    /// </summary>
    public async Task RunStartupBackgroundTasksAsync()
    {
        // Independent of the library rebuild — start them now and let them run concurrently.
        var texturePacks = LoadTexturePacksAtStartupAsync();
        var updateCheck = Updates?.CheckOnLaunchAsync() ?? Task.CompletedTask;

        // Let the initial build finish before the refresh passes so they can't clear its scope cache
        // or bump its load generation mid-flight. The load path logs its own failures.
        try
        {
            await _selectedSystemLoad;
        }
        catch
        {
            // Swallowed: a failed initial load already reported itself; the refresh passes below still
            // run so a transient first-load error doesn't strand availability/achievement refreshes.
        }

        // Sequential, not concurrent: each can rebuild the grid, and overlapping them just rebuilds
        // the same view twice for nothing.
        await RefreshAvailabilityAsync();
        await RefreshRetroAchievementsProgressAtStartupAsync();

        // Fold the independent passes back in so a caller awaiting startup sees them through, and any
        // exception is observed rather than surfacing later as an unobserved-task fault.
        await Task.WhenAll(texturePacks, updateCheck);
    }

    /// <summary>
    /// Startup/after-rescan availability pass: stat every game's path off the UI thread and
    /// persist changes, then reload the current view from the updated DB. No discovery scan.
    /// Reloading (rather than patching in place) makes the result independent of whether the
    /// initial load has finished — the generation-guarded reload always reflects the DB.
    /// </summary>
    public async Task RefreshAvailabilityAsync()
    {
        try
        {
            var changed = await UpdateAvailabilityAsync();

            if (changed > 0)
                await ReloadGamesAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Library availability check failed.", ex);
            SetStatus($"Availability check failed: {ex.Message}", StatusSeverity.Error);
        }
    }

    /// <summary>
    /// Startup's only RetroAchievements refresh path. It shares the account pipeline with
    /// connect/disconnect work and leaves the current library display untouched when the cached
    /// summary is still within its fifteen-minute freshness window.
    /// </summary>
    public async Task RefreshRetroAchievementsProgressAtStartupAsync()
    {
        if (_retroRefresh is null)
            return;

        await _retroAchievementsPipeline.WaitAsync();
        try
        {
            var refreshed = await _retroRefresh.RefreshSummaryAtStartupIfStaleAsync();
            if (refreshed is not null)
                await ReloadGamesAsync();
        }
        catch (Exception ex)
        {
            // Startup cache refresh is optional. Keep the library usable and let its existing
            // display state explain any stale cached values.
            _logger.Warning("RetroAchievements startup progress refresh failed.", ex);
        }
        finally
        {
            _retroAchievementsPipeline.Release();
        }
    }

    private Task<int> UpdateAvailabilityAsync() => Task.Run(() =>
    {
        var updates = new List<GameAvailabilityUpdate>();
        foreach (var game in _library.GetGames())
        {
            // An external source owns this state. A generic startup stat must not revive an
            // entry that a later source sync retained as source-missing.
            if (game.ExternalSourceId is not null)
                continue;

            var available = _availabilityChecker.IsAvailable(game);
            if (available != game.IsAvailable)
                updates.Add(new GameAvailabilityUpdate(game.Id, available));
        }
        _library.SetAvailabilities(updates);
        return updates.Count;
    });

    [RelayCommand]
    private Task LaunchGameAsync(GameViewModel? game) => LaunchGameCoreAsync(game);

    private Task LaunchSelectedDiscFromLibraryAsync(GameViewModel game, GameDisc disc) =>
        SelectDiscFromLibraryAsync(game, disc);

    private async Task SelectDiscFromLibraryAsync(GameViewModel game, GameDisc disc)
    {
        if (!game.Discs.Any(candidate => candidate.Game.Id == disc.Game.Id))
            return;

        // Only report success once the selection is actually persisted and applied in memory; on a
        // write failure RememberSelectedDiscAsync leaves the previous disc active, so claiming success
        // would tell the user a switch happened that did not.
        if (await RememberSelectedDiscAsync(game, disc))
            SetStatus($"Disc {disc.Number} selected for {game.DisplayTitle}");
        else
            SetStatus(
                $"Could not select Disc {disc.Number} for {game.DisplayTitle}.",
                StatusSeverity.Error);
    }

    private bool ShouldAnimatePhysicalShelfLaunch(GameViewModel game) =>
        ShowGamepadShelf && ShelfSceneSupported && ReferenceEquals(FocusedGame, game);

    private async Task PlayPhysicalShelfLaunchAsync(
        GameViewModel game,
        CancellationToken cancellationToken)
    {
        if (!ShouldAnimatePhysicalShelfLaunch(game))
        {
            return;
        }

        _shelfLaunchTransition.Start(
            game.Id,
            PhysicalShelfLaunchStyles.ForAnimation(
                game.ShelfMediaProfile.InsertionAnimationId),
            _shelfHeroRotation.Yaw,
            _shelfHeroRotation.Pitch);
        OnPropertyChanged(nameof(ShelfLaunchPose));
        // A previous launch's return can still be easing back on the timer: an exception tail (a
        // failed post-exit save sync) releases IsBusy and resumes input without awaiting the return,
        // so a quick relaunch lands here while that animation is live. Release its abandoned waiter
        // before taking the field, or the earlier RestorePhysicalShelfAfterLaunchAsync Task — awaiting
        // the source we are about to overwrite — never completes and leaks pending forever.
        _shelfLaunchCompletion?.TrySetResult();
        _shelfLaunchCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _shelfLaunchTimestamp = Stopwatch.GetTimestamp();
        _shelfLaunchTimer.Start();
        await _shelfLaunchCompletion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Keeps a launch animation that nobody is waiting for from faulting unobserved.
    /// </summary>
    /// <remarks>
    /// Only reachable when save sync fails while the medium is still moving. Awaiting it there
    /// would make a failed launch sit through the rest of the choreography before saying so.
    /// </remarks>
    private static void ObserveShelfLaunch(Task launch) =>
        _ = launch.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private async Task RestorePhysicalShelfAfterLaunchAsync()
    {
        if (_shelfLaunchTransition.IsIdle)
        {
            return;
        }

        if (!_shelfLaunchTransition.BeginReturn())
        {
            return;
        }

        OnPropertyChanged(nameof(ShelfLaunchPose));
        // Release anyone still waiting on the outward animation before taking the field over.
        // Since the animation now runs beside save sync, a sync failure can begin the return while
        // the launch task is still pending, and replacing the source outright would strand it.
        _shelfLaunchCompletion?.TrySetResult();
        _shelfLaunchCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _shelfLaunchTimestamp = Stopwatch.GetTimestamp();
        _shelfLaunchTimer.Start();
        await _shelfLaunchCompletion.Task;
    }

    private async Task LaunchGameCoreAsync(GameViewModel? game)
    {
        if (game is null || IsBusy)
            return;

        // The name the user sees on the tile (the normalized scraped title when one exists), reused
        // for every status toast in this launch — including the save-sync ones, which only have the
        // Game model, so it is threaded in.
        var displayTitle = game.DisplayTitle;
        var launchDisc = game.Discs.FirstOrDefault(disc => disc.Game.Id == game.LaunchModel.Id);
        if (launchDisc is null)
            return;

        var launchGame = launchDisc.Game;
        if (!launchGame.IsAvailable)
        {
            // Single-disc titles get the context-aware status (it distinguishes an external library
            // that no longer lists the game from a plain missing file); multi-disc titles name the
            // specific disc that could not be found.
            SetStatus(
                game.IsMultiDisc
                    ? $"Cannot launch Disc {launchDisc.Number} of {displayTitle}: its game file could not be found."
                    : game.UnavailableLaunchStatus,
                StatusSeverity.Error);
            return;
        }

        IsBusy = true;
        SetStatus($"Launching {displayTitle}…", StatusSeverity.Progress);
        SuspendFrontendUiWork();
        // Hoisted so the Recently Played refresh runs in finally: it must happen whenever a play was
        // recorded (including a game stamped just before a start failure), and a refresh error must
        // never be able to overwrite the launch-completion status with a false failure message.
        var recordedPlay = false;
        try
        {
            CloudSaveSyncOutcome? beforeSync = null;
            var result = await _launchService.LaunchAsync(
                launchGame,
                displayTitle,
                async cancellationToken =>
                {
                    // The medium starts moving *beside* the cloud save sync, not after it. Both
                    // still have to finish before the emulator starts, so the ordering guarantee is
                    // unchanged — but a slow cloud round-trip now happens behind the choreography
                    // instead of in front of it, which is the delay the animation exists to cover.
                    // Non-shelf layouts take this path as an immediate no-op.
                    var shelfLaunch = PlayPhysicalShelfLaunchAsync(game, cancellationToken);
                    try
                    {
                        beforeSync = await SyncSavesForLaunchAsync(
                            launchGame,
                            displayTitle,
                            afterExit: false,
                            cancellationToken);
                    }
                    catch
                    {
                        // The launch is already failing and the caller's finally restores the
                        // shelf. Observe the animation so its task cannot fault unobserved, but do
                        // not wait out the remaining choreography before reporting the failure.
                        ObserveShelfLaunch(shelfLaunch);
                        throw;
                    }

                    SetStatus(
                        beforeSync?.Status == CloudSaveSyncStatus.Failed
                            ? $"Save sync incomplete; launching {displayTitle} with the saves currently on disk…"
                            : $"Launching {displayTitle}…",
                        StatusSeverity.Progress);
                    // Hold the process start until the medium reaches its insertion pose.
                    await shelfLaunch;
                    // This callback runs only after preflight passes and immediately before the
                    // emulator process starts, so a game whose launch fails validation is never
                    // recorded, and one that starts is recorded even if EmuShelf is killed mid-session.
                    // Stamps last-played and increments the play count in one write.
                    await Task.Run(
                        () => _library.RecordLaunchStarted(launchGame.Id, DateTimeOffset.UtcNow),
                        cancellationToken);
                    recordedPlay = true;
                });
            // The launch service returns only after a tracked emulator exits, or immediately when
            // process start fails. The medium comes back *beside* the post-exit save sync rather
            // than in front of it — the mirror of what the outward launch already does, and for the
            // same reason: the upload is the wait the choreography exists to cover. Awaited in
            // sequence, the player watched the shelf reassemble and only then began waiting.
            var shelfRestore = RestorePhysicalShelfAfterLaunchAsync();
            if (!result.Succeeded)
                _logger.Warning($"Launch did not start or complete successfully: {result.StatusText}");
            if (result.ProcessExited && game.RetroAchievementsGameId is { } retroAchievementsGameId)
                _ = RefreshRetroAchievementsAfterTrackedExitAsync(retroAchievementsGameId);
            // A tracked exit reports the emulator's runtime; accrue it as play time. Guarded and off the
            // UI thread so a write failure can never turn a completed launch into a reported error. The
            // Recently Played refresh in the finally then rebuilds the collection with the new total.
            if (result is { ProcessExited: true, PlayDuration: { } playDuration })
            {
                try
                {
                    await Task.Run(() => _library.AddPlaytime(launchGame.Id, playDuration));
                }
                catch (Exception ex)
                {
                    _logger.Error("Could not record playtime after a launch.", ex);
                }
            }

            CloudSaveSyncOutcome? afterSync = null;
            try
            {
                if (result.ProcessExited)
                    afterSync = await SyncSavesForLaunchAsync(
                        launchGame,
                        displayTitle,
                        afterExit: true,
                        CancellationToken.None);
            }
            catch
            {
                // Report the failure without sitting through the rest of the return first, but
                // observe the animation so its task cannot fault unwatched.
                ObserveShelfLaunch(shelfRestore);
                throw;
            }

            // Still before the closing status, so the shelf is whole again when it is written —
            // which is what the sequential version was really buying, and is kept.
            await shelfRestore;
            SetStatus(
                DescribeLaunchAndSaveSync(result, beforeSync, afterSync),
                result.Succeeded ? StatusSeverity.Info : StatusSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            SetStatus($"Launch cancelled for {displayTitle}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected launch failure for game id {game.Id}.", ex);
            SetStatus($"Could not launch {displayTitle}: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            // Covers cancellation and unexpected exceptions raised while preflight, sync, animation,
            // or process tracking is active. The helper is idempotent after the normal return above.
            await RestorePhysicalShelfAfterLaunchAsync();
            ResumeFrontendUiWork();
            IsBusy = false;
            if (recordedPlay)
            {
                // Cache refresh only — never touches the launch status. Guarded so a reload failure
                // cannot turn a completed launch into a reported error.
                try
                {
                    await RefreshAfterPlayRecordedAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error("Could not refresh the Recently Played collection after a launch.", ex);
                }
            }
        }
    }

    // The longest wall-clock gap between launch and return that is still credited as play time. Beyond
    // this the number is almost certainly a session recovered long after process death, not real play.
    private static readonly TimeSpan MaxDeferredPlaySession = TimeSpan.FromHours(12);

    /// <summary>
    /// Runs the deferred post-play work for a fire-and-forget launch (the Android path): accrues play
    /// time, pushes saves after the session, and refreshes Recently Played. The desktop path does this
    /// inline once the tracked process exits; Android has no process to await, so its platform return
    /// signal calls this when EmuShelf comes back to the foreground — and once at startup, to complete a
    /// session interrupted by process death. Must be called on the UI thread (it touches launch status and
    /// grid state). The caller clears the pending-session record after this returns.
    /// </summary>
    public async Task CompleteDeferredPlaySessionAsync(long gameId, TimeSpan playDuration)
    {
        // Off the UI thread: this runs the instant the couch returns to the foreground, and the library
        // read (a full-table query) would otherwise hitch that first frame on a large library.
        var game = await Task.Run(() => _library.GetGames().FirstOrDefault(candidate => candidate.Id == gameId));
        if (game is null)
        {
            _logger.Warning(
                $"Deferred play session for game id {gameId} could not complete: it is no longer in the library.");
            return;
        }

        // The duration is wall-clock from launch to this return, so it over-counts any time spent away
        // from the game before coming back — and after process death, "coming back" may be hours or days
        // later, which would otherwise accrue a wildly inflated session. Cap it: beyond a plausible single
        // sitting the number is meaningless, so drop it rather than record a fake multi-day playtime. The
        // launch itself was already stamped (last-played + play count) at start, so a dropped duration only
        // loses the (approximate) minutes, not the "played" signal.
        if (playDuration > TimeSpan.Zero && playDuration <= MaxDeferredPlaySession)
        {
            try
            {
                await Task.Run(() => _library.AddPlaytime(gameId, playDuration));
            }
            catch (Exception ex)
            {
                _logger.Error("Could not record playtime after a deferred launch.", ex);
            }
        }
        else if (playDuration > MaxDeferredPlaySession)
        {
            _logger.Information(
                $"Skipped implausible deferred playtime of {playDuration:g} for {game.Title} " +
                "(likely a session recovered long after process death).");
        }

        CloudSaveSyncOutcome? afterSync = null;
        try
        {
            // No-op until Android save sync is configured (CanSyncSystem is false), so this safely wires
            // the auto-sync path ahead of the Milestone E-android save providers that give it something to
            // push. On desktop this class never reaches here.
            afterSync = await SyncSavesForLaunchAsync(game, game.Title, afterExit: true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.Error($"Post-play save sync failed for {game.Title}.", ex);
        }

        // SyncSavesForLaunchAsync raised the "Syncing saves…" progress toast, which never auto-dismisses
        // (StatusDismissDelay is zero for Progress) — the operation that raised it must replace it with a
        // result. The desktop launch path does that via DescribeLaunchAndSaveSync; this Android deferred
        // path forgot to, so the toast lingered on screen after the background sync had finished. A null
        // outcome means the system does not participate, so no progress toast was raised — leave it be.
        if (afterSync is not null)
        {
            SetStatus(
                DescribeDeferredExitSaveSync(game.Title, afterSync),
                afterSync.Status == CloudSaveSyncStatus.Failed ? StatusSeverity.Error : StatusSeverity.Info);
        }

        try
        {
            await RefreshAfterPlayRecordedAsync();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not refresh Recently Played after {game.Title}: {ex.Message}");
        }
    }

    // A just-recorded play makes the Recently Played collection stale. If the user launched from
    // within it, rebuild it now so the game jumps to the front on return; otherwise just drop its
    // cached tiles so the next visit rebuilds from the DB — no reflow of the scope they returned to.
    private async Task RefreshAfterPlayRecordedAsync()
    {
        if (CurrentLibraryScope == LibraryScope.RecentlyPlayed)
            await ReloadGamesAsync();
        else
            InvalidateScopeCache(LibraryScope.RecentlyPlayed);
    }

    // Evicts one built scope from the navigation cache so its next visit rebuilds from the DB.
    // Disposes the evicted view models unless that scope is the one currently on screen.
    private void InvalidateScopeCache(LibraryScope scope)
    {
        var key = DescribeScope(scope, scope == LibraryScope.System ? SelectedSystem : null);
        if (_scopeCache.Remove(key, out var evicted) &&
            !string.Equals(key, _displayedScopeKey, StringComparison.Ordinal))
        {
            foreach (var viewModel in evicted)
                viewModel.Dispose();
        }
    }

    private async Task<bool> RememberSelectedDiscAsync(GameViewModel game, GameDisc disc)
    {
        if (game.DiscSelectionKey is null)
            return false;

        try
        {
            await Task.Run(() => _library.SetDiscSelection(game.DiscSelectionKey, disc.Game.Id));
            game.SetSelectedDisc(disc);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not remember Disc {disc.Number} for {game.Title}: {ex.Message}");
            return false;
        }
    }

    private async Task<CloudSaveSyncOutcome?> SyncSavesForLaunchAsync(
        Game game,
        string displayTitle,
        bool afterExit,
        CancellationToken cancellationToken)
    {
        if (_gameSaveSync?.CanSyncSystem(game.SystemId) != true)
            return null;

        IsSyncingSavesForLaunch = true;
        // Only the pre-launch pass blocks the grid: the emulator cannot start until it finishes. The
        // post-exit pass runs while the player browses, so it stays off the modal panel.
        IsBlockingLaunchSaveSync = !afterExit;
        SetStatus(
            afterExit
                ? $"{displayTitle} finished. Syncing saves…"
                : $"Syncing saves before launching {displayTitle}…",
            StatusSeverity.Progress);

        try
        {
            var outcome = await _gameSaveSync.SyncSystemAsync(
                game.SystemId,
                cancellationToken,
                LaunchStateKeysFor(game));
            if (outcome.Status == CloudSaveSyncStatus.Failed)
            {
                _logger.Warning(
                    $"Cloud save sync failed {(afterExit ? "after" : "before")} launching " +
                    $"game id {game.Id}: {outcome.Message}");
            }
            else if (outcome.Status == CloudSaveSyncStatus.AlreadyRunning)
            {
                // Expected whenever the user starts a game while a manual sync is running. That
                // pass covers this system too, so there is nothing to repair and nothing to report.
                _logger.Information(
                    $"Skipped the {(afterExit ? "post-exit" : "pre-launch")} save sync for game id " +
                    $"{game.Id}: a cloud sync was already running.");
            }
            else if (outcome.Status == CloudSaveSyncStatus.NotConfigured)
            {
                // CanSyncSystem already said this system participates, so reaching NotConfigured
                // means the participation check and the provider construction disagree. Nothing is
                // surfaced to the user for this status, which would make it a silent no-sync.
                _logger.Warning(
                    $"Cloud save sync reported no configured provider for system '{game.SystemId}' " +
                    $"{(afterExit ? "after" : "before")} launching game id {game.Id}, even though " +
                    "the system was reported as syncable. Saves were not synchronized.");
            }
            return outcome;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"Unexpected cloud save sync failure {(afterExit ? "after" : "before")} launching game id {game.Id}.",
                ex);
            return CloudSaveSyncOutcome.Failed(ex.Message);
        }
        finally
        {
            IsSyncingSavesForLaunch = false;
            IsBlockingLaunchSaveSync = false;
        }
    }

    // Keys that scope a launch/exit state sync to just the launched game, so launching one game no
    // longer hashes and syncs every game's states in a shared folder. The game's ROM file stem
    // covers RetroArch (it names states after the ROM); the stored serials/disc/title/arcade ids
    // cover DuckStation, PCSX2, PPSSPP, Dolphin, and RPCS3 (they name states after those). If none
    // are known, the state phase has nothing to match and stays out of the way; a manual Sync all
    // passes no keys and still covers every state.
    private IReadOnlyCollection<string> LaunchStateKeysFor(Game game)
    {
        var keys = new List<string>();
        var stem = System.IO.Path.GetFileNameWithoutExtension(game.Path);
        if (!string.IsNullOrWhiteSpace(stem))
            keys.Add(stem);

        if (_metadataStore is not null)
        {
            foreach (var identifier in _metadataStore.GetIdentifiers(game.Id))
            {
                if (identifier.Kind is GameIdentifierKind.Serial or GameIdentifierKind.DiscId
                        or GameIdentifierKind.TitleId or GameIdentifierKind.ArcadeSetName &&
                    !string.IsNullOrWhiteSpace(identifier.Value))
                {
                    keys.Add(identifier.Value);
                }
            }
        }

        return keys;
    }

    private static string DescribeLaunchAndSaveSync(
        GameLaunchResult launch,
        CloudSaveSyncOutcome? beforeSync,
        CloudSaveSyncOutcome? afterSync)
    {
        var syncParts = new List<string>();
        if (beforeSync?.Status == CloudSaveSyncStatus.Failed)
            syncParts.Add("pre-launch save sync did not complete; the saves currently on disk were used");
        else if (beforeSync?.Report?.Conflicts > 0)
            syncParts.Add(DescribeConflicts(beforeSync.Report.Conflicts, "during pre-launch sync"));
        if (afterSync?.Status == CloudSaveSyncStatus.Completed)
            syncParts.Add(DescribeCompletedSyncAfterExit(afterSync.Report!));
        else if (afterSync?.Status == CloudSaveSyncStatus.Failed)
            syncParts.Add($"save sync after exit failed: {afterSync.Message ?? "unknown error"}");
        // Say why a save state the player just made was not synced, rather than leaving a bare
        // "no saves were found" that hides the off toggle.
        if (afterSync?.SaveStatesSkipped == true)
            syncParts.Add("save-state sync is off for this platform (enable it in Settings to include save states)");
        if (syncParts.Count == 0)
            return launch.StatusText;

        return launch.StatusText.TrimEnd('.', ' ') + ". " + string.Join(". ", syncParts) + ".";
    }

    // The closing status for the Android deferred post-exit sync, which replaces the "Syncing saves…"
    // progress toast SyncSavesForLaunchAsync raised. The desktop path folds the same information into
    // DescribeLaunchAndSaveSync alongside the launch result; here only the sync outcome is available, so
    // it stands on its own. AlreadyRunning/NotConfigured still raised the progress toast, so they too get
    // a plain "finished" line rather than being left to hang.
    private static string DescribeDeferredExitSaveSync(string title, CloudSaveSyncOutcome outcome)
    {
        var head = $"{title} finished";
        return outcome.Status switch
        {
            CloudSaveSyncStatus.Completed =>
                $"{head}. {DescribeCompletedSyncAfterExit(outcome.Report!)}" +
                (outcome.SaveStatesSkipped
                    ? ". Save-state sync is off for this platform (enable it in Settings to include save states)."
                    : "."),
            CloudSaveSyncStatus.Failed =>
                $"{head}. Save sync after exit failed: {outcome.Message ?? "unknown error"}.",
            _ => $"{head}.",
        };
    }

    private static string DescribeCompletedSyncAfterExit(SaveSyncReport report)
    {
        if (report.Results.Count == 0)
            return "no saves were found to sync after exit";

        var parts = new List<string>();
        if (report.Uploaded > 0)
            parts.Add($"{report.Uploaded} uploaded");
        if (report.Downloaded > 0)
            parts.Add($"{report.Downloaded} downloaded");
        if (report.Conflicts > 0)
            parts.Add(DescribeConflicts(report.Conflicts, context: null));
        if (report.Unchanged > 0)
            parts.Add($"{report.Unchanged} already in sync");
        // Without this, a pass whose units all skipped leaves parts empty and prints a dangling
        // "save sync after exit: " with nothing after the colon.
        if (report.Skipped.Count > 0)
            parts.Add($"{report.Skipped.Count} skipped");
        return "save sync after exit: " + string.Join(", ", parts);
    }

    private static string DescribeConflicts(int count, string? context) =>
        $"{count} conflict{(count == 1 ? "" : "s")} resolved" +
        (context is null ? "" : $" {context}") +
        " (older copy backed up)";

    private async Task RefreshRetroAchievementsAfterTrackedExitAsync(int retroAchievementsGameId)
    {
        if (_retroRefresh is null)
            return;

        try
        {
            var response = await _retroRefresh.RefreshAfterTrackedExitAsync(retroAchievementsGameId);
            if (response?.IsSuccess == true)
                await ReloadGamesAsync();
        }
        catch (Exception ex)
        {
            // A game has already finished; this read-only follow-up must never change its launch
            // result or surface as a launch failure. Cached progress remains visible.
            _logger.Warning(
                $"RetroAchievements post-exit refresh failed for game {retroAchievementsGameId}.", ex);
        }
    }

    [RelayCommand]
    private async Task OpenAchievementDetailsAsync(GameViewModel? game)
    {
        if (game?.RetroAchievementsGameId is not { } retroAchievementsGameId)
            return;

        if (IsGamepadMode)
        {
            if (_retroDetails is null || _retroAccount is null)
            {
                SetStatus("Achievement details are unavailable right now.", StatusSeverity.Error);
                return;
            }

            try
            {
                // The cache read is small but remains off the UI thread, matching the desktop host.
                var cached = await Task.Run(() => _retroDetails.GetCached(retroAchievementsGameId));
                var details = new AchievementDetailsViewModel(
                    game.DisplayTitle,
                    retroAchievementsGameId,
                    _retroDetails,
                    _retroAccount,
                    _retroBadges,
                    cached,
                    logger: _logger,
                    deferBadgeLoading: true);
                OpenGamepadOverlay(GamepadOverlayKind.Achievements);
                // Assigning the details subscribes the change handler and slices the initial rows
                // (OnGamepadAchievementDetailsChanged); a stale column count is corrected by the
                // ListBox's SizeChanged during this overlay's layout pass.
                GamepadAchievementDetails = details;
                FocusFirstAchievement();
                _ = details.RefreshIfStaleAsync();
                return;
            }
            catch (Exception ex)
            {
                _logger.Error($"Could not open Gamepad achievements for game id {game.Id}.", ex);
                SetStatus($"Could not open achievements for {game.DisplayTitle}: {ex.Message}", StatusSeverity.Error);
                return;
            }
        }

        try
        {
            await _dialogs.ShowAchievementDetailsAsync(game.DisplayTitle, retroAchievementsGameId);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not open achievements for game id {game.Id}.", ex);
            SetStatus($"Could not open achievements for {game.DisplayTitle}: {ex.Message}", StatusSeverity.Error);
        }
    }

    private void SuspendFrontendUiWork()
    {
        _isFrontendSuspended = true;
        _searchDebounce.Stop();
    }

    private void ResumeFrontendUiWork()
    {
        _isFrontendSuspended = false;
        if (IsGamepadMode)
            _gamepadInputGuardUntil = DateTimeOffset.UtcNow + GamepadReturnInputGuard;
        if (!string.Equals(SearchText.Trim(), _appliedSearchText, StringComparison.Ordinal))
            ApplyFilter();

        if (_deferredCoverLoads.Count == 0)
            return;

        var pendingIds = _deferredCoverLoads.ToArray();
        _deferredCoverLoads.Clear();
        foreach (var gameId in pendingIds)
        {
            var game = _systemGames.FirstOrDefault(candidate => candidate.Id == gameId);
            if (game is not null)
                _ = LoadGameCoverCommand.ExecuteAsync(game);
        }
    }

    [RelayCommand]
    private async Task SaveGameTitleAsync(GameViewModel? game)
    {
        if (game is null || IsBusy)
            return;

        var title = game.DraftTitle.Trim();
        if (title.Length == 0)
        {
            SetStatus("A game title cannot be empty.", StatusSeverity.Error);
            return;
        }

        if (string.Equals(title, game.Title, StringComparison.Ordinal))
        {
            game.CompleteTitleEdit(title);
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => _library.UpdateTitle(game.Id, title));
            game.CompleteTitleEdit(title);
            await ReloadGamesAsync();
            SetStatus($"Renamed game to {title}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not rename game id {game.Id}.", ex);
            SetStatus($"Could not rename {game.DisplayTitle}: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ScrapeGameAsync(GameViewModel? game)
    {
        if (game is null || IsBusy)
            return;

        var applied = await _dialogs.ShowScraperAsync(game.Id, game.Title);
        if (applied)
            await ReloadGamesAsync();
    }

    /// <summary>
    /// Reveals the folder the game's emulator loads replacement textures from, creating it (with the
    /// correct id) when it doesn't exist yet so the user can drop a downloaded pack straight in. The
    /// coordinator resolves root + id; this command performs the one create-and-open and reports the
    /// outcome. The created folder is empty and lives in the emulator's own textures directory — no
    /// game file is touched and no existing pack is altered.
    /// </summary>
    [RelayCommand]
    private async Task OpenTextureFolderAsync(GameViewModel? game)
    {
        if (game is null)
            return;
        if (_texturePacks is null)
        {
            SetStatus("Texture packs are unavailable right now.", StatusSeverity.Error);
            return;
        }

        try
        {
            var resolution = await _texturePacks.ResolveTextureFolderAsync(game.LaunchModel);
            if (!resolution.IsResolved || resolution.FullPath is null)
            {
                SetStatus(
                    resolution.Diagnostic ?? $"Could not open the texture folder for {game.DisplayTitle}.",
                    StatusSeverity.Error);
                return;
            }

            var existed = System.IO.Directory.Exists(resolution.FullPath);
            System.IO.Directory.CreateDirectory(resolution.FullPath);
            await _fileReveal.OpenDirectoryAsync(resolution.FullPath);
            SetStatus(existed
                ? $"Opened texture folder {resolution.FolderId} for {game.DisplayTitle}"
                : $"Created and opened texture folder {resolution.FolderId} for {game.DisplayTitle}");
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open the texture folder for {game.DisplayTitle}: {ex.Message}", StatusSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task ShowGameInFolderAsync(GameViewModel? game)
    {
        // The path the ROM/folder is revealed at is the concrete source that would launch — the
        // currently selected disc for a multi-disc set — so it stays correct and preselected.
        if (game?.LaunchModel.Path is not { Length: > 0 } path)
            return;

        try
        {
            await _fileReveal.RevealAsync(path);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not reveal the folder for game id {game.Id}.", ex);
            SetStatus(
                $"Could not open the folder for {game.DisplayTitle}: {ex.Message}",
                StatusSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task SetGameCoverAsync(GameViewModel? game)
    {
        if (game is null || IsBusy)
            return;

        var preferredAspectRatio = _systemsById.TryGetValue(game.SystemId, out var system)
            ? system.CoverAspectRatio
            : game.CoverAspectRatio;
        var pickedCover = await _dialogs.PickGameCoverAsync(new GameCoverPickerContext(
            game.DisplayTitle,
            game.SystemName,
            preferredAspectRatio));
        if (pickedCover is null)
            return;

        await ImportPickedCoverAsync(game, pickedCover);
    }

    /// <summary>
    /// Imports a chosen cover (a local file or a downloaded web image) into EmuShelf's own Covers/
    /// store, refreshes the grid tile and cover projection, and removes the previous EmuShelf-owned
    /// cover. Shared by the Desktop "Set cover" dialog and the Gamepad controller-native cover search.
    /// </summary>
    private async Task ImportPickedCoverAsync(GameViewModel game, PickedGameCover pickedCover)
    {
        IsBusy = true;
        SetStatus($"Preparing cover for {game.DisplayTitle}…", StatusSeverity.Progress);
        var previousCoverPath = game.CoverPath;
        try
        {
            var imported = await _covers.ImportAsync(game.Id, pickedCover.SourcePath);
            try
            {
                await Task.Run(() => _library.UpdateCoverPath(game.Id, imported.CoverPath));
            }
            catch
            {
                try
                {
                    await _covers.DeleteOwnedCoverAsync(game.Id, imported.CoverPath);
                }
                catch (Exception cleanupException)
                {
                    // The database still points to the previous cover. A failed best-effort
                    // cleanup can leave only an unreferenced EmuShelf-owned staged file.
                    _logger.Warning(
                        $"Could not remove an uncommitted cover for game id {game.Id}.",
                        cleanupException);
                }
                throw;
            }

            var currentGame = _systemGames.FirstOrDefault(candidate => candidate.Id == game.Id);
            if (currentGame is not null)
                currentGame.ApplyCoverPath(imported.CoverPath);

            Exception? previewFailure = null;
            if (currentGame is not null)
            {
                try
                {
                    var thumbnailPath = await _covers.GetThumbnailAsync(game.Id, imported.CoverPath)
                        ?? throw new IOException("The cached cover thumbnail is unavailable.");
                    var image = await Task.Run(() => new Bitmap(thumbnailPath));
                    var latestGame = _systemGames.FirstOrDefault(candidate => candidate.Id == game.Id);
                    if (latestGame is not null &&
                        string.Equals(latestGame.CoverPath, imported.CoverPath, StringComparison.Ordinal))
                    {
                        latestGame.CoverImage = image;
                    }
                    else
                    {
                        image.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Could not preview the new cover for game id {game.Id}.", ex);
                    previewFailure = ex;
                }
            }

            Exception? cleanupFailure = null;
            if (previousCoverPath is not null &&
                !string.Equals(previousCoverPath, imported.CoverPath, StringComparison.Ordinal))
            {
                try
                {
                    await _covers.DeleteOwnedCoverAsync(game.Id, previousCoverPath);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Could not remove the previous cover for game id {game.Id}.", ex);
                    cleanupFailure = ex;
                }
            }

            var warnings = new List<string>();
            if (previewFailure is not null)
                warnings.Add($"the preview could not be loaded: {previewFailure.Message}");
            if (cleanupFailure is not null)
                warnings.Add($"the previous EmuShelf cover could not be removed: {cleanupFailure.Message}");

            SetStatus(
                warnings.Count == 0
                    ? $"Updated cover for {game.DisplayTitle}"
                    : $"Updated cover for {game.DisplayTitle}, but {string.Join("; ", warnings)}",
                warnings.Count == 0 ? StatusSeverity.Info : StatusSeverity.Error);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not set a cover for game id {game.Id}.", ex);
            SetStatus($"Could not set cover for {game.DisplayTitle}: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            if (pickedCover.IsTemporary)
            {
                try
                {
                    File.Delete(pickedCover.SourcePath);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not remove a downloaded cover staging file.", ex);
                }
            }
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveGameAsync(GameViewModel? game)
    {
        if (game is null || IsBusy ||
            !await _dialogs.ConfirmRemoveGameAsync(game.DisplayTitle))
        {
            return;
        }

        await RemoveGameCoreAsync(game);
    }

    private async Task RemoveGameCoreAsync(GameViewModel game)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            await Task.Run(() => _library.RemoveGames(game.Discs.Select(disc => disc.Game.Id).ToArray()));
            await ReloadGamesAsync();
            SetStatus($"Removed {game.DisplayTitle} from the library — game files were not touched");
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not remove game id {game.Id} from the library.", ex);
            SetStatus($"Could not remove {game.DisplayTitle}: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGames))]
    private async Task RemoveSelectedGamesAsync()
    {
        if (IsBusy)
            return;

        var selectedGames = Games.Where(game => game.IsSelected).ToArray();
        if (selectedGames.Length == 0)
        {
            return;
        }

        var confirmed = selectedGames.Length == 1
            ? await _dialogs.ConfirmRemoveGameAsync(selectedGames[0].Title)
            : await _dialogs.ConfirmRemoveGamesAsync(selectedGames.Length);
        if (!confirmed)
            return;

        IsBusy = true;
        try
        {
            await Task.Run(() => _library.RemoveGames(selectedGames
                .SelectMany(game => game.Discs.Select(disc => disc.Game.Id))
                .Distinct()
                .ToArray()));
            await ReloadGamesAsync();
            SetStatus($"Removed {selectedGames.Length} {(selectedGames.Length == 1 ? "game" : "games")} from the library — game files and covers were not touched");
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not remove {selectedGames.Length} selected games from the library.", ex);
            SetStatus($"Could not remove the selected games: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedGames))]
    private async Task ScrapeSelectedGamesAsync()
    {
        if (IsBusy)
            return;

        var selectedGames = Games.Where(game => game.IsSelected).ToArray();
        if (selectedGames.Length == 0)
            return;

        var gameIds = selectedGames.Select(game => game.Id).Distinct().ToList();
        var systemName = selectedGames.Select(game => game.SystemName).Distinct().Count() == 1
            ? selectedGames[0].SystemName
            : "selected";

        var applied = await _dialogs.ShowBatchScraperAsync(gameIds, systemName);
        if (applied)
            await ReloadGamesAsync();
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (IsBusy)
            return;

        try
        {
            await _dialogs.ShowEmulatorSettingsAsync(
                Systems,
                _emulators,
                _emulatorConfigurations,
                CreateLibraryMaintenanceActions(),
                _metadataPreferences,
                CreateRetroAchievementsSettingsContext(),
                _cloudSaveSync?.CreateSettingsContext(),
                CreateTexturePackSettingsContext(),
                CreateScreenScraperSettingsContext(),
                ThemeChoices,
                AmbientThemeFromArtwork,
                SetAmbientThemeFromArtworkAsync,
                Updates,
                CreateHotkeySettingsContext);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not open emulator settings.", ex);
            SetStatus($"Could not open emulator settings: {ex.Message}", StatusSeverity.Error);
        }
    }

    private async Task<EmulatorSettingsViewModel> CreateSettingsViewModelAsync()
    {
        var systemIds = Systems.Select(system => system.Id).ToArray();
        // Read every database source the panel needs — configs, profiles, and all remembered folders —
        // in one worker pass so building the rows never reopens a connection per system on the UI thread.
        var (configured, profiles, libraryFolders, hotkeyContext) = await Task.Run(() =>
            (_emulatorConfigurations.GetAll(systemIds),
             _emulatorConfigurations.GetAllProfiles(systemIds),
             EmulatorSettingsViewModel.GroupLibraryFolders(GetAllLibraryFoldersForSettings()),
             CreateHotkeySettingsContext()));
        return new EmulatorSettingsViewModel(
            Systems,
            _emulators,
            configured,
            _emulatorConfigurations,
            _dialogs,
            CreateLibraryMaintenanceActions(),
            _metadataPreferences,
            _logger,
            CreateRetroAchievementsSettingsContext(),
            _cloudSaveSync?.CreateSettingsContext(),
            CreateTexturePackSettingsContext(),
            // Mirrors Desktop: the account can be managed from Settings, not only the per-game scraper
            // overlay. The controller text-entry flow handles the username/password rows.
            CreateScreenScraperSettingsContext(),
            hotkeys: hotkeyContext,
            themeChoices: ThemeChoices,
            ambientThemeFromArtwork: AmbientThemeFromArtwork,
            setAmbientThemeFromArtwork: SetAmbientThemeFromArtworkAsync,
            setCrtScreenEffect: enabled =>
            {
                CrtScreenEffect = enabled;
                return Task.CompletedTask;
            },
            crtShelfEffect: CrtScreenEffect,
            profiles: profiles,
            updates: Updates,
            libraryFolders: libraryFolders,
            fixedEmulatorChoices: OperatingSystem.IsAndroid()
                ? AndroidEmulatorChoiceCatalog.BySystem
                : null);
    }

    private Task SetAmbientThemeFromArtworkAsync(bool value)
    {
        // Route through the source-of-truth property so persistence and the live retint fire once.
        AmbientThemeFromArtwork = value;
        return Task.CompletedTask;
    }

    private LibraryMaintenanceActions CreateLibraryMaintenanceActions() => new(
        RescanSystemFromSettingsAsync,
        RescanAllFromSettingsAsync,
        FetchMetadataForSystemFromSettingsAsync,
        FetchAllMetadataFromSettingsAsync,
        SyncRpcs3LibraryFromSettingsAsync,
        () => ShowEmptyPlatforms,
        SetShowEmptyPlatformsAsync,
        new LibraryFolderManagementActions(
            GetLibraryFoldersForSettings,
            AddLibraryFolderFromSettingsAsync,
            ChangeLibraryFolderFromSettingsAsync,
            ForgetLibraryFolderFromSettingsAsync,
            GetAll: GetAllLibraryFoldersForSettings),
        DataDirectory: _dataDirectory);

    private RetroAchievementsSettingsContext? CreateRetroAchievementsSettingsContext() =>
        _retroAccount is null
            ? null
            : new RetroAchievementsSettingsContext(
                _retroAccount.Account,
                _retroAccount.IsConnected,
                ConnectRetroAchievementsAsync,
                DisconnectRetroAchievementsAsync,
                RefreshRetroAchievementsMatchesAsync);

    // Reads each configured emulator's hotkey config to build the section state; the caller runs it
    // on a worker so opening Settings never does file IO on the UI thread.
    //
    // Android has no hotkeys section: the feature writes a *keyboard* hotkey scheme into each desktop
    // emulator's own config so it can be driven by Steam Input. On Android there is no Steam Input, and
    // the emulators are sandboxed apps whose config EmuShelf cannot rewrite — so the whole section is
    // inert there. Returning null drops SettingsSection.Hotkeys (gated on a non-empty context) and, with
    // it, HasHotkeys — which also disables the gamepad hotkey-editor overlay entry.
    private HotkeySettingsContext? CreateHotkeySettingsContext() =>
        OperatingSystem.IsAndroid() ? null : _hotkeys?.CreateSettingsContext();

    private TexturePackSettingsContext? CreateTexturePackSettingsContext() =>
        // Detection is a desktop-shaped feature: the resolvers walk emulator user directories in the
        // Documents/.config/Library layouts that do not exist on Android, and the handful of Android
        // emulators that do support texture packs (Dolphin, PPSSPP) keep them under Android/data in a
        // shape those resolvers cannot read. Rather than show a Texture Packs section that reports
        // every platform as unconfigured, omit it on Android until an Android-native resolver exists.
        OperatingSystem.IsAndroid()
            ? null
            // Titles come from the whole library, not the visible collection: a Dolphin pack must
            // still name the GameCube game it matched while the user is viewing PS1.
            : _texturePacks?.CreateSettingsContext(
                BuildLibraryTitleLookup,
                RefreshTexturePacksAsync);

    private ScreenScraperSettingsContext? CreateScreenScraperSettingsContext() =>
        _screenScraperAccount is null
            ? null
            : new ScreenScraperSettingsContext(
                _screenScraperAccount.IsConnected,
                _screenScraperAccount.LastAccountInfo,
                _screenScraperAccount.ConnectAsync,
                _screenScraperAccount.DisconnectAsync);

    private void OnGamepadSettingsCloseRequested(bool saved)
    {
        if (!IsGamepadSettingsOpen && GamepadSettings is null)
            return;

        CloseGamepadSettingsProjection();
        if (!IsGamepadMode)
            return;

        OpenGamepadOverlay(GamepadOverlayKind.SystemMenu);
        var settingsIndex = GamepadOverlayOptions.ToList().FindIndex(option => option.Label == "Settings");
        if (settingsIndex >= 0)
            GamepadOverlaySelectionIndex = settingsIndex;
        if (saved)
            SetStatus("Settings saved.");
    }

    private void OnGamepadSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GamepadSettingsViewModel.FocusRevision) or
            nameof(GamepadSettingsViewModel.FocusedRowIndex) or
            nameof(GamepadSettingsViewModel.SelectedSection) or
            nameof(GamepadSettingsViewModel.IsTextEntryOpen) or
            nameof(GamepadSettingsViewModel.IsConfirmationOpen) or
            nameof(GamepadSettingsViewModel.IsChoicePickerOpen) or
            nameof(GamepadSettingsViewModel.IsConfirmChoiceSelected) or
            nameof(GamepadSettingsViewModel.TextEntryRevision))
        {
            OnPropertyChanged(nameof(GamepadSettingsFocusRevision));
            OnPropertyChanged(nameof(IsGamepadSettingsTextEntryOpen));
            OnPropertyChanged(nameof(IsGamepadSettingsConfirmationOpen));
            OnPropertyChanged(nameof(IsGamepadSettingsChoicePickerOpen));
            OnPropertyChanged(nameof(IsGamepadSettingsNormal));
            OnPropertyChanged(nameof(GamepadOverlayOwnsTextInput));
        }
    }

    partial void OnGamepadSettingsChanged(
        GamepadSettingsViewModel? oldValue,
        GamepadSettingsViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.CloseRequested -= OnGamepadSettingsCloseRequested;
            oldValue.PropertyChanged -= OnGamepadSettingsPropertyChanged;
        }
        if (newValue is not null)
        {
            newValue.CloseRequested += OnGamepadSettingsCloseRequested;
            newValue.PropertyChanged += OnGamepadSettingsPropertyChanged;
        }
        OnPropertyChanged(nameof(GamepadSettingsFocusRevision));
        OnPropertyChanged(nameof(IsGamepadSettingsTextEntryOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsConfirmationOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsChoicePickerOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsNormal));
        OnPropertyChanged(nameof(GamepadOverlayOwnsTextInput));
    }

    private void CloseGamepadSettingsProjection()
    {
        if (GamepadSettings is not { } settings)
            return;

        settings.Dispose();
        GamepadSettings = null;
        OnPropertyChanged(nameof(GamepadSettingsFocusRevision));
        OnPropertyChanged(nameof(IsGamepadSettingsTextEntryOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsConfirmationOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsChoicePickerOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsNormal));
        OnPropertyChanged(nameof(GamepadOverlayOwnsTextInput));
    }

    // Connect pipeline: validate the account, identify the existing library, resolve hashes
    // against console catalogues, refresh progress, and reload so marks and columns appear.
    // Matching and progress failures are non-fatal — the validated account stays connected.
    internal async Task<RetroAchievementsConnectionSummary> ConnectRetroAchievementsAsync(
        string username,
        string apiKey,
        IProgress<RetroAchievementsLibrarySyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_retroAccount is null)
            return new RetroAchievementsConnectionSummary(
                RetroAchievementsConnectionResult.ServerError);

        var result = await _retroAccount.ConnectAsync(username, apiKey, cancellationToken);
        if (result != RetroAchievementsConnectionResult.Connected)
            return new RetroAchievementsConnectionSummary(result);

        var gameIds = await Task.Run(
            () => _library.GetGames().Select(game => game.Id).ToArray(),
            cancellationToken);
        var sync = await SynchronizeRetroAchievementsAsync(gameIds, progress, cancellationToken);
        return new RetroAchievementsConnectionSummary(result, sync);
    }

    internal async Task DisconnectRetroAchievementsAsync(CancellationToken cancellationToken)
    {
        if (_retroAccount is null)
            return;

        // Account-scoped progress must not be cleared while an import-triggered sync still has
        // the old credentials. The same lock also makes any queued import recheck the now-
        // disconnected account before it reads a game or writes new progress.
        await _retroAchievementsPipeline.WaitAsync(cancellationToken);
        try
        {
            await _retroAccount.DisconnectAsync(cancellationToken);
            _retroProgress?.Clear();
            _retroDetails?.Clear();
            await ReloadGamesAsync();
        }
        finally
        {
            _retroAchievementsPipeline.Release();
        }
    }

    /// <summary>
    /// Explicit Settings maintenance for games which were previously unmatched or had no set.
    /// Identification still uses the fingerprint cache; only the remote console catalogues are
    /// forced to refresh before every cached hash is matched again.
    /// </summary>
    internal async Task<RetroAchievementsLibrarySyncSummary?> RefreshRetroAchievementsMatchesAsync(
        IProgress<RetroAchievementsLibrarySyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_retroAchievements is null || _retroAccount?.IsConnected != true)
            return null;

        var gameIds = await Task.Run(
            () => _library.GetGames().Select(game => game.Id).ToArray(),
            cancellationToken);
        return await SynchronizeRetroAchievementsAsync(
            gameIds,
            progress,
            cancellationToken,
            forceRefreshCatalogues: true);
    }

    private async Task MaybeStartMetadataForImportAsync(IReadOnlyList<long> addedGameIds)
    {
        if (addedGameIds.Count == 0)
            return;

        // Disc identification is now explicitly gated on a connected account. Existing games
        // are backfilled once on connect; later imports use the same serialized full pipeline.
        if (_retroAchievements is not null && _retroAccount?.IsConnected == true)
            _ = SynchronizeImportedRetroAchievementsAsync(addedGameIds);

        var shouldFetch = _metadataPreferences.AutomaticallyFetchAfterImport;
        if (!shouldFetch && !_metadataPreferences.ConsentPromptShown)
        {
            if (OperatingSystem.IsAndroid())
            {
                // The one-time consent prompt is a Desktop dialog; the gamepad shell has no consent
                // overlay, so the injected dialog service is a no-op that always declines. Rather than
                // silently swallow that, point the user at the toggle that controls this on Android and
                // record the choice so the hint shows once, not after every import. Turning the toggle
                // on later takes over through AutomaticallyFetchAfterImport above.
                SetStatus(
                    "Imported. To fetch artwork and details automatically, turn on "
                    + "Settings → Artwork & Metadata → “Fetch after import”.",
                    StatusSeverity.Info);
                try
                {
                    await _metadataPreferences.RecordConsentAsync(MetadataConsentChoice.NotNow);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not persist the metadata consent preference.", ex);
                }
            }
            else
            {
                var choice = await _dialogs.PromptForMetadataConsentAsync(addedGameIds.Count);
                shouldFetch = choice is MetadataConsentChoice.FetchOnce or MetadataConsentChoice.Always;
                try
                {
                    await _metadataPreferences.RecordConsentAsync(choice);
                }
                catch (Exception ex)
                {
                    _logger.Warning("Could not persist the metadata consent preference.", ex);
                    SetStatus(StatusText + " — metadata preference could not be saved", StatusSeverity.Error);
                }
            }
        }

        if (shouldFetch)
            _ = EnrichImportedGamesAsync(addedGameIds);
    }

    private async Task EnrichImportedGamesAsync(IReadOnlyList<long> gameIds)
    {
        try
        {
            SetStatus(
                gameIds.Count == 1
                    ? "Fetching metadata for 1 new game…"
                    : $"Fetching metadata for {gameIds.Count} new games…",
                StatusSeverity.Progress);
            var summary = await _metadataService.EnrichAsync(gameIds);
            await ReloadGamesAsync();
            SetStatus(summary.ToStatusText());
        }
        catch (Exception ex)
        {
            _logger.Error("Automatic metadata enrichment failed.", ex);
            SetStatus($"Metadata failed: {ex.Message}", StatusSeverity.Error);
        }
    }

    private async Task<RetroAchievementsLibrarySyncSummary?> SynchronizeRetroAchievementsAsync(
        IReadOnlyList<long> gameIds,
        IProgress<RetroAchievementsLibrarySyncProgress>? progress,
        CancellationToken cancellationToken,
        bool forceRefreshCatalogues = false)
    {
        if (_retroAchievements is null)
            return null;

        await _retroAchievementsPipeline.WaitAsync(cancellationToken);
        try
        {
            // An import can have been queued while connected, then waited behind a different
            // sync until after disconnect. Do not identify media or recreate account-scoped
            // cache rows for a no-longer-connected account.
            if (_retroAccount?.IsConnected != true)
                return null;

            var identification = await _retroAchievements.IdentifyAsync(
                gameIds,
                cancellationToken,
                progress);

            RetroAchievementsMatchSummary? matching = null;
            RetroAchievementsProgressRefreshSummary? achievementProgress = null;
            var credentials = _retroAccount?.CurrentCredentials;

            if (_retroMatching is not null && credentials is not null)
            {
                try
                {
                    matching = await _retroMatching.MatchAsync(
                        credentials,
                        forceRefreshCatalogues,
                        cancellationToken,
                        progress);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A validated account remains connected if a cache/network operation fails;
                    // stale links and cached progress are still useful to the library display.
                    _logger.Warning("RetroAchievements catalogue matching failed.", ex);
                }
            }

            if (_retroProgress is not null && credentials is not null)
            {
                try
                {
                    achievementProgress = await _retroProgress.RefreshAllAsync(
                        credentials,
                        cancellationToken,
                        progress);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning("RetroAchievements progress refresh failed.", ex);
                }
            }

            await ReloadGamesAsync();
            return new RetroAchievementsLibrarySyncSummary(
                identification,
                matching,
                achievementProgress);
        }
        finally
        {
            _retroAchievementsPipeline.Release();
        }
    }

    private async Task SynchronizeImportedRetroAchievementsAsync(IReadOnlyList<long> gameIds)
    {
        try
        {
            await SynchronizeRetroAchievementsAsync(gameIds, progress: null, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Import-triggered work has no cancellation source today, but retain the same
            // cancellation semantics as the explicit Settings connection path.
        }
        catch (Exception ex)
        {
            _logger.Warning("RetroAchievements synchronization for imported games failed.", ex);
        }
    }

    private Task<string> FetchMetadataForSystemFromSettingsAsync(string systemId) =>
        FetchMissingMetadataFromSettingsAsync(systemId);

    private Task<string> FetchAllMetadataFromSettingsAsync(IProgress<MetadataEnrichmentProgress> progress) =>
        FetchMissingMetadataFromSettingsAsync(null, progress);

    private async Task<string> FetchMissingMetadataFromSettingsAsync(
        string? systemId,
        IProgress<MetadataEnrichmentProgress>? progress = null)
    {
        // Clicking a manual fetch is itself an explicit one-time opt-in. Remember that
        // decision so the first-import prompt is not shown later for the same user.
        if (!_metadataPreferences.ConsentPromptShown)
            await _metadataPreferences.RecordConsentAsync(MetadataConsentChoice.FetchOnce);

        var summary = await _metadataService.EnrichMissingAsync(systemId, progress);
        await ReloadGamesAsync();
        SetStatus(summary.ToStatusText());
        return StatusText;
    }

    [RelayCommand]
    private async Task SetThemeAsync(ThemePreference preference)
    {
        if (preference == CurrentTheme)
            return;

        try
        {
            await _themeService.SetThemeAsync(preference);
            CurrentTheme = _themeService.Current;
            SetStatus($"Appearance set to {preference.ToString().ToLowerInvariant()}");
        }
        catch (Exception ex)
        {
            _logger.Error("Could not persist the appearance preference.", ex);
            CurrentTheme = _themeService.Current;
            SetStatus(
                $"Appearance changed for this session, but could not be saved: {ex.Message}",
                StatusSeverity.Error);
        }
    }
}
