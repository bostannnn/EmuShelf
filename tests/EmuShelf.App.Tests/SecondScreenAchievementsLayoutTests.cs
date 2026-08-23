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
/// The Thor companion's achievements overlay is a badge grid with the game title and status pinned to
/// the BOTTOM (a redesign to match the gamepad achievements screen). This guards that layout — the badge
/// grid fills the panel above the title/status footer — and that the grid is row-virtualized: the flat
/// set is sliced into rows of the width-derived column count.
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
            AchievementsStatus = "Updated just now",
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
    [InlineData(1240, 520)]
    [InlineData(960, 420)]
    public async Task AchievementsPanel_PutsTitleAndStatusBelowBadgeGrid(double width, double height)
    {
        var model = BuildModel(achievementCount: 24);
        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = width, Height = height };
        window.Show();
        try
        {
            await SettleLayoutAsync();

            var list = view.FindControl<ListBox>("AchievementsBadgeList");
            var footer = view.FindControl<Grid>("AchievementsFooter");
            Assert.NotNull(list);
            Assert.NotNull(footer);
            Assert.True(list!.IsVisible);
            Assert.True(footer!.IsVisible);

            // The title/status footer sits BELOW the badge grid — that is the whole point of the redesign.
            var listBottom = list.TranslatePoint(new Point(0, list.Bounds.Height), view)!.Value.Y;
            var footerTop = footer.TranslatePoint(default, view)!.Value.Y;
            Assert.True(
                footerTop >= listBottom - 1,
                $"Footer top {footerTop} should be at/below the badge grid bottom {listBottom}.");

            // The bottom bar carries the game title and its status line.
            var footerTexts = footer.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            Assert.Contains("Sonic the Hedgehog", footerTexts);
            Assert.Contains("Updated just now", footerTexts);

            // Every achievement is present, sliced into a grid of rows (not a single column) whose width is
            // driven by the viewport.
            Assert.Equal(24, model.AchievementRows.Sum(row => row.Count));
            Assert.True(model.AchievementColumnCount > 1, "Wide panel should render multiple badge columns.");

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
    public async Task AchievementsGrid_VirtualizesLargeSets()
    {
        // A 400-achievement set must not realize a tile per achievement in a short viewport — the row list
        // virtualizes, so only a bounded number of rows (and their badge tiles) are realized at once.
        var model = BuildModel(achievementCount: 400);
        var view = new SecondScreenView { DataContext = model };
        var window = new Window { Content = view, Width = 1240, Height = 520 };
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
}
