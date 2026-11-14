using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Core.Handheld;
using EmuShelf.Integrations.Systems;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.Backdrops;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B4: the Stage at rest in the Big Type look. The baked legibility table is the mock's <c>legibility.json</c>; the name is
/// fitted as the mock fits it; the ink follows the backdrop that is showing; the Stage's numbers are tokens.js's.
/// </summary>
public class HandheldBigTypeStageTests
{
    private static string Prototype(string file) =>
        File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", file));

    // ---- The baked table ----

    [Fact]
    public void BakedTable_IsLegibilityJson_ZoneByZone()
    {
        using var json = JsonDocument.Parse(Prototype("legibility.json"));
        var rows = json.RootElement.GetProperty("backdrops").EnumerateArray().ToList();
        Assert.Equal(53, rows.Count);
        Assert.Equal(rows.Count, BackdropLegibility.All.Count);
        foreach (var row in rows)
        {
            var id = row.GetProperty("id").GetString()!;
            Assert.True(BackdropLegibility.All.TryGetValue(id, out var baked), $"{id} is not baked");
            Assert.Equal(row.GetProperty("console").GetString(), baked!.Console);
            foreach (var (name, zone) in new[] { ("left", StageInkZone.Left), ("topRight", StageInkZone.TopRight), ("bottomRight", StageInkZone.BottomRight) })
            {
                var cell = row.GetProperty(name);
                var ink = cell.GetProperty("ink").GetString();
                var veil = (float)cell.GetProperty("veil").GetDouble();
                var answer = baked[zone];
                if (ink == "changes")
                {
                    // The one zone that could not be baked: white with a 0.5 veil, marked for the owner's look on the Thor.
                    Assert.Equal(("ps2-sea", StageInkZone.BottomRight), (id, zone));
                    Assert.False(answer.Baked);
                    Assert.False(answer.Dark);
                    Assert.Equal(0.5f, answer.Veil);
                    continue;
                }
                Assert.True(answer.Baked, $"{id} {name}");
                Assert.Equal(ink == "dark", answer.Dark);
                Assert.Equal(veil, answer.Veil);
            }
        }
    }

    [Fact]
    public void EveryBackdropTheSystemsUse_HasAnAnswer_AndTheRestingVeilsAreTokens()
    {
        var used = KnownSystems.All.SelectMany(system => system.Backdrops ?? []).Distinct().ToList();
        Assert.NotEmpty(used);
        Assert.All(used, id => Assert.True(BackdropLegibility.All.ContainsKey(id), $"{id} has no ink"));

        var tokens = Prototype("tokens.js");
        Assert.Equal(Number(tokens, "BaseVeilLeft"), BackdropLegibility.BaseVeilLeft, 3);
        Assert.Equal(Number(tokens, "BaseVeilCorner"), BackdropLegibility.BaseVeilCorner, 3);
        Assert.Equal(Number(tokens, "LeftVeilPastName"), BigTypeVeil.PastName, 3);
        Assert.Equal(Number(tokens, "LeftVeilFade"), BigTypeVeil.Fade, 3);
        Assert.Equal(Number(tokens, "InkMix"), BigTypeVeil.InkMix, 3);
        Assert.Equal(BackdropLegibility.Fallback, BackdropLegibility.For(null));
        Assert.Equal(BackdropLegibility.Fallback, BackdropLegibility.For("not-a-backdrop"));
    }

    // ---- The ink follows the backdrop ----

    [Fact]
    public void ZoneInk_TakesTheShowingBackdropsAnswer_AndMixesThroughARotation()
    {
        var deep = new Vector3(0.1f, 0.2f, 0.3f);

        // ps2-towers: dark ink everywhere, a white veil at the resting strength.
        var towers = StageCompositor.ZoneInk(StageInkZone.Left, [("ps2-towers", 1f)], deep);
        Assert.Equal(deep, towers.Ink);
        Assert.Equal(Vector3.One, towers.Veil);
        Assert.Equal(0.28f, towers.VeilStrength, 4);

        // ps2-trails-v3: white ink, a veil of the dark ink.
        var trails = StageCompositor.ZoneInk(StageInkZone.TopRight, [("ps2-trails-v3", 1f)], deep);
        Assert.Equal(Vector3.One, trails.Ink);
        Assert.Equal(deep, trails.Veil);
        Assert.Equal(0.25f, trails.VeilStrength, 4);

        // No shader (the pictured fallback): white and the resting veils.
        var fallback = StageCompositor.ZoneInk(StageInkZone.Left, [(null, 1f)], deep);
        Assert.Equal(Vector3.One, fallback.Ink);
        Assert.Equal(0.28f, fallback.VeilStrength, 4);

        // Halfway through a rotation from one to the other, the ink is between the two.
        var half = StageCompositor.ZoneInk(StageInkZone.Left, [("ps2-towers", 0.5f), ("ps2-trails-v3", 0.5f)], deep);
        Assert.Equal((deep + Vector3.One) / 2f, half.Ink);

        // gc-caustics needs more than the resting veil on the left.
        Assert.Equal(0.39f, StageCompositor.ZoneInk(StageInkZone.Left, [("gc-caustics", 1f)], deep).VeilStrength, 4);
    }

    [Fact]
    public void DarkInk_IsTheColourMixed74PercentTowardNearBlack()
    {
        // PS2's ground #2B5BC7 → 0.26 × it + 0.74 × #0B0A18.
        var deep = HandheldTokens.StageDeep(Color.Parse("#2B5BC7"));
        Assert.Equal(Color.FromRgb(19, 31, 70), deep);
    }

    // ---- The veils' shape ----

    [Fact]
    public void Veils_AreFlatThenFade_AsTheMockDrawsThem()
    {
        // Left: flat to the name box's edge + 32, gone 24% of the width later.
        var flatEnd = BigTypeVeil.LeftFlatEnd(80, 1360);
        Assert.Equal(1472, flatEnd);
        Assert.Equal(1f, BigTypeVeil.LeftAlpha(1472, flatEnd, 1920));
        Assert.Equal(0.5f, BigTypeVeil.LeftAlpha(1472 + (0.12f * 1920), flatEnd, 1920), 4);
        // (With a 1360 box it reaches nothing only at 1933, just past the screen's edge, as in the mock.)
        Assert.Equal(0f, BigTypeVeil.LeftAlpha(1472 + (0.24f * 1920), flatEnd, 1920), 4);
        Assert.Equal(0f, BigTypeVeil.LeftAlpha(1800, BigTypeVeil.LeftFlatEnd(80, 1120), 1920), 4);

        // The top right patch: flat to 60% of its 56% × 40% radii, nothing at its edge.
        Assert.Equal(1f, BigTypeVeil.CornerAlpha(BigTypeVeil.TopRight, 1920, 0, 1920, 1080));
        Assert.Equal(1f, BigTypeVeil.CornerAlpha(BigTypeVeil.TopRight, 1920 - (0.6f * 0.56f * 1920), 0, 1920, 1080), 3);
        Assert.Equal(0f, BigTypeVeil.CornerAlpha(BigTypeVeil.TopRight, 1920 - (0.56f * 1920), 0, 1920, 1080), 3);
        Assert.Equal(0f, BigTypeVeil.CornerAlpha(BigTypeVeil.BottomRight, 0, 0, 1920, 1080));

        // The masks are white, premultiplied, and cover their zone.
        var mask = BigTypeVeil.Mask(StageInkZone.BottomRight, flatEnd, 1920, 1080);
        Assert.True(mask.X <= 1920 - (0.68f * 1920) && mask.X + mask.Width >= 1920);
        Assert.Equal(255, mask.Pixels.Data[^1]);
    }

    // ---- The name fit ----

    private static BigTypeWord Word(string text, float width) => new(text, width, []);

    [Fact]
    public void Fit_AMonogram_TakesTheBoxsHeight_OnOneLine()
    {
        // "PS2" measured at 100 px: 180 wide. One line of 0.9 em fits 680 tall at 755; the width allows 1360 / 1.8.
        var fit = BigTypeLayout.Fit([Word("PS2", 180)], 25, 100, 1360, 680);
        Assert.Equal(1, fit.Lines);
        Assert.Equal(MathF.Floor(680 / 0.9f), fit.Size, 0);
    }

    [Fact]
    public void Fit_ALongTitle_WrapsWordsWhole_IntoAtMostThreeLines()
    {
        // "Professor Layton and the Curious Village" with widths in proportion to the face's.
        BigTypeWord[] words =
        [
            Word("Professor", 501), Word("Layton", 351), Word("and", 186), Word("the", 176), Word("Curious", 391), Word("Village", 351),
        ];
        var fit = BigTypeLayout.Fit(words, 25, 100, 1120, 680);
        Assert.True(fit.Lines is > 1 and <= 3);
        Assert.True(fit.Lines * 0.9f * fit.Size <= 680.5f);
        var scale = fit.Size / 100f;
        foreach (var placed in fit.Words)
            Assert.True(placed.X + (words[placed.Word].Width * scale) <= 1120.5f, $"{words[placed.Word].Text} runs past the box");
        // Every word is whole and in order.
        Assert.Equal(Enumerable.Range(0, words.Length), fit.Words.Select(placed => placed.Word));
    }

    [Fact]
    public void Fit_ASingleVeryLongWord_IsSizedToTheWidth_AndNeverBroken()
    {
        // 30 letters measured at 100 px: 1700 wide. It is set smaller until it fits the 1360 box, on one line.
        var fit = BigTypeLayout.Fit([Word("Supercalifragilisticexpialidoc", 1700)], 25, 100, 1360, 680);
        Assert.Equal(1, fit.Lines);
        Assert.True(fit.Size * 17f <= 1360.5f);
        Assert.True((fit.Size + 1) * 17f > 1360f);

        // Wider than the box even at the smallest size: it keeps the smallest size and its one line.
        var tooLong = BigTypeLayout.Fit([Word(new string('W', 80), 9000)], 25, 100, 1360, 680);
        Assert.Equal(56, tooLong.Size);
        Assert.Equal(1, tooLong.Lines);
    }

    [AvaloniaFact]
    public void TheNameControl_FitsWithTheFace_BottomAnchored()
    {
        var face = new Typeface(new FontFamily(HandheldTokens.FaceSource), FontStyle.Normal, FontWeight.Black);
        var monogram = HandheldBigTypeName.Fit("PS2", face, 1360, 680);
        var title = HandheldBigTypeName.Fit("Professor Layton and the Curious Village", face, 1120, 680);
        var word = HandheldBigTypeName.Fit("Supercalifragilisticexpialidocious", face, 1360, 680);

        Assert.Equal(1, monogram.Lines);
        Assert.True(monogram.Size > 500, $"the monogram is only {monogram.Size}");
        Assert.InRange(title.Lines, 2, 3);
        Assert.Equal(1, word.Lines);
        Assert.True(word.Size < monogram.Size);
        // The last line's words sit lower than the first line's (bottom-anchored, line by line).
        Assert.True(title.Words[^1].Top > title.Words[0].Top);
    }

    private static double Number(string tokens, string name)
    {
        var match = Regex.Match(tokens, $@"\b{name}:\s*(?<value>-?[0-9.]+)");
        Assert.True(match.Success, $"tokens.js has no {name}");
        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }
}

/// <summary>M47 B4: the Stage's Big Type values are tokens.js's <c>type</c> and <c>stage</c>.</summary>
[Collection(nameof(HandheldTokenLookCollection))]
public class HandheldBigTypeStageTokenTests
{
    [Fact]
    public void StageTokens_AreTheMocks()
    {
        var tokens = File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));
        double N(string name) => double.Parse(Regex.Match(tokens, $@"\b{name}:\s*(?<v>-?[0-9.]+)").Groups["v"].Value, CultureInfo.InvariantCulture);
        double Size(string role) => double.Parse(Regex.Match(tokens, $@"\b{role}:\s*\{{\s*size:\s*(?<v>[0-9.]+)").Groups["v"].Value, CultureInfo.InvariantCulture);
        try
        {
            HandheldTokens.Use(HandheldLook.BigType);
            Assert.Equal(N("NameLeft"), HandheldTokens.StageNameLeft);
            Assert.Equal(N("NameBottom"), HandheldTokens.StageNameBottom);
            Assert.Equal(1360, HandheldTokens.StageNameWidth);
            Assert.Equal(1120, HandheldTokens.StageNameWidthBesideMedium);
            Assert.Equal(680, HandheldTokens.StageNameHeight);
            Assert.Equal(N("maxLines"), HandheldTokens.StageNameMaxLines);
            Assert.Equal(N("lineHeight"), BigTypeLayout.LineHeight, 3);
            Assert.Equal(N("NameDimmedOpacity"), HandheldTokens.StageNameDimmedOpacity);
            Assert.Equal(new Avalonia.Thickness(N("LabelLeft"), N("LabelTop"), 0, 0), HandheldTokens.StageLabelMargin);
            Assert.Equal(N("ClockRight"), HandheldTokens.StageStatusMargin.Right);
            Assert.Equal(N("ClockTop"), HandheldTokens.StageStatusMargin.Top);
            Assert.Equal(new Avalonia.Thickness(N("LineLeft"), 0, 0, N("LineBottom")), HandheldTokens.StageLineMargin);
            Assert.Equal(new Avalonia.Thickness(N("FactsLeft"), 0, 0, N("FactsBottom")), HandheldTokens.StageFactsMargin);
            Assert.Equal(new Avalonia.Thickness(0, 0, N("HintsRight"), N("HintsBottom")), HandheldTokens.StageHintsMargin);
            Assert.Equal(N("HintGap"), HandheldTokens.StageHintGap);
            Assert.Equal(Size("Label"), HandheldTokens.StageLabel);
            Assert.Equal(Size("Clock"), HandheldTokens.StageClock);
            Assert.Equal(Size("Battery"), HandheldTokens.StageBattery);
            Assert.Equal(Size("Line"), HandheldTokens.StageLine);
            Assert.Equal(Size("Facts"), HandheldTokens.StageFacts);
            Assert.Equal(Size("Hint"), HandheldTokens.StageHint);
            Assert.Equal(Size("HintGlyph"), HandheldTokens.StageHintGlyphSize);
            Assert.Equal(N("MediumRight"), HandheldTokens.StageMediumRight);
            Assert.Equal(N("MediumTop"), HandheldTokens.StageMediumTop);
            Assert.Equal(N("MediumWidth"), HandheldTokens.StageMediumWidth);
            Assert.Equal(N("MediumHeight"), HandheldTokens.StageMediumHeight);
            // The native medium's frame is centred on the case's box.
            var frame = HandheldTokens.StageMediumFrame(1920);
            Assert.Equal(1920 - N("MediumRight") - (N("MediumWidth") / 2), frame.Center.X, 3);
            Assert.Equal(N("MediumTop") + (N("MediumHeight") / 2), frame.Center.Y, 3);
            Assert.True(HandheldTokens.IsBigType);
        }
        finally
        {
            HandheldTokens.Use(HandheldLook.Classic);
        }
        Assert.True(HandheldTokens.IsClassic);
    }
}
