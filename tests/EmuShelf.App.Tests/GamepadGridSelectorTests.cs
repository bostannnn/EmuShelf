using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Media;
using EmuShelf.App.Controls;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// Renders the real gamepad grid and asserts the focus ring stays glued to FocusedGame — including
/// under the input-priority flood (fast d-pad / LB-RB) that used to strand a reveal posted at
/// DispatcherPriority.Input — and that the justified rows still virtualize.
/// </summary>
public class GamepadGridSelectorTests
{
    private readonly ITestOutputHelper _output;

    public GamepadGridSelectorTests(ITestOutputHelper output) => _output = output;

    // Realized gamepad tiles (only visible rows exist under virtualization), each with its game and its
    // top-left in window coordinates.
    private static IReadOnlyList<(GameViewModel Game, Button Tile, Point TopLeft)> RealizedTiles(Window window) =>
        window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("gamepad-game") && b.DataContext is GameViewModel)
            .Select(b => ((GameViewModel)b.DataContext!, b, b.TranslatePoint(new Point(0, 0), window) ?? new Point(double.NaN, double.NaN)))
            .Where(t => !double.IsNaN(t.Item3.X))
            .ToList();

    // Proves the grid virtualizes and tile visual trees recycle: for a 300-game library only a bounded
    // viewport buffer is materialized, and scrolling to the bottom rebinds existing tiles rather than
    // rebuilding their deep cover trees — so cost is flat wherever you are in the list.
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

            // Walk focus down row by row — the d-pad-hold path whose per-row realization cost is the
            // thing this grid optimizes. Each realized Button is remembered with the game it showed:
            // a row scrolling in must REBIND a pooled tile (same Button, new game), not build a new
            // tile tree. (A single large jump legitimately double-buffers fresh containers, so only
            // this gradual walk asserts instance reuse.)
            var seen = new Dictionary<Button, GameViewModel>();
            foreach (var t in atTop)
                seen[t.Tile] = t.Game;
            var rebound = false;
            for (var row = 1; row < viewModel.GamepadRows.Count && row <= 12; row++)
            {
                viewModel.FocusedGame = viewModel.GamepadRows[row][0];
                await Pump();
                foreach (var t in RealizedTiles(window))
                {
                    if (seen.TryGetValue(t.Tile, out var was) && !ReferenceEquals(was, t.Game))
                        rebound = true;
                    seen[t.Tile] = t.Game;
                }
            }
            Assert.True(rebound, "no tile Button was ever rebound to a different game during a 12-row walk — rows are rebuilding tile trees instead of recycling them");

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
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
            // Let the momentum glide finish before asserting: the burst intentionally outruns the
            // reveal, and the ring/realization contract holds at the SETTLED offset (the glide's
            // convergence needs more headless render ticks than one fixed Pump provides).
            await SettleScroll(window);
            await Pump();
            CheckRing(viewModel, window, GamepadAction.NavigateDown, -1, problems);

            foreach (var problem in problems)
                _output.WriteLine(problem);
            _output.WriteLine($"problems={problems.Count}");
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

    // A fast held Down deep into a long library: d-pad auto-repeat outruns the glide, so each revealed row
    // is not yet materialized when its reveal runs — the path that used to fall through to a ScrollIntoView
    // snap (the residual Up/Down jank Left/Right never had). The fix keeps easing, position-relative to the
    // still-realized previous row, so the vertical scroll stays one continuous glide. This asserts the
    // invariant that guards: through the whole burst focus is never scrolled off-screen (its tile stays
    // realized), and once released the focused row settles on the centre line like a single step does.
    [AvaloniaFact]
    public async Task FastDownBurst_GlidesIntoUnrealizedRows_AndSettlesCentered()
    {
        var system = KnownSystems.All.First(s => s.Id == "playstation2");
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

            // Fire a long run of Down flushing ONLY Render between moves, so focus races ahead of the
            // easing offset and lands on rows below the realized set — exactly the fast-hold regime.
            for (var i = 0; i < 24; i++)
            {
                viewModel.DispatchGamepadAction(GamepadAction.NavigateDown);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            }
            await SettleScroll(window);
            // Let the final centred row realize so its tile can be measured.
            await Pump();

            var focused = viewModel.FocusedGame!;
            var match = RealizedTiles(window).FirstOrDefault(t => ReferenceEquals(t.Game, focused));
            Assert.NotNull(match.Tile); // focus was never scrolled off-screen during the burst

            var scroller = GamepadScroller(window);
            var viewportCentreY = scroller.TranslatePoint(new Point(0, scroller.Viewport.Height / 2), window)!.Value.Y;
            var tileCentreY = match.Tile.TranslatePoint(new Point(0, match.Tile.Bounds.Height / 2), window)!.Value.Y;
            var offset = Math.Abs(tileCentreY - viewportCentreY);
            _output.WriteLine($"final focus={viewModel.Games.IndexOf(focused)} offset={offset:F0}");

            Assert.True(offset < 40, $"after a fast Down burst the focused tile is {offset:F0}px off centre (expected < 40)");
        }
        finally
        {
            window.Close();
        }
    }

    private static ScrollViewer GamepadScroller(Window window) =>
        window.GetVisualDescendants().OfType<ScrollViewer>()
            .First(scroller => scroller.Name == "GamepadRowList");

    // Pump until the eased scroll offset stops moving, so an assertion reads the settled position rather
    // than a mid-ease frame. The ease advances one step per Render flush, so this converges quickly.
    private static async Task<double> SettleScroll(Window window)
    {
        var scroller = GamepadScroller(window);
        var last = double.NaN;
        for (var i = 0; i < 60; i++)
        {
            // The glide now advances once per RENDERED frame (TopLevel.RequestAnimationFrame), not once
            // per Dispatcher Render job, so drive a real compositor frame before flushing the reveal posts.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
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

    // The edge insets GamepadGridPanel adds for the focus shadow (top) and the overlay dock (bottom)
    // are EXTENT-only. Folding the bottom one into the last row's band made TryGetRowBounds report that
    // row 156px taller than it is, which skewed the reveal's centring (rowTop + rowHeight / 2) for that
    // row alone: the last row settled ~78px above the line every other row rests on, and a library
    // small enough to fit the viewport scrolled anyway and clipped the tops of its only row's covers.
    [AvaloniaTheory]
    // Short viewport, many rows: the couch panel geometry (the Thor) where the last row's centring
    // target falls below the scroller's max offset and a skewed band height shows up.
    [InlineData(40, 460)]
    // One row, in a window TALL ENOUGH TO HOLD IT (a row band plus both edge insets is ~614px, so the
    // viewport has to clear that): the case where the grid must not scroll at all. The height is part
    // of the case, not scenery — at 460 the single row no longer fits, and "did it scroll?" stops
    // being a question with an answer.
    [InlineData(3, 800)]
    public async Task GamepadGrid_EdgeInsets_StayOutOfEveryRowsBand(int gameCount, double windowHeight)
    {
        var system = KnownSystems.All.Single(candidate => candidate.Id == "playstation2");
        var viewModel = new MainViewModel();
        await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
        viewModel.IsGamepadMode = true;
        var games = Enumerable.Range(1, gameCount).Select(index => new GameViewModel(
            new Game
            {
                Id = index,
                SystemId = system.Id,
                Path = $"/Games/playstation2/Game {index}.chd",
                Title = $"Game {index}",
                IsAvailable = true,
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name, system.ShortName, system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio)).ToArray();
        viewModel.Games.ReplaceAll(games);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = games[0];

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = windowHeight };
        window.Show();
        try
        {
            await Pump();
            var panel = window.FindNamed<GamepadGridPanel>("GamepadGridSurface");
            var scroller = window.FindNamed<ScrollViewer>("GamepadRowList");
            Assert.NotNull(panel);
            Assert.NotNull(scroller);
            var rowCount = viewModel.GamepadRows.Count;

            // Uniform library: every row's band is the same height, last one included.
            Assert.True(panel.TryGetRowBounds(rowCount - 1, out var lastTop, out var lastHeight));
            Assert.True(panel.TryGetRowBounds(0, out _, out var firstHeight));
            Assert.Equal(firstHeight, lastHeight, 1);

            // The extent still carries both insets, so the last row can scroll clear of the dock.
            Assert.True(
                scroller.Extent.Height > lastTop + lastHeight,
                "the scrollable extent no longer reserves the dock inset past the last row");

            if (rowCount == 1)
            {
                // One row that fits: nothing to scroll, and in particular the covers must not be
                // pushed up under the rail by an inset the row does not own.
                Assert.True(
                    scroller.Extent.Height <= scroller.Viewport.Height + 1,
                    $"the single row no longer fits a {windowHeight}px window "
                    + $"(extent {scroller.Extent.Height}, viewport {scroller.Viewport.Height}) — "
                    + "raise the InlineData height so this case still tests 'does not scroll'");
                Assert.Equal(0, scroller.Offset.Y, 1);
                return;
            }

            viewModel.FocusedGame = games[^1];
            await Pump();

            // The last row lands on the same viewport line every other row does.
            var centreOnScreen = lastTop + (lastHeight / 2) - scroller.Offset.Y;
            Assert.Equal(scroller.Viewport.Height / 2, centreOnScreen, 1);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task Pump()
    {
        // Flush low priorities too: the selector reveal is posted at Input/Loaded, which are LOWER
        // than Render in Avalonia, so a Render-only pump would leave the ring update pending.
        for (var i = 0; i < 8; i++)
        {
            // Drive a real compositor frame so any in-flight RequestAnimationFrame scroll glide advances,
            // then flush the reveal posts (Input/Loaded, lower than Render) that reposition the ring.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
    }
}
