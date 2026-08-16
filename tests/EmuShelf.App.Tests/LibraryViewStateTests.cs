using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Systems;
using EmuShelf.Infrastructure.Importing;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Settings;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// Covers the library view state that survives a restart: what a saved state restores to, and
/// what the current view produces to be saved. The debounce timer is bypassed the same way the
/// search tests bypass theirs — by asserting the snapshot the timer would have written.
/// </summary>
public class LibraryViewStateTests : IDisposable
{
    private readonly string _baseDirectory =
        Path.Combine(Path.GetTempPath(), "EmuShelfViewState", Guid.NewGuid().ToString("N"));
    private readonly GameLibrary _library;
    private readonly LibraryDatabase _database;
    private readonly FakeDialogService _dialogs = new();
    private static readonly GameSystem GameCube = KnownSystems.All.Single(s => s.Id == "gamecube");

    public LibraryViewStateTests()
    {
        var appPaths = new AppPaths(_baseDirectory);
        appPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(appPaths);
        _database.Initialize();
        _library = new GameLibrary(_database, new RelativePathResolver(appPaths));
    }

    private MainViewModel CreateViewModel(
        ILibraryViewStateService? viewState = null,
        IInterfaceModeService? interfaceMode = null,
        IGameDetailsStore? gameDetails = null)
    {
        IGameImportRules rules = new FileImportRules();
        return new MainViewModel(
            _library,
            new FolderScanner(rules),
            rules,
            new FileAvailabilityChecker(),
            _dialogs,
            KnownSystems.All,
            interfaceModeService: interfaceMode,
            libraryViewState: viewState,
            gameDetails: gameDetails);
    }

    // Counts the bulk projection reads so a test can prove grid/gamepad views skip it (M40 item 2).
    private sealed class CountingDetailsStore : IGameDetailsStore
    {
        public int Calls { get; private set; }

        public IReadOnlyDictionary<long, GameDetailsProjection> GetAllDetailsProjections()
        {
            Calls++;
            return new Dictionary<long, GameDetailsProjection>();
        }

        public GameDetails GetDetails(long gameId) => new(gameId, [], [], []);
        public bool TryApplyMetadata(GameMetadataValue value, GameMetadataApplyMode mode) => false;
        public GameMediaAsset SaveMedia(GameMediaAsset media) => media;
        public bool SelectMedia(long gameId, GameMediaKind kind, long mediaId) => false;
        public void UpsertProviderMatch(GameProviderMatch match) { }
    }

    private sealed class StubInterfaceMode : IInterfaceModeService
    {
        public InterfaceMode Current { get; private set; } = InterfaceMode.Desktop;
        public bool IsCommandLineOverride => false;
        public event EventHandler<InterfaceMode>? ModeChanged;

        public void Switch(InterfaceMode mode)
        {
            Current = mode;
            ModeChanged?.Invoke(this, mode);
        }

        public Task SetModeAsync(InterfaceMode mode, CancellationToken cancellationToken = default)
        {
            Switch(mode);
            return Task.CompletedTask;
        }
    }

    [AvaloniaFact]
    public void RestoresTheSavedViewOnStartup()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings
        {
            IsGridView = false,
            SortColumn = nameof(LibrarySortColumn.Console),
            SortDescending = true,
            IsNavigationCollapsed = true,
            ShowEmptyPlatforms = true,
            Scope = nameof(LibraryScope.System),
            SelectedSystemId = "gamecube",
        }));

        Assert.False(viewModel.IsGridView);
        Assert.Equal(LibrarySortColumn.Console, viewModel.SortColumn);
        Assert.True(viewModel.SortDescending);
        Assert.True(viewModel.IsNavigationCollapsed);
        Assert.Equal(GameCube.Id, viewModel.SelectedSystem?.Id);
    }

    [AvaloniaFact]
    public void RestoresACollectionScopeWithoutSelectingASystem()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings
        {
            Scope = nameof(LibraryScope.RecentlyAdded),
        }));

        Assert.Equal(LibraryScope.RecentlyAdded, viewModel.CurrentLibraryScope);
        Assert.Null(viewModel.SelectedSystem);
        Assert.True(viewModel.IsRecentlyAddedSelected);
    }

    [AvaloniaFact]
    public void FallsBackToTheFirstSystemWhenTheSavedOneIsGone()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings
        {
            ShowEmptyPlatforms = true,
            Scope = nameof(LibraryScope.System),
            SelectedSystemId = "a-console-that-no-longer-exists",
        }));

        Assert.Equal(KnownSystems.All[0].Id, viewModel.SelectedSystem?.Id);
    }

    [AvaloniaFact]
    public void UnreadableNamesFallBackToDefaultsInsteadOfThrowing()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings
        {
            ShowEmptyPlatforms = true,
            SortColumn = "NotAColumn",
            Scope = "NotAScope",
        }));

        Assert.Equal(LibrarySortColumn.Title, viewModel.SortColumn);
        Assert.Equal(LibraryScope.System, viewModel.CurrentLibraryScope);
    }

    [AvaloniaFact]
    public async Task EmptyPlatformsAreHiddenButUnavailableLibraryEntriesStayVisible()
    {
        var missingGame = new Game
        {
            SystemId = GameCube.Id,
            Path = Path.Combine(_baseDirectory, "missing.iso"),
            Title = "Missing SD card game",
            IsAvailable = false,
            DateAdded = DateTimeOffset.UtcNow,
        };
        _library.AddGames([missingGame]);

        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings
        {
            Scope = nameof(LibraryScope.AllGames),
        }));
        await viewModel.ReloadGamesAsync();

        Assert.Equal([GameCube.Id], viewModel.NavigationSystems.Select(system => system.Id));
        Assert.Equal([GameCube.Id], viewModel.GamepadPlatforms.Select(platform => platform.System.Id));
    }

    [AvaloniaFact]
    public void ShowEmptyPlatformsRestoresTheCompleteSupportedList()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings
        {
            ShowEmptyPlatforms = true,
            Scope = nameof(LibraryScope.AllGames),
        }));

        Assert.Equal(KnownSystems.All, viewModel.NavigationSystems);
    }

    [AvaloniaFact]
    public void RestoringDoesNotWriteBackOverTheSavedState()
    {
        var stub = new StubViewState(new LibraryViewSettings { IsGridView = false });

        _ = CreateViewModel(stub);

        Assert.Equal(0, stub.SaveCount);
    }

    [AvaloniaFact]
    public void TheSavedSnapshotDescribesTheCurrentView()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings()));
        viewModel.SelectedSystem = GameCube;
        viewModel.IsGridView = false;
        viewModel.SortColumn = LibrarySortColumn.Format;
        viewModel.SortDescending = true;
        viewModel.IsNavigationCollapsed = true;

        var state = viewModel.BuildLibraryViewState();

        Assert.False(state.IsGridView);
        Assert.Equal(nameof(LibrarySortColumn.Format), state.SortColumn);
        Assert.True(state.SortDescending);
        Assert.True(state.IsNavigationCollapsed);
        Assert.Equal(nameof(LibraryScope.System), state.Scope);
        Assert.Equal(GameCube.Id, state.SelectedSystemId);
    }

    [AvaloniaFact]
    public void GamepadModeDoesNotOverwriteTheDesktopViewPreference()
    {
        // Gamepad mode forces IsGridView true to render its tiles. That is a rendering
        // requirement, not the user choosing a grid, so the stored desktop preference stands.
        var stub = new StubViewState(new LibraryViewSettings { IsGridView = false });
        var viewModel = CreateViewModel(stub);
        viewModel.IsGamepadMode = true;
        viewModel.IsGridView = true;

        Assert.False(viewModel.BuildLibraryViewState().IsGridView);
    }

    /// <summary>
    /// Regression: Gamepad mode forces a grid to render its tiles. Returning to Desktop has to put
    /// the user's own choice back, or a list-view user is stranded in a grid and the next unrelated
    /// change persists that grid over a preference they never touched.
    /// </summary>
    [AvaloniaFact]
    public void ReturningFromGamepadModeRestoresTheListViewPreference()
    {
        var mode = new StubInterfaceMode();
        var stub = new StubViewState(new LibraryViewSettings { IsGridView = false });
        var viewModel = CreateViewModel(stub, mode);
        Assert.False(viewModel.IsGridView);

        mode.Switch(InterfaceMode.Gamepad);
        Assert.True(viewModel.IsGridView); // gamepad renders tiles

        mode.Switch(InterfaceMode.Desktop);

        Assert.False(viewModel.IsGridView);
        Assert.False(viewModel.BuildLibraryViewState().IsGridView);
    }

    /// <summary>
    /// Regression: the save is debounced, so switching view and quitting immediately is well
    /// inside the interval. Without the flush, the change the user just made is the one lost.
    /// </summary>
    [AvaloniaFact]
    public void ClosingFlushesAViewChangeThatIsStillWaitingOutTheDebounce()
    {
        var stub = new StubViewState(new LibraryViewSettings { IsGridView = true });
        var viewModel = CreateViewModel(stub);

        viewModel.IsGridView = false;
        Assert.Equal(0, stub.SaveCount); // still debouncing

        viewModel.FlushPendingLibraryViewStateSave();

        Assert.Equal(1, stub.SaveCount);
        Assert.False(stub.Current.IsGridView);
    }

    [AvaloniaFact]
    public void FlushingWithNothingPendingWritesNothing()
    {
        var stub = new StubViewState(new LibraryViewSettings());
        var viewModel = CreateViewModel(stub);

        viewModel.FlushPendingLibraryViewStateSave();

        Assert.Equal(0, stub.SaveCount);
    }

    [AvaloniaFact]
    public async Task TheServiceRoundTripsThroughTheSettingsFile()
    {
        var appPaths = new AppPaths(_baseDirectory);
        var settingsService = new JsonSettingsService(appPaths);
        var service = new LibraryViewStateService(settingsService, settingsService.Load());

        await service.SaveAsync(new LibraryViewSettings
        {
            IsGridView = false,
            SortColumn = nameof(LibrarySortColumn.Achievements),
            SelectedSystemId = "gamecube",
        });

        var reloaded = settingsService.Load().LibraryView;
        Assert.False(reloaded.IsGridView);
        Assert.Equal(nameof(LibrarySortColumn.Achievements), reloaded.SortColumn);
        Assert.Equal("gamecube", reloaded.SelectedSystemId);
    }

    [AvaloniaFact]
    public async Task SavingTheViewKeepsUnrelatedSettings()
    {
        var appPaths = new AppPaths(_baseDirectory);
        var settingsService = new JsonSettingsService(appPaths);
        settingsService.Save(settingsService.Load() with { Theme = ThemePreference.Dark });
        var service = new LibraryViewStateService(settingsService, settingsService.Load());

        await service.SaveAsync(new LibraryViewSettings { IsGridView = false });

        Assert.Equal(ThemePreference.Dark, settingsService.Load().Theme);
    }

    [AvaloniaFact]
    public void RestoresThePersistedColumnLayout()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings
        {
            ListColumns =
            [
                new LibraryColumnSetting { Key = nameof(LibraryColumnKey.Status), IsVisible = true, Width = 130 },
                new LibraryColumnSetting { Key = nameof(LibraryColumnKey.Title), IsVisible = true },
                new LibraryColumnSetting { Key = nameof(LibraryColumnKey.Cover), IsVisible = false },
                new LibraryColumnSetting { Key = nameof(LibraryColumnKey.Console), IsVisible = true },
                new LibraryColumnSetting { Key = nameof(LibraryColumnKey.Format), IsVisible = true },
                new LibraryColumnSetting { Key = nameof(LibraryColumnKey.Achievements), IsVisible = true },
                new LibraryColumnSetting { Key = nameof(LibraryColumnKey.Textures), IsVisible = true },
            ],
        }));

        var status = viewModel.Columns.Single(column => column.Key == LibraryColumnKey.Status);
        Assert.Equal(0, viewModel.Columns.IndexOf(status)); // saved first, so reordered to the front
        Assert.Equal(130, status.Width);
        Assert.Equal(LibraryColumnKey.Status, viewModel.VisibleColumns[0].Key);
        Assert.False(viewModel.Columns.Single(column => column.Key == LibraryColumnKey.Cover).IsVisible);
        Assert.DoesNotContain(viewModel.VisibleColumns, column => column.Key == LibraryColumnKey.Cover);
    }

    [AvaloniaFact]
    public void ToleratesUnknownAndMissingColumnsInThePersistedLayout()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings
        {
            ListColumns =
            [
                new LibraryColumnSetting { Key = "AColumnThatNoLongerExists", IsVisible = true },
                new LibraryColumnSetting { Key = nameof(LibraryColumnKey.Console), IsVisible = false },
            ],
        }));

        // Unknown key dropped; the console setting applied; every unlisted column keeps its default.
        Assert.DoesNotContain(viewModel.Columns, column => column.DisplayName == "AColumnThatNoLongerExists");
        Assert.False(viewModel.Columns.Single(column => column.Key == LibraryColumnKey.Console).IsVisible);
        Assert.Contains(viewModel.VisibleColumns, column => column.Key == LibraryColumnKey.Title);
        Assert.Contains(viewModel.VisibleColumns, column => column.Key == LibraryColumnKey.Cover);
    }

    [AvaloniaFact]
    public void TheSavedSnapshotIncludesTheColumnLayout()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings()));
        viewModel.Columns.Single(column => column.Key == LibraryColumnKey.Textures).IsVisible = false;

        var state = viewModel.BuildLibraryViewState();

        Assert.Equal(viewModel.Columns.Count, state.ListColumns.Count);
        Assert.Equal(nameof(LibraryColumnKey.Cover), state.ListColumns[0].Key);
        Assert.False(state.ListColumns.Single(column => column.Key == nameof(LibraryColumnKey.Textures)).IsVisible);
    }

    [AvaloniaFact]
    public void TheTitleColumnCannotBeHidden()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings()));
        var title = viewModel.Columns.Single(column => column.Key == LibraryColumnKey.Title);

        title.IsVisible = false;

        Assert.True(title.IsVisible);
        Assert.Contains(viewModel.VisibleColumns, column => column.Key == LibraryColumnKey.Title);
    }

    [AvaloniaFact]
    public void HidingAColumnRemovesItFromTheVisibleSet()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings()));
        var console = viewModel.Columns.Single(column => column.Key == LibraryColumnKey.Console);

        console.IsVisible = false;
        Assert.DoesNotContain(viewModel.VisibleColumns, column => column.Key == LibraryColumnKey.Console);

        console.IsVisible = true;
        Assert.Contains(viewModel.VisibleColumns, column => column.Key == LibraryColumnKey.Console);
    }

    [AvaloniaFact]
    public void ResizingAFixedColumnAdjustsTheFlexColumnAndPersists()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings()));
        viewModel.ListViewportWidth = 1300; // enough room that the flex column is above its minimum
        var console = viewModel.Columns.Single(column => column.Key == LibraryColumnKey.Console);
        var title = viewModel.Columns.Single(column => column.Key == LibraryColumnKey.Title);
        var flexBefore = title.Width;

        console.Width += 100;

        Assert.True(title.Width < flexBefore, "the flex column should shrink to absorb the wider column");
        Assert.Equal(
            console.Width,
            viewModel.BuildLibraryViewState().ListColumns
                .Single(setting => setting.Key == nameof(LibraryColumnKey.Console)).Width);
    }

    [AvaloniaFact]
    public void RestoringAnOverlyWideColumnClampsToTheMaximum()
    {
        var viewModel = CreateViewModel(new StubViewState(new LibraryViewSettings
        {
            ListColumns =
            [
                new LibraryColumnSetting { Key = nameof(LibraryColumnKey.Console), IsVisible = true, Width = 100_000 },
            ],
        }));

        var console = viewModel.Columns.Single(column => column.Key == LibraryColumnKey.Console);
        Assert.Equal(console.MaxWidth, console.Width); // a corrupt/huge width can't push Title off-screen
    }

    [AvaloniaFact]
    public async Task ProjectionReadIsSkippedInGridViewButRunsForTheListView()
    {
        _library.AddGames([
            new Game
            {
                SystemId = GameCube.Id,
                Path = Path.Combine(_baseDirectory, "g.iso"),
                Title = "G",
                IsAvailable = true,
                DateAdded = DateTimeOffset.UtcNow,
            },
        ]);

        var gridCounter = new CountingDetailsStore();
        var grid = CreateViewModel(
            new StubViewState(new LibraryViewSettings { IsGridView = true, Scope = nameof(LibraryScope.AllGames) }),
            gameDetails: gridCounter);
        await grid.ReloadGamesAsync();
        Assert.Equal(0, gridCounter.Calls); // grid view never reads the scraped-metadata projection

        var listCounter = new CountingDetailsStore();
        var list = CreateViewModel(
            new StubViewState(new LibraryViewSettings { IsGridView = false, Scope = nameof(LibraryScope.AllGames) }),
            gameDetails: listCounter);
        await list.ReloadGamesAsync();
        Assert.True(listCounter.Calls >= 1, "the list view should load the projection at least once");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_baseDirectory))
            return;

        // Same reason as MainViewModelTests: the view model's constructor kicks off a background
        // library read, which can still hold library.db open when a fast test finishes.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_baseDirectory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 40)
            {
                Thread.Sleep(50);
            }
        }
    }

    private sealed class StubViewState(LibraryViewSettings current) : ILibraryViewStateService
    {
        public LibraryViewSettings Current { get; private set; } = current;
        public int SaveCount { get; private set; }

        public Task SaveAsync(LibraryViewSettings state, CancellationToken cancellationToken = default)
        {
            Save(state);
            return Task.CompletedTask;
        }

        public void Save(LibraryViewSettings state)
        {
            Current = state;
            SaveCount++;
        }
    }
}
