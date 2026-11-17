using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B15: the four screens' motion (proposals-screens.html, the owner's picks of 16 November 2026) as pure functions of time, held to
/// tokens.js, at fixed moments, and held still (the device's say or the app's Reduce motion: everything in place at once).
/// </summary>
public class HandheldBigTypeScreensMotionTests
{
    private static readonly string Tokens =
        File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));

    private static double Entry(string name, string field)
    {
        var match = Regex.Match(Tokens, $@"\b{name}:\s*\{{[^}}]*\b{field}:\s*(?<value>-?[0-9.]+)");
        Assert.True(match.Success, $"tokens.js has no {name}.{field}");
        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    private static double Screen(string name)
    {
        var block = Regex.Match(Tokens, @"\n  screens: \{(?<body>.*?)\n  \},", RegexOptions.Singleline).Groups["body"].Value;
        var match = Regex.Match(block, $@"\b{name}:\s*(?<value>-?[0-9.]+)");
        Assert.True(match.Success, $"tokens.js screens has no {name}");
        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TheMotion_IsTokensJs()
    {
        Assert.Equal(Entry("ScreenLettersIn", "ms"), BigTypeScreensMotion.LettersInMilliseconds);
        Assert.Equal(Entry("ScreenLettersIn", "delay"), BigTypeScreensMotion.LettersInDelay);
        Assert.Equal(Entry("ScreenLettersIn", "stagger"), BigTypeScreensMotion.LettersInStagger);
        Assert.Equal(Entry("ScreenLettersIn", "staggerTotal"), BigTypeScreensMotion.LettersInStaggerTotal);
        Assert.Equal(Entry("ScreenLettersIn", "fromEmUp"), BigTypeScreensMotion.LettersFromEmUp, 4);
        Assert.Equal(Entry("ScreenLettersIn", "tiltUp"), BigTypeScreensMotion.LettersTiltUp, 4);
        Assert.Equal(Entry("ScreenLettersIn", "fromEmDown"), BigTypeScreensMotion.LettersFromEmDown, 4);
        Assert.Equal(Entry("ScreenLettersIn", "tiltDown"), BigTypeScreensMotion.LettersTiltDown, 4);
        Assert.Equal(Entry("ScreenLettersIn", "fromEmSide"), BigTypeScreensMotion.LettersFromEmSide, 4);
        Assert.Equal(Entry("ScreenLettersOut", "ms"), BigTypeScreensMotion.LettersOutMilliseconds);
        Assert.Equal(Entry("ScreenLettersOut", "stagger"), BigTypeScreensMotion.LettersOutStagger);
        Assert.Equal(Entry("ScreenLettersOut", "staggerMax"), BigTypeScreensMotion.LettersOutStaggerMax);
        Assert.Equal(Entry("ScreenLettersOut", "toEmSide"), BigTypeScreensMotion.LettersOutEmSide, 4);
        Assert.Equal(Entry("ScreenLettersOut", "toEmVertical"), BigTypeScreensMotion.LettersOutEmVertical, 4);
        Assert.Equal(Entry("ScreenHintsIn", "ms"), BigTypeScreensMotion.HintsInMilliseconds);
        Assert.Equal(Entry("ScreenHintsIn", "rise"), BigTypeScreensMotion.HintsInRise);
        Assert.Equal(Entry("ScreenHintsIn", "stagger"), BigTypeScreensMotion.HintsInStagger);
        Assert.Equal(Entry("ScreenLayerFade", "ms"), BigTypeScreensMotion.LayerFadeMilliseconds);
        Assert.Equal(Entry("ShelfIn", "ms"), BigTypeScreensMotion.ShelfInMilliseconds);
        Assert.Equal(Entry("ShelfIn", "fromScale"), BigTypeScreensMotion.ShelfInFromScale, 4);
        Assert.Equal(Entry("ShelfIn", "stagger"), BigTypeScreensMotion.ShelfInStagger);
        Assert.Equal(Entry("ShelfIn", "maxSteps"), BigTypeScreensMotion.ShelfMaxSteps);
        Assert.Equal(Entry("ShelfOut", "ms"), BigTypeScreensMotion.ShelfOutMilliseconds);
        Assert.Equal(Entry("ShelfOut", "toScale"), BigTypeScreensMotion.ShelfOutToScale, 4);
        Assert.Equal(Entry("ShelfOut", "push"), BigTypeScreensMotion.ShelfOutPush);
        Assert.Equal(Entry("ShelfOut", "stagger"), BigTypeScreensMotion.ShelfOutStagger);
        Assert.Equal(Entry("ShelfFocus", "ms"), BigTypeScreensMotion.ShelfFocusMilliseconds);
        Assert.Equal(Entry("ShelfScroll", "ms"), BigTypeScreensMotion.ShelfScrollMilliseconds);
        Assert.Equal(Entry("ShelfBob", "msEach"), BigTypeScreensMotion.ShelfBobMilliseconds);
        Assert.Equal(Entry("ShelfBob", "px"), BigTypeScreensMotion.ShelfBobRise);
        Assert.Equal(Entry("WallIn", "ms"), BigTypeScreensMotion.WallInMilliseconds);
        Assert.Equal(Entry("WallIn", "fromScale"), BigTypeScreensMotion.WallInFromScale, 4);
        Assert.Equal(Entry("WallIn", "stagger"), BigTypeScreensMotion.WallInStagger);
        Assert.Equal(Entry("WallOut", "ms"), BigTypeScreensMotion.WallOutMilliseconds);
        Assert.Equal(Entry("WallOut", "drop"), BigTypeScreensMotion.WallOutDrop);
        Assert.Equal(Entry("WallOut", "stagger"), BigTypeScreensMotion.WallOutStagger);
        Assert.Equal(Entry("WallMove", "ms"), BigTypeScreensMotion.WallMoveMilliseconds);
        Assert.Equal(Entry("WallFilterOut", "ms"), BigTypeScreensMotion.WallFilterOutMilliseconds);
        Assert.Equal(Entry("WallFilterOut", "toScale"), BigTypeScreensMotion.WallFilterOutToScale, 4);
        Assert.Equal(Entry("WallFilterOut", "fadeMs"), BigTypeScreensMotion.WallFilterOutFadeMilliseconds);
        Assert.Equal(Entry("Pulse", "ms"), BigTypeScreensMotion.PulseMilliseconds);
        Assert.Equal(Entry("Pulse", "from"), BigTypeScreensMotion.PulseFrom, 4);
        Assert.Equal(Entry("ReelSlide", "ms"), BigTypeScreensMotion.ReelSlideMilliseconds);
        Assert.Equal(Entry("PhotoOpen", "ms"), BigTypeScreensMotion.PhotoOpenMilliseconds);
        Assert.Equal(Entry("PhotoClose", "ms"), BigTypeScreensMotion.PhotoCloseMilliseconds);
        Assert.Equal(Entry("ReelChrome", "ms"), BigTypeScreensMotion.ReelChromeMilliseconds);
        Assert.Equal(Entry("PortIn", "ms"), BigTypeScreensMotion.PortInMilliseconds);
        Assert.Equal(Entry("PortIn", "drop"), BigTypeScreensMotion.PortInDrop);
        Assert.Equal(Entry("PortIn", "fromScale"), BigTypeScreensMotion.PortInFromScale, 4);
        Assert.Equal(Entry("PortIn", "fadeMs"), BigTypeScreensMotion.PortFadeMilliseconds);
        Assert.Equal(Entry("PortOut", "ms"), BigTypeScreensMotion.PortOutMilliseconds);
        Assert.Equal(Entry("PortOut", "toScale"), BigTypeScreensMotion.PortOutToScale, 4);
        Assert.Equal(Entry("PortFloat", "msEach"), BigTypeScreensMotion.PortFloatMilliseconds);
        Assert.Equal(Entry("PortFloat", "from"), BigTypeScreensMotion.PortFloatFrom);
        Assert.Equal(Entry("PortFloat", "to"), BigTypeScreensMotion.PortFloatTo);
        Assert.Equal(Entry("PortWipe", "ms"), BigTypeScreensMotion.PortWipeMilliseconds);
        Assert.Equal(Entry("PortWipe", "toShare"), BigTypeScreensMotion.PortWipeShare, 4);
        Assert.Equal(Screen("ReelPitch"), BigTypeScreensMotion.ReelPitch);
        Assert.Equal(Screen("ReelSideScale"), BigTypeScreensMotion.ReelSideScale, 4);
        Assert.Equal(Screen("ReelSideOpacity"), BigTypeScreensMotion.ReelSideOpacity, 4);
    }

    [Fact]
    public void TheLayout_IsTokensJs()
    {
        // Every number of tokens.js's screens block is HandheldTokens' of the same name (a radius share by its own name).
        var block = Regex.Match(Tokens, @"\n  screens: \{(?<body>.*?)\n  \},", RegexOptions.Singleline).Groups["body"].Value;
        var names = Regex.Matches(block, @"\b(?<name>[A-Z][A-Za-z]+):\s*(?<value>-?[0-9.]+)");
        Assert.True(names.Count > 100);
        foreach (Match match in names)
        {
            var name = match.Groups["name"].Value;
            var mapped = name switch
            {
                "ShelfIconRadius" => "ShelfIconRadiusShare",
                "WallBadgeRadius" => "WallBadgeRadiusShare",
                "ReelPlayMark" => "ReelPlayMarkShare",
                "HintsRight" or "HintsBottom" or "Hint" or "HintGap" or "HintOpacity" or "HintKey" or "HintKeyStroke" or "HintKeyLetter"
                    or "HintKeyGap" or "HintKeyWidePadX" or "HintKeyWideRadius" => "Screen" + name,
                "ReelPitch" or "ReelSideScale" or "ReelSideOpacity" => null,
                _ => name,
            };
            if (mapped is null)
                continue;
            var property = typeof(HandheldTokens).GetProperty(mapped);
            Assert.True(property is not null, $"HandheldTokens has no {mapped} (tokens.js screens.{name})");
            var value = Convert.ToDouble(property!.GetValue(null), CultureInfo.InvariantCulture);
            Assert.Equal(double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture), value, 4);
        }
    }

    [Fact]
    public void Letters_LandFromTheSidePressed_AndLeaveTheOtherWay()
    {
        // From the right: the first letter starts 0.55 em to the right, unseen, and lands by delay + 520.
        var start = BigTypeScreensMotion.LetterIn(0, 6, BigTypeScreenSide.Right, 0f);
        Assert.Equal(BigTypeScreensMotion.LettersFromEmSide, start.X, 4);
        Assert.Equal(0f, start.Opacity);
        Assert.Equal(BigTypeLetterPose.Rest, BigTypeScreensMotion.LetterIn(0, 6, BigTypeScreenSide.Right, 580f));
        // From the left the last letter goes first.
        Assert.True(BigTypeScreensMotion.LetterDelay(5, 6, BigTypeScreenSide.Left) < BigTypeScreensMotion.LetterDelay(0, 6, BigTypeScreenSide.Left));
        // From below (a first show) it rises 0.9 em tilted 6 degrees.
        var rise = BigTypeScreensMotion.LetterIn(0, 3, BigTypeScreenSide.Up, 0f);
        Assert.Equal((0f, 0.9f, 6f), (rise.X, rise.Y, rise.Degrees));
        // Many letters share 300 ms of stagger.
        Assert.Equal(60f + (29 * 10f), BigTypeScreensMotion.LetterDelay(29, 30, BigTypeScreenSide.Right), 3);
        // The old name leaves the other way: from the right, toward the left.
        var leaving = BigTypeScreensMotion.LetterOut(0, BigTypeScreenSide.Right, 85f)!.Value;
        Assert.True(leaving.X < 0f && leaving.Opacity is > 0f and < 1f);
        Assert.Null(BigTypeScreensMotion.LetterOut(0, BigTypeScreenSide.Right, 171f));
        // A run's short form: all together, a short way, over 200 ms.
        Assert.Equal(BigTypeScreensMotion.LetterIn(0, 6, BigTypeScreenSide.Right, 100f, quick: true),
            BigTypeScreensMotion.LetterIn(5, 6, BigTypeScreenSide.Right, 100f, quick: true));
        Assert.Equal(BigTypeLetterPose.Rest, BigTypeScreensMotion.LetterIn(5, 6, BigTypeScreenSide.Right, 200f, quick: true));
    }

    [Fact]
    public void Shelf_GrowsInFromTheFocus_AndGoesAwayFromIt()
    {
        Assert.Equal((0.45f, 0f), BigTypeScreensMotion.ShelfIconIn(0, 0, 0f));
        // Two cells away starts 2 x 34 ms later.
        Assert.Equal((0.45f, 0f), BigTypeScreensMotion.ShelfIconIn(2, 0, 68f));
        Assert.Equal((1f, 1f), BigTypeScreensMotion.ShelfIconIn(2, 0, 68f + 440f));
        // Far ones wait at most six steps.
        Assert.Equal(BigTypeScreensMotion.Steps(9, 9), 6f);
        var (x, y, scale, opacity) = BigTypeScreensMotion.ShelfIconOut(1, -1, 240f + 26f);
        Assert.Equal((52f, -52f, 0.55f, 0f), (x, y, scale, opacity));
        Assert.Equal((0f, 0f, 1f, 1f), BigTypeScreensMotion.ShelfIconOut(1, 0, 18f));
        // The focus's change is the CSS transition: from where it is, on the spring, there by 380 ms.
        Assert.Equal(1.12f, BigTypeScreensMotion.Focus(1f, 1.12f, 380f));
        Assert.Equal(1f, BigTypeScreensMotion.Focus(1f, 1.12f, 0f));
        // The bob: up 6 px at 1.5 s, back at 3 s.
        Assert.Equal(-6f, BigTypeScreensMotion.ShelfBob(1500f), 3);
        Assert.Equal(0f, BigTypeScreensMotion.ShelfBob(3000f), 3);
    }

    [Fact]
    public void Wall_ArrivesByColumnAndRow_DropsByColumn_PulsesAnUnlock()
    {
        Assert.Equal((0.3f, 0f), BigTypeScreensMotion.WallBadgeIn(3, 2, 5 * 38f));
        Assert.Equal((1f, 1f), BigTypeScreensMotion.WallBadgeIn(3, 2, (5 * 38f) + 440f));
        Assert.Equal((0f, 1f), BigTypeScreensMotion.WallBadgeOut(4, 48f));
        Assert.Equal((80f, 0f), BigTypeScreensMotion.WallBadgeOut(4, 48f + 180f));
        Assert.Equal(0f, BigTypeScreensMotion.WallMoved(0f));
        Assert.Equal(1f, BigTypeScreensMotion.WallMoved(420f));
        Assert.Equal((0.3f, 0f), BigTypeScreensMotion.WallBadgeFiltered(380f));
        Assert.Equal(0.6f, BigTypeScreensMotion.Pulse(0f), 4);
        Assert.Equal(1f, BigTypeScreensMotion.Pulse(320f));
    }

    [Fact]
    public void Reel_PlacesThePicturesEitherSide_AndFlipsFromTheThumbnail()
    {
        Assert.Equal((0f, 1f, 1f), BigTypeScreensMotion.ReelPose(0f));
        Assert.Equal((956f, 0.86f, 0.45f), BigTypeScreensMotion.ReelPose(1f));
        Assert.Equal((-956f, 0.86f, 0.45f), BigTypeScreensMotion.ReelPose(-1f));
        Assert.Equal(0f, BigTypeScreensMotion.ReelPose(2f).Opacity);
        // Halfway between two stops it is halfway (the row sliding).
        var half = BigTypeScreensMotion.ReelPose(0.5f);
        Assert.Equal(478f, half.X);
        Assert.Equal(0.93f, half.Scale, 4);
        Assert.Equal(0f, BigTypeScreensMotion.PhotoFlip(opening: true, 0f));
        Assert.Equal(1f, BigTypeScreensMotion.PhotoFlip(opening: true, 480f));
        Assert.Equal(1f, BigTypeScreensMotion.PhotoFlip(opening: false, 0f));
        Assert.Equal(0f, BigTypeScreensMotion.PhotoFlip(opening: false, 260f));
        Assert.Equal(0f, BigTypeScreensMotion.Chrome(1f, shown: false, 300f));
    }

    [Fact]
    public void Porthole_RisesThroughTheHinge_Floats_WipesTheNext_AndDrops()
    {
        Assert.Equal((1040f, 0.5f, 0f), BigTypeScreensMotion.PortIn(0f));
        Assert.Equal((0f, 1f, 1f), BigTypeScreensMotion.PortIn(700f));
        Assert.Equal((0f, 1f, 1f), BigTypeScreensMotion.PortOut(0f));
        Assert.Equal((1040f, 0.5f, 0f), BigTypeScreensMotion.PortOut(320f));
        Assert.Equal(10f, BigTypeScreensMotion.PortFloat(0f), 3);
        Assert.Equal(-14f, BigTypeScreensMotion.PortFloat(5000f), 3);
        Assert.Equal(10f, BigTypeScreensMotion.PortFloat(10000f), 3);
        Assert.Equal(0f, BigTypeScreensMotion.PortWipeRadius(0f, 800f));
        Assert.Equal(960f, BigTypeScreensMotion.PortWipeRadius(760f, 800f), 3);
    }

    [Fact]
    public void HeldStill_EverythingIsInPlaceAtOnce()
    {
        Assert.Equal(BigTypeLetterPose.Rest, BigTypeScreensMotion.LetterIn(0, 4, BigTypeScreenSide.Left, 0f, holdStill: true));
        Assert.Null(BigTypeScreensMotion.LetterOut(0, BigTypeScreenSide.Left, 0f, holdStill: true));
        Assert.Equal((0f, 1f), BigTypeScreensMotion.HintIn(2, 0f, holdStill: true));
        Assert.Equal(1f, BigTypeScreensMotion.LayerOpacity(true, 0f, holdStill: true));
        Assert.Equal(0f, BigTypeScreensMotion.LayerOpacity(false, 0f, holdStill: true));
        Assert.Equal((1f, 1f), BigTypeScreensMotion.ShelfIconIn(3, 3, 0f, holdStill: true));
        Assert.Equal(0f, BigTypeScreensMotion.ShelfIconOut(1, 1, 0f, holdStill: true).Opacity);
        Assert.Equal(1.12f, BigTypeScreensMotion.Focus(1f, 1.12f, 0f, holdStill: true));
        Assert.Equal(0f, BigTypeScreensMotion.ShelfBob(700f, holdStill: true));
        Assert.Equal((1f, 1f), BigTypeScreensMotion.WallBadgeIn(6, 2, 0f, holdStill: true));
        Assert.Equal(0f, BigTypeScreensMotion.WallBadgeOut(0, 0f, holdStill: true).Opacity);
        Assert.Equal(1f, BigTypeScreensMotion.WallMoved(0f, holdStill: true));
        Assert.Equal(1f, BigTypeScreensMotion.Pulse(0f, holdStill: true));
        Assert.Equal(1f, BigTypeScreensMotion.PhotoFlip(opening: true, 0f, holdStill: true));
        Assert.Equal(0f, BigTypeScreensMotion.PhotoFlip(opening: false, 0f, holdStill: true));
        Assert.Equal((0f, 1f, 1f), BigTypeScreensMotion.PortIn(0f, holdStill: true));
        Assert.Equal(0f, BigTypeScreensMotion.PortOut(0f, holdStill: true).Opacity);
        Assert.Equal(0f, BigTypeScreensMotion.PortFloat(1234f, holdStill: true));
        Assert.Equal(960f, BigTypeScreensMotion.PortWipeRadius(0f, 800f, holdStill: true), 3);
        var glide = new BigTypeGlide(0f, 500f, BigTypeScreensMotion.Soft);
        glide.To(3f, 10f, holdStill: true);
        Assert.Equal(3f, glide.At(10f));
    }

    [Fact]
    public void Glide_OneChangeRunsItsCurve_AHeldRunNeverJumpsNorRestarts()
    {
        var glide = new BigTypeGlide(0f, 500f, BigTypeScreensMotion.Soft);
        glide.To(1f, 0f);
        Assert.Equal(0f, glide.At(0f));
        Assert.Equal(BigTypeScreensMotion.Soft(0.5f), glide.At(250f), 4);
        Assert.Equal(1f, glide.At(500f));
        // A direction held: a new target every 40 ms. Each frame's value moves on from the last and never jumps back to rest or past
        // the target, and it settles 500 ms after the last change.
        var held = new BigTypeGlide(0f, 500f, BigTypeScreensMotion.Soft);
        var last = 0f;
        var now = 0f;
        for (var step = 1; step <= 8; step++)
        {
            held.To(step, now);
            for (var frame = 0; frame < 5; frame++, now += 8f)
            {
                var value = held.At(now);
                Assert.True(value >= last - 0.001f, $"went back at {now} ms: {value} after {last}");
                Assert.True(value - last < 1.0f, $"jumped at {now} ms: {last} to {value}");
                Assert.True(value <= 8.3f);
                last = value;
            }
        }
        Assert.Equal(8f, held.At(now + 500f));
    }
}
