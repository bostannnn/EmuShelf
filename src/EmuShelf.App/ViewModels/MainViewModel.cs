using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
using EmuShelf.Core.Systems;
using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Systems;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Rpcs3;

namespace EmuShelf.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const int SearchDebounceMs = 250;
    private const int ViewStateSaveDebounceMs = 500;
    // Fast LB/RB cycling changes the selected platform many times a second; each change used to run a
    // full clear-and-rebuild of the grid (BeginScopeChange + a fresh DB query + hundreds of new
    // GameViewModels), which is what blanked covers, dropped the selector and reset focus mid-scroll.
    // Coalesce a burst into one reload of the platform the user settles on.
    private const int PlatformReloadDebounceMs = 180;
    private static readonly TimeSpan GamepadReturnInputGuard = TimeSpan.FromMilliseconds(500);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

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
    private readonly IEmulatorLaunchService _launchService;
    private readonly IEmulatorConfigurationStore _emulatorConfigurations;
    private readonly IReadOnlyList<EmulatorDefinition> _emulators;
    private readonly IGameCoverService _covers;
    private readonly IGameDetailsStore? _gameDetails;
    private readonly IAppThemeService _themeService;
    private readonly IInterfaceModeService? _interfaceModeService;
    private readonly IApplicationLifetimeService? _applicationLifetime;
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
    private readonly IRemoteArtworkDownloader? _artworkDownloader;
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
    private readonly IAppLogger _logger;
    private readonly IReadOnlyDictionary<string, GameSystem> _systemsById;

    private readonly DispatcherTimer _searchDebounce;
    private readonly DispatcherTimer _statusDismiss;
    private readonly DispatcherTimer _viewStateSave;
    private readonly DispatcherTimer _platformReloadDebounce;
    private TaskCompletionSource? _platformReloadCompletion;
    private readonly ILibraryViewStateService _libraryViewState;
    private bool _isRestoringViewState;
    private readonly List<GameViewModel> _systemGames = [];
    private readonly HashSet<long> _deferredCoverLoads = [];
    private GameViewModel? _selectionAnchor;
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

    // Bumped on every reload so a slow load that finishes after a newer one is discarded,
    // keeping the shown games in sync with the current selection.
    private int _loadGeneration;
    private Task _selectedSystemLoad = Task.CompletedTask;

    public ObservableCollection<GameSystem> Systems { get; }
    public ObservableCollection<GameSystem> NavigationSystems { get; }
    public ObservableCollection<GamepadPlatformTabViewModel> GamepadPlatforms { get; }
    public BulkObservableCollection<GameViewModel> Games { get; } = [];

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

    [ObservableProperty]
    public partial LibraryScope CurrentLibraryScope { get; set; } = LibraryScope.System;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsGridView { get; set; } = true;

    /// <summary>Gamepad (couch) mode only: the spotlight layout (list + fanart hero) when true, the
    /// cover grid when false. Toggled from the couch toolbar and remembered across launches.</summary>
    [ObservableProperty]
    public partial bool IsGamepadSpotlightView { get; set; }

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
        ScheduleLibraryViewStateSave();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        NotifySortGlyphs();
        ScheduleLibraryViewStateSave();
    }

    partial void OnIsGridViewChanged(bool value) => ScheduleLibraryViewStateSave();

    partial void OnIsGamepadSpotlightViewChanged(bool value)
    {
        ScheduleLibraryViewStateSave();
        OnPropertyChanged(nameof(ShowGamepadGrid));
        OnPropertyChanged(nameof(ShowGamepadSpotlight));
        IsSpotlightAchievementsFocused = false; // the hero always opens on Play
        OnPropertyChanged(nameof(IsSpotlightPlayFocused));
        if (value)
            LoadSpotlightHero(FocusedGame);
        else
            ClearSpotlightHero();
    }

    /// <summary>Flips the couch layout between the cover grid and the spotlight list + hero.</summary>
    [RelayCommand]
    private void ToggleGamepadView()
    {
        if (IsGamepadMode)
            IsGamepadSpotlightView = !IsGamepadSpotlightView;
    }

    /// <summary>The system-menu entry point: switch the couch layout, then close the menu so the
    /// change is immediately visible.</summary>
    [RelayCommand]
    private void ToggleGamepadViewFromMenu()
    {
        ToggleGamepadView();
        CloseGamepadOverlay();
    }

    private void NotifySortGlyphs()
    {
        OnPropertyChanged(nameof(TitleSortGlyph));
        OnPropertyChanged(nameof(ConsoleSortGlyph));
        OnPropertyChanged(nameof(FormatSortGlyph));
        OnPropertyChanged(nameof(AchievementsSortGlyph));
        OnPropertyChanged(nameof(TexturesSortGlyph));
        OnPropertyChanged(nameof(StatusSortGlyph));
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

    /// <summary>The couch cover grid is on screen: gamepad mode, games present, spotlight off.</summary>
    public bool ShowGamepadGrid => IsGamepadMode && HasGames && !IsGamepadSpotlightView;

    /// <summary>The couch spotlight (list + fanart hero) is on screen: gamepad mode, games present,
    /// spotlight on.</summary>
    public bool ShowGamepadSpotlight => IsGamepadMode && HasGames && IsGamepadSpotlightView;

    partial void OnHasGamesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGamepadGrid));
        OnPropertyChanged(nameof(ShowGamepadSpotlight));
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

    [ObservableProperty]
    public partial ThemePreference CurrentTheme { get; set; }

    /// <summary>When true, the couch UI recolours from the focused game's artwork; the chosen theme is
    /// the fallback for artwork with no usable colour. Offered next to the theme gallery in both modes.</summary>
    [ObservableProperty]
    public partial bool AmbientThemeFromArtwork { get; set; }

    /// <summary>Every built-in appearance, offered in Desktop Settings. The controller
    /// theme gallery projects the same instances so both modes stay in lock-step.</summary>
    public IReadOnlyList<ThemeChoiceViewModel> ThemeChoices { get; }

    [ObservableProperty]
    public partial GameViewModel? SelectedGame { get; set; }

    [ObservableProperty]
    public partial bool IsNavigationCollapsed { get; set; }

    [ObservableProperty]
    public partial bool IsGamepadMode { get; set; }

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

    public bool HasGamepadOverlay => GamepadOverlay != GamepadOverlayKind.None;
    public bool GamepadOverlayOwnsTextInput => GamepadOverlay is GamepadOverlayKind.Search or GamepadOverlayKind.Rename ||
        IsGamepadSettingsOpen && GamepadSettings?.IsTextEntryOpen == true;
    public bool IsGamepadAchievementsOpen => GamepadOverlay == GamepadOverlayKind.Achievements;
    public bool IsGamepadSearchOpen => GamepadOverlay == GamepadOverlayKind.Search;
    public bool IsGamepadCollectionsOpen => GamepadOverlay == GamepadOverlayKind.Collections;
    public bool IsGamepadRenameOpen => GamepadOverlay == GamepadOverlayKind.Rename;
    public bool IsGamepadDiscSelectionOpen => GamepadOverlay == GamepadOverlayKind.DiscSelection;
    public bool IsGamepadRemoveOpen => GamepadOverlay == GamepadOverlayKind.RemoveConfirmation;
    public bool IsGamepadCoverHandoffOpen => GamepadOverlay == GamepadOverlayKind.CoverDesktopHandoff;
    public bool IsGamepadScraperOpen => GamepadOverlay == GamepadOverlayKind.Scraper;
    public bool IsGamepadSystemMenuOpen => GamepadOverlay == GamepadOverlayKind.SystemMenu;
    public bool IsGamepadSettingsOpen => GamepadOverlay == GamepadOverlayKind.Settings;
    public bool IsGamepadSettingsTextEntryOpen => IsGamepadSettingsOpen && GamepadSettings?.IsTextEntryOpen == true;
    public bool IsGamepadSettingsConfirmationOpen => IsGamepadSettingsOpen && GamepadSettings?.IsConfirmationOpen == true;
    public int GamepadSettingsFocusRevision => GamepadSettings?.FocusRevision ?? 0;
    public bool IsGamepadDesktopModeConfirmationOpen => GamepadOverlay == GamepadOverlayKind.DesktopModeConfirmation;
    public bool IsGamepadQuitConfirmationOpen => GamepadOverlay == GamepadOverlayKind.QuitConfirmation;
    public bool AreGamepadOverlayOptionsTopAligned => GamepadOverlay is
        GamepadOverlayKind.Actions or GamepadOverlayKind.Collections or
        GamepadOverlayKind.DiscSelection or GamepadOverlayKind.SystemMenu;
    // The Achievements, Settings and Scraper overlays render their own bespoke bodies, so the
    // shared option-button list and the chrome title are hidden for them.
    public bool UsesGamepadDefaultOverlayHints => GamepadOverlay is not
        (GamepadOverlayKind.Achievements or GamepadOverlayKind.Search or
         GamepadOverlayKind.Rename or GamepadOverlayKind.Scraper or GamepadOverlayKind.Settings);
    public bool ShowsGamepadOverlayOptions => GamepadOverlay is not
        (GamepadOverlayKind.Achievements or GamepadOverlayKind.Search or GamepadOverlayKind.Rename or
         GamepadOverlayKind.Settings or GamepadOverlayKind.Scraper);
    public bool ShowsGamepadOverlayChromeTitle => GamepadOverlay is not
        (GamepadOverlayKind.Achievements or GamepadOverlayKind.Settings or GamepadOverlayKind.Scraper);
    public string GamepadOverlayTitle => GamepadOverlay switch
    {
        GamepadOverlayKind.Actions => FocusedGame is null ? "Game actions" : $"{FocusedGame.Title} actions",
        GamepadOverlayKind.Search => "Search",
        GamepadOverlayKind.Collections => "Collections",
        GamepadOverlayKind.Rename => "Rename game",
        GamepadOverlayKind.DiscSelection => FocusedGame is null ? "Select disc" : $"{FocusedGame.Title} — select disc",
        GamepadOverlayKind.RemoveConfirmation => "Remove game",
        GamepadOverlayKind.CoverDesktopHandoff => "Set cover",
        GamepadOverlayKind.Scraper => "Scrape with ScreenScraper",
        GamepadOverlayKind.SystemMenu => "Menu",
        GamepadOverlayKind.Settings => "Settings",
        GamepadOverlayKind.DesktopModeConfirmation => "Switch to Desktop mode?",
        GamepadOverlayKind.QuitConfirmation => "Quit EmuShelf?",
        _ => string.Empty,
    };
    public string GamepadOverlayHelpText => GamepadOverlay switch
    {
        GamepadOverlayKind.Achievements => "D-pad Browse   X Refresh   B Back",
        GamepadOverlayKind.Settings => "LB/RB Sections   D-pad Rows   A Select   B Cancel",
        GamepadOverlayKind.Search => "Steam + X Keyboard   B Back",
        GamepadOverlayKind.Rename => "A Save   B Back",
        GamepadOverlayKind.Scraper => "D-pad Move   A Select   B Back",
        _ => "D-pad Choose   A Select   B Back",
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

    // The view reports a gamepad-grid fault here (e.g. a focused row's container did not realize after
    // several attempts) so a Deck run leaves a warning in Logs/EmuShelf-*.log without per-move noise.
    internal void LogGamepadGridFault(string detail) => _logger.Warning($"Gamepad grid: {detail}");

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

    /// <summary>Lets the toast mark a failure without the text having to say "failed".</summary>
    public bool IsStatusError => StatusSeverity == StatusSeverity.Error;
    public bool IsStatusProgress => StatusSeverity == StatusSeverity.Progress;
    public bool IsStatusInfo => StatusSeverity == StatusSeverity.Info;
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
    public string LibraryShortName => CurrentLibraryScope switch
    {
        LibraryScope.AllGames => "ALL",
        LibraryScope.RecentlyAdded => "NEW",
        LibraryScope.RecentlyPlayed => "PLAYED",
        _ => SelectedSystem?.ShortName ?? "LIB",
    };
    public string LibraryAccentColor => SelectedSystem?.AccentColor ?? "#E04B52";
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
    /// <summary>Design-time / fallback constructor. The real app injects services.</summary>
    private readonly CloudSaveSyncCoordinator? _cloudSaveSync;
    private readonly IGameSaveSyncService? _gameSaveSync;
    private readonly TexturePackCoordinator? _texturePacks;

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
        IRemoteArtworkDownloader? artworkDownloader = null,
        ISettingsService? settingsService = null,
        IOnScreenKeyboardService? onScreenKeyboard = null,
        IGameDetailsStore? gameDetails = null)
    {
        _libraryViewState = libraryViewState ?? new NullLibraryViewStateService();
        _screenScraperAccount = screenScraperAccount;
        _screenScraperPreview = screenScraperPreview;
        _scrapeApply = scrapeApply;
        _artworkDownloader = artworkDownloader;
        _settingsService = settingsService;
        _library = library;
        _scanner = scanner;
        _importRules = importRules;
        _availabilityChecker = availabilityChecker;
        _dialogs = dialogs;
        _launchService = launchService ?? new NullEmulatorLaunchService();
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
                if (!IsGamepadMode)
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
        // Assigned after the timer exists: a persisted "on" fires OnAmbientThemeFromArtworkChanged.
        AmbientThemeFromArtwork = _themeService.AmbientFromArtwork;

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

        // Keep the gamepad row projection in lockstep with Games no matter how Games is changed
        // (reload, filter, or a direct test mutation), so the virtualized row grid never goes stale.
        Games.CollectionChanged += (_, _) => BuildGamepadRows();

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
            // Couch-only preference, independent of the desktop grid/list choice above.
            IsGamepadSpotlightView = state.GamepadSpotlightView;
            SortColumn = Enum.TryParse<LibrarySortColumn>(state.SortColumn, out var column)
                ? column
                : LibrarySortColumn.Title;
            SortDescending = state.SortDescending;
            IsNavigationCollapsed = state.IsNavigationCollapsed;

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
                {
                    CurrentLibraryScope = LibraryScope.AllGames;
                    _selectedSystemLoad = ReloadGamesAsync();
                }
            }
            else
            {
                CurrentLibraryScope = scope;
                _selectedSystemLoad = ReloadGamesAsync();
            }
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
        GamepadSpotlightView = IsGamepadSpotlightView,
        SortColumn = SortColumn.ToString(),
        SortDescending = SortDescending,
        IsNavigationCollapsed = IsNavigationCollapsed,
        ShowEmptyPlatforms = ShowEmptyPlatforms,
        Scope = CurrentLibraryScope.ToString(),
        SelectedSystemId = SelectedSystem?.Id,
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
        }

        UpdateGamepadPlatformState();
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
        if (value is not null)
            CurrentLibraryScope = LibraryScope.System;
        NotifyLibraryPresentationChanged();
        UpdateGamepadPlatformState();
        ScheduleLibraryViewStateSave();
        // Debounced: the rail highlight and title above move immediately, but the heavy grid reload is
        // coalesced so holding/tapping LB/RB does not rebuild the library on every press.
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
        ScheduleStatusDismiss();
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
    // both ends. Collections and Recently Added are not platforms; they live in the Start menu and
    // the Collections overlay respectively, so they are not stops on this cycle.
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
            OpenGamepadOverlay(GamepadOverlayKind.CoverDesktopHandoff);
            return Task.CompletedTask;
        }

        return SetGameCoverAsync(FocusedGame);
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

    [RelayCommand]
    private void OpenFocusedGameActions()
    {
        if (FocusedGame is not null)
            OpenGamepadOverlay(GamepadOverlayKind.Actions);
    }

    [RelayCommand]
    private void CloseFocusedGameActions() => CloseGamepadOverlay();

    [RelayCommand]
    private void OpenGamepadSearch() => OpenGamepadOverlay(GamepadOverlayKind.Search);

    [RelayCommand]
    private void OpenGamepadCollections() => OpenGamepadOverlay(GamepadOverlayKind.Collections);

    [RelayCommand]
    private void OpenGamepadMenu()
    {
        if (IsGamepadSystemMenuOpen)
            CloseGamepadOverlay();
        else
            OpenGamepadOverlay(GamepadOverlayKind.SystemMenu);
    }

    [RelayCommand]
    private void RequestDesktopModeFromGamepad() =>
        OpenGamepadOverlay(GamepadOverlayKind.DesktopModeConfirmation);

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
                settings, _onScreenKeyboard, ThemeChoices, SetThemeAsync);
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
    private void FocusGamepadAchievement(AchievementRowViewModel? achievement)
    {
        if (IsGamepadAchievementsOpen && achievement is not null &&
            GamepadAchievementDetails?.VisibleAchievements.Contains(achievement) == true)
        {
            FocusedGamepadAchievement = achievement;
        }
    }

    [RelayCommand]
    private void ActivateGamepadOverlay()
    {
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
        if (closingOverlay == GamepadOverlayKind.Settings)
            CloseGamepadSettingsProjection();
        FocusedGamepadAchievement = null;
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
            GamepadOverlayKind.CoverDesktopHandoff => GamepadOverlayKind.Actions,
            GamepadOverlayKind.DesktopModeConfirmation or
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
    private async Task SwitchToDesktopForCoverAsync()
    {
        CloseGamepadOverlay();
        await SetInterfaceModeAsync(InterfaceMode.Desktop);
        SetStatus("Cover selection is available in Desktop mode.");
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

        if (IsGamepadSettingsOpen && GamepadSettings is { } settings)
            return settings.Dispatch(action);

        if (GamepadOverlayOwnsTextInput)
            return DispatchTextOverlayAction(action);

        if (IsGamepadScraperOpen)
            return DispatchScraperOverlayAction(action);

        return HasGamepadOverlay
            ? DispatchOverlayAction(action)
            : DispatchLibraryAction(action);
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
            // The spotlight is a single-column list: Up/Down step one game. Left/Right instead move the
            // hero action ring — Left arms the Achievements widget (only when the game has a set),
            // Right arms Play. The cover grid keeps its 2-D movement (Up/Down span a full row).
            case GamepadAction.NavigateLeft:
                if (IsGamepadSpotlightView)
                {
                    if (FocusedGame?.ShowAchievementMark == true)
                        IsSpotlightAchievementsFocused = true;
                }
                else
                    MoveGamepadFocusLeftCommand.Execute(null);
                return true;
            case GamepadAction.NavigateRight:
                if (IsGamepadSpotlightView)
                    IsSpotlightAchievementsFocused = false;
                else
                    MoveGamepadFocusRightCommand.Execute(null);
                return true;
            case GamepadAction.NavigateUp:
                if (IsGamepadSpotlightView)
                    FocusPreviousGameCommand.Execute(null);
                else
                    MoveGamepadFocusUpCommand.Execute(null);
                return true;
            case GamepadAction.NavigateDown:
                if (IsGamepadSpotlightView)
                    FocusNextGameCommand.Execute(null);
                else
                    MoveGamepadFocusDownCommand.Execute(null);
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
        FocusedGamepadAchievement = null;
        GamepadOverlayOptions.Clear();
        GamepadOverlay = overlay;
        IsGameActionsOpen = overlay == GamepadOverlayKind.Actions; // compatibility for existing bindings/tests

        switch (overlay)
        {
            case GamepadOverlayKind.Actions:
                AddGameActions();
                break;
            case GamepadOverlayKind.Search:
                break;
            case GamepadOverlayKind.Collections:
                AddOption("Recently Played", ShowGamepadRecentlyPlayedCommand);
                AddOption("Recently Added", ShowGamepadRecentlyAddedCommand);
                break;
            case GamepadOverlayKind.Rename:
                break;
            case GamepadOverlayKind.DiscSelection:
                AddDiscSelectionOptions();
                break;
            case GamepadOverlayKind.RemoveConfirmation:
                AddOption("Remove from library", ConfirmGamepadRemoveCommand, true);
                break;
            case GamepadOverlayKind.CoverDesktopHandoff:
                AddOption("Continue to Desktop mode", RequestDesktopModeFromGamepadCommand);
                break;
            case GamepadOverlayKind.Achievements:
                FocusFirstAchievement();
                break;
            case GamepadOverlayKind.Scraper:
                // The scraper overlay renders its own body and owns its D-pad focus; no option list.
                break;
            case GamepadOverlayKind.SystemMenu:
                AddOption("Search", OpenGamepadSearchCommand);
                AddOption("Collections", OpenGamepadCollectionsCommand);
                AddOption(
                    IsGamepadSpotlightView ? "Cover grid view" : "Spotlight view",
                    ToggleGamepadViewFromMenuCommand);
                AddOption("Settings", RequestSettingsFromGamepadCommand);
                AddOption("Switch to Desktop mode", RequestDesktopModeFromGamepadCommand);
                AddOption("Quit EmuShelf", RequestQuitFromGamepadCommand, true);
                break;
            case GamepadOverlayKind.Settings:
                break;
            case GamepadOverlayKind.DesktopModeConfirmation:
                AddOption("Switch to Desktop mode", SwitchToDesktopModeCommand);
                break;
            case GamepadOverlayKind.QuitConfirmation:
                AddOption("Quit EmuShelf", ConfirmQuitGamepadCommand, true);
                break;
        }

        GamepadOverlaySelectionIndex = overlay == GamepadOverlayKind.DiscSelection && FocusedGame is { } selectedGame
            ? Math.Max(0, selectedGame.Discs.ToList().FindIndex(disc => disc.Game.Id == selectedGame.LaunchModel.Id))
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

    private void AddOption(string label, ICommand command, bool isDestructive = false) =>
        GamepadOverlayOptions.Add(new GamepadOverlayOptionViewModel(label, command, isDestructive));

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
        for (var index = 0; index < GamepadOverlayOptions.Count; index++)
            GamepadOverlayOptions[index].IsFocused = index == GamepadOverlaySelectionIndex;
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

    private void NotifyGamepadOverlayState()
    {
        OnPropertyChanged(nameof(HasGamepadOverlay));
        OnPropertyChanged(nameof(GamepadOverlayOwnsTextInput));
        OnPropertyChanged(nameof(IsGamepadAchievementsOpen));
        OnPropertyChanged(nameof(IsGamepadSearchOpen));
        OnPropertyChanged(nameof(IsGamepadCollectionsOpen));
        OnPropertyChanged(nameof(IsGamepadRenameOpen));
        OnPropertyChanged(nameof(IsGamepadDiscSelectionOpen));
        OnPropertyChanged(nameof(IsGamepadRemoveOpen));
        OnPropertyChanged(nameof(IsGamepadCoverHandoffOpen));
        OnPropertyChanged(nameof(IsGamepadScraperOpen));
        OnPropertyChanged(nameof(IsGamepadSystemMenuOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsTextEntryOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsConfirmationOpen));
        OnPropertyChanged(nameof(GamepadSettingsFocusRevision));
        OnPropertyChanged(nameof(IsGamepadDesktopModeConfirmationOpen));
        OnPropertyChanged(nameof(IsGamepadQuitConfirmationOpen));
        OnPropertyChanged(nameof(AreGamepadOverlayOptionsTopAligned));
        OnPropertyChanged(nameof(UsesGamepadDefaultOverlayHints));
        OnPropertyChanged(nameof(ShowsGamepadOverlayOptions));
        OnPropertyChanged(nameof(ShowsGamepadOverlayChromeTitle));
        OnPropertyChanged(nameof(GamepadOverlayTitle));
        OnPropertyChanged(nameof(GamepadOverlayHelpText));
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

    [RelayCommand]
    private async Task ShowGamepadRecentlyAddedAsync()
    {
        await ShowRecentlyAddedAsync();
        CloseGamepadOverlay();
    }

    [RelayCommand]
    private async Task ShowGamepadRecentlyPlayedAsync()
    {
        await ShowRecentlyPlayedAsync();
        CloseGamepadOverlay();
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
        OnPropertyChanged(nameof(LibraryShortName));
        OnPropertyChanged(nameof(LibraryAccentColor));
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

    partial void OnSelectedGameChanged(GameViewModel? oldValue, GameViewModel? newValue)
    {
    }

    partial void OnFocusedGameChanged(GameViewModel? oldValue, GameViewModel? newValue)
    {
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
            if (!game.AreSpotlightDetailsLoaded && _gameDetails is not null)
            {
                var resolved = await Task.Run(() => ResolveSpotlightDetails(game.Id));
                if (generation != _spotlightHeroGeneration)
                    return; // focus moved on while the details were being read
                game.ApplySpotlightDetails(resolved.FanartPath, resolved.WheelPath, resolved.RatingText, resolved.InfoLine);
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

    private (string? FanartPath, string? WheelPath, string? RatingText, string? InfoLine) ResolveSpotlightDetails(long gameId)
    {
        if (_gameDetails is null)
            return (null, null, null, null);

        var details = _gameDetails.GetDetails(gameId);
        return (
            SelectSpotlightMedia(details.Media, GameMediaKind.Fanart),
            SelectSpotlightMedia(details.Media, GameMediaKind.Wheel),
            FormatSpotlightRating(details.Metadata),
            ComposeSpotlightInfo(details.Metadata));
    }

    // The spotlight hero's info line: genre · year · players · developer · publisher, from the scraped
    // metadata, joined with the parts that are present. The filename is appended by the view model.
    internal static string? ComposeSpotlightInfo(IReadOnlyList<GameMetadataValue> metadata)
    {
        string? Field(GameMetadataField field) =>
            metadata.FirstOrDefault(value => value.Field == field)?.Value is { Length: > 0 } v ? v : null;

        var year = Field(GameMetadataField.ReleaseDate) is { } date && date.Length >= 4 ? date[..4] : null;
        var players = Field(GameMetadataField.Players) is { } count ? $"{count}P" : null;

        var parts = new[]
        {
            Field(GameMetadataField.Genre),
            year,
            players,
            Field(GameMetadataField.Developer),
            Field(GameMetadataField.Publisher),
        }.Where(part => !string.IsNullOrWhiteSpace(part));

        var line = string.Join("  ·  ", parts);
        return line.Length == 0 ? null : line;
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
            SelectedGame = game;
        }
        else if (toggle)
        {
            game.IsSelected = !game.IsSelected;
            _selectionAnchor = game;
            SelectedGame = game.IsSelected ? game : Games.FirstOrDefault(candidate => candidate.IsSelected);
        }
        else
        {
            DeselectAllGames();
            game.IsSelected = true;
            _selectionAnchor = game;
            SelectedGame = game;
        }

        // Shift without an existing anchor behaves like an ordinary click and establishes one.
        if (_selectionAnchor is null || !Games.Contains(_selectionAnchor))
            _selectionAnchor = game;
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
        SelectedGame = _selectionAnchor;
        NotifySelectionChanged();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        DeselectAllGames();

        _selectionAnchor = null;
        SelectedGame = null;
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
        var removalText = SelectionRemovalText;
        var canScrapeSelection = SelectedGameCount > 1;
        var scrapeText = $"Scrape {SelectedGameCount} selected with ScreenScraper…";
        foreach (var game in _systemGames.Concat(Games).Distinct())
        {
            game.SelectionRemovalText = removalText;
            game.SelectionScrapeText = scrapeText;
            game.CanScrapeSelection = canScrapeSelection;
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
            // Cancel any slow reload still in flight so it cannot land after us and overwrite the
            // scope we just switched to.
            ++_loadGeneration;
            _systemGames.Clear();
            _systemGames.AddRange(cachedGames);
            UpdateCoverLayout(applyVisibleShelf: false);
            ApplyFilter();
            _displayedScopeKey = scopeKey;
            IsLibraryLoading = false;
            return;
        }

        var generation = ++_loadGeneration;

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
                        RemoveGameCommand,
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
                        ScrapeGameCommand);
                    viewModels.Add(viewModel);
                }

                ApplyAchievementDisplays(viewModels);
                ApplyTexturePackDisplays(viewModels);
                ApplySpotlightTitles(viewModels);
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

    // Overlays the scraped canonical title onto each game for spotlight display, in one bulk read.
    // Runs on the build worker before the view models are bound, so no cross-thread notify occurs.
    private void ApplySpotlightTitles(IReadOnlyList<GameViewModel> viewModels)
    {
        if (_metadataStore is null || viewModels.Count == 0)
            return;

        try
        {
            var titles = _metadataStore.GetProviderTitles();
            if (titles.Count == 0)
                return;

            foreach (var viewModel in viewModels)
                if (titles.TryGetValue(viewModel.Id, out var canonical))
                    viewModel.ApplySpotlightTitle(canonical);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not load canonical titles for the spotlight list: {ex.Message}");
        }
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
                SetStatus($"Could not load cover for {game.Title}: {ex.Message}", StatusSeverity.Error);
            }
        }
        finally
        {
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
            LibrarySortColumn.Textures => By(g => g.TextureSortKey),
            LibrarySortColumn.Status => By(g => g.AvailabilityText, text),
            _ => By(g => g.Title, text),
        };
        // Title is the stable secondary key so equal rows keep a deterministic order.
        return ordered.ThenBy(g => g.Title, text);
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
                g.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

        Games.ReplaceAll(SortGames(filtered));
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

    private async Task<string> RescanSystemFromSettingsAsync(string systemId)
    {
        var system = Systems.FirstOrDefault(candidate => candidate.Id == systemId);
        if (system is null)
            return "That console is no longer available.";

        if (system.Id == "playstation3")
            return "Use Sync RPCS3 library to refresh PlayStation 3 games.";

        await RescanAsync([system], SelectedSystem);
        return StatusText;
    }

    private async Task<string> RescanAllFromSettingsAsync()
    {
        await RescanAsync(NonRpcs3Systems(), SelectedSystem);
        return StatusText;
    }

    private IReadOnlyList<LibraryFolder> GetLibraryFoldersForSettings(string systemId) =>
        _library.GetLibraryFolders(systemId);

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
            return StatusText;
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

    private async Task RescanAsync(IEnumerable<GameSystem> systems, GameSystem? systemToShow)
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
                foreach (var folder in folders)
                {
                    var progress = new Progress<ScanProgress>(p =>
                        SetStatus($"Rescanning {system.Name}… {p.CandidatesFound} found", StatusSeverity.Progress));
                    var selection = await _scanner.ScanAsync(folder.Path, system, progress);
                    var importResult = await ReconcileImportAsync(system, selection);
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

        await RememberSelectedDiscAsync(game, disc);
        SetStatus($"Disc {disc.Number} selected for {game.Title}");
    }

    private async Task LaunchGameCoreAsync(GameViewModel? game)
    {
        if (game is null || IsBusy)
            return;

        var launchDisc = game.Discs.FirstOrDefault(disc => disc.Game.Id == game.LaunchModel.Id);
        if (launchDisc is null)
            return;

        var launchGame = launchDisc.Game;
        if (!launchGame.IsAvailable)
        {
            SetStatus(
                launchGame.IsAvailable ? game.UnavailableLaunchStatus :
                    $"Cannot launch Disc {launchDisc.Number} of {game.Title}: its game file could not be found.",
                StatusSeverity.Error);
            return;
        }

        IsBusy = true;
        SetStatus($"Launching {game.Title}…", StatusSeverity.Progress);
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
                async cancellationToken =>
                {
                    beforeSync = await SyncSavesForLaunchAsync(
                        launchGame,
                        afterExit: false,
                        cancellationToken);
                    SetStatus(
                        beforeSync?.Status == CloudSaveSyncStatus.Failed
                            ? $"Save sync incomplete; launching {game.Title} with the saves currently on disk…"
                            : $"Launching {game.Title}…",
                        StatusSeverity.Progress);
                    // This callback runs only after preflight passes and immediately before the
                    // emulator process starts, so a game whose launch fails validation is never
                    // recorded, and one that starts is recorded even if EmuShelf is killed mid-session.
                    await Task.Run(
                        () => _library.SetLastPlayed(launchGame.Id, DateTimeOffset.UtcNow),
                        cancellationToken);
                    recordedPlay = true;
                });
            if (!result.Succeeded)
                _logger.Warning($"Launch did not start or complete successfully: {result.StatusText}");
            if (result.ProcessExited && game.RetroAchievementsGameId is { } retroAchievementsGameId)
                _ = RefreshRetroAchievementsAfterTrackedExitAsync(retroAchievementsGameId);

            CloudSaveSyncOutcome? afterSync = null;
            if (result.ProcessExited)
                afterSync = await SyncSavesForLaunchAsync(
                    launchGame,
                    afterExit: true,
                    CancellationToken.None);
            SetStatus(
                DescribeLaunchAndSaveSync(result, beforeSync, afterSync),
                result.Succeeded ? StatusSeverity.Info : StatusSeverity.Error);
        }
        catch (OperationCanceledException)
        {
            SetStatus($"Launch cancelled for {game.Title}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected launch failure for game id {game.Id}.", ex);
            SetStatus($"Could not launch {game.Title}: {ex.Message}", StatusSeverity.Error);
        }
        finally
        {
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
        bool afterExit,
        CancellationToken cancellationToken)
    {
        if (_gameSaveSync?.CanSyncSystem(game.SystemId) != true)
            return null;

        SetStatus(
            afterExit
                ? $"{game.Title} finished. Syncing saves…"
                : $"Syncing saves before launching {game.Title}…",
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
        if (syncParts.Count == 0)
            return launch.StatusText;

        return launch.StatusText.TrimEnd('.', ' ') + ". " + string.Join(". ", syncParts) + ".";
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
        return "save sync after exit: " + string.Join(", ", parts);
    }

    private static string DescribeConflicts(int count, string? context) =>
        $"{count} conflict{(count == 1 ? "" : "s")} resolved" +
        (context is null ? "" : $" {context}") +
        " (older copy backed up)";

    public void NotifyGamepadPointerInput()
    {
        if (IsGamepadMode)
            IsGamepadControllerInputActive = false;
    }

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
                    game.Title,
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
                SetStatus($"Could not open achievements for {game.Title}: {ex.Message}", StatusSeverity.Error);
                return;
            }
        }

        try
        {
            await _dialogs.ShowAchievementDetailsAsync(game.Title, retroAchievementsGameId);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not open achievements for game id {game.Id}.", ex);
            SetStatus($"Could not open achievements for {game.Title}: {ex.Message}", StatusSeverity.Error);
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
            SetStatus($"Could not rename {game.Title}: {ex.Message}", StatusSeverity.Error);
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

    [RelayCommand]
    private async Task SetGameCoverAsync(GameViewModel? game)
    {
        if (game is null || IsBusy)
            return;

        var preferredAspectRatio = _systemsById.TryGetValue(game.SystemId, out var system)
            ? system.CoverAspectRatio
            : game.CoverAspectRatio;
        var pickedCover = await _dialogs.PickGameCoverAsync(new GameCoverPickerContext(
            game.Title,
            game.SystemName,
            preferredAspectRatio));
        if (pickedCover is null)
            return;

        IsBusy = true;
        SetStatus($"Preparing cover for {game.Title}…", StatusSeverity.Progress);
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
                    ? $"Updated cover for {game.Title}"
                    : $"Updated cover for {game.Title}, but {string.Join("; ", warnings)}",
                warnings.Count == 0 ? StatusSeverity.Info : StatusSeverity.Error);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not set a cover for game id {game.Id}.", ex);
            SetStatus($"Could not set cover for {game.Title}: {ex.Message}", StatusSeverity.Error);
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
            !await _dialogs.ConfirmRemoveGameAsync(game.Title))
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
            SetStatus($"Removed {game.Title} from the library — game files were not touched");
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not remove game id {game.Id} from the library.", ex);
            SetStatus($"Could not remove {game.Title}: {ex.Message}", StatusSeverity.Error);
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
                SetAmbientThemeFromArtworkAsync);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not open emulator settings.", ex);
            SetStatus($"Could not open emulator settings: {ex.Message}", StatusSeverity.Error);
        }
    }

    private async Task<EmulatorSettingsViewModel> CreateSettingsViewModelAsync()
    {
        var configured = await Task.Run(() =>
            _emulatorConfigurations.GetAll(Systems.Select(system => system.Id)));
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
            themeChoices: ThemeChoices,
            ambientThemeFromArtwork: AmbientThemeFromArtwork,
            setAmbientThemeFromArtwork: SetAmbientThemeFromArtworkAsync);
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
            ForgetLibraryFolderFromSettingsAsync));

    private RetroAchievementsSettingsContext? CreateRetroAchievementsSettingsContext() =>
        _retroAccount is null
            ? null
            : new RetroAchievementsSettingsContext(
                _retroAccount.Account,
                _retroAccount.IsConnected,
                ConnectRetroAchievementsAsync,
                DisconnectRetroAchievementsAsync,
                RefreshRetroAchievementsMatchesAsync);

    private TexturePackSettingsContext? CreateTexturePackSettingsContext() =>
        // Titles come from the whole library, not the visible collection: a Dolphin pack must
        // still name the GameCube game it matched while the user is viewing PS1.
        _texturePacks?.CreateSettingsContext(
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
            nameof(GamepadSettingsViewModel.IsConfirmChoiceSelected) or
            nameof(GamepadSettingsViewModel.TextEntryRevision))
        {
            OnPropertyChanged(nameof(GamepadSettingsFocusRevision));
            OnPropertyChanged(nameof(IsGamepadSettingsTextEntryOpen));
            OnPropertyChanged(nameof(IsGamepadSettingsConfirmationOpen));
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
