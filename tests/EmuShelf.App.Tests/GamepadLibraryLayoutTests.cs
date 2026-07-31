using Avalonia.Headless.XUnit;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Library;
using EmuShelf.Core.Systems;
using EmuShelf.Infrastructure.Importing;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// The two faults behind "gamepad mode is broken after switching from desktop": cover sizing state
/// that both modes shared, and a window where one platform's name sat above another's games.
/// </summary>
public class GamepadLibraryLayoutTests : IDisposable
{
    private readonly string _baseDirectory =
        Path.Combine(Path.GetTempPath(), "EmuShelfGamepadLayout", Guid.NewGuid().ToString("N"));
    private readonly GameLibrary _library;
    private readonly LibraryDatabase _database;
    private readonly FakeDialogService _dialogs = new();
    private static readonly GameSystem Ps1 = KnownSystems.All.Single(s => s.Id == "playstation");
    private static readonly GameSystem Gba = KnownSystems.All.Single(s => s.Id == "gba");

    public GamepadLibraryLayoutTests()
    {
        var appPaths = new AppPaths(_baseDirectory);
        appPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(appPaths);
        _database.Initialize();
        _library = new GameLibrary(_database, new RelativePathResolver(appPaths));
    }

    private MainViewModel CreateViewModel()
    {
        IGameImportRules rules = new FileImportRules();
        return new MainViewModel(
            _library,
            new FolderScanner(rules),
            rules,
            new FileAvailabilityChecker(),
            _dialogs,
            KnownSystems.All);
    }

    private void AddGame(GameSystem system, string title, string extension)
    {
        var folder = Path.Combine(_baseDirectory, "roms", system.Id);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{title}{extension}");
        File.WriteAllText(path, "x");
        _library.AddGames([new Game
        {
            SystemId = system.Id,
            Path = path,
            Title = title,
            DateAdded = DateTimeOffset.UtcNow,
        }]);
    }

    /// <summary>
    /// Regression: the gamepad viewport used to be written into LibraryViewportWidth, so entering
    /// gamepad mode left the desktop grid sized for the gamepad viewport (and vice versa).
    /// </summary>
    [AvaloniaFact]
    public void TheGamepadViewportDoesNotOverwriteTheDesktopOne()
    {
        var viewModel = CreateViewModel();
        viewModel.LibraryViewportWidth = 1600;

        viewModel.IsGamepadMode = true;
        viewModel.GamepadViewportWidth = 1176;

        Assert.Equal(1600, viewModel.LibraryViewportWidth);
    }

    /// <summary>
    /// Regression: both modes derived the cover width from one viewport and one inset constant, so
    /// whichever view raised SizeChanged last defined the size for the view that was on screen.
    /// </summary>
    [AvaloniaFact]
    public void EachModeSizesCoversFromItsOwnViewport()
    {
        var viewModel = CreateViewModel();
        viewModel.LibraryViewportWidth = 1600;
        viewModel.GamepadViewportWidth = 1176;

        var desktopWidth = viewModel.GridCoverWidth;
        viewModel.IsGamepadMode = true;
        var gamepadWidth = viewModel.GridCoverWidth;
        viewModel.IsGamepadMode = false;

        Assert.NotEqual(desktopWidth, gamepadWidth);
        // Switching back restores the desktop sizing rather than keeping the gamepad's.
        Assert.Equal(desktopWidth, viewModel.GridCoverWidth);
    }

    /// <summary>
    /// Regression: D-pad up/down steps a whole row, so the stride has to equal the number of
    /// columns the layout renders from the same width and cover size. A disagreement of one sent
    /// focus to the wrong tile and scrolled the grid to it.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1176)]  // Steam Deck, fullscreen
    [InlineData(1280)]
    [InlineData(1920)]
    [InlineData(800)]
    public void TheFocusStrideMatchesTheRenderedColumnCount(double viewportWidth)
    {
        var viewModel = CreateViewModel();
        viewModel.IsGamepadMode = true;
        viewModel.GamepadViewportWidth = viewportWidth;

        // UniformGridLayout fits floor((available + spacing) / (itemWidth + spacing)) columns,
        // measured against the same viewport, because the gamepad grid adds no inset of its own.
        const double spacing = 28;
        var expected = Math.Max(
            1,
            (int)((viewportWidth + spacing) / (viewModel.GridCoverWidth + spacing)));

        Assert.Equal(expected, viewModel.GamepadColumnCount);
    }

    /// <summary>
    /// Regression: the rail and title moved to the new platform immediately while Games kept the
    /// old platform's tiles until the load finished two awaits later, so a GBA game could be seen
    /// under the PlayStation tab.
    /// </summary>
    [AvaloniaFact]
    public async Task ChangingPlatformClearsTheOutgoingTilesBeforeAwaiting()
    {
        AddGame(Ps1, "Crash Bandicoot", ".cue");
        AddGame(Gba, "Advance Wars", ".gba");
        var viewModel = CreateViewModel();
        viewModel.SelectedSystem = Gba;
        await viewModel.ReloadGamesAsync();
        Assert.Single(viewModel.Games);
        Assert.Equal("Advance Wars", viewModel.Games[0].Title);

        // Setting the platform runs synchronously up to the first await, which is exactly the
        // window the user was seeing. Nothing from the previous platform may survive it.
        viewModel.SelectedSystem = Ps1;

        Assert.Empty(viewModel.Games);
        Assert.False(viewModel.HasGames);
        Assert.True(viewModel.IsLibraryLoading);
        // "No games here" must not be claimed about a platform that has not been read yet.
        Assert.False(viewModel.IsLibraryEmpty);
        Assert.False(viewModel.IsSearchEmpty);

        await viewModel.ReloadGamesAsync();
        Assert.False(viewModel.IsLibraryLoading);
        Assert.Equal("Crash Bandicoot", Assert.Single(viewModel.Games).Title);
    }

    /// <summary>
    /// The counterpart: re-reading the platform already on screen (an availability pass, a rescan)
    /// must not blank the grid, or every background refresh would flash.
    /// </summary>
    [AvaloniaFact]
    public async Task ReloadingTheSameScopeKeepsItsTilesOnScreen()
    {
        AddGame(Ps1, "Crash Bandicoot", ".cue");
        var viewModel = CreateViewModel();
        viewModel.SelectedSystem = Ps1;
        await viewModel.ReloadGamesAsync();
        Assert.Single(viewModel.Games);

        var reload = viewModel.ReloadGamesAsync();

        Assert.Single(viewModel.Games);
        Assert.False(viewModel.IsLibraryLoading);
        await reload;
    }

    public void Dispose()
    {
        if (!Directory.Exists(_baseDirectory))
            return;

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
}
