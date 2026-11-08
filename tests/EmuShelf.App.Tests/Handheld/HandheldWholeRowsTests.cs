using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Launching;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// Scrolling lists on the Handheld screens show whole rows only (owner, 2026-11-08: All apps cut its top row and the
// selector ring at the top edge, and a half row under the hints). Every realized row is either fully inside the scroller
// or fully outside it, wherever the selection goes. Set EMUSHELF_SNAPSHOT_DIR for the frames.
public partial class HandheldShellViewModelTests
{
    private static void AssertWholeRows(ScrollViewer scroller, ItemsControl items, string where)
    {
        var height = scroller.Bounds.Height;
        Assert.True(height > 0, $"{where}: the scroller has no height");
        for (var index = 0; index < items.ItemCount; index++)
        {
            if (items.ContainerFromIndex(index) is not Control container || !container.IsVisible)
                continue;
            // A virtualized list keeps recycled containers aside, out of the tree: only the ones laid out count.
            if (container.TranslatePoint(new Point(0, 0), scroller) is not { } at)
                continue;
            var top = at.Y;
            var bottom = top + container.Bounds.Height;
            var inside = top >= -0.5 && bottom <= height + 0.5;
            var outside = bottom <= 0.5 || top >= height - 0.5;
            Assert.True(inside || outside, $"{where}: row {index} is cut ({top:0.#}..{bottom:0.#} in a {height:0.#} view)");
        }
    }

    // All apps' scroller does not clip (the lifted ring reaches past its edge): rows not wholly inside it are hidden instead.
    private static void AssertOnlyWholeRowsShow(ScrollViewer scroller, ItemsControl items, string where)
    {
        var shown = 0;
        for (var index = 0; index < items.ItemCount; index++)
        {
            if (items.ContainerFromIndex(index) is not Control container || container.TranslatePoint(default, scroller) is not { } at)
                continue;
            var inside = at.Y >= -0.5 && at.Y + container.Bounds.Height <= scroller.Bounds.Height + 0.5;
            Assert.True(inside ? container.Opacity == 1 : container.Opacity == 0,
                $"{where}: row {index} at {at.Y:0.#} is {(inside ? "hidden inside" : "shown outside")} the view");
            if (inside)
                shown++;
        }
        Assert.True(shown > 0, $"{where}: nothing shows");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllApps_ScrollsInWholeRows_AndNeverCutsARowOrTheRing(bool wide)
    {
        var model = CompanionModel();
        for (var i = 11; i < 34; i++)
            model.Apps.Add(new SecondScreenAppViewModel($"app/{i}", $"App {i}", null));
        var companion = new HandheldCompanionViewModel(model) { IsOnWideScreen = wide };
        var view = new HandheldCompanionView(wide) { DataContext = companion };
        var window = new Window { Content = view, Width = wide ? 1920 : 1240, Height = 1080 };
        window.Show();
        var name = wide ? "wide" : "narrow";
        try
        {
            model.DrawerTitle = "All apps";
            model.Overlay = SecondScreenOverlayKind.Drawer;
            await SettleAsync();
            var scroller = view.FindControl<ScrollViewer>("DrawerScroll")!;
            var apps = view.FindControl<ItemsControl>("DrawerApps")!;
            AssertWholeRows(scroller, apps, "opened");
            AssertOnlyWholeRowsShow(scroller, apps, "opened");
            // Four rows of five (or eight) apps fit under the title, above the hints.
            var pitch = apps.ContainerFromIndex(0)!.Bounds.Height;
            Assert.Equal(216, pitch, 1);
            Assert.Equal(4, (int)Math.Round(scroller.Bounds.Height / pitch));
            Dump(window, $"rows-{name}-drawer-0");

            for (var step = 1; step <= 6; step++)
            {
                Press(companion, GamepadAction.NavigateDown);
                await SettleAsync();
                AssertWholeRows(scroller, apps, $"down {step}");
                AssertOnlyWholeRowsShow(scroller, apps, $"down {step}");
            }
            Dump(window, $"rows-{name}-drawer-1-down");
            Assert.True(scroller.Offset.Y > 0);

            // A drag or the wheel that stops part-way settles on whole rows too: on down the wheel goes on to the next row.
            var settled = scroller.Offset.Y;
            scroller.Offset = new Vector(0, settled - (pitch * 0.4));
            await SettleAsync();
            Assert.Equal(settled - pitch, scroller.Offset.Y, 1);
            scroller.Offset = new Vector(0, scroller.Offset.Y + (pitch * 0.3));
            await SettleAsync();
            Assert.Equal(settled, scroller.Offset.Y, 1);
            AssertOnlyWholeRowsShow(scroller, apps, "after the wheel");

            for (var step = 1; step <= 6; step++)
            {
                Press(companion, GamepadAction.NavigateUp);
                await SettleAsync();
                AssertWholeRows(scroller, apps, $"up {step}");
                AssertOnlyWholeRowsShow(scroller, apps, $"up {step}");
            }
            Dump(window, $"rows-{name}-drawer-2-up");
            // Back on the first row, the scroller is back at its top: the ring's room above the row shows again.
            Assert.Equal(0, scroller.Offset.Y, 1);
        }
        finally
        {
            window.Close();
            companion.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheBadgeGrid_ScrollsInWholeRows(bool wide)
    {
        var model = CompanionModel();
        model.SetAchievements(Enumerable.Range(1, 90).Select(id => CompanionRow(id, earned: id % 4 == 0)).ToList());
        var companion = StartGame(model);
        companion.IsOnWideScreen = wide;
        var view = new HandheldCompanionView(wide) { DataContext = companion };
        var window = new Window { Content = view, Width = wide ? 1920 : 1240, Height = 1080 };
        window.Show();
        try
        {
            Press(companion, GamepadAction.Confirm); // every badge
            await SettleAsync();
            var badges = view.FindControl<ListBox>("BadgeList")!;
            var scroller = (ScrollViewer)badges.Scroll!;
            AssertWholeRows(scroller, badges, "opened");
            for (var step = 0; step < 10; step++)
            {
                Press(companion, GamepadAction.NavigateDown);
                await SettleAsync();
                AssertWholeRows(scroller, badges, $"down {step}");
            }
            Assert.True(scroller.Offset.Y > 0);
            Dump(window, $"rows-{(wide ? "wide" : "narrow")}-badges-down");
            for (var step = 0; step < 10; step++)
            {
                Press(companion, GamepadAction.NavigateUp);
                await SettleAsync();
                AssertWholeRows(scroller, badges, $"up {step}");
            }
        }
        finally
        {
            window.Close();
            companion.Dispose();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheLaunchersLists_ScrollInWholeRows(bool wide)
    {
        var library = new FakeLibrary().WithPlatform("gc", 3).WithPlatform("ps2", 2);
        var game = library.Games["gc"][0];
        library.Details[game.Id] = new HandheldTitleDetails("Dolphin", [1, 2], 1, LaunchScreenDecision.External, true, false);
        library.SettingsFactory = CreateSettingsProjection;
        var provider = new FakeAchievements();
        library.AchievementsFactory = _ =>
        {
            var reference = new AchievementGameRef("fake", "1");
            return new AchievementDetailsViewModel("Okami", reference, provider, new AchievementSnapshot(reference, "me", "Okami",
                [.. Enumerable.Range(0, 30).Select(i => new AchievementEntry($"a{i}", $"Achievement {i}", "Do the thing", $"{1000 + i}",
                    i, i % 3 == 0, Points: 5))], DateTimeOffset.UtcNow), deferBadgeLoading: true);
        };
        library.Games["gc"][0] = game with { AchievementText = "10/30" };
        var shell = await CreateAsync(library);
        var launcher = new HandheldLauncherView(null, pinnedWide: wide) { DataContext = shell };
        var window = new Window { Content = launcher, Width = wide ? 1920 : 1240, Height = 1080 };
        window.Show();
        var name = wide ? "wide" : "narrow";
        try
        {
            // The Title page's sheet: nine rows in a view of eight.
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Actions);
            await SettleAsync();
            var sheet = launcher.FindControl<ScrollViewer>("SheetScroller")!;
            var sheetRows = launcher.FindControl<ItemsControl>("SheetRows")!;
            Assert.True(shell.Sheet!.Items.Count > 8);
            for (var step = 0; step < shell.Sheet.Items.Count; step++)
            {
                AssertWholeRows(sheet, sheetRows, $"sheet {step}");
                Press(shell, GamepadAction.NavigateDown);
                await SettleAsync();
            }
            Assert.True(sheet.Offset.Y > 0);
            Dump(window, $"rows-{name}-sheet-end");
            Press(shell, GamepadAction.Cancel);

            // A long panel: thirty achievements.
            Press(shell, GamepadAction.NavigateRight, 2);
            Press(shell, GamepadAction.Confirm);
            await SettleAsync();
            Assert.IsType<HandheldAchievementsPanel>(shell.Panel);
            var panelView = launcher.FindControl<EmuShelf.App.Views.Handheld.HandheldPanelView>("PanelView")!;
            var panelScroller = panelView.FindControl<ScrollViewer>("PanelScroller")!;
            var panelRows = panelView.FindControl<ItemsControl>("PanelRows")!;
            for (var step = 0; step < 14; step++)
            {
                Press(shell, GamepadAction.NavigateDown);
                await SettleAsync();
                AssertWholeRows(panelScroller, panelRows, $"panel down {step}");
            }
            Assert.True(panelScroller.Offset.Y > 0);
            Dump(window, $"rows-{name}-panel-down");
            for (var step = 0; step < 14; step++)
            {
                Press(shell, GamepadAction.NavigateUp);
                await SettleAsync();
                AssertWholeRows(panelScroller, panelRows, $"panel up {step}");
            }
            Press(shell, GamepadAction.Cancel);
            Press(shell, GamepadAction.Cancel);
            Press(shell, GamepadAction.Cancel);

            // Settings: down through a long section.
            await shell.OpenSettingsAsync();
            await SettleAsync();
            var settings = shell.Settings!;
            var rowScroller = launcher.FindControl<ScrollViewer>("SettingsRowScroller")!;
            var rows = launcher.FindControl<ItemsControl>("SettingsRows")!;
            var longest = 0;
            for (var section = 0; section < settings.Sections.Count; section++)
            {
                for (var step = 0; step < 12; step++)
                {
                    Press(shell, GamepadAction.NavigateDown);
                    await SettleAsync();
                    AssertWholeRows(rowScroller, rows, $"settings section {section} down {step}");
                }
                longest = Math.Max(longest, (int)rowScroller.Offset.Y);
                if (section == 1)
                    Dump(window, $"rows-{name}-settings-down");
                Press(shell, GamepadAction.NextPlatform);
                await SettleAsync();
            }
            Assert.True(longest > 0, "no Settings section scrolled");
        }
        finally
        {
            window.Close();
        }
    }
}
