using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public partial class HandheldShellViewModelTests
{
    /// <summary>
    /// M47 B11: the Big Type Stage on the narrow screen (the screens swapped) is laid out again for 1240 × 1080, with no bars: the name
    /// fitted to the narrow box, the hints on a row of their own, the lines a hint row higher, the case in a corner of its own, the
    /// console's name ending before the clock. Nothing overlaps, clips or runs off for the longest names in the design set and with a
    /// message on the line. Frames for a look go to <c>EMUSHELF_DESIGN_SNAPSHOT_DIR</c> when it is set.
    /// </summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeNarrowStage
    {
        private const string LongError =
            "Could not start NetherSX2: the emulator is not installed. Choose one in Settings → Emulators.";

        // The design set's longest titles, a three-line one from the Thor, a single long word, and a monogram.
        private static readonly string[] Titles =
        [
            "The Legend of Zelda: A Link Between Worlds",
            "Shin Megami Tensei : Persona 2, Innocent Sin",
            "Jak and Daxter: The Precursor Legacy",
            "Professor Layton and the Curious Village",
            "Supercalifragilisticexpialidocious",
            "Ico",
        ];

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

        [Fact]
        public void TheNarrowNumbers_AreTokensJs() => BigType(() =>
        {
            var tokens = File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));
            double N(string name) => double.Parse(Regex.Match(tokens, $@"\b{name}:\s*(?<v>-?[0-9.]+)").Groups["v"].Value, CultureInfo.InvariantCulture);
            var narrow = Regex.Match(tokens, @"DisplayNameNarrow:\s*\{\s*fitBox:\s*\[(?<w>\d+),\s*(?<h>\d+)\],\s*fitBoxBesideMedium:\s*\[(?<bw>\d+),\s*(?<bh>\d+)\]");
            Assert.True(narrow.Success, "tokens.js has no type.DisplayNameNarrow");
            Assert.Equal(double.Parse(narrow.Groups["w"].Value, CultureInfo.InvariantCulture), HandheldTokens.StageNarrowNameWidth);
            Assert.Equal(double.Parse(narrow.Groups["h"].Value, CultureInfo.InvariantCulture), HandheldTokens.StageNarrowNameHeight);
            Assert.Equal(double.Parse(narrow.Groups["bw"].Value, CultureInfo.InvariantCulture), HandheldTokens.StageNarrowNameWidthBesideMedium);
            Assert.Equal(double.Parse(narrow.Groups["bh"].Value, CultureInfo.InvariantCulture), HandheldTokens.StageNarrowNameHeight);
            Assert.Equal(N("NarrowHintsRow"), HandheldTokens.StageNarrowHintsRow);
            Assert.Equal(N("NarrowMediumRight"), HandheldTokens.StageNarrowMediumRight);
            Assert.Equal(N("NarrowMediumTop"), HandheldTokens.StageNarrowMediumTop);
            Assert.Equal(N("NarrowLabelGap"), HandheldTokens.StageNarrowLabelGap);
            Assert.Equal(N("NarrowLaunchTitleMaxWidth"), HandheldTokens.StageNarrowLaunchTitleMaxWidth);
            // The box spans the narrow canvas less the name's margin at each side; beside the case it ends that margin before the case.
            Assert.Equal(1240 - (2 * HandheldTokens.StageNameLeft), HandheldTokens.StageNarrowNameWidth);
            var frame = HandheldTokens.StageMediumFrame(1240);
            var caseLeft = frame.Center.X - (HandheldTokens.StageMediumWidth / 2);
            Assert.Equal(caseLeft - HandheldTokens.StageNameLeft, HandheldTokens.StageNameLeft + HandheldTokens.StageNarrowNameWidthBesideMedium);
            // Its top is the wide Stage's (the name raised a hint row, the box shorter by as much).
            Assert.Equal(1080 - HandheldTokens.StageNameBottom - HandheldTokens.StageNameHeight,
                1080 - (HandheldTokens.StageNameBottom + HandheldTokens.StageNarrowHintsRow) - HandheldTokens.StageNarrowNameHeight);
            // The takeover grows from the narrow case's middle, as it does beside the wide one.
            var wideCase = HandheldTokens.StageMediumFrame(1920).Center;
            Assert.Equal(HandheldTokens.StageTakeoverCentre(false) - wideCase, HandheldTokens.StageTakeoverCentre(true) - frame.Center);
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

        [AvaloniaFact]
        public Task TheNarrowStage_FillsTheScreen_AndNothingOverlaps_ForTheLongestNames() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("ps2", Titles.Length);
            library.Games["ps2"] = library.Games["ps2"].Select((game, i) => game with { Title = Titles[i] }).ToList();
            var shell = await CreateAsync(library);
            // The pieces at rest: the letters land at once.
            shell.ReduceMotion = true;
            var view = new HandheldStageView(null, pinnedNarrow: true) { DataContext = shell };
            var window = new Window { Content = view, Width = 1240, Height = 1080 };
            window.Show();
            try
            {
                var canvas = view.FindControl<Grid>("StageCanvas")!;
                Assert.Contains("btnarrow", canvas.Classes);
                Assert.DoesNotContain("narrow", canvas.Classes);
                await SettleAsync();
                // No bars: the canvas is the screen's own size, fitted at 1.
                Assert.Equal(1240, canvas.Bounds.Width);
                Assert.Equal(new Size(1240, 1080), canvas.Bounds.Size);

                // The folder, with a long console name.
                shell.Stage.Label = "Nintendo Entertainment System";
                await CheckAsync(view, canvas, shell, "folder", besideMedium: false);
                Press(shell, GamepadAction.Confirm);
                // The folder's games stand in columns of three: down its first column, across, and up its second.
                var seen = new HashSet<string>();
                var moves = new GamepadAction?[] { null, GamepadAction.NavigateDown, GamepadAction.NavigateDown, GamepadAction.NavigateRight,
                    GamepadAction.NavigateUp, GamepadAction.NavigateUp };
                foreach (var move in moves)
                {
                    if (move is { } action)
                        Press(shell, action);
                    shell.Stage.Facts = "NetherSX2 · last played today · disc 2 of 3";
                    shell.Stage.Label = "Nintendo Entertainment System";
                    seen.Add(shell.Stage.Name!);
                    // M47 B12: a game's name view has no case beside it, so the name has the whole narrow box.
                    await CheckAsync(view, canvas, shell, $"game-{seen.Count}", besideMedium: false);
                }
                Assert.Equal(Titles.OrderBy(title => title).ToArray(), seen.OrderBy(title => title).ToArray());

                // M47 B12: the model view on the narrow Stage, the model (here the flat cover standing in) in the middle, clear of every
                // corner's text and above the lines.
                Press(shell, GamepadAction.NextPlatform);
                Assert.True(shell.Stage.IsModelView);
                await SettleAsync();
                Save(view, "model");
                Rect Of(string control)
                {
                    var c = view.FindControl<Control>(control)!;
                    return new Rect(c.TranslatePoint(default, canvas)!.Value, c.Bounds.Size);
                }
                var model = Of("BtModelCover");
                Assert.True(new Rect(canvas.Bounds.Size).Contains(model), $"the model ({model}) runs off the screen");
                Assert.InRange(model.Center.X, 619.5, 620.5);
                var modelLine = view.FindControl<TextBlock>("BtLine")!;
                var modelLineTop = modelLine.TranslatePoint(default, canvas)!.Value.Y;
                Assert.True(model.Bottom <= modelLineTop, $"the model ({model}) runs into the line ({modelLineTop})");
                foreach (var piece in new[] { "BtLabelShot", "BtStatusShot", "BtHintsShot", "BtFactsShot" })
                    Assert.False(model.Intersects(Of(piece).Deflate(HandheldTokens.StageShotPad)), $"the model ({model}) runs into {piece} ({Of(piece)})");
                Assert.False(view.FindControl<Control>("BtName")!.IsVisible);
                Press(shell, GamepadAction.PreviousPlatform);
                Assert.False(shell.Stage.IsModelView);

                shell.ShowStatus(LongError, StatusSeverity.Error);
                await CheckAsync(view, canvas, shell, "message", besideMedium: false);
                var line = view.FindControl<TextBlock>("BtLine")!;
                Assert.Equal(2, line.TextLayout.TextLines.Count);
                Assert.DoesNotContain(line.TextLayout.TextLines, text => text.HasCollapsed);
            }
            finally
            {
                window.Close();
                shell.Dispose();
            }
        });

        private static async Task CheckAsync(HandheldStageView view, Grid canvas, HandheldShellViewModel shell, string what, bool besideMedium)
        {
            await SettleAsync();
            Save(view, what);
            Rect At(string name)
            {
                var control = view.FindControl<Control>(name)!;
                var at = control.TranslatePoint(default, canvas)!.Value;
                return new Rect(at, control.Bounds.Size);
            }
            var bounds = new Rect(canvas.Bounds.Size);
            var name = view.FindControl<HandheldBigTypeName>("BtName")!;
            Assert.Equal(besideMedium ? HandheldTokens.StageNarrowNameWidthBesideMedium : HandheldTokens.StageNarrowNameWidth, name.Bounds.Width);
            var nameAt = name.TranslatePoint(default, canvas)!.Value;
            var box = new Rect(nameAt.X, nameAt.Y, name.Bounds.Width, HandheldTokens.StageNarrowNameHeight);
            var fitted = name.Fitted!;
            Assert.InRange(fitted.Lines, 1, HandheldTokens.StageNameMaxLines);
            // The name's ink (its pixels, read back from a picture of the control alone) inside the box across, below the top corners and
            // inside the screen.
            var ink = Ink(name, nameAt);
            Assert.True(ink.X >= box.X - 2 && ink.Right <= box.Right + 2, $"{what}: \"{shell.Stage.Name}\" ({ink}) leaves the name's box ({box}) across");
            Assert.True(bounds.Contains(ink), $"{what}: \"{shell.Stage.Name}\" ({ink}) runs off the screen");
            var words = new[] { ink };
            var label = At("BtLabelShot");
            var status = At("BtStatusShot");
            var hints = At("BtHintsShot");
            var lineShot = At("BtLineShot");
            var factsShot = At("BtFactsShot");
            foreach (var (piece, rect) in new[] { ("label", label), ("status", status), ("hints", hints), ("line", lineShot), ("facts", factsShot) })
                Assert.True(bounds.Contains(rect), $"{what}: the {piece} ({rect}) runs off the screen");
            // The console's name ends before the battery and the clock; a long one is set smaller, not cut.
            Assert.True(label.Right - HandheldTokens.StageShotPad <= status.X + HandheldTokens.StageShotPad - HandheldTokens.StageNarrowLabelGap + 0.5,
                $"{what}: the label ({label}) runs into the status ({status})");
            var labelText = view.FindControl<TextBlock>("BtLabel")!;
            Assert.DoesNotContain(labelText.TextLayout.TextLines, text => text.HasCollapsed);
            // The name sits under the top corners and above the lines; the lines above the hints' row.
            var labelInk = Ink(view.FindControl<Control>("BtLabelShot")!, label.Position);
            var statusInk = Ink(view.FindControl<Control>("BtStatusShot")!, status.Position);
            Assert.False(ink.Intersects(labelInk) || ink.Intersects(statusInk), $"{what}: the name ({ink}) runs into the top corners ({labelInk}, {statusInk})");
            Assert.False(labelInk.Intersects(statusInk), $"{what}: the label ({labelInk}) runs into the clock ({statusInk})");
            var lineTop = view.FindControl<TextBlock>("BtLine")!;
            var lineFirst = lineTop.TextLayout.TextLines[0];
            var emTop = lineTop.TranslatePoint(default, canvas)!.Value.Y + ((lineFirst.Height - lineTop.FontSize) / 2);
            Assert.True(emTop >= box.Bottom - 0.5, $"{what}: the line ({emTop}) runs into the name's box (bottom {box.Bottom})");
            var lineInk = Ink(lineTop, lineTop.TranslatePoint(default, canvas)!.Value);
            Assert.False(lineInk.Intersects(ink), $"{what}: the line ({lineInk}) runs into the name ({ink})");
            Assert.True(factsShot.Bottom - HandheldTokens.StageShotPad <= hints.Y + HandheldTokens.StageShotPad,
                $"{what}: the facts ({factsShot}) reach the hints' row ({hints})");
            Assert.True(lineShot.Right <= bounds.Right && factsShot.Right <= bounds.Right);
            foreach (var line in new[] { "BtLine", "BtFacts" })
            {
                var text = view.FindControl<TextBlock>(line)!;
                Assert.True(text.Bounds.Width <= hints.Right - HandheldTokens.StageShotPad - (text.TranslatePoint(default, canvas)!.Value.X) + 0.5,
                    $"{what}: {line} runs past the hints' right edge");
            }
            if (besideMedium)
            {
                var cover = At("BtCoverShot");
                Assert.True(bounds.Contains(cover), $"{what}: the case ({cover}) runs off the screen");
                foreach (var word in words)
                    Assert.False(word.Intersects(cover), $"{what}: a word ({word}) runs into the case ({cover})");
                Assert.False(cover.Intersects(status.Deflate(HandheldTokens.StageShotPad)), $"{what}: the case runs into the clock");
                Assert.True(cover.Bottom <= emTop, $"{what}: the case ({cover}) runs into the line ({emTop})");
                Assert.False(cover.Intersects(hints.Deflate(HandheldTokens.StageShotPad)), $"{what}: the case runs into the hints");
            }
        }

        private static void Save(HandheldStageView view, string what)
        {
            if (DesignOutput is { } directory)
            {
                Directory.CreateDirectory(directory);
                var target = new RenderTargetBitmap(new PixelSize(1240, 1080));
                target.Render(view);
                target.Save(Path.Combine(directory, $"b11-narrow-stage-{what}.png"));
            }
        }

        // Where a control draws, on the canvas: the box round its pixels that are not clear.
        private static Rect Ink(Control control, Point at)
        {
            // Wider and taller than the control, so a piece that runs past its own edge is seen.
            var width = (int)Math.Ceiling(control.Bounds.Width) + 1240;
            var height = (int)Math.Ceiling(control.Bounds.Height) + 400;
            using var target = new RenderTargetBitmap(new PixelSize(width, height));
            target.Render(control);
            var pixels = new byte[width * height * 4];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                target.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, width * 4);
            }
            finally
            {
                handle.Free();
            }
            int left = width, top = height, right = -1, bottom = -1;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[(((y * width) + x) * 4) + 3] < 24)
                        continue;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
            return right < 0 ? default : new Rect(at.X + left, at.Y + top, right - left + 1, bottom - top + 1);
        }

        private static async Task SettleAsync()
        {
            for (var i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs();
                await Task.Delay(30);
            }
            Dispatcher.UIThread.RunJobs();
        }
    }
}

/// <summary>
/// M47 B11: the veils and the legibility answer on the narrow Stage. The text keeps its size there, so the veils keep theirs, anchored
/// to their own corners; the ink is the table's; where the table's left veil fell short over the narrow crop the narrow Stage's own is
/// stronger (measured with <c>tools/EmuShelf.Rendering.Preview -- --stage-narrow-legibility</c>).
/// </summary>
public class HandheldBigTypeNarrowLegibilityTests
{
    [Fact]
    public void TheVeils_KeepTheirSize_AnchoredToTheirCorners_OnTheNarrowCanvas()
    {
        foreach (var corner in new[] { EmuShelf.Rendering.Stage.BigType.BigTypeVeil.TopRight, EmuShelf.Rendering.Stage.BigType.BigTypeVeil.BottomRight })
        {
            for (var inset = 0f; inset <= 1000f; inset += 50f)
            {
                var y = corner.CentreY == 0f ? 100f : 980f;
                var wide = EmuShelf.Rendering.Stage.BigType.BigTypeVeil.CornerAlpha(corner, 1920f - inset, y, 1920f, 1080f);
                var narrow = EmuShelf.Rendering.Stage.BigType.BigTypeVeil.CornerAlpha(corner, 1240f - inset, y, 1240f, 1080f);
                Assert.Equal(wide, narrow, 5);
            }
        }
        // The left veil fades over the same pixels past its flat end.
        for (var past = 0f; past <= 500f; past += 25f)
        {
            Assert.Equal(EmuShelf.Rendering.Stage.BigType.BigTypeVeil.LeftAlpha(712f + past, 712f, 1920f),
                EmuShelf.Rendering.Stage.BigType.BigTypeVeil.LeftAlpha(712f + past, 712f, 1240f), 5);
        }
        // The wide Stage's shapes are the mock's, unchanged.
        Assert.Equal(1f, EmuShelf.Rendering.Stage.BigType.BigTypeVeil.CornerAlpha(EmuShelf.Rendering.Stage.BigType.BigTypeVeil.TopRight, 1920f - (0.5f * 0.56f * 1920f), 0f, 1920f, 1080f));
    }

    [Fact]
    public void TheNarrowAnswer_KeepsTheInk_AndOnlyStrengthensTheLeftVeil_WhereMeasured()
    {
        foreach (var (id, answer) in EmuShelf.Rendering.Stage.Backdrops.BackdropLegibility.All)
        {
            var narrow = EmuShelf.Rendering.Stage.Backdrops.BackdropLegibility.For(id, narrow: true);
            Assert.Equal(answer.Left.Dark, narrow.Left.Dark);
            Assert.Equal(answer.TopRight, narrow.TopRight);
            Assert.Equal(answer.BottomRight, narrow.BottomRight);
            Assert.True(narrow.Left.Veil >= answer.Left.Veil);
            Assert.Equal(answer, EmuShelf.Rendering.Stage.Backdrops.BackdropLegibility.For(id, narrow: false));
            if (EmuShelf.Rendering.Stage.Backdrops.BackdropLegibility.NarrowLeftVeils.TryGetValue(id, out var veil))
            {
                Assert.Equal(veil, narrow.Left.Veil);
                Assert.True(veil <= 0.94f, "tokens.js legibility.MaxVeil");
            }
            else
            {
                Assert.Equal(answer, narrow);
            }
        }
        Assert.Equal(["arc-checker", "gc-caustics", "ps2-sea"],
            EmuShelf.Rendering.Stage.Backdrops.BackdropLegibility.NarrowLeftVeils.Keys.OrderBy(id => id, StringComparer.Ordinal));
        // The compositor's ink mixes the narrow answer on the narrow canvas only.
        var deep = new System.Numerics.Vector3(0.1f, 0.1f, 0.2f);
        var showing = new (string?, float)[] { ("ps2-sea", 1f) };
        Assert.Equal(0.30f, EmuShelf.Rendering.Stage.StageCompositor.ZoneInk(EmuShelf.Rendering.Stage.Backdrops.StageInkZone.Left, showing, deep).VeilStrength, 3);
        Assert.Equal(0.66f, EmuShelf.Rendering.Stage.StageCompositor.ZoneInk(EmuShelf.Rendering.Stage.Backdrops.StageInkZone.Left, showing, deep, narrow: true).VeilStrength, 3);
    }
}
