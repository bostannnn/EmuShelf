using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
