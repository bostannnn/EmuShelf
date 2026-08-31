using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;

namespace EmuShelf.App.Tests;

/// <summary>
/// The couch start menu's rows stay inside the panel that contains them.
/// </summary>
/// <remarks>
/// Reported from a television on both Windows and macOS: the view-mode row's cards ("Grid / List /
/// Shelf") and the sort row's cards ("Played / Added / A–Z / Rating") render somewhere else entirely
/// — over the artwork to the left of the menu, or above the panel — leaving an empty focus outline
/// where they belong. Whichever row currently owns focus is the one that escapes.
/// </remarks>
public class CouchMenuLayoutTests
{
    [AvaloniaFact]
    public async Task CouchStartMenu_KeepsBothSelectorRowsInsideTheMenuPanel()
    {
        var viewModel = new MainViewModel { IsGamepadMode = true };
        await viewModel.ReloadGamesAsync();
        viewModel.OpenGamepadMenuCommand.Execute(null);
        Assert.True(viewModel.IsGamepadSystemMenuOpen, "Test needs the couch start menu open.");

        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1920,
            Height = 1080,
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var rows = window.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("gamepad-viewmode-row"))
                .ToList();

            Assert.Equal(2, rows.Count);

            foreach (var row in rows)
            {
                var cards = row.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.Classes.Contains("gamepad-viewmode-card"))
                    .ToList();

                Assert.NotEmpty(cards);

                foreach (var card in cards)
                {
                    var origin = card.TranslatePoint(default, row);
                    Assert.NotNull(origin);
                    Assert.True(
                        origin.Value.X >= -1 && origin.Value.Y >= -1,
                        $"A '{Label(card)}' card sits at {origin.Value} relative to its own row.");
                    Assert.True(
                        origin.Value.X + card.Bounds.Width <= row.Bounds.Width + 1,
                        $"A '{Label(card)}' card overflows its row: "
                        + $"{origin.Value.X + card.Bounds.Width} > {row.Bounds.Width}.");
                    Assert.True(
                        card.Bounds.Width > 0 && card.Bounds.Height > 0,
                        $"A '{Label(card)}' card measured to nothing.");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Focus is the reported trigger: the escaping row is always the one wearing the focus outline,
    /// and focus is the only thing that changes about these rows.
    /// </summary>
    [AvaloniaFact]
    public async Task CouchStartMenu_SelectorRowsStayPutWhenFocusMovesOntoThem()
    {
        var viewModel = new MainViewModel { IsGamepadMode = true };
        await viewModel.ReloadGamesAsync();
        viewModel.OpenGamepadMenuCommand.Execute(null);

        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1920,
            Height = 1080,
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var rows = window.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("gamepad-viewmode-row"))
                .ToList();
            Assert.Equal(2, rows.Count);

            var resting = rows.Select(RowCardOrigins).ToList();

            foreach (var region in new[]
                     {
                         GamepadMenuFocusRegion.ViewMode,
                         GamepadMenuFocusRegion.Sort,
                         GamepadMenuFocusRegion.Options,
                     })
            {
                viewModel.MenuFocusRegion = region;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

                for (var index = 0; index < rows.Count; index++)
                {
                    var moved = RowCardOrigins(rows[index]);
                    Assert.Equal(resting[index].Count, moved.Count);
                    for (var card = 0; card < moved.Count; card++)
                    {
                        Assert.True(
                            Math.Abs(moved[card].X - resting[index][card].X) < 1
                            && Math.Abs(moved[card].Y - resting[index][card].Y) < 1,
                            $"Row {index} card {card} moved from {resting[index][card]} to "
                            + $"{moved[card]} when focus went to {region}.");
                    }
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The selected view-mode card has as much room for its label as the unselected ones.
    /// </summary>
    /// <remarks>
    /// The check badge used to be IsVisible-bound, so it took twenty-two pixels plus a gap on the
    /// selected card and nothing on the other two. The selected label — and only the selected label
    /// — was squeezed until the badge sat on top of it: "Grid" rendered as "Gri●" and "Shelf" as
    /// "Shel✓". The labels also reflowed every time the selection moved.
    /// </remarks>
    [AvaloniaFact]
    public async Task CouchStartMenu_ViewModeLabelsAreNotSqueezedByTheSelectionBadge()
    {
        var viewModel = new MainViewModel { IsGamepadMode = true };
        await viewModel.ReloadGamesAsync();
        viewModel.OpenGamepadMenuCommand.Execute(null);

        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1920,
            Height = 1080,
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            // Selection is a solid accent fill now — the check badge is gone, so a view-mode card is
            // any -card button whose label is one of the three couch layouts.
            var expectedLabels = new[] { "Grid", "List", "Shelf" };
            var cards = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("gamepad-viewmode-card"))
                .Where(button => button.GetVisualDescendants().OfType<TextBlock>()
                    .Any(text => expectedLabels.Contains(text.Text)))
                .ToList();
            Assert.Equal(3, cards.Count);

            // The labels all get the same width and none of them reflows when the selection moves.
            // Within a pixel: the row splits into three star columns, so a panel width that does not
            // divide by three leaves one card a rounding pixel wider.
            var labelWidths = cards
                .Select(card => card.GetVisualDescendants().OfType<TextBlock>().First().Bounds.Width)
                .ToList();
            Assert.All(
                labelWidths,
                width => Assert.True(
                    Math.Abs(width - labelWidths[0]) <= 1,
                    $"label widths differ by more than rounding: {string.Join(", ", labelWidths)}"));

            foreach (var card in cards)
            {
                var label = card.GetVisualDescendants().OfType<TextBlock>().First();

                // The label is wide enough to actually show the word rather than trimming it —
                // "Shelf" must never render as "Sh…" again.
                Assert.True(
                    label.Bounds.Width >= label.DesiredSize.Width - 1,
                    $"'{label.Text}' is being trimmed: {label.Bounds.Width} < {label.DesiredSize.Width}.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Moving focus through the menu does not move the menu.
    /// </summary>
    /// <remarks>
    /// This is the one that matters, and the first version of these tests missed it by measuring the
    /// cards against their own row — which moves as a unit, so everything looked still. Measured
    /// against the window instead, the sort row jumps nine logical pixels down the moment it takes
    /// focus and back up when it loses it, carrying the whole options list with it. On a television
    /// at 2x that is eighteen physical pixels of the lower half of the panel lurching on every d-pad
    /// press.
    /// </remarks>
    [AvaloniaFact]
    public async Task CouchStartMenu_DoesNotShiftWhenFocusMovesBetweenRows()
    {
        var viewModel = new MainViewModel { IsGamepadMode = true };
        await viewModel.ReloadGamesAsync();
        viewModel.OpenGamepadMenuCommand.Execute(null);

        // The size the fault was recorded at: a fullscreen couch window on a 2x display.
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1470,
            Height = 923,
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var rows = window.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("gamepad-viewmode-row"))
                .ToList();
            Assert.Equal(2, rows.Count);

            var baseline = new Dictionary<GamepadMenuFocusRegion, List<Point>>();
            foreach (var region in new[]
                     {
                         GamepadMenuFocusRegion.Options,
                         GamepadMenuFocusRegion.ViewMode,
                         GamepadMenuFocusRegion.Sort,
                         GamepadMenuFocusRegion.Options,
                     })
            {
                viewModel.MenuFocusRegion = region;
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                baseline[region] = rows
                    .Select(row => row.TranslatePoint(default, window) ?? default)
                    .ToList();
            }

            var reference = baseline[GamepadMenuFocusRegion.Options];
            foreach (var (region, positions) in baseline)
            {
                for (var index = 0; index < positions.Count; index++)
                {
                    Assert.True(
                        Math.Abs(positions[index].Y - reference[index].Y) < 1,
                        $"Row {index} sits at y={positions[index].Y} with focus on {region} "
                        + $"but y={reference[index].Y} with focus on the options list — the menu "
                        + "moves under the player as they scroll through it.");
                    Assert.True(
                        Math.Abs(positions[index].X - reference[index].X) < 1,
                        $"Row {index} moved horizontally with focus on {region}.");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static List<Point> RowCardOrigins(Border row) =>
        row.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("gamepad-viewmode-card"))
            .Select(button => button.TranslatePoint(default, row) ?? new Point(-9999, -9999))
            .ToList();

    private static string Label(Button card) =>
        card.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? "?";
}
