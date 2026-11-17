using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public partial class HandheldShellViewModelTests
{
    /// <summary>M47 B14 leftover 3 under Big Type: a long line or second line on the Stage runs to another row at a whole word, below the name and clear of the hints and the screen's edge.</summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeLongLines
    {
        private static async Task BigType(Func<Task> test)
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                await test();
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        // ---- 3. A long line runs to a second row, at a whole word ----

        private const string LongLine = "Everything arrives in place at once and the backdrop holds still · Android's own setting does this too";
        private const string MediumLine = "Off fills only what is missing; on also replaces what ScreenScraper wrote before";
        private const string LongFacts = "First played 9 November 2026 · last played yesterday · also played before the log began · 3 discs";

        private static Rect BoxOf(Control control, Visual canvas) =>
            new(control.TranslatePoint(default, canvas)!.Value, control.Bounds.Size);

        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public Task ALongLine_OrALongSecondLine_IsReadWhole_BelowTheName_AndClearOfTheHints(bool narrow) => BigType(async () =>
        {
            var (shell, _) = await CreateFullHomeAsync();
            var view = new HandheldStageView(null, pinnedNarrow: narrow) { DataContext = shell };
            var window = new Window { Content = view, Width = narrow ? 1240 : 1920, Height = 1080 };
            window.Show();
            try
            {
                await SettleViewAsync();
                var canvas = view.FindControl<Control>("StageCanvas")!;
                var line = view.FindControl<TextBlock>("BtLine")!;
                var facts = view.FindControl<TextBlock>("BtFacts")!;
                var hints = view.FindControl<Control>("BtHintsShot")!;
                var nameBottom = canvas.Bounds.Height - (narrow ? HandheldTokens.StageNameBottom + HandheldTokens.StageNarrowHintsRow : HandheldTokens.StageNameBottom);

                var hintsLeft = BoxOf(hints, canvas).Left;
                void Clear(TextBlock text)
                {
                    var box = BoxOf(text, canvas);
                    Assert.DoesNotContain(text.TextLayout.TextLines, row => row.HasCollapsed);
                    // The em box of its last row stays at least the free row's leading above the screen's edge.
                    var last = text.TextLayout.TextLines[^1];
                    var emBottom = box.Bottom - ((last.Height - text.FontSize) / 2);
                    Assert.True(emBottom <= canvas.Bounds.Height - 20, $"\"{text.Text}\" ({box}, em bottom {emBottom}) runs to the screen's edge");
                    Assert.True(narrow || box.Right <= hintsLeft, $"\"{text.Text}\" ({box}) runs under the hints");
                }
                // The second line's em box starts below the line's (their boxes overlap by their leading, as B7 set them).
                void AssertApart(TextBlock upper, TextBlock lower)
                {
                    var up = BoxOf(upper, canvas);
                    var low = BoxOf(lower, canvas);
                    var upEm = up.Bottom - ((upper.TextLayout.TextLines[^1].Height - upper.FontSize) / 2);
                    var lowEm = low.Top + ((lower.TextLayout.TextLines[0].Height - lower.FontSize) / 2);
                    Assert.True(lowEm >= upEm, $"the second line ({low}, em top {lowEm}) runs into the line ({up}, em bottom {upEm})");
                }
                void BelowTheName(TextBlock text)
                {
                    var box = BoxOf(text, canvas);
                    Assert.True(box.Top + ((text.TextLayout.TextLines[0].Height - text.FontSize) / 2) >= nameBottom - 0.5,
                        $"\"{text.Text}\" ({box}) runs into the name's box (bottom {nameBottom})");
                }

                // A row's long description, with no second line: two rows, none cut, growing down from the line's place.
                shell.Stage.Line = MediumLine;
                shell.Stage.Facts = string.Empty;
                await SettleViewAsync();
                Assert.Equal(2, line.TextLayout.TextLines.Count);
                Clear(line);
                BelowTheName(line);

                // Longer than two rows: it ends in an ellipsis at a whole word, never inside one.
                shell.Stage.Line = LongLine + " " + LongLine;
                await SettleViewAsync();
                Assert.Equal(2, line.TextLayout.TextLines.Count);
                Assert.True(line.TextTrimming == TextTrimming.WordEllipsis);

                // A line of two rows over a second line: the second line moves down a row under it.
                shell.Stage.Line = MediumLine;
                shell.Stage.Facts = "PlayStation 2";
                await SettleViewAsync();
                Assert.Equal(2, line.TextLayout.TextLines.Count);
                Assert.Equal(1, facts.TextLayout.TextLines.Count);
                Clear(line);
                Clear(facts);
                BelowTheName(line);
                AssertApart(line, facts);

                // A long second line under a short line: it grows down into the free row.
                shell.Stage.Line = "Off";
                shell.Stage.Facts = LongFacts;
                await SettleViewAsync();
                Assert.Equal(1, line.TextLayout.TextLines.Count);
                Assert.Equal(2, facts.TextLayout.TextLines.Count);
                Clear(line);
                Clear(facts);
                AssertApart(line, facts);

                // Short again: each one row, where B7 put them.
                shell.Stage.Line = "Off";
                shell.Stage.Facts = "PlayStation 2";
                await SettleViewAsync();
                Assert.Equal(1, line.TextLayout.TextLines.Count);
                Assert.Equal(1, facts.TextLayout.TextLines.Count);
                Assert.Equal(narrow ? HandheldTokens.StageNarrowFactsShotMargin : HandheldTokens.StageFactsShotMargin, ((Control)facts.Parent!).Margin);
            }
            finally
            {
                window.Close();
                shell.Dispose();
            }
        });

        private static async Task SettleViewAsync()
        {
            for (var i = 0; i < 4; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }
}
