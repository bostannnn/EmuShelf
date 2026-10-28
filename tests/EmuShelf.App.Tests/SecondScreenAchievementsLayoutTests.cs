using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Achievements;
using Xunit;

namespace EmuShelf.App.Tests;

/// <summary>
/// The Thor companion's achievements overlay (touch, square panel): a header carrying the game name +
/// progress + Refresh/Close icons, a row-virtualized badge grid, and — since touch has no hover tooltip —
/// the tapped badge's title/subtitle/points in a detail strip beneath the grid. These guard the
/// header→grid→detail order (no bottom footer), the grid's virtualization, and the tap-to-select behaviour.
/// </summary>
public class SecondScreenAchievementsLayoutTests
{
    private static AchievementRowViewModel Row(int id, bool earned, bool hardcore) =>
        new(
            new RetroAchievementsAchievement(
                id,
                $"Achievement {id}",
                "Description",
                Points: 10,
                BadgeName: "12345",
                DisplayOrder: id,
                DateEarned: earned ? DateTimeOffset.UtcNow : null,
                DateEarnedHardcore: hardcore ? DateTimeOffset.UtcNow : null),
            badges: null,
            loadBadge: false);

    private static SecondScreenViewModel BuildModel(int achievementCount)
    {
        var model = new SecondScreenViewModel
        {
            AchievementsTitle = "Sonic the Hedgehog",
            AchievementsSummary = "12 / 24 · 240 pts",
            CanRefresh = true,
        };
        model.SetAchievements(
            Enumerable.Range(1, achievementCount)
                .Select(i => Row(i, earned: i % 2 == 0, hardcore: i % 5 == 0))
                .ToList());
        model.Overlay = SecondScreenOverlayKind.Achievements;
        return model;
    }

    private static async Task SettleLayoutAsync()
    {
        // The badge list's SizeChanged sets the column count, which re-slices the rows and triggers a
        // second layout pass; pump a few render frames so it settles before we read bounds.
        for (var i = 0; i < 4; i++)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    [AvaloniaTheory]
    [InlineData(1240, 1080)]
    [InlineData(1024, 900)]
    public async Task AchievementsPanel_StacksHeaderGridDetail_NoFooter(double width, double height)
    {
        var model = BuildModel(achievementCount: 24);
        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        try
        {
            await SettleLayoutAsync();

            var header = view.FindControl<Grid>("AchievementsHeader");
            var list = view.FindControl<ListBox>("AchievementsBadgeList");
            var detail = view.FindControl<StackPanel>("AchievementsDetail");
            Assert.NotNull(header);
            Assert.NotNull(list);
            Assert.NotNull(detail);
            Assert.True(list!.IsVisible);

            // The old bottom footer is gone — game name + actions live in the header now.
            Assert.Null(view.FindControl<Grid>("AchievementsFooter"));

            // Order down the panel: header, then the badge grid, then the selected-badge detail strip.
            var headerBottom = header!.TranslatePoint(new Point(0, header.Bounds.Height), view)!.Value.Y;
            var listTop = list.TranslatePoint(default, view)!.Value.Y;
            var listBottom = list.TranslatePoint(new Point(0, list.Bounds.Height), view)!.Value.Y;
            var detailTop = detail!.TranslatePoint(default, view)!.Value.Y;
            Assert.True(
                listTop >= headerBottom - 1,
                $"Grid top {listTop} should be at/below the header bottom {headerBottom}.");
            Assert.True(
                detailTop >= listBottom - 1,
                $"Detail strip top {detailTop} should be at/below the badge grid bottom {listBottom}.");

            // The header carries the game name and the progress summary.
            var headerTexts = header.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains("Sonic the Hedgehog", headerTexts);
            Assert.Contains("12 / 24 · 240 pts", headerTexts);

            // First badge is auto-selected, so the detail strip shows its title/description (touch's answer
            // to the grid's missing hover tooltip).
            Assert.True(detail.IsVisible);
            var detailTexts = detail.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains("Achievement 1", detailTexts);
            Assert.Contains("Description", detailTexts);

            // Every achievement is present, sliced into a grid of rows (not a single column) whose width is
            // driven by the viewport.
            Assert.Equal(24, model.AchievementRows.Sum(row => row.Count));
            Assert.True(model.AchievementColumnCount > 1, "Panel should render multiple badge columns.");

            // The grid actually realized badge tiles (all rows fit these window sizes).
            var tiles = list.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("ss-badge"));
            Assert.Equal(24, tiles);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AchievementsGrid_FillsTheRowFlushWithTheHeader_AtThorPanelSize()
    {
        // The Thor's Screen-2 is 1240×1080 px at density 369 → 538×468 dip. There the grid must be six columns
        // wide and its outer tiles must line up with the header: the leftmost tile under the game name, the
        // rightmost under the Close button. It used to stop five columns in with a dead strip (and a stray
        // scrollbar line) on the right, and its tiles sat 5px in from the text above and below.
        var model = BuildModel(achievementCount: 24);
        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = 538, Height = 468 };
        window.Show();
        try
        {
            await SettleLayoutAsync();

            Assert.Equal(6, model.AchievementColumnCount);

            var header = view.FindControl<Grid>("AchievementsHeader")!;
            var list = view.FindControl<ListBox>("AchievementsBadgeList")!;
            var headerLeft = header.TranslatePoint(default, view)!.Value.X;
            var headerRight = header.TranslatePoint(new Point(header.Bounds.Width, 0), view)!.Value.X;
            var tiles = list.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("ss-badge"))
                .ToList();
            var rowTop = tiles.Min(t => t.TranslatePoint(default, view)!.Value.Y);
            var firstRow = tiles.Where(t => Math.Abs(t.TranslatePoint(default, view)!.Value.Y - rowTop) < 1).ToList();
            Assert.Equal(6, firstRow.Count);

            var left = firstRow.Min(t => t.TranslatePoint(default, view)!.Value.X);
            var right = firstRow.Max(t => t.TranslatePoint(new Point(t.Bounds.Width, 0), view)!.Value.X);
            Assert.Equal(headerLeft, left, 0.5);
            // Tiles are snapped down to whole device pixels, so up to a pixel per column may be left over.
            Assert.InRange(headerRight - right, -0.5, 6);

            // No scrollbar is drawn beside the last column; the grid still scrolls.
            Assert.DoesNotContain(
                list.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>(),
                bar => bar.IsEffectivelyVisible && bar.Orientation == Avalonia.Layout.Orientation.Vertical);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task OpeningThePanel_MovesNothingUnderneath()
    {
        // Opening used to collapse the dock bar, which re-centred the game logo in the taller spotlight row:
        // the logo jumped down a dock's height just before the sheet appeared (and back up on close). The dock
        // now fades in place and the logo fades out under the sheet instead.
        var model = BuildModel(achievementCount: 12);
        model.Overlay = SecondScreenOverlayKind.None;
        model.SetSpotlight(new WriteableBitmap(new PixelSize(64, 32), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888), null);
        model.LogoOpacity = 1;
        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = 538, Height = 468 };
        window.Show();
        try
        {
            await SettleLayoutAsync();
            var dock = view.FindControl<Border>("CompanionDock")!;
            var logo = view.GetVisualDescendants().OfType<Image>()
                .First(image => ReferenceEquals(image.Source, model.WheelImage) && image.IsEffectivelyVisible);
            var dockBefore = dock.TranslatePoint(default, view)!.Value;
            var logoBefore = logo.TranslatePoint(default, view)!.Value;

            model.Overlay = SecondScreenOverlayKind.Achievements;
            await SettleLayoutAsync();

            Assert.True(dock.IsVisible, "the dock must keep its place in the layout");
            Assert.Contains("away", dock.Classes);
            Assert.False(dock.IsHitTestVisible);
            Assert.Equal(dockBefore, dock.TranslatePoint(default, view)!.Value);
            Assert.Equal(logoBefore, logo.TranslatePoint(default, view)!.Value);
            Assert.Equal(0, model.SpotlightLogoOpacity);

            // The sheet covers the dock's area too, so the grid still gets the full panel height. (Layout
            // bounds: the sheet's scale-in transition may still be running.)
            var sheet = view.FindControl<Border>("AchievementsSheet")!;
            Assert.True(sheet.Bounds.Bottom >= dock.Bounds.Bottom - 6, $"sheet ends at {sheet.Bounds.Bottom}, dock at {dock.Bounds.Bottom}");
        }
        finally
        {
            window.Close();
            model.SetSpotlight(null, null);
        }
    }

    [AvaloniaFact]
    public async Task StatusMessage_FloatsOverTheGrid_WithoutMovingIt()
    {
        // "Refreshing achievement details…" used to be a line ABOVE the grid that appeared and disappeared a
        // second later, shoving every badge down and back up. Refreshing is now the spinning Refresh icon, and
        // the remaining messages (offline, reconnect, errors) float as a pill over the bottom of the grid.
        var model = BuildModel(achievementCount: 24);
        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = 538, Height = 468 };
        window.Show();
        try
        {
            await SettleLayoutAsync();
            var list = view.FindControl<ListBox>("AchievementsBadgeList")!;
            var before = new Rect(list.TranslatePoint(default, view)!.Value, list.Bounds.Size);

            model.AchievementsStatus = "Offline — showing cached achievement details.";
            await SettleLayoutAsync();

            Assert.Equal(before, new Rect(list.TranslatePoint(default, view)!.Value, list.Bounds.Size));
            var pill = view.FindControl<Border>("AchievementsStatusPill")!;
            Assert.True(pill.IsEffectivelyVisible);
            var pillBottom = pill.TranslatePoint(new Point(0, pill.Bounds.Height), view)!.Value.Y;
            Assert.True(pillBottom <= before.Bottom, "the pill sits inside the grid area");

            model.AchievementsStatus = null;
            model.IsAchievementsRefreshing = true;
            await SettleLayoutAsync();
            Assert.Equal(before, new Rect(list.TranslatePoint(default, view)!.Value, list.Bounds.Size));
            Assert.Contains("ss-spin", view.FindControl<PathIcon>("AchievementsRefreshIcon")!.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void RefreshSpinner_RunsOnlyWhileThePanelIsUp()
    {
        // The sheet stays mounted when closed, so an animation bound to the bare flag would keep spinning
        // invisibly behind the spotlight (a perpetual animation is expensive on the Thor).
        var model = BuildModel(achievementCount: 6);
        model.IsAchievementsRefreshing = true;
        Assert.True(model.IsRefreshSpinning);

        model.Overlay = SecondScreenOverlayKind.None;
        Assert.False(model.IsRefreshSpinning);
    }

    [AvaloniaFact]
    public void Refresh_KeepsLoadedBadgesAndTheSelection()
    {
        // When fresh details land for the set on screen, the replacement rows take over the badges that were
        // already decoded — the grid used to blank back to placeholder trophies and pop every badge in again —
        // and the selected achievement stays selected (and the grid stays scrolled where it was).
        var model = new SecondScreenViewModel();
        model.SetAchievements(Enumerable.Range(1, 6).Select(i => DistinctBadgeRow(i)).ToList());
        var rows = model.AchievementRows.SelectMany(row => row).ToList();
        var bitmaps = rows.Select(row =>
        {
            var bitmap = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888);
            Assert.True(row.AdoptBadge(bitmap));
            return bitmap;
        }).ToList();
        model.SelectAchievement(rows[2]);
        var scrolls = new List<int>();
        model.SelectionScrolledTo += scrolls.Add;

        model.SetAchievements(Enumerable.Range(1, 6).Select(i => DistinctBadgeRow(i)).ToList());

        var refreshed = model.AchievementRows.SelectMany(row => row).ToList();
        Assert.Equal(bitmaps, refreshed.Select(row => row.Badge));
        Assert.All(rows, old => Assert.Null(old.Badge));
        Assert.Equal("3", model.SelectedAchievement!.AchievementId);
        Assert.Empty(scrolls);

        // A different set starts over: first badge, scrolled to the top.
        model.SetAchievements(Enumerable.Range(100, 4).Select(i => DistinctBadgeRow(i)).ToList());
        Assert.Equal("100", model.SelectedAchievement!.AchievementId);
        Assert.Equal(new[] { 0 }, scrolls);
        model.ClearAchievements();
    }

    [AvaloniaFact]
    public async Task Badges_LoadTogether_InOneBatch()
    {
        // Loaded row by row, every badge landed on its own frame and the grid filled in one random tile at a
        // time. A set's badges now load together and are published in one go.
        var path = Path.Combine(Path.GetTempPath(), $"emushelf-badge-{Guid.NewGuid():N}.png");
        using (var source = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888))
            source.Save(path);
        try
        {
            var cache = new StubBadgeCache(path);
            var rows = Enumerable.Range(1, 30).Select(i => DistinctBadgeRow(i, cache)).ToList();
            var published = new List<int>();
            foreach (var row in rows)
                row.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(AchievementRowViewModel.Badge))
                        published.Add(Environment.CurrentManagedThreadId);
                };

            await AchievementRowViewModel.LoadBadgesTogetherAsync(rows, TimeSpan.FromSeconds(5));

            Assert.All(rows, row => Assert.NotNull(row.Badge));
            Assert.Equal(30, published.Count);
            Assert.Equal(30, cache.Requests);

            // Rows that already have their badge are not loaded again.
            await AchievementRowViewModel.LoadBadgesTogetherAsync(rows, TimeSpan.FromSeconds(5));
            Assert.Equal(30, cache.Requests);
            rows.ForEach(row => row.Dispose());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task Badges_LoadInOrder_AndACancelledSetStopsAsking()
    {
        // Browsing the library with the panel open replaces the set every time the selection settles. A set
        // whose badges are not cached yet must not queue a download for every badge it has: only a few are
        // ever in flight, the first rows go first, and a replaced set asks for nothing more.
        var gate = new TaskCompletionSource();
        var cache = new GatedBadgeCache(gate.Task);
        var rows = Enumerable.Range(1, 100).Select(i => DistinctBadgeRow(i, cache)).ToList();
        using var cancel = new System.Threading.CancellationTokenSource();

        var load = AchievementRowViewModel.LoadBadgesTogetherAsync(rows, TimeSpan.FromMilliseconds(20), cancel.Token);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (cache.Requested.Count < 6 && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        await Task.Delay(50);
        var inFlight = cache.Requested.ToArray();
        cancel.Cancel();
        gate.SetResult();
        await load;

        Assert.InRange(inFlight.Length, 1, 6);
        Assert.All(inFlight, name => Assert.True(int.Parse(name["badge".Length..]) <= 6, $"{name} went before the first rows"));
        Assert.Equal(inFlight.Length, cache.Requested.Count);
        Assert.All(rows, row => Assert.Null(row.Badge));
    }

    private sealed class GatedBadgeCache(Task gate) : IRetroAchievementsBadgeCache
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Requested { get; } = new();

        public async Task<string?> GetBadgePathAsync(string badgeName, System.Threading.CancellationToken cancellationToken = default)
        {
            Requested.Enqueue(badgeName);
            await gate;
            return null;
        }
    }

    private sealed class StubBadgeCache(string path) : IRetroAchievementsBadgeCache
    {
        private int _requests;
        public int Requests => _requests;

        public Task<string?> GetBadgePathAsync(string badgeName, System.Threading.CancellationToken cancellationToken = default)
        {
            System.Threading.Interlocked.Increment(ref _requests);
            return Task.FromResult<string?>(path);
        }
    }

    private static AchievementRowViewModel DistinctBadgeRow(int id, IRetroAchievementsBadgeCache? badges = null) =>
        new(
            new RetroAchievementsAchievement(
                id,
                $"Achievement {id}",
                "Description",
                Points: 10,
                BadgeName: $"badge{id}",
                DisplayOrder: id,
                DateEarned: null,
                DateEarnedHardcore: null),
            badges,
            loadBadge: false);

    [AvaloniaFact]
    public async Task BadgeRings_ResolveByState_SelectedBeatsHardcoreBeatsDefault()
    {
        // Guards the Avalonia local-value-vs-style trap: the tile's default border must live in a base
        // Border.ss-badge style (not as a local BorderThickness on the tile), or the .hardcore / .selected
        // thickness overrides are silently ignored and every ring renders at 1px.
        var model = BuildModel(achievementCount: 6); // ids: 1 plain+selected, 2 earned, 5 hardcore
        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            await SettleLayoutAsync();

            var tiles = view.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("ss-badge"))
                .ToDictionary(b => ((AchievementRowViewModel)b.DataContext!).Title);

            // Achievement 1 is auto-selected → 3px accent ring; a plain unselected tile stays 1px; the
            // hardcore tile (id 5) gets the 2px gold ring.
            Assert.Equal(3d, tiles["Achievement 1"].BorderThickness.Top);
            Assert.Equal(1d, tiles["Achievement 2"].BorderThickness.Top);
            Assert.Equal(2d, tiles["Achievement 5"].BorderThickness.Top);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AchievementsGrid_VirtualizesLargeSets()
    {
        // A 400-achievement set must not realize a tile per achievement — the row list virtualizes, so only
        // a bounded number of rows (and their badge tiles) are realized at once.
        var model = BuildModel(achievementCount: 400);
        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = 1240, Height = 1080 };
        window.Show();
        try
        {
            await SettleLayoutAsync();

            var list = view.FindControl<ListBox>("AchievementsBadgeList");
            Assert.NotNull(list);
            Assert.Equal(400, model.AchievementRows.Sum(row => row.Count));

            var realizedTiles = list!.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("ss-badge"));
            Assert.InRange(realizedTiles, 1, 200); // far fewer than 400 — the off-screen rows never realized
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void SetAchievements_AutoSelectsFirstBadge()
    {
        var model = BuildModel(achievementCount: 6);

        Assert.NotNull(model.SelectedAchievement);
        Assert.Equal("Achievement 1", model.SelectedAchievement!.Title);
        Assert.True(model.SelectedAchievement.IsFocused);
        Assert.True(model.HasSelectedAchievement);
        // Achievement 1 is locked (odd id), so the meta is points only — no "Earned" and no state word.
        Assert.Equal("10 pts", model.SelectedAchievementMeta);
    }

    [Fact]
    public void SelectedAchievementMeta_ShowsEarnedDate_ForUnlocked_AndOmitsState()
    {
        var model = BuildModel(achievementCount: 6);
        var earned = model.AchievementRows.SelectMany(row => row).First(r => r.Title == "Achievement 2");

        model.SelectAchievement(earned);

        Assert.StartsWith("10 pts · Earned", model.SelectedAchievementMeta);
        // The redundant "Hardcore"/"Softcore"/"Locked" word is intentionally not in the line.
        Assert.DoesNotContain("Hardcore", model.SelectedAchievementMeta!);
        Assert.DoesNotContain("Softcore", model.SelectedAchievementMeta!);
    }

    [Fact]
    public void InlineStatus_ShowsOnlyWithStatusOverALoadedGrid()
    {
        var model = BuildModel(achievementCount: 6);

        // A loaded grid with no status (the normal case) shows no inline status line.
        Assert.False(model.HasInlineStatus);

        // A transient status over a loaded grid does show.
        model.AchievementsStatus = "Refreshing achievement details…";
        Assert.True(model.HasInlineStatus);

        // But an empty/message state uses the centered message instead, not the inline line.
        model.ClearAchievements();
        Assert.False(model.HasAchievements);
        Assert.False(model.HasInlineStatus);
    }

    [Fact]
    public void SelectAchievement_MovesFocusAndDetailToTappedBadge()
    {
        var model = BuildModel(achievementCount: 6);
        var first = model.SelectedAchievement!;
        var third = model.AchievementRows.SelectMany(row => row).First(r => r.Title == "Achievement 3");

        model.SelectAchievement(third);

        Assert.False(first.IsFocused);
        Assert.True(third.IsFocused);
        Assert.Same(third, model.SelectedAchievement);
        Assert.Equal("Achievement 3", model.SelectedAchievement!.Title);
    }

    [Fact]
    public void Gamepad_DpadWalksSelectionAcrossTheGrid()
    {
        var model = BuildModel(achievementCount: 12);
        model.AchievementColumnCount = 4; // 3 rows of 4
        // Auto-selected on the first badge.
        Assert.Equal("Achievement 1", model.SelectedAchievement!.Title);

        // Right walks along the row.
        Assert.True(model.DispatchGamepadAction(GamepadAction.NavigateRight));
        Assert.Equal("Achievement 2", model.SelectedAchievement!.Title);

        // Down moves one row (stride = columns).
        Assert.True(model.DispatchGamepadAction(GamepadAction.NavigateDown));
        Assert.Equal("Achievement 6", model.SelectedAchievement!.Title);

        // Left walks back.
        Assert.True(model.DispatchGamepadAction(GamepadAction.NavigateLeft));
        Assert.Equal("Achievement 5", model.SelectedAchievement!.Title);

        // Up returns to the first row.
        Assert.True(model.DispatchGamepadAction(GamepadAction.NavigateUp));
        Assert.Equal("Achievement 1", model.SelectedAchievement!.Title);
    }

    [Fact]
    public void Gamepad_DpadStopsAtRowAndGridEdges()
    {
        var model = BuildModel(achievementCount: 12);
        model.AchievementColumnCount = 4;

        // Left on the first column of the first row is a no-op (no wrap to the previous row).
        Assert.True(model.DispatchGamepadAction(GamepadAction.NavigateLeft));
        Assert.Equal("Achievement 1", model.SelectedAchievement!.Title);

        // Up on the top row is a no-op.
        Assert.True(model.DispatchGamepadAction(GamepadAction.NavigateUp));
        Assert.Equal("Achievement 1", model.SelectedAchievement!.Title);

        // Move to the last column, then Right must not jump to the next row.
        model.DispatchGamepadAction(GamepadAction.NavigateRight);
        model.DispatchGamepadAction(GamepadAction.NavigateRight);
        model.DispatchGamepadAction(GamepadAction.NavigateRight);
        Assert.Equal("Achievement 4", model.SelectedAchievement!.Title);
        Assert.True(model.DispatchGamepadAction(GamepadAction.NavigateRight));
        Assert.Equal("Achievement 4", model.SelectedAchievement!.Title);
    }

    [Fact]
    public void Gamepad_RaisesScrollRequestOnMove_NotOnEdgeNoOp()
    {
        var model = BuildModel(achievementCount: 12);
        model.AchievementColumnCount = 4;
        var scrolls = new List<int>();
        model.SelectionScrolledTo += scrolls.Add;

        model.DispatchGamepadAction(GamepadAction.NavigateRight); // 1 -> 2 (index 1)
        model.DispatchGamepadAction(GamepadAction.NavigateDown);  // -> 6 (index 5)
        Assert.Equal(new[] { 1, 5 }, scrolls);

        scrolls.Clear();
        // Up-up walks to the top row then a blocked edge — only the first move should scroll.
        model.DispatchGamepadAction(GamepadAction.NavigateUp);   // index 5 -> 1
        model.DispatchGamepadAction(GamepadAction.NavigateUp);   // blocked (top row)
        Assert.Equal(new[] { 1 }, scrolls);
    }

    [Fact]
    public void Gamepad_CancelClosesOpenOverlay_AndConfirmIsSwallowed()
    {
        var model = BuildModel(achievementCount: 6);
        var closed = 0;
        model.OverlayClosed = () => closed++;

        // Confirm is swallowed (the detail strip already shows the badge) but doesn't close the panel.
        Assert.True(model.DispatchGamepadAction(GamepadAction.Confirm));
        Assert.Equal(0, closed);

        // Cancel closes the open achievements overlay (Back behaviour).
        Assert.True(model.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Gamepad_SwallowsNonNavActionsWhileGridIsUp_SoNothingLeaksToTheCouch()
    {
        // With the grid up the second screen owns the pad completely: Search/Actions/Menu/platform switches
        // must be consumed here, not fall through to the couch and open an overlay on the unseen main screen.
        var model = BuildModel(achievementCount: 6);

        Assert.True(model.DispatchGamepadAction(GamepadAction.Search));
        Assert.True(model.DispatchGamepadAction(GamepadAction.Actions));
        Assert.True(model.DispatchGamepadAction(GamepadAction.Menu));
        Assert.True(model.DispatchGamepadAction(GamepadAction.NextPlatform));
        Assert.True(model.DispatchGamepadAction(GamepadAction.PreviousPlatform));

        // But an empty "select a game" panel (no badges) lets the couch keep the pad, so the user can still
        // browse to a game that has achievements.
        model.ClearAchievements();
        Assert.True(model.IsAchievementsOpen);
        Assert.False(model.HasAchievements);
        Assert.False(model.DispatchGamepadAction(GamepadAction.NextPlatform));
        Assert.False(model.DispatchGamepadAction(GamepadAction.NavigateRight));
    }

    [Fact]
    public void Gamepad_FallsThroughWhenNothingNavigable()
    {
        var model = BuildModel(achievementCount: 6);
        model.Overlay = SecondScreenOverlayKind.None; // resting spotlight, panel closed

        // With no navigable overlay up, nav actions are not consumed — the couch keeps the gamepad.
        Assert.False(model.DispatchGamepadAction(GamepadAction.NavigateRight));
        Assert.False(model.DispatchGamepadAction(GamepadAction.Cancel));
    }

    [Fact]
    public void ClearAchievements_DropsSelection()
    {
        var model = BuildModel(achievementCount: 6);
        Assert.NotNull(model.SelectedAchievement);

        model.ClearAchievements();

        Assert.Null(model.SelectedAchievement);
        Assert.False(model.HasSelectedAchievement);
        Assert.Null(model.SelectedAchievementMeta);
    }
}
