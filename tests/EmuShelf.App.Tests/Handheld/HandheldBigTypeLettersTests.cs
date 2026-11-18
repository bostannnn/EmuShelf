using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B6: the name's letters, the flood, the wipe and the letters' hop, as maths at fixed moments that renders nothing (the numbers
/// held to tokens.js), and the native Stage's atlas of a name, held to where the Avalonia name draws its letters at rest.
/// </summary>
[Collection(nameof(HandheldTokenLookCollection))]
public class HandheldBigTypeLettersTests
{
    [Fact]
    public void ACircularTransition_CoversEveryCornerIncludingItsSoftEdge()
    {
        // The Stage starts its wipe below the canvas. The upper right corner is five pixels from this origin.
        var centre = new System.Numerics.Vector2(0, 4);
        Assert.False(BigTypeMotion.CoversCanvas(centre, 5.49f, 3, 2));
        Assert.True(BigTypeMotion.CoversCanvas(centre, 5.5f, 3, 2));
    }

    // ---- tokens.js ----

    [Fact]
    public void TheNumbers_AreTokensJs()
    {
        var tokens = File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));
        double Entry(string name, string field)
        {
            var match = Regex.Match(tokens, $@"\b{name}:\s*\{{[^}}]*\b{field}:\s*(?<value>-?[0-9.]+)");
            Assert.True(match.Success, $"tokens.js has no {name}.{field}");
            return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        }
        Assert.Equal(Entry("LetterIn", "ms"), BigTypeMotion.LetterInMilliseconds);
        Assert.Equal(Entry("LetterIn", "delay"), BigTypeMotion.LetterInDelay);
        Assert.Equal(Entry("LetterIn", "staggerMax"), BigTypeMotion.LetterInStaggerMax);
        Assert.Equal(Entry("LetterIn", "staggerTotal"), BigTypeMotion.LetterInStaggerTotal);
        Assert.Equal(Entry("LetterIn", "fromEm"), BigTypeMotion.LetterInFromEm, 4);
        Assert.Equal(Entry("LetterIn", "fromEmVertical"), BigTypeMotion.LetterInFromEmVertical, 4);
        Assert.Equal(Entry("LetterIn", "openEm"), BigTypeMotion.LetterInOpenEm, 4);
        Assert.Equal(Entry("LetterIn", "openTilt"), BigTypeMotion.LetterInOpenTilt, 4);
        Assert.Equal(Entry("LetterIn", "backEm"), BigTypeMotion.LetterInBackEm, 4);
        Assert.Equal(Entry("LetterIn", "backTilt"), BigTypeMotion.LetterInBackTilt, 4);
        Assert.Equal(Entry("LetterIn", "riseEm"), BigTypeMotion.LetterInRiseEm, 4);
        Assert.Equal(Entry("LetterIn", "riseTilt"), BigTypeMotion.LetterInRiseTilt, 4);
        Assert.Equal(Entry("LetterOut", "ms"), BigTypeMotion.LetterOutMilliseconds);
        Assert.Equal(Entry("LetterOut", "staggerMax"), BigTypeMotion.LetterOutStaggerMax);
        Assert.Equal(Entry("LetterOut", "staggerTotal"), BigTypeMotion.LetterOutStaggerTotal);
        Assert.Equal(Entry("LetterOut", "toEm"), BigTypeMotion.LetterOutToEm, 4);
        Assert.Equal(Entry("LetterOut", "openEm"), BigTypeMotion.LetterOutOpenEm, 4);
        Assert.Equal(Entry("LetterOut", "backEm"), BigTypeMotion.LetterOutBackEm, 4);
        Assert.Equal(Entry("LetterOut", "riseEm"), BigTypeMotion.LetterOutRiseEm, 4);
        Assert.Equal(Entry("LetterOut", "stillMs"), BigTypeMotion.LetterOutStillMilliseconds);
        Assert.Equal(Entry("Flood", "ms"), BigTypeMotion.FloodMilliseconds);
        Assert.Equal(Entry("Flood", "radius"), BigTypeMotion.FloodRadius);
        Assert.Equal(Entry("Wipe", "ms"), BigTypeMotion.WipeMilliseconds);
        Assert.Equal(Entry("Wipe", "delay"), BigTypeMotion.WipeDelay);
        Assert.Equal(Entry("Wipe", "radius"), BigTypeMotion.WipeRadius);
        Assert.Equal(Entry("Wipe", "below"), BigTypeMotion.WipeBelow);
        Assert.Equal(Entry("IdleHop", "letterDelay"), BigTypeMotion.LetterHopDelay);
        Assert.Equal(Entry("IdleHop", "letterStagger"), BigTypeMotion.LetterHopStagger);
        Assert.Equal(Entry("IdleHop", "letterRiseEm"), BigTypeMotion.LetterHopRiseEm, 4);
        Assert.Contains("Leave: 'cubic-bezier(.5, 0, .9, .5)'", tokens);
        Assert.Contains("Flood:     { ms: 640, easing: 'cubic-bezier(.2, .6, .2, 1)'", tokens);
    }

    // ---- The letters arriving (move 2) ----

    [Fact]
    public void Letters_ArriveFromTheMove_StaggeredInReadingOrder_AndLandOnTime()
    {
        const int count = 10; // 26 ms apart (300 / 10 is more than 26)
        Assert.Equal(26f, BigTypeMotion.LetterInStagger(count));

        // Before its turn a letter waits at its start, clear: moving right, 0.55 em to the right.
        Assert.Equal(new BigTypeLetterPose(0.55f, 0f, 0f, 0f), BigTypeMotion.Arrive(BigTypeEntrance.Right, 0, count, 0f));
        Assert.Equal(new BigTypeLetterPose(0.55f, 0f, 0f, 0f), BigTypeMotion.Arrive(BigTypeEntrance.Right, 0, count, 90f));

        // Half way through its 540 ms on the landing curve the first letter has already overshot its place (the spring's 1.5).
        var middle = BigTypeMotion.Arrive(BigTypeEntrance.Right, 0, count, 90f + 270f);
        Assert.True(middle.X < 0f, $"half way the letter is at {middle.X} em");
        Assert.Equal(1f, middle.Opacity);

        // The first letter lands 630 ms in; the last 26 ms later for each letter before it.
        Assert.Equal(BigTypeLetterPose.Rest, BigTypeMotion.Arrive(BigTypeEntrance.Right, 0, count, 630f));
        Assert.Equal(new BigTypeLetterPose(0.55f, 0f, 0f, 0f), BigTypeMotion.Arrive(BigTypeEntrance.Right, 9, count, 90f + (9 * 26f)));
        Assert.Equal(BigTypeLetterPose.Rest, BigTypeMotion.Arrive(BigTypeEntrance.Right, 9, count, 90f + (9 * 26f) + 540f));
        Assert.Equal(90f + (9 * 26f) + 540f, BigTypeMotion.ArrivalMilliseconds(count));

        // Moving left the letters come from the left and the last one leads.
        var leftLast = BigTypeMotion.Arrive(BigTypeEntrance.Left, count - 1, count, 90f + 100f);
        var leftFirst = BigTypeMotion.Arrive(BigTypeEntrance.Left, 0, count, 90f + 100f);
        Assert.True(leftLast.Opacity > 0f && leftFirst.Opacity == 0f);
        Assert.Equal(-0.55f, leftFirst.X, 4);

        // A long name shares 300 ms: 30 letters are 10 ms apart.
        Assert.Equal(10f, BigTypeMotion.LetterInStagger(30));
    }

    [Theory]
    [InlineData(BigTypeEntrance.Right, 0.55f, 0f, 0f, -0.45f, 0f)]
    [InlineData(BigTypeEntrance.Left, -0.55f, 0f, 0f, 0.45f, 0f)]
    [InlineData(BigTypeEntrance.Up, 0f, -0.5f, 0f, 0f, 0.45f)]
    [InlineData(BigTypeEntrance.Down, 0f, 0.5f, 0f, 0f, -0.45f)]
    [InlineData(BigTypeEntrance.Open, 0f, 0.95f, 7f, 0f, -0.6f)]
    [InlineData(BigTypeEntrance.Back, 0f, -0.7f, -5f, 0f, 0.6f)]
    [InlineData(BigTypeEntrance.Rise, 0f, 0.7f, 5f, 0f, -0.4f)]
    public void Letters_ComeAndGo_AsTheMocksEnterAndExit(BigTypeEntrance entrance, float fromX, float fromY, float tilt, float toX, float toY)
    {
        Assert.Equal((fromX, fromY, tilt), BigTypeMotion.LetterFrom(entrance));
        Assert.Equal((toX, toY), BigTypeMotion.LetterTo(entrance));
    }

    // ---- The letters leaving ----

    [Fact]
    public void Letters_Leave_TheOtherWay_Quicker_ThenAreGone()
    {
        const int count = 20; // 110 / 20 = 5.5 ms apart (under 12)
        Assert.Equal(5.5f, BigTypeMotion.LetterOutStagger(count));
        Assert.Equal(BigTypeLetterPose.Rest, BigTypeMotion.Depart(BigTypeEntrance.Right, 0, count, 0f));
        var half = BigTypeMotion.Depart(BigTypeEntrance.Right, 0, count, 85f)!.Value;
        Assert.True(half.X < 0f && half.X > -0.45f && half.Opacity is > 0f and < 1f);
        Assert.Null(BigTypeMotion.Depart(BigTypeEntrance.Right, 0, count, 170f));
        // The last letter starts 19 × 5.5 ms late and is still there when the first has gone.
        Assert.NotNull(BigTypeMotion.Depart(BigTypeEntrance.Right, 19, count, 170f));
        Assert.Null(BigTypeMotion.Depart(BigTypeEntrance.Right, 19, count, 19 * 5.5f + 170f));
        Assert.Equal((19 * 5.5f) + 170f, BigTypeMotion.DepartureMilliseconds(count), 3);
        // Leaving is always quicker than landing.
        Assert.True(BigTypeMotion.DepartureMilliseconds(count) < BigTypeMotion.ArrivalMilliseconds(count));
    }

    [Fact]
    public void HeldStill_TheNewNameIsThere_AndTheOldGoesInSixtyMilliseconds()
    {
        Assert.Equal(BigTypeLetterPose.Rest, BigTypeMotion.Arrive(BigTypeEntrance.Right, 5, 10, 0f, holdStill: true));
        Assert.Equal(0f, BigTypeMotion.ArrivalMilliseconds(10, holdStill: true));
        // Every old letter goes together, unstaggered.
        Assert.Equal(BigTypeMotion.Depart(BigTypeEntrance.Right, 0, 10, 30f, holdStill: true),
            BigTypeMotion.Depart(BigTypeEntrance.Right, 9, 10, 30f, holdStill: true));
        Assert.Null(BigTypeMotion.Depart(BigTypeEntrance.Right, 9, 10, 60f, holdStill: true));
        Assert.Equal(60f, BigTypeMotion.DepartureMilliseconds(10, holdStill: true));
    }

    // ---- The flood and the wipe (move 3) ----

    [Fact]
    public void TheFlood_GrowsFromNothingTo1640In640Milliseconds_OrCoversAtOnceHeldStill()
    {
        Assert.Equal(0f, BigTypeMotion.FloodRadiusAt(0f));
        var half = BigTypeMotion.FloodRadiusAt(320f);
        // cubic-bezier(.2, .6, .2, 1) is well past half way at half time.
        Assert.InRange(half, 0.8f * 1640f, 1640f);
        Assert.Equal(1640f, BigTypeMotion.FloodRadiusAt(640f));
        Assert.True(BigTypeMotion.Flooding(639f));
        Assert.False(BigTypeMotion.Flooding(640f));
        Assert.Equal(1640f, BigTypeMotion.FloodRadiusAt(0f, holdStill: true));
        Assert.False(BigTypeMotion.Flooding(0f, holdStill: true));
    }

    [Fact]
    public void TheWipe_GrowsTo2300In760Milliseconds_FromBelowTheFocusedTilesX()
    {
        Assert.Equal(0f, BigTypeMotion.WipeRadiusAt(0f));
        Assert.InRange(BigTypeMotion.WipeRadiusAt(380f), 0.8f * 2300f, 2300f);
        Assert.Equal(2300f, BigTypeMotion.WipeRadiusAt(760f));
        // The Launcher's 1240 canvas sits centred under the Stage's 1920 (the mock's cx + 170 at half scale), 40 below its edge.
        Assert.Equal((960f, 1120f), BigTypeMotion.WipeOrigin(620f, 1240f, 1920f, 1080f));
        Assert.Equal((340f + 100f, 1120f), BigTypeMotion.WipeOrigin(100f, 1240f, 1920f, 1080f));
        // Swapped, the Launcher is as wide as the Stage's canvas: the same x.
        Assert.Equal((500f, 1120f), BigTypeMotion.WipeOrigin(500f, 1920f, 1920f, 1080f));
    }

    // ---- The letters' hop (move 8) ----

    [Fact]
    public void TheLetters_HopAfterTheTiles_45MillisecondsApart_A0Point07EmHigh()
    {
        Assert.Equal(0f, BigTypeMotion.LetterHop(0, 0f));
        Assert.Equal(0f, BigTypeMotion.LetterHop(0, 260f));
        // At the top of its 560 ms (where the hop's curve is half way) the letter is 0.07 em up, and never higher.
        var highest = Enumerable.Range(0, 561).Min(ms => BigTypeMotion.LetterHop(0, 260f + ms));
        Assert.Equal(-0.07f, highest, 3);
        Assert.Equal(0f, BigTypeMotion.LetterHop(0, 260f + 560f));
        // Letter 3 is 135 ms behind letter 0.
        Assert.Equal(BigTypeMotion.LetterHop(0, 400f), BigTypeMotion.LetterHop(3, 400f + 135f), 5);
        Assert.Equal(260f + (9 * 45f) + 560f, BigTypeMotion.LetterHopWaveMilliseconds(10));
        // On the tiles' schedule: none before 4.8 s after a press, none held still.
        Assert.Null(BigTypeMotion.HopStart(4799f, holdStill: false));
        Assert.Equal(4800f, BigTypeMotion.HopStart(4800f, holdStill: false));
        Assert.Null(BigTypeMotion.HopStart(10_000f, holdStill: true));
    }

    // ---- One pass for a zone every backdrop answers the same way ----

    [Fact]
    public void AWipe_DrawsAZoneOnce_WhenEveryBackdropShowingGivesItTheSameAnswer_AndOncePerBackdropOtherwise()
    {
        var dark = new StageCompositor.ZoneColours(new System.Numerics.Vector3(0.1f, 0.2f, 0.3f), System.Numerics.Vector3.One, 0.28f);
        var white = new StageCompositor.ZoneColours(System.Numerics.Vector3.One, new System.Numerics.Vector3(0.1f, 0.2f, 0.3f), 0.28f);
        var circle = new System.Numerics.Vector4(700, -40, 900, 1);
        var under = new StageCompositor.InkPass(default, circle with { W = -1 }, default, zone => zone == EmuShelf.Rendering.Stage.Backdrops.StageInkZone.Left ? dark : white);
        var over = new StageCompositor.InkPass(circle, default, default, zone => zone == EmuShelf.Rendering.Stage.Backdrops.StageInkZone.Left ? dark : dark);

        // Outside a wipe: the one pass, as it is.
        Assert.Same(under, Assert.Single(StageCompositor.PassesFor(EmuShelf.Rendering.Stage.Backdrops.StageInkZone.Left, [under])));
        // The left zone is dark over both: one pass, with no circle.
        var once = Assert.Single(StageCompositor.PassesFor(EmuShelf.Rendering.Stage.Backdrops.StageInkZone.Left, [under, over]));
        Assert.Equal((default(System.Numerics.Vector4), default(System.Numerics.Vector4), default(System.Numerics.Vector4)), (once.Clip0, once.Clip1, once.Clip2));
        Assert.Equal(dark, once.Ink(EmuShelf.Rendering.Stage.Backdrops.StageInkZone.Left));
        // A corner is white over one and dark over the other: a pass per backdrop, each in its own circle.
        var each = StageCompositor.PassesFor(EmuShelf.Rendering.Stage.Backdrops.StageInkZone.TopRight, [under, over]);
        Assert.Equal(2, each.Count);
        Assert.Same(under, each[0]);
        Assert.Same(over, each[1]);
        // Text with no zone (the message toast) is drawn once, uncut.
        var toast = Assert.Single(StageCompositor.PassesFor(EmuShelf.Rendering.Stage.Backdrops.StageInkZone.None, [under, over]));
        Assert.Equal(default, toast.Clip0);
    }

    // ---- The atlas ----

    [AvaloniaFact]
    public void TheAtlas_PutsEachLetterWhereTheNameDrawsIt_AtRest()
    {
        HandheldTokens.Use(EmuShelf.Core.Handheld.HandheldLook.BigType);
        try
        {
            foreach (var (text, width) in new[] { ("PS2", 1360d), ("Burnout 3: Takedown", 1120d), ("Ico", 1360d) })
                AssertAtlasMatches(text, width);
        }
        finally
        {
            HandheldTokens.Use(EmuShelf.Core.Handheld.HandheldLook.Classic);
        }
    }

    private static void AssertAtlasMatches(string text, double width)
    {
        var height = HandheldTokens.StageNameControlHeight;
        var name = new HandheldBigTypeName
        {
            Text = text,
            Width = width,
            Height = height,
            BoxHeight = HandheldTokens.StageNameHeight,
            Foreground = Brushes.White,
        };
        name.SetValue(TextElement.FontFamilyProperty, new FontFamily(HandheldTokens.FaceSource));
        RenderOptions.SetTextRenderingMode(name, TextRenderingMode.Antialias);
        var window = new Window { Width = width, Height = height, Content = name, Background = Brushes.Transparent };
        window.Show();
        try
        {
            name.Measure(new Size(width, height));
            name.Arrange(new Rect(0, 0, width, height));
            var pixelWidth = (int)Math.Ceiling(width);
            var pixelHeight = (int)Math.Ceiling(height);
            using var drawn = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight));
            drawn.Render(name);
            var expected = Alpha(drawn);

            var letters = name.Letters(default);
            Assert.NotNull(letters);
            name.Text = "NES";
            Assert.NotNull(name.Letters(default));
            name.Text = text;
            Assert.Same(letters, name.Letters(default));
            Assert.NotSame(letters, name.Letters(new Point(10, 20)));
            Assert.Equal(text.Count(c => !char.IsWhiteSpace(c)), letters.Letters.Count);
            var composed = new float[pixelWidth * pixelHeight];
            var atlas = letters.Atlas;
            foreach (var letter in letters.Letters)
            {
                // Each cell lands on whole pixels where the native Stage puts it at rest (1:1 on the Thor's top screen).
                var left = (int)MathF.Round(letter.X);
                var top = (int)MathF.Round(letter.Y);
                for (var y = 0; y < (int)letter.Height; y++)
                {
                    for (var x = 0; x < (int)letter.Width; x++)
                    {
                        var tx = left + x;
                        var ty = top + y;
                        if (tx < 0 || ty < 0 || tx >= pixelWidth || ty >= pixelHeight)
                            continue;
                        var a = atlas.Data[((((int)letter.AtlasY + y) * atlas.Width) + (int)letter.AtlasX + x) * 4 + 3] / 255f;
                        composed[(ty * pixelWidth) + tx] = a + (composed[(ty * pixelWidth) + tx] * (1f - a));
                    }
                }
            }
            // The atlas holds each letter once: "Burnout 3: Takedown" has repeated letters that share a cell.
            var ink = 0;
            var wrong = 0;
            double expectedX = 0, expectedY = 0, composedX = 0, composedY = 0, expectedSum = 0, composedSum = 0;
            for (var i = 0; i < composed.Length; i++)
            {
                var x = i % pixelWidth;
                var y = i / pixelWidth;
                expectedX += expected[i] * x;
                expectedY += expected[i] * y;
                expectedSum += expected[i];
                composedX += composed[i] * x;
                composedY += composed[i] * y;
                composedSum += composed[i];
                if (expected[i] > 0.5f)
                    ink++;
                if (Math.Abs(expected[i] - composed[i]) > 0.5f)
                    wrong++;
            }
            Assert.True(ink > 1000, $"'{text}' drew only {ink} ink pixels");
            // The same ink in the same place: the centres of mass within half a pixel, and only edge pixels differing a lot.
            Assert.InRange(composedSum / expectedSum, 0.97, 1.03);
            Assert.InRange(Math.Abs((composedX / composedSum) - (expectedX / expectedSum)), 0, 0.5);
            Assert.InRange(Math.Abs((composedY / composedSum) - (expectedY / expectedSum)), 0, 0.5);
            Assert.True(wrong < ink / 50, $"'{text}': {wrong} of {ink} ink pixels differ by more than half");
        }
        finally
        {
            window.Close();
        }
    }

    private static float[] Alpha(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var data = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), data.Length, size.Width * 4);
        }
        finally
        {
            handle.Free();
        }
        var alpha = new float[size.Width * size.Height];
        for (var i = 0; i < alpha.Length; i++)
            alpha[i] = data[(i * 4) + 3] / 255f;
        return alpha;
    }
}
