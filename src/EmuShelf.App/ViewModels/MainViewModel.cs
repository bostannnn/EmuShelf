using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
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
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
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
    private static readonly TimeSpan GamepadReturnInputGuard = TimeSpan.FromMilliseconds(500);

    private readonly IGameLibrary _library;
    private readonly IFolderScanner _scanner;
    private readonly IGameImportRules _importRules;
    private readonly IAvailabilityChecker _availabilityChecker;
    private readonly IDialogService _dialogs;
    private readonly IEmulatorLaunchService _launchService;
    private readonly IEmulatorConfigurationStore _emulatorConfigurations;
    private readonly IReadOnlyList<EmulatorDefinition> _emulators;
    private readonly IGameCoverService _covers;
    private readonly IAppThemeService _themeService;
    private readonly IInterfaceModeService? _interfaceModeService;
    private readonly IApplicationLifetimeService? _applicationLifetime;
    private readonly IGameMetadataService _metadataService;
    private readonly IGameMetadataStore? _metadataStore;
    private readonly IMetadataPreferencesService _metadataPreferences;
    private readonly IRetroAchievementsIdentificationService? _retroAchievements;
    private readonly IRetroAchievementsReadStore? _retroAchievementsRead;
    private readonly IRetroAchievementsAccountService? _retroAccount;
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
    private readonly List<GameViewModel> _systemGames = [];
    private readonly HashSet<long> _deferredCoverLoads = [];
    private GameViewModel? _selectionAnchor;
    private bool _isFrontendSuspended;
    private DateTimeOffset _gamepadInputGuardUntil;
    private string _appliedSearchText = string.Empty;
    private readonly Dictionary<string, long> _focusedGameByScope = new(StringComparer.Ordinal);

    // Bumped on every reload so a slow load that finishes after a newer one is discarded,
    // keeping the shown games in sync with the current selection.
    private int _loadGeneration;
    private Task _selectedSystemLoad = Task.CompletedTask;

    public ObservableCollection<GameSystem> Systems { get; }
    public ObservableCollection<GamepadPlatformTabViewModel> GamepadPlatforms { get; }
    public BulkObservableCollection<GameViewModel> Games { get; } = [];
    public ObservableCollection<GamepadOverlayOptionViewModel> GamepadOverlayOptions { get; } = [];

    [ObservableProperty]
    public partial GameSystem? SelectedSystem { get; set; }

    [ObservableProperty]
    public partial LibraryScope CurrentLibraryScope { get; set; } = LibraryScope.System;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsGridView { get; set; } = true;

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

    partial void OnSortColumnChanged(LibrarySortColumn value) => NotifySortGlyphs();
    partial void OnSortDescendingChanged(bool value) => NotifySortGlyphs();

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

    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    [ObservableProperty]
    public partial string LibraryCountText { get; set; } = "0 games";

    /// <summary>True when the current filter yields at least one game (drives the views).</summary>
    [ObservableProperty]
    public partial bool HasGames { get; set; }

    /// <summary>True only when the selected system has no games at all — drives the "add your first game" prompt.</summary>
    [ObservableProperty]
    public partial bool IsLibraryEmpty { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSearchEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial ThemePreference CurrentTheme { get; set; }

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

    [ObservableProperty]
    public partial int GamepadOverlaySelectionIndex { get; set; }

    [ObservableProperty]
    public partial AchievementDetailsViewModel? GamepadAchievementDetails { get; set; }

    [ObservableProperty]
    public partial AchievementRowViewModel? FocusedGamepadAchievement { get; set; }

    public bool HasGamepadOverlay => GamepadOverlay != GamepadOverlayKind.None;
    public bool GamepadOverlayOwnsTextInput => GamepadOverlay is GamepadOverlayKind.Search or GamepadOverlayKind.Rename;
    public bool IsGamepadAchievementsOpen => GamepadOverlay == GamepadOverlayKind.Achievements;
    public bool IsGamepadSearchOpen => GamepadOverlay == GamepadOverlayKind.Search;
    public bool IsGamepadCollectionsOpen => GamepadOverlay == GamepadOverlayKind.Collections;
    public bool IsGamepadRenameOpen => GamepadOverlay == GamepadOverlayKind.Rename;
    public bool IsGamepadDiscSelectionOpen => GamepadOverlay == GamepadOverlayKind.DiscSelection;
    public bool IsGamepadRemoveOpen => GamepadOverlay == GamepadOverlayKind.RemoveConfirmation;
    public bool IsGamepadCoverHandoffOpen => GamepadOverlay == GamepadOverlayKind.CoverDesktopHandoff;
    public bool IsGamepadSystemMenuOpen => GamepadOverlay == GamepadOverlayKind.SystemMenu;
    public bool IsGamepadDesktopModeConfirmationOpen => GamepadOverlay == GamepadOverlayKind.DesktopModeConfirmation;
    public bool IsGamepadSettingsHandoffOpen => GamepadOverlay == GamepadOverlayKind.SettingsDesktopHandoff;
    public bool IsGamepadQuitConfirmationOpen => GamepadOverlay == GamepadOverlayKind.QuitConfirmation;
    public bool AreGamepadOverlayOptionsTopAligned => GamepadOverlay is
        GamepadOverlayKind.Actions or GamepadOverlayKind.Collections or
        GamepadOverlayKind.DiscSelection or GamepadOverlayKind.SystemMenu;
    public bool IsGamepadAllGamesRailFocused => IsGamepadRailFocused && GamepadRailIndex == 0;
    public bool IsGamepadCollectionsRailFocused => IsGamepadRailFocused && GamepadRailIndex == 1;
    public string GamepadOverlayTitle => GamepadOverlay switch
    {
        GamepadOverlayKind.Actions => FocusedGame is null ? "Game actions" : $"{FocusedGame.Title} actions",
        GamepadOverlayKind.Search => "Search",
        GamepadOverlayKind.Collections => "Collections",
        GamepadOverlayKind.Rename => "Rename game",
        GamepadOverlayKind.DiscSelection => FocusedGame is null ? "Select disc" : $"{FocusedGame.Title} — select disc",
        GamepadOverlayKind.RemoveConfirmation => "Remove game",
        GamepadOverlayKind.CoverDesktopHandoff => "Set cover",
        GamepadOverlayKind.SystemMenu => "Menu",
        GamepadOverlayKind.DesktopModeConfirmation => "Switch to Desktop mode?",
        GamepadOverlayKind.SettingsDesktopHandoff => "Open Settings?",
        GamepadOverlayKind.QuitConfirmation => "Quit EmuShelf?",
        _ => string.Empty,
    };
    public string GamepadOverlayHelpText => GamepadOverlay switch
    {
        GamepadOverlayKind.Achievements => "D-pad Browse   X Refresh   B Back",
        GamepadOverlayKind.Search => "Steam + X Keyboard   B Back",
        GamepadOverlayKind.Rename => "A Save   B Back",
        _ => "D-pad Choose   A Select   B Back",
    };

    [ObservableProperty]
    public partial bool IsGamepadRailFocused { get; set; }

    /// <summary>Logical rail cursor: All Games, Collections, then each platform tab.</summary>
    [ObservableProperty]
    public partial int GamepadRailIndex { get; set; }

    [ObservableProperty]
    public partial double GamepadViewportWidth { get; set; }

    public int GamepadColumnCount { get; private set; } = 1;

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
    }

    // Grid cover sizing: covers grow from a 188px floor up to a cap so a whole number of columns
    // fills the library width (no lopsided right gutter) as the window or sidebar resizes.
    private const double MinCoverWidth = 188;
    private const double MaxCoverWidth = 232;
    private const double CoverColumnSpacing = 28;    // matches UniformGridLayout MinColumnSpacing
    private const double GridHorizontalPadding = 60; // ItemsRepeater Margin left(32) + right(28)

    /// <summary>Current width of the library grid area; the cover width is derived from it.</summary>
    [ObservableProperty]
    public partial double LibraryViewportWidth { get; set; }

    /// <summary>Cover width computed for the current viewport. The grid layout uses it as the
    /// uniform cell width (MinItemWidth) so a whole number of columns fills the row.</summary>
    [ObservableProperty]
    public partial double GridCoverWidth { get; set; }

    partial void OnLibraryViewportWidthChanged(double value) => UpdateCoverLayout();

    public bool IsSystemTheme => CurrentTheme == ThemePreference.System;
    public bool IsLightTheme => CurrentTheme == ThemePreference.Light;
    public bool IsDarkTheme => CurrentTheme == ThemePreference.Dark;
    public bool IsAllGamesSelected => CurrentLibraryScope == LibraryScope.AllGames;
    public bool IsRecentlyAddedSelected => CurrentLibraryScope == LibraryScope.RecentlyAdded;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusText);
    /// <summary>
    /// True while an emulator owns the game session, plus a short return guard that absorbs the
    /// controller/key used to close it. Input services poll this directly, so it remains accurate
    /// without a timer or UI notification.
    /// </summary>
    public bool IsGamepadInputSuspended =>
        _isFrontendSuspended || DateTimeOffset.UtcNow < _gamepadInputGuardUntil;
    public int SelectedGameCount => Games.Count(game => game.IsSelected);
    public bool HasSelectedGames => SelectedGameCount > 0;
    public string LibraryTitle => CurrentLibraryScope switch
    {
        LibraryScope.AllGames => "All Games",
        LibraryScope.RecentlyAdded => "Recently Added",
        _ => SelectedSystem?.Name ?? "Library",
    };
    public string LibraryShortName => CurrentLibraryScope switch
    {
        LibraryScope.AllGames => "ALL",
        LibraryScope.RecentlyAdded => "NEW",
        _ => SelectedSystem?.ShortName ?? "LIB",
    };
    public string LibraryAccentColor => SelectedSystem?.AccentColor ?? "#E04B52";
    public string EmptyLibraryTitle => CurrentLibraryScope switch
    {
        LibraryScope.AllGames => "Your game library is empty",
        LibraryScope.RecentlyAdded => "No recently added games",
        _ => $"Your {SelectedSystem?.Name ?? "game"} library is empty",
    };
    public string EmptyLibraryDescription => CurrentLibraryScope == LibraryScope.RecentlyAdded
        ? "Games you import will appear here in newest-first order."
        : SelectedSystem?.Id == "playstation3"
            ? "Sync the explicitly selected RPCS3 library from Settings to add PlayStation 3 games."
        : "Add game files or a dedicated folder to begin building this shelf.";
    public string ThemeDescription => CurrentTheme switch
    {
        ThemePreference.Light => "Light appearance",
        ThemePreference.Dark => "Dark appearance",
        _ => "Follow system appearance",
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
        TexturePackCoordinator? texturePacks = null)
    {
        _library = library;
        _scanner = scanner;
        _importRules = importRules;
        _availabilityChecker = availabilityChecker;
        _dialogs = dialogs;
        _launchService = launchService ?? new NullEmulatorLaunchService();
        _emulatorConfigurations = emulatorConfigurations ?? new NullEmulatorConfigurationStore();
        _emulators = emulators ?? KnownEmulators.All;
        _covers = covers ?? new NullGameCoverService();
        _themeService = themeService ?? new NullAppThemeService();
        _interfaceModeService = interfaceModeService;
        _applicationLifetime = applicationLifetime;
        IsGamepadMode = interfaceModeService?.Current == InterfaceMode.Gamepad;
        if (_interfaceModeService is not null)
        {
            _interfaceModeService.ModeChanged += (_, mode) =>
            {
                IsGamepadMode = mode == InterfaceMode.Gamepad;
                if (IsGamepadMode)
                    IsGridView = true;
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
        _retroRefresh = retroRefresh;
        _retroBadges = retroBadges;
        _cloudSaveSync = cloudSaveSync;
        _texturePacks = texturePacks;
        _gameSaveSync = gameSaveSync ?? cloudSaveSync;
        _logger = logger ?? NullAppLogger.Instance;
        CurrentTheme = _themeService.Current;

        Systems = new ObservableCollection<GameSystem>(systems);
        GamepadPlatforms = new ObservableCollection<GamepadPlatformTabViewModel>(
            systems.Select(system => new GamepadPlatformTabViewModel(system)));
        _systemsById = systems.ToDictionary(system => system.Id, StringComparer.Ordinal);

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ApplyFilter();
        };

        SelectedSystem = Systems.FirstOrDefault();
    }

    partial void OnSelectedSystemChanged(GameSystem? value)
    {
        if (value is not null)
            CurrentLibraryScope = LibraryScope.System;
        NotifyLibraryPresentationChanged();
        UpdateGamepadPlatformState();
        _selectedSystemLoad = ReloadGamesAsync();
    }

    partial void OnCurrentLibraryScopeChanged(LibraryScope value)
    {
        OnPropertyChanged(nameof(IsAllGamesSelected));
        OnPropertyChanged(nameof(IsRecentlyAddedSelected));
        NotifyLibraryPresentationChanged();
        UpdateGamepadPlatformState();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    partial void OnStatusTextChanged(string value) =>
        OnPropertyChanged(nameof(HasStatusMessage));

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
    private async Task PreviousPlatformAsync() => await MovePlatformAsync(-1);

    [RelayCommand]
    private async Task NextPlatformAsync() => await MovePlatformAsync(1);

    [RelayCommand]
    private void SelectPlatform(GameSystem? system)
    {
        if (system is not null)
            SelectedSystem = system;
    }

    // LB/RB walk the same order the controller rail shows — All Games, Collections, then each
    // system — so Collections is reachable by shoulder buttons instead of being stepped over.
    private async Task MovePlatformAsync(int direction)
    {
        var current = CurrentLibraryScope switch
        {
            LibraryScope.AllGames => 0,
            LibraryScope.RecentlyAdded => 1,
            _ => SelectedSystem is null ? 0 : Systems.IndexOf(SelectedSystem) + 2,
        };
        var target = current + direction;
        if (target < 0 || target > Systems.Count + 1)
            return;

        if (target == 0)
            await ShowCollectionAsync(LibraryScope.AllGames);
        else if (target == 1)
            await ShowCollectionAsync(LibraryScope.RecentlyAdded);
        else
            SelectedSystem = Systems[target - 2];
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
    private void RequestSettingsFromGamepad() =>
        OpenGamepadOverlay(GamepadOverlayKind.SettingsDesktopHandoff);

    [RelayCommand]
    private void RequestQuitFromGamepad() =>
        OpenGamepadOverlay(GamepadOverlayKind.QuitConfirmation);

    [RelayCommand]
    private void MoveGamepadOverlayUp()
    {
        if (IsGamepadAchievementsOpen)
        {
            MoveFocusedAchievement(-1);
            return;
        }

        MoveGamepadOverlaySelection(-1);
    }

    [RelayCommand]
    private void MoveGamepadOverlayDown()
    {
        if (IsGamepadAchievementsOpen)
        {
            MoveFocusedAchievement(1);
            return;
        }

        MoveGamepadOverlaySelection(1);
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
        if (GamepadAchievementDetails is not null)
            GamepadAchievementDetails.Achievements.CollectionChanged -= HandleGamepadAchievementsChanged;
        GamepadAchievementDetails?.Dispose();
        GamepadAchievementDetails = null;
        FocusedGamepadAchievement = null;
        GamepadOverlayOptions.Clear();
        GamepadOverlay = GamepadOverlayKind.None;
        IsGameActionsOpen = false;
        RestoreFocusedGame();
    }

    [RelayCommand]
    private void BackFromGamepadOverlay()
    {
        var returnOverlay = GamepadOverlay switch
        {
            GamepadOverlayKind.Rename or
            GamepadOverlayKind.DiscSelection or
            GamepadOverlayKind.RemoveConfirmation or
            GamepadOverlayKind.CoverDesktopHandoff => GamepadOverlayKind.Actions,
            GamepadOverlayKind.DesktopModeConfirmation or
            GamepadOverlayKind.SettingsDesktopHandoff or
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
        StatusText = "Cover selection is available in Desktop mode.";
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

        if (GamepadOverlayOwnsTextInput)
            return DispatchTextOverlayAction(action);

        return HasGamepadOverlay
            ? DispatchOverlayAction(action)
            : DispatchLibraryAction(action);
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
            case GamepadAction.Search when IsGamepadAchievementsOpen:
                GamepadAchievementDetails?.RefreshCommand.Execute(null);
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
            case GamepadAction.Confirm when IsGamepadRailFocused:
                ActivateGamepadRailCommand.Execute(null);
                return true;
            case GamepadAction.Confirm:
                LaunchFocusedGameCommand.Execute(null);
                return true;
            case GamepadAction.Cancel:
                if (IsGamepadRailFocused)
                {
                    IsGamepadRailFocused = false;
                    RestoreFocusedGame();
                }
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
            case GamepadAction.NavigateLeft:
                MoveGamepadFocusLeftCommand.Execute(null);
                return true;
            case GamepadAction.NavigateRight:
                MoveGamepadFocusRightCommand.Execute(null);
                return true;
            case GamepadAction.NavigateUp:
                MoveGamepadFocusUpCommand.Execute(null);
                return true;
            case GamepadAction.NavigateDown:
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

        GamepadAchievementDetails?.Dispose();
        GamepadAchievementDetails = null;
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
                AddOption("Recently Added", ShowGamepadRecentlyAddedCommand);
                break;
            case GamepadOverlayKind.Rename:
                break;
            case GamepadOverlayKind.DiscSelection:
                AddDiscSelectionOptions();
                break;
            case GamepadOverlayKind.RemoveConfirmation:
                AddOption("Remove from library", ConfirmGamepadRemoveCommand);
                break;
            case GamepadOverlayKind.CoverDesktopHandoff:
                AddOption("Continue to Desktop mode", RequestDesktopModeFromGamepadCommand);
                break;
            case GamepadOverlayKind.Achievements:
                FocusFirstAchievement();
                break;
            case GamepadOverlayKind.SystemMenu:
                AddOption("Search", OpenGamepadSearchCommand);
                AddOption("Collections", OpenGamepadCollectionsCommand);
                AddOption("Settings", RequestSettingsFromGamepadCommand);
                AddOption("Switch to Desktop mode", RequestDesktopModeFromGamepadCommand);
                AddOption("Quit EmuShelf", RequestQuitFromGamepadCommand);
                break;
            case GamepadOverlayKind.DesktopModeConfirmation:
                AddOption("Switch to Desktop mode", SwitchToDesktopModeCommand);
                break;
            case GamepadOverlayKind.SettingsDesktopHandoff:
                AddOption("Open Settings in Desktop mode", OpenSettingsFromGamepadCommand);
                break;
            case GamepadOverlayKind.QuitConfirmation:
                AddOption("Quit EmuShelf", ConfirmQuitGamepadCommand);
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
        AddOption("Remove", RemoveFocusedGameCommand);
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

    private void AddOption(string label, ICommand command) =>
        GamepadOverlayOptions.Add(new GamepadOverlayOptionViewModel(label, command));

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
        FocusedGamepadAchievement = GamepadAchievementDetails?.Achievements.FirstOrDefault();
    }

    private void MoveFocusedAchievement(int delta)
    {
        var rows = GamepadAchievementDetails?.Achievements;
        if (rows is not { Count: > 0 })
            return;
        var index = FocusedGamepadAchievement is null ? 0 : rows.IndexOf(FocusedGamepadAchievement);
        FocusedGamepadAchievement = rows[Math.Clamp(index + delta, 0, rows.Count - 1)];
    }

    private void HandleGamepadAchievementsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsGamepadAchievementsOpen && FocusedGamepadAchievement is null)
            FocusFirstAchievement();
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
        OnPropertyChanged(nameof(IsGamepadSystemMenuOpen));
        OnPropertyChanged(nameof(IsGamepadDesktopModeConfirmationOpen));
        OnPropertyChanged(nameof(IsGamepadSettingsHandoffOpen));
        OnPropertyChanged(nameof(IsGamepadQuitConfirmationOpen));
        OnPropertyChanged(nameof(AreGamepadOverlayOptionsTopAligned));
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
    private async Task OpenSettingsFromGamepadAsync()
    {
        CloseGamepadOverlay();
        await SetInterfaceModeAsync(InterfaceMode.Desktop);
        await OpenSettingsAsync();
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
            await ReloadGamesAsync();
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
        IsGamepadRailFocused = false;
    }

    [RelayCommand]
    private void MoveGamepadFocusLeft()
    {
        if (IsGamepadRailFocused)
            GamepadRailIndex = Math.Max(0, GamepadRailIndex - 1);
        else if (FocusedGame is { } focused)
        {
            var index = Games.IndexOf(focused);
            if (index > 0 && index % GamepadColumnCount != 0)
                FocusedGame = Games[index - 1];
        }
    }

    [RelayCommand]
    private void MoveGamepadFocusRight()
    {
        if (IsGamepadRailFocused)
            GamepadRailIndex = Math.Min(Systems.Count + 1, GamepadRailIndex + 1);
        else if (FocusedGame is { } focused)
        {
            var index = Games.IndexOf(focused);
            if (index >= 0 && index + 1 < Games.Count && index % GamepadColumnCount < GamepadColumnCount - 1)
                FocusedGame = Games[index + 1];
        }
    }

    [RelayCommand]
    private void MoveGamepadFocusUp()
    {
        if (!IsGamepadMode || Games.Count == 0)
            return;

        var index = FocusedGame is null ? 0 : Math.Max(0, Games.IndexOf(FocusedGame));
        if (index < GamepadColumnCount)
        {
            GamepadRailIndex = CurrentLibraryScope == LibraryScope.AllGames
                ? 0
                : CurrentLibraryScope == LibraryScope.RecentlyAdded
                    ? 1
                    : SelectedSystem is null ? 2 : Math.Max(2, Systems.IndexOf(SelectedSystem) + 2);
            IsGamepadRailFocused = true;
            return;
        }

        FocusedGame = Games[index - GamepadColumnCount];
    }

    [RelayCommand]
    private void MoveGamepadFocusDown()
    {
        if (!IsGamepadMode || Games.Count == 0)
            return;

        if (IsGamepadRailFocused)
        {
            IsGamepadRailFocused = false;
            RestoreFocusedGame();
            return;
        }

        var index = FocusedGame is null ? 0 : Math.Max(0, Games.IndexOf(FocusedGame));
        var target = index + GamepadColumnCount;
        if (target < Games.Count)
            FocusedGame = Games[target];
    }

    [RelayCommand]
    private async Task ActivateGamepadRailAsync()
    {
        if (!IsGamepadRailFocused)
            return;

        if (GamepadRailIndex == 0)
            await ShowAllGamesAsync();
        else if (GamepadRailIndex == 1)
            OpenGamepadOverlay(GamepadOverlayKind.Collections);
        else if (GamepadRailIndex - 2 is var systemIndex && systemIndex >= 0 && systemIndex < Systems.Count)
            SelectedSystem = Systems[systemIndex];

        IsGamepadRailFocused = false;
    }

    private string FocusScopeKey() => CurrentLibraryScope switch
    {
        LibraryScope.AllGames => "all",
        LibraryScope.RecentlyAdded => "recent",
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

        LibraryViewportWidth = value;
        GamepadColumnCount = Math.Max(1, (int)((Math.Max(0, value - GridHorizontalPadding) + CoverColumnSpacing) /
                                               (GridCoverWidth + CoverColumnSpacing)));
    }

    private void UpdateGamepadPlatformState()
    {
        foreach (var platform in GamepadPlatforms)
            platform.IsActive = CurrentLibraryScope == LibraryScope.System &&
                                string.Equals(platform.System.Id, SelectedSystem?.Id, StringComparison.Ordinal);
        UpdateGamepadRailFocus();
    }

    private void UpdateGamepadRailFocus()
    {
        OnPropertyChanged(nameof(IsGamepadAllGamesRailFocused));
        OnPropertyChanged(nameof(IsGamepadCollectionsRailFocused));
        for (var index = 0; index < GamepadPlatforms.Count; index++)
            GamepadPlatforms[index].IsRailFocused = IsGamepadRailFocused && index + 2 == GamepadRailIndex;
    }

    partial void OnCurrentThemeChanged(ThemePreference value)
    {
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeDescription));
    }

    partial void OnSelectedGameChanged(GameViewModel? oldValue, GameViewModel? newValue)
    {
    }

    partial void OnFocusedGameChanged(GameViewModel? oldValue, GameViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsFocused = false;
        if (newValue is not null)
        {
            newValue.IsFocused = true;
            _focusedGameByScope[FocusScopeKey()] = newValue.Id;
        }
    }

    partial void OnGamepadOverlayChanged(GamepadOverlayKind value) => NotifyGamepadOverlayState();

    partial void OnGamepadOverlaySelectionIndexChanged(int value) => UpdateGamepadOverlayOptionFocus();

    partial void OnGamepadRailIndexChanged(int value) => UpdateGamepadRailFocus();

    partial void OnIsGamepadRailFocusedChanged(bool value) => UpdateGamepadRailFocus();

    partial void OnFocusedGamepadAchievementChanged(
        AchievementRowViewModel? oldValue,
        AchievementRowViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsFocused = false;
        if (newValue is not null)
            newValue.IsFocused = true;
    }

    partial void OnIsGamepadModeChanged(bool value)
    {
        if (value)
        {
            IsGamepadControllerInputActive = true;
            IsGridView = true;
            RestoreFocusedGame();
        }
        else
        {
            CloseGamepadOverlay();
        }
    }

    /// <summary>
    /// Applies a library item gesture. The view only reports modifier keys; selection state and
    /// its range anchor remain shared between the grid and list representations.
    /// </summary>
    public void SelectGame(GameViewModel game, bool toggle = false, bool selectRange = false)
    {
        if (IsBusy || !Games.Contains(game))
            return;

        if (selectRange && _selectionAnchor is not null && Games.Contains(_selectionAnchor))
        {
            var start = Games.IndexOf(_selectionAnchor);
            var end = Games.IndexOf(game);
            foreach (var candidate in Games)
                candidate.IsSelected = false;
            for (var index = Math.Min(start, end); index <= Math.Max(start, end); index++)
                Games[index].IsSelected = true;
        }
        else if (toggle)
        {
            game.IsSelected = !game.IsSelected;
        }
        else
        {
            foreach (var candidate in Games)
                candidate.IsSelected = ReferenceEquals(candidate, game);
        }

        _selectionAnchor = game.IsSelected ? game : Games.FirstOrDefault(candidate => candidate.IsSelected);
        SelectedGame = _selectionAnchor;
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

    private void ClearSelection()
    {
        foreach (var game in _systemGames)
            game.IsSelected = false;

        _selectionAnchor = null;
        SelectedGame = null;
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedGameCount));
        OnPropertyChanged(nameof(HasSelectedGames));
        RemoveSelectedGamesCommand.NotifyCanExecuteChanged();
    }

    // Recompute the cover width for the current viewport so a whole number of columns fills the
    // row (no lopsided right gutter), then push it and the shared shelf height to every tile. The
    // shelf height is the tallest cover in the view so a mixed collection stays baseline-aligned.
    private void UpdateCoverLayout()
    {
        var coverWidth = MinCoverWidth;
        var available = LibraryViewportWidth - GridHorizontalPadding;
        if (available >= MinCoverWidth)
        {
            var columns = Math.Max(
                1,
                (int)((available + CoverColumnSpacing) / (MinCoverWidth + CoverColumnSpacing)));
            coverWidth = Math.Floor((available - (columns - 1) * CoverColumnSpacing) / columns);
            coverWidth = Math.Clamp(coverWidth, MinCoverWidth, MaxCoverWidth);
        }

        // Drives the layout's cell width; the view sets UniformGridLayout.MinItemWidth from it.
        GridCoverWidth = coverWidth;

        if (_systemGames.Count == 0)
            return;

        var shelfCoverHeight = _systemGames.Max(
            game => Math.Round(coverWidth / game.CoverAspectRatio));
        foreach (var game in _systemGames)
            game.ApplyCoverLayout(coverWidth, shelfCoverHeight);
    }

    internal async Task ReloadGamesAsync()
    {
        var system = SelectedSystem;
        var scope = CurrentLibraryScope;
        if (scope == LibraryScope.System && system is null)
            return;

        var generation = ++_loadGeneration;
        try
        {
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
                    // its discs falls just outside the newest 30 imported files.
                    LibraryScope.RecentlyAdded => _library.GetGames(),
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
                        titleSet.Discs,
                        titleSet.SelectedDisc,
                        titleSet.DisplayTitle,
                        titleSet.SelectionKey,
                        LaunchSelectedDiscFromLibraryAsync);
                    viewModel.CoverAspectRatioChanged += OnGameCoverAspectRatioChanged;
                    viewModels.Add(viewModel);
                }

                ApplyAchievementDisplays(viewModels);
                ApplyTexturePackDisplays(viewModels);
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
            foreach (var existingGame in _systemGames)
                existingGame.Dispose();
            _systemGames.Clear();
            _systemGames.AddRange(games);
            UpdateCoverLayout();
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger.Error("Could not load the current library view.", ex);
            StatusText = $"Could not load library: {ex.Message}";
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

            var image = await Task.Run(() => new Bitmap(thumbnailPath));
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
                StatusText = $"Could not load cover for {game.Title}: {ex.Message}";
            }
        }
        finally
        {
            game.IsCoverLoading = false;
        }
    }

    private void OnGameCoverAspectRatioChanged(object? sender, EventArgs e) => UpdateCoverLayout();

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

    private IEnumerable<GameViewModel> SortGames(IEnumerable<GameViewModel> games)
    {
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
        _appliedSearchText = query;
        IEnumerable<GameViewModel> filtered = _systemGames;
        if (query.Length > 0)
            filtered = _systemGames.Where(g =>
                g.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

        Games.ReplaceAll(SortGames(filtered));

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
        IsBusy = true;
        try
        {
            StatusText = paths.Count == 1 ? "Inspecting game…" : $"Inspecting {paths.Count} files…";
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
                StatusText = previousStatus;
                return;
            }

            if (system.Id == "playstation3")
            {
                StatusText = "PlayStation 3 games are imported only from RPCS3. Use Settings to sync its library.";
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
            StatusText = BuildAddGamesStatus(
                importResult.AddedCount,
                incompatible,
                unsupported,
                confirmedUnrecognized,
                system.Name);
            await MaybeStartMetadataForImportAsync(importResult.AddedGameIds);
        }
        catch (Exception ex)
        {
            _logger.Error("Game import failed.", ex);
            StatusText = $"Import failed: {ex.Message}";
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
            StatusText = "PlayStation 3 games are imported only from RPCS3. Use Settings to sync its library.";
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<ScanProgress>(p =>
                StatusText = $"Scanning {system.Name}… {p.CandidatesFound} found");

            var selection = await _scanner.ScanAsync(folder, system, progress);
            await Task.Run(() => _library.AddLibraryFolder(system.Id, folder));

            var importResult = await ReconcileImportAsync(system, selection);
            await ShowSystemAsync(system);
            StatusText = importResult.AddedCount == 1
                ? "Added 1 game from folder"
                : $"Added {importResult.AddedCount} games from folder";
            await MaybeStartMetadataForImportAsync(importResult.AddedGameIds);
        }
        catch (Exception ex)
        {
            _logger.Error($"Folder scan failed for system {system.Id}.", ex);
            StatusText = $"Scan failed: {ex.Message}";
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
            StatusText = "Use Settings to sync the explicitly selected RPCS3 library.";
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
            StatusText = "Reading the RPCS3 game list…";
            var source = new Rpcs3LibrarySource(configurationDirectory);
            var result = await new ExternalLibrarySyncService(_library).SyncAsync(source);
            var playStation3 = Systems.FirstOrDefault(system => system.Id == "playstation3");
            if (playStation3 is not null)
                await ShowSystemAsync(playStation3);

            StatusText = BuildRpcs3SyncStatus(result);
            return StatusText;
        }
        catch (Rpcs3LibraryFormatException ex)
        {
            _logger.Warning($"RPCS3 library sync was rejected: {ex.Message}");
            StatusText = $"RPCS3 library sync failed: {ex.Message}";
            return StatusText;
        }
        catch (ExternalLibrarySourceConflictException ex)
        {
            // Expected, recoverable condition: an entry collides with another game's path. The
            // reconciliation left the library unchanged, so surface the actionable message plainly.
            _logger.Warning($"RPCS3 library sync stopped on a path conflict: {ex.Message}");
            StatusText = $"RPCS3 library sync stopped: {ex.Message}";
            return StatusText;
        }
        catch (Exception ex)
        {
            _logger.Error("RPCS3 library sync failed.", ex);
            StatusText = $"RPCS3 library sync failed: {ex.Message}";
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
                        StatusText = $"Rescanning {system.Name}… {p.CandidatesFound} found");
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
            StatusText = total == 0 ? "Rescan complete — no new games" : $"Rescan added {total} game(s)";
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
            StatusText = $"Rescan failed: {ex.Message}";
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
        if (selection.EntryPaths.Count == 0 && selection.SuppressedPaths.Count == 0)
            return GameImportResult.Empty;

        var preparedEntries = await Task.Run(() => selection.EntryPaths
            .Select(path => new PreparedImportEntry(
                path,
                _importRules.ReadImportMetadata(path, system)))
            .ToArray());
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
            StatusText = $"Availability check failed: {ex.Message}";
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
        StatusText = $"Disc {disc.Number} selected for {game.Title}";
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
            StatusText = launchGame.IsAvailable ? game.UnavailableLaunchStatus :
                $"Cannot launch Disc {launchDisc.Number} of {game.Title}: its game file could not be found.";
            return;
        }

        IsBusy = true;
        StatusText = $"Launching {game.Title}…";
        SuspendFrontendUiWork();
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
                    StatusText = beforeSync?.Status == CloudSaveSyncStatus.Failed
                        ? $"Save sync incomplete; launching {game.Title} with the saves currently on disk…"
                        : $"Launching {game.Title}…";
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
            StatusText = DescribeLaunchAndSaveSync(result, beforeSync, afterSync);
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Launch cancelled for {game.Title}";
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected launch failure for game id {game.Id}.", ex);
            StatusText = $"Could not launch {game.Title}: {ex.Message}";
        }
        finally
        {
            ResumeFrontendUiWork();
            IsBusy = false;
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

        StatusText = afterExit
            ? $"{game.Title} finished. Syncing saves…"
            : $"Syncing saves before launching {game.Title}…";
        try
        {
            var outcome = await _gameSaveSync.SyncSystemAsync(game.SystemId, cancellationToken);
            if (outcome.Status == CloudSaveSyncStatus.Failed)
            {
                _logger.Warning(
                    $"Cloud save sync failed {(afterExit ? "after" : "before")} launching " +
                    $"game id {game.Id}: {outcome.Message}");
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
                StatusText = "Achievement details are unavailable right now.";
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
                    cached);
                OpenGamepadOverlay(GamepadOverlayKind.Achievements);
                GamepadAchievementDetails = details;
                details.Achievements.CollectionChanged += HandleGamepadAchievementsChanged;
                FocusFirstAchievement();
                _ = details.RefreshIfStaleAsync();
                return;
            }
            catch (Exception ex)
            {
                _logger.Error($"Could not open Gamepad achievements for game id {game.Id}.", ex);
                StatusText = $"Could not open achievements for {game.Title}: {ex.Message}";
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
            StatusText = $"Could not open achievements for {game.Title}: {ex.Message}";
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
            StatusText = "A game title cannot be empty.";
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
            StatusText = $"Renamed game to {title}";
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not rename game id {game.Id}.", ex);
            StatusText = $"Could not rename {game.Title}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetGameCoverAsync(GameViewModel? game)
    {
        if (game is null || IsBusy)
            return;

        var sourcePath = await _dialogs.PickCoverImageAsync(game.Title);
        if (sourcePath is null)
            return;

        IsBusy = true;
        StatusText = $"Preparing cover for {game.Title}…";
        var previousCoverPath = game.CoverPath;
        try
        {
            var imported = await _covers.ImportAsync(game.Id, sourcePath);
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

            StatusText = warnings.Count == 0
                ? $"Updated cover for {game.Title}"
                : $"Updated cover for {game.Title}, but {string.Join("; ", warnings)}";
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not set a cover for game id {game.Id}.", ex);
            StatusText = $"Could not set cover for {game.Title}: {ex.Message}";
        }
        finally
        {
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
            StatusText = $"Removed {game.Title} from the library — game files were not touched";
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not remove game id {game.Id} from the library.", ex);
            StatusText = $"Could not remove {game.Title}: {ex.Message}";
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
        if (selectedGames.Length == 0 ||
            !await _dialogs.ConfirmRemoveGamesAsync(selectedGames.Length))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => _library.RemoveGames(selectedGames
                .SelectMany(game => game.Discs.Select(disc => disc.Game.Id))
                .Distinct()
                .ToArray()));
            await ReloadGamesAsync();
            StatusText = $"Removed {selectedGames.Length} {(selectedGames.Length == 1 ? "game" : "games")} from the library — game files and covers were not touched";
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not remove {selectedGames.Length} selected games from the library.", ex);
            StatusText = $"Could not remove the selected games: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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
                new LibraryMaintenanceActions(
                    RescanSystemFromSettingsAsync,
                    RescanAllFromSettingsAsync,
                    FetchMetadataForSystemFromSettingsAsync,
                    FetchAllMetadataFromSettingsAsync,
                    SyncRpcs3LibraryFromSettingsAsync),
                _metadataPreferences,
                _retroAccount is null
                    ? null
                    : new RetroAchievementsSettingsContext(
                        _retroAccount.Account,
                        _retroAccount.IsConnected,
                        ConnectRetroAchievementsAsync,
                        DisconnectRetroAchievementsAsync,
                        RefreshRetroAchievementsMatchesAsync),
                _cloudSaveSync?.CreateSettingsContext(),
                // Titles come from the whole library, not the visible collection: a Dolphin pack
                // must still name the GameCube game it matched while the user is viewing PS1.
                _texturePacks?.CreateSettingsContext(
                    BuildLibraryTitleLookup,
                    RefreshTexturePacksAsync));
        }
        catch (Exception ex)
        {
            _logger.Error("Could not open emulator settings.", ex);
            StatusText = $"Could not open emulator settings: {ex.Message}";
        }
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
                StatusText += " — metadata preference could not be saved";
            }
        }

        if (shouldFetch)
            _ = EnrichImportedGamesAsync(addedGameIds);
    }

    private async Task EnrichImportedGamesAsync(IReadOnlyList<long> gameIds)
    {
        try
        {
            StatusText = gameIds.Count == 1
                ? "Fetching metadata for 1 new game…"
                : $"Fetching metadata for {gameIds.Count} new games…";
            var summary = await _metadataService.EnrichAsync(gameIds);
            await ReloadGamesAsync();
            StatusText = summary.ToStatusText();
        }
        catch (Exception ex)
        {
            _logger.Error("Automatic metadata enrichment failed.", ex);
            StatusText = $"Metadata failed: {ex.Message}";
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
        StatusText = summary.ToStatusText();
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
            StatusText = $"Appearance set to {preference.ToString().ToLowerInvariant()}";
        }
        catch (Exception ex)
        {
            _logger.Error("Could not persist the appearance preference.", ex);
            CurrentTheme = _themeService.Current;
            StatusText = $"Appearance changed for this session, but could not be saved: {ex.Message}";
        }
    }
}
