using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Media;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// Renders the real gamepad grid and asserts the two things that made "the selector is broken":
/// the navigation stride (GamepadColumnCount) equals the columns UniformGridLayout actually lays out
/// at every width, and the focus ring stays glued to FocusedGame — including under the input-priority
/// flood (fast d-pad / LB-RB) that used to strand a reveal posted at DispatcherPriority.Input.
/// </summary>
public class GamepadGridSelectorTests
{
    private readonly ITestOutputHelper _output;

    public GamepadGridSelectorTests(ITestOutputHelper output) => _output = output;

    [AvaloniaFact]
    public async Task ArithmeticColumnCountMatchesRenderedLayout_AcrossWidths()
    {
        var mismatches = new List<string>();
        // Sweep window widths; the gamepad scroller's fixed 52+52 margin makes viewport = window - 104.
        for (var windowWidth = 820; windowWidth <= 1960; windowWidth += 16)
        {
            var (arithmetic, rendered, viewport, coverWidth, rowXs) = await MeasureColumns(windowWidth);
            if (arithmetic != rendered)
            {
                mismatches.Add(
                    $"window={windowWidth} viewport={viewport:F0} coverW={coverWidth:F0} " +
                    $"arithmetic={arithmetic} rendered={rendered} X=[{rowXs}]");
            }
        }

        foreach (var line in mismatches)
            _output.WriteLine(line);
        _output.WriteLine($"total mismatches: {mismatches.Count}");

        Assert.Empty(mismatches);
    }

    private static async Task<(int Arithmetic, int Rendered, double Viewport, double CoverWidth, string RowXs)>
        MeasureColumns(int windowWidth)
    {
        var systems = KnownSystems.All.Take(6).ToArray();
        var viewModel = new MainViewModel();
        await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
        viewModel.IsGamepadMode = true;

        var games = Enumerable.Range(0, 33)
            .Select(index =>
            {
                var system = systems[index % systems.Length];
                return new GameViewModel(
                    new Game
                    {
                        Id = index + 1,
                        SystemId = system.Id,
                        Path = $"/Games/{system.Id}/Game {index + 1}.bin",
                        Title = $"Game {index + 1}",
                        IsAvailable = true,
                        DateAdded = DateTimeOffset.UtcNow,
                    },
                    system.Name,
                    system.ShortName,
                    system.AccentColor,
                    coverAspectRatio: system.CoverAspectRatio);
            })
            .ToArray();
        viewModel.Games.ReplaceAll(games);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = games[0];

        var window = new MainWindow { DataContext = viewModel, Width = windowWidth, Height = 800 };
        window.Show();
        try
        {
            await Pump();

            // Only the on-screen rows realize under virtualization; group them into rows by Y and take
            // the busiest row as the rendered column count (each full row holds GamepadColumnCount tiles).
            var tiles = RealizedTiles(window);
            var byRow = tiles.GroupBy(t => Math.Round(t.TopLeft.Y)).OrderBy(g => g.Key).ToArray();
            var topRow = byRow.FirstOrDefault();
            var rendered = byRow.Length == 0 ? 0 : byRow.Max(g => g.Count());
            var rowXs = topRow is null
                ? ""
                : string.Join(", ", topRow.OrderBy(t => t.TopLeft.X).Select(t => t.TopLeft.X.ToString("F0")));

            return (viewModel.GamepadColumnCount, rendered, viewModel.GamepadViewportWidth,
                viewModel.GridCoverWidth, rowXs);
        }
        finally
        {
            window.Close();
        }
    }

    // Realized gamepad tiles (only visible rows exist under virtualization), each with its game and its
    // top-left in window coordinates.
    private static IReadOnlyList<(GameViewModel Game, Button Tile, Point TopLeft)> RealizedTiles(Window window) =>
        window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("gamepad-game") && b.DataContext is GameViewModel)
            .Select(b => ((GameViewModel)b.DataContext!, b, b.TranslatePoint(new Point(0, 0), window) ?? new Point(double.NaN, double.NaN)))
            .Where(t => !double.IsNaN(t.Item3.X))
            .ToList();

    // Proves the grid virtualizes: for a 300-game library only the on-screen rows are ever materialized,
    // and scrolling to the bottom recycles rows rather than accumulating them — so cost is flat wherever
    // you are in the list, top or bottom.
    [AvaloniaFact]
    public async Task GamepadGrid_VirtualizesRows_CostIsFlatFromTopToBottom()
    {
        var systems = KnownSystems.All.Take(6).ToArray();
        var viewModel = new MainViewModel();
        await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
        viewModel.IsGamepadMode = true;
        var games = Enumerable.Range(0, 300).Select(index => new GameViewModel(
            new Game
            {
                Id = index + 1,
                SystemId = systems[index % systems.Length].Id,
                Path = $"/Games/g{index}.bin",
                Title = $"Game {index + 1}",
                IsAvailable = true,
                DateAdded = DateTimeOffset.UtcNow,
            },
            systems[index % systems.Length].Name,
            systems[index % systems.Length].ShortName,
            systems[index % systems.Length].AccentColor,
            coverAspectRatio: systems[index % systems.Length].CoverAspectRatio)).ToArray();
        viewModel.Games.ReplaceAll(games);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = games[0];

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await Pump();
            var atTop = RealizedTiles(window);
            _output.WriteLine($"realized at top: {atTop.Count} of 300");
            Assert.True(atTop.Count < 120, $"expected only on-screen rows realized, but {atTop.Count} tiles exist");
            Assert.Contains(atTop, t => ReferenceEquals(t.Game, games[0]));

            // Jump focus to the last game — the grid scrolls to the bottom.
            viewModel.FocusedGame = games[^1];
            await Pump();
            var atBottom = RealizedTiles(window);
            _output.WriteLine($"realized at bottom: {atBottom.Count} of 300");

            // Cost did not grow: the same handful of rows, recycled — not 300 accumulated.
            Assert.True(atBottom.Count < 120, $"realized tile count grew to {atBottom.Count} at the bottom (rows not recycled)");
            // The bottom rows now show the last games, and the first game's tile has been recycled away.
            Assert.Contains(atBottom, t => ReferenceEquals(t.Game, games[^1]));
            Assert.DoesNotContain(atBottom, t => ReferenceEquals(t.Game, games[0]));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SelectorStaysOnFocusedGame_DuringWalkAndBurst()
    {
        var systems = KnownSystems.All.Take(6).ToArray();
        var viewModel = new MainViewModel();
        await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
        viewModel.IsGamepadMode = true;

        var games = Enumerable.Range(0, 33)
            .Select(index =>
            {
                var system = systems[index % systems.Length];
                return new GameViewModel(
                    new Game
                    {
                        Id = index + 1,
                        SystemId = system.Id,
                        Path = $"/Games/{system.Id}/Game {index + 1}.bin",
                        Title = $"Game {index + 1}",
                        IsAvailable = true,
                        DateAdded = DateTimeOffset.UtcNow,
                    },
                    system.Name,
                    system.ShortName,
                    system.AccentColor,
                    coverAspectRatio: system.CoverAspectRatio);
            })
            .ToArray();
        viewModel.Games.ReplaceAll(games);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = games[0];

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await Pump();
            var columns = viewModel.GamepadColumnCount;

            var problems = new List<string>();

            // 1) Walk the whole grid one step at a time, pumping each move. The ring must sit on the
            //    focused game's realized cover after every settled move.
            var order = new[]
            {
                GamepadAction.NavigateRight, GamepadAction.NavigateRight, GamepadAction.NavigateRight,
                GamepadAction.NavigateRight, GamepadAction.NavigateDown, GamepadAction.NavigateLeft,
                GamepadAction.NavigateLeft, GamepadAction.NavigateLeft, GamepadAction.NavigateLeft,
                GamepadAction.NavigateDown, GamepadAction.NavigateDown, GamepadAction.NavigateUp,
                GamepadAction.NavigateRight, GamepadAction.NavigateRight,
            };
            foreach (var action in order)
            {
                var before = viewModel.Games.IndexOf(viewModel.FocusedGame!);
                viewModel.DispatchGamepadAction(action);
                await Pump();
                CheckRing(viewModel, window, action, before, problems);
            }

            // 2) Burst: fire a run of NavigateDown faster than the reveal settles (only Render pumps
            //    between them), then let it settle. This mimics d-pad auto-repeat / fast scrolling.
            viewModel.FocusedGame = games[0];
            await Pump();
            for (var i = 0; i < 6; i++)
            {
                viewModel.DispatchGamepadAction(GamepadAction.NavigateDown);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
            await Pump();
            CheckRing(viewModel, window, GamepadAction.NavigateDown, -1, problems);

            foreach (var problem in problems)
                _output.WriteLine(problem);
            _output.WriteLine($"columns={columns}, problems={problems.Count}");
            Assert.Empty(problems);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SelectorTracksFocus_WhenInputPriorityIsStarved()
    {
        var systems = KnownSystems.All.Take(6).ToArray();
        var viewModel = new MainViewModel();
        await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
        viewModel.IsGamepadMode = true;

        var games = Enumerable.Range(0, 33)
            .Select(index =>
            {
                var system = systems[index % systems.Length];
                return new GameViewModel(
                    new Game
                    {
                        Id = index + 1,
                        SystemId = system.Id,
                        Path = $"/Games/{system.Id}/Game {index + 1}.bin",
                        Title = $"Game {index + 1}",
                        IsAvailable = true,
                        DateAdded = DateTimeOffset.UtcNow,
                    },
                    system.Name,
                    system.ShortName,
                    system.AccentColor,
                    coverAspectRatio: system.CoverAspectRatio);
            })
            .ToArray();
        viewModel.Games.ReplaceAll(games);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = games[0];

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await Pump();

            var problems = new List<string>();
            // Move one step at a time WITHIN the already-visible top rows, but only flush Render (and
            // measure/arrange) between moves — never the Input priority the reveal posts on. This is
            // the input-flood regime: the target tile is realized, but the ring reposition is pending.
            var moves = new[]
            {
                GamepadAction.NavigateRight, GamepadAction.NavigateRight, GamepadAction.NavigateRight,
                GamepadAction.NavigateLeft, GamepadAction.NavigateLeft, GamepadAction.NavigateDown,
            };
            foreach (var action in moves)
            {
                var before = viewModel.Games.IndexOf(viewModel.FocusedGame!);
                viewModel.DispatchGamepadAction(action);
                // Only Render + a couple layout passes — deliberately NOT Input/Loaded/Background.
                for (var i = 0; i < 4; i++)
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                CheckRing(viewModel, window, action, before, problems);
            }

            foreach (var problem in problems)
                _output.WriteLine(problem);
            _output.WriteLine($"starved problems={problems.Count}");
            Assert.Empty(problems);
        }
        finally
        {
            window.Close();
        }
    }

    // The selector is normalized: after a step-move deep in the list, the focused row is anchored to the
    // SAME vertical line — the viewport centre — no matter the platform's cover aspect ratio. This is the
    // fix for "sometimes the selector is at the top, sometimes the middle, sometimes the bottom." Portrait
    // PSP (0.581) and wide SNES (1.434) rows differ hugely in height, yet both settle the focused tile on
    // the centre line, so the selector position is predictable across platforms.
    [AvaloniaTheory]
    [InlineData("psp")]        // tall portrait cover
    [InlineData("snes")]       // short wide cover
    [InlineData("playstation2")] // standard disc box
    public async Task SelectorIsCentered_DeepInList_RegardlessOfAspectRatio(string systemId)
    {
        var system = KnownSystems.All.First(s => s.Id == systemId);
        var viewModel = new MainViewModel();
        await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
        viewModel.IsGamepadMode = true;

        var games = Enumerable.Range(0, 200)
            .Select(index => new GameViewModel(
                new Game
                {
                    Id = index + 1,
                    SystemId = system.Id,
                    Path = $"/Games/{system.Id}/Game {index + 1}.bin",
                    Title = $"Game {index + 1}",
                    IsAvailable = true,
                    DateAdded = DateTimeOffset.UtcNow,
                },
                system.Name,
                system.ShortName,
                system.AccentColor,
                coverAspectRatio: system.CoverAspectRatio))
            .ToArray();
        viewModel.Games.ReplaceAll(games);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = games[0];

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await Pump();

            // Land deep in the list (a big jump), then take ONE d-pad step so the reveal centres the new
            // row — a row far from either end, where centring is not clamped.
            viewModel.FocusedGame = games[100];
            await Pump();
            viewModel.DispatchGamepadAction(GamepadAction.NavigateDown);
            await SettleScroll(window);

            var scroller = GamepadScroller(window);
            var viewportCentreY = scroller.TranslatePoint(new Point(0, scroller.Viewport.Height / 2), window)!.Value.Y;

            var focused = viewModel.FocusedGame!;
            var tile = RealizedTiles(window).First(t => ReferenceEquals(t.Game, focused)).Tile;
            var tileCentreY = tile.TranslatePoint(new Point(0, tile.Bounds.Height / 2), window)!.Value.Y;

            var offset = Math.Abs(tileCentreY - viewportCentreY);
            _output.WriteLine(
                $"system={systemId} tileH={tile.Bounds.Height:F0} viewportCentre={viewportCentreY:F0} " +
                $"tileCentre={tileCentreY:F0} offset={offset:F0}");

            // The focused tile sits on the centre line (a small, aspect-independent bias from the row's
            // 10/18 top/bottom gutters aside); top- or bottom-anchoring would miss by well over 200px.
            Assert.True(offset < 40, $"selector for {systemId} is {offset:F0}px off centre (expected < 40)");
        }
        finally
        {
            window.Close();
        }
    }

    // Invariant guard: RevealFocusedGame also fires on overlay/selection changes and while a text overlay
    // is open (its box holds live keyboard input). The grid reveal must never disturb that focus — it
    // takes no focus of its own. Opening search and then changing the focused game (as filtering does)
    // must leave focus on the search box, so the on-screen keyboard keeps typing into it.
    [AvaloniaFact]
    public async Task Reveal_DoesNotStealFocus_FromOpenSearchBox()
    {
        var system = KnownSystems.All.First(s => s.Id == "playstation2");
        var viewModel = new MainViewModel();
        await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
        viewModel.IsGamepadMode = true;

        var games = Enumerable.Range(0, 40)
            .Select(index => new GameViewModel(
                new Game
                {
                    Id = index + 1,
                    SystemId = system.Id,
                    Path = $"/Games/{system.Id}/Game {index + 1}.bin",
                    Title = $"Game {index + 1}",
                    IsAvailable = true,
                    DateAdded = DateTimeOffset.UtcNow,
                },
                system.Name,
                system.ShortName,
                system.AccentColor,
                coverAspectRatio: system.CoverAspectRatio))
            .ToArray();
        viewModel.Games.ReplaceAll(games);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = games[0];

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await Pump();

            // Open the gamepad search overlay and put focus on its text box, as the window does.
            viewModel.GamepadOverlay = GamepadOverlayKind.Search;
            await Pump();
            var searchBox = window.GetVisualDescendants().OfType<TextBox>()
                .First(box => box.Name == "GamepadSearchBox");
            searchBox.Focus();
            await Pump();
            Assert.True(searchBox.IsFocused, "precondition: the search box should hold focus");

            // Filtering while typing changes the focused game, which fires RevealFocusedGame synchronously.
            viewModel.FocusedGame = games[5];
            await Pump();

            Assert.True(searchBox.IsFocused,
                "reveal stole focus from the open search box — the d-pad would swallow typing");
        }
        finally
        {
            window.Close();
        }
    }

    private static ScrollViewer GamepadScroller(Window window) =>
        window.GetVisualDescendants().OfType<ListBox>()
            .First(list => list.Name == "GamepadRowList")
            .GetVisualDescendants().OfType<ScrollViewer>().First();

    // Pump until the eased scroll offset stops moving, so an assertion reads the settled position rather
    // than a mid-ease frame. The ease advances one step per Render flush, so this converges quickly.
    private static async Task<double> SettleScroll(Window window)
    {
        var scroller = GamepadScroller(window);
        var last = double.NaN;
        for (var i = 0; i < 60; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            var y = scroller.Offset.Y;
            if (!double.IsNaN(last) && Math.Abs(y - last) < 0.5)
                return y;
            last = y;
        }

        return scroller.Offset.Y;
    }

    // The focus ring is part of each tile (Border.gamepad-focus-tile-ring, shown via opacity on the
    // .focused state). "The ring is on the focused game" therefore means: the focused tile's ring is
    // opaque and no other realized tile's ring is. Position is guaranteed by construction — the ring is
    // a child of the tile's cover — so there is nothing to reposition and nothing that can drift.
    private static void CheckRing(
        MainViewModel viewModel,
        Window window,
        GamepadAction action,
        int beforeIndex,
        List<string> problems)
    {
        var focused = viewModel.FocusedGame!;
        var index = viewModel.Games.IndexOf(focused);
        var tiles = RealizedTiles(window);
        var focusedTile = tiles.FirstOrDefault(t => ReferenceEquals(t.Game, focused)).Tile;
        if (focusedTile is null)
        {
            problems.Add($"after {action} focus={index}: focused tile not realized");
            return;
        }
        var focusRing = focusedTile.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("gamepad-focus-tile-ring"));
        if (focusRing is null || focusRing.Opacity < 0.99)
        {
            problems.Add($"after {action} (from {beforeIndex}) focus={index}: focused tile's ring not shown (opacity={focusRing?.Opacity ?? -1})");
        }

        // No other realized tile may show its ring.
        foreach (var (game, tile, _) in tiles)
        {
            if (ReferenceEquals(game, focused))
                continue;
            var otherRing = tile.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("gamepad-focus-tile-ring"));
            if (otherRing is { Opacity: > 0.01 })
                problems.Add($"after {action} focus={index}: non-focused tile ({(game.Title)}) still shows its ring (opacity={otherRing.Opacity})");
        }
    }

    private static async Task Pump()
    {
        // Flush low priorities too: the selector reveal is posted at Input/Loaded, which are LOWER
        // than Render in Avalonia, so a Render-only pump would leave the ring update pending.
        for (var i = 0; i < 8; i++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }
}
