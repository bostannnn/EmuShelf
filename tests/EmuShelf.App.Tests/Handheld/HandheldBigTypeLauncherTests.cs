using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>Tests that switch <see cref="HandheldTokens"/>' look, which is process-wide: they never run beside another test.</summary>
[CollectionDefinition(nameof(HandheldTokenLookCollection), DisableParallelization = true)]
public sealed class HandheldTokenLookCollection;

/// <summary>
/// M47 B3: the Launcher at rest in the Big Type look takes tokens.js's numbers (<c>shape</c>, <c>launcher</c>, <c>colour</c>),
/// and switching back leaves today's look exactly as it was.
/// </summary>
[Collection(nameof(HandheldTokenLookCollection))]
public class HandheldBigTypeLauncherTokenTests
{
    private static string TokensJs() =>
        File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));

    private static double Number(string tokens, string name)
    {
        var match = Regex.Match(tokens, $@"\b{name}:\s*(?<value>-?[0-9.]+)");
        Assert.True(match.Success, $"tokens.js has no {name}");
        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    private static Color Hex(string tokens, string name)
    {
        var match = Regex.Match(tokens, $@"\b{name}:\s*'(?<value>#[0-9A-Fa-f]{{6}})'");
        Assert.True(match.Success, $"tokens.js has no {name}");
        return Color.Parse(match.Groups["value"].Value);
    }

    [Fact]
    public void BigType_TakesTheMocksShapeColourAndLayout()
    {
        var tokens = TokensJs();
        try
        {
            HandheldTokens.Use(HandheldLook.BigType);

            // shape
            Assert.Equal(Number(tokens, "TileHome"), HandheldTokens.TileHome);
            Assert.Equal(Number(tokens, "TileFolder"), HandheldTokens.TileFolder);
            Assert.Equal(Number(tokens, "TileRadius"), HandheldTokens.TileRadius.TopLeft);
            Assert.Equal(Number(tokens, "TileRadius"), HandheldTokens.TileRadiusFolder.TopLeft);
            Assert.Equal(Number(tokens, "DockTile"), HandheldTokens.DockTile);
            Assert.Equal(Number(tokens, "DockRadius"), HandheldTokens.DockRadius.TopLeft);
            Assert.Equal(Number(tokens, "RingStroke"), HandheldTokens.RingStroke);
            Assert.Equal(Number(tokens, "RingOutset"), -HandheldTokens.RingOutsetTile.Left);
            Assert.Equal(Number(tokens, "RingRadiusTile"), HandheldTokens.RingRadiusTile.TopLeft);
            Assert.Equal(Number(tokens, "RingRadiusTile"), HandheldTokens.RingRadiusTileFolder.TopLeft);
            Assert.Equal(Number(tokens, "RingRadiusDock"), HandheldTokens.RingRadiusDock.TopLeft);
            Assert.Equal(Number(tokens, "FocusScale"), HandheldTokens.FocusScale);
            Assert.Equal(Number(tokens, "DockFocusScale"), HandheldTokens.DockFocusScale);
            Assert.Equal(1, HandheldTokens.RingLift);

            // colour
            Assert.Equal(Color.FromArgb(0x2B, 255, 255, 255), ((ISolidColorBrush)HandheldTokens.TileRest).Color);
            Assert.Equal(Colors.White, ((ISolidColorBrush)HandheldTokens.TileFocus).Color);
            Assert.Equal(Colors.White, ((ISolidColorBrush)HandheldTokens.RingBand).Color);
            Assert.Equal(Colors.White, ((ISolidColorBrush)HandheldTokens.OnColour).Color);
            Assert.Equal(Number(tokens, "MissingTileOpacity"), HandheldTokens.MissingTileOpacity);
            Assert.Equal(Number(tokens, "EmptySlotDotOpacity"), HandheldTokens.EmptySlotDotOpacity);
            Assert.Equal(Hex(tokens, "Recent"), HandheldTokens.RecentGround);
            Assert.Equal(Hex(tokens, "Settings"), HandheldTokens.AppGround);

            // type
            Assert.Equal(100, HandheldTokens.FolderHeading);
            Assert.Equal(100, HandheldTokens.HeadingCount);
            Assert.Equal(0.45, HandheldTokens.HeadingCountOpacity);

            // launcher: where the first tile of home and of a folder lands on the 1240 × 1080 canvas, and the pitches
            var homePitch = HandheldTokens.TileHome + HandheldTokens.HomeSlotMargin.Left + HandheldTokens.HomeSlotMargin.Right;
            Assert.Equal(Number(tokens, "HomePitch"), homePitch);
            var homeColumns = Number(tokens, "HomeColumns");
            Assert.Equal(Number(tokens, "HomeLeft"), ((1240 - (homeColumns * homePitch)) / 2) + HandheldTokens.HomeSlotMargin.Left);
            Assert.Equal(Number(tokens, "HomeTop"), HandheldTokens.HomeLayerMargin.Top + HandheldTokens.HomeSlotMargin.Top);
            Assert.Equal(Number(tokens, "FolderPitch"), HandheldTokens.FolderPitch);
            Assert.Equal(Number(tokens, "FolderPitch"), HandheldTokens.FolderRowPitch);
            Assert.Equal(Number(tokens, "FolderLeft"), HandheldTokens.FolderPageMargin.Left + HandheldTokens.FolderGridMargin.Left);
            Assert.Equal(Number(tokens, "FolderTop"),
                HandheldTokens.FolderGridMargin.Top + ((HandheldTokens.FolderRowPitch - HandheldTokens.TileFolder) / 2));
            Assert.Equal(Number(tokens, "HeaderLeft"), HandheldTokens.HeaderMargin.Left);
            Assert.Equal(Number(tokens, "HeaderTop"), HandheldTokens.HeaderMargin.Top);
            Assert.Equal(Number(tokens, "HeaderHeight"), HandheldTokens.HeaderHeight);
            Assert.Equal(Number(tokens, "HeaderGap"), HandheldTokens.HeaderGap);
            Assert.Equal(HandheldTokens.HeaderGap, HandheldTokens.HeaderCountMargin.Left);
            // The dock sits DockBottom from the edge (its bar is a button's height, 32 above the canvas's bottom).
            Assert.Equal(Number(tokens, "DockBottom"), HandheldTokens.DockCornerMargin.Bottom);
            Assert.Equal(HandheldTokens.DockTile, HandheldTokens.DockBarHeight);
        }
        finally
        {
            HandheldTokens.Use(HandheldLook.Classic);
        }
    }

    [Fact]
    public void BackToTodaysLook_EveryLauncherValueIsTodays()
    {
        HandheldTokens.Use(HandheldLook.BigType);
        HandheldTokens.Use(HandheldLook.Classic);

        Assert.True(HandheldTokens.IsClassic);
        Assert.Equal(232, HandheldTokens.TileFolder);
        Assert.Equal(264, HandheldTokens.FolderPitch);
        Assert.Equal(14, HandheldTokens.RingStroke);
        Assert.Equal(HandheldMotion.LiftScale, HandheldTokens.RingLift);
        Assert.Equal(-24, HandheldTokens.RingOutsetTile.Left);
        Assert.Equal(-16, HandheldTokens.RingOutsetDock.Left);
        Assert.Equal(new Avalonia.Thickness(0, 48, 0, 0), HandheldTokens.HomeLayerMargin);
        Assert.Equal(new Avalonia.Thickness(24, 10), HandheldTokens.HomeSlotMargin);
        Assert.Equal(new Avalonia.Thickness(32, 0, 0, 0), HandheldTokens.FolderPageMargin);
        Assert.Equal(new Avalonia.Thickness(16, 40, 0, 0), HandheldTokens.FolderGridMargin);
        Assert.Same(HandheldTokens.ClassicDockFill, HandheldTokens.DockBarFill);
        Assert.Equal(112, HandheldTokens.DockBarHeight);
        Assert.Equal(32, HandheldTokens.FolderHeading);
        Assert.Equal(0.4, HandheldTokens.MissingTileOpacity);
    }

    [Fact]
    public void BigType_YourFoldersColour_IsDarkenedUntilWhiteTypeHolds()
    {
        try
        {
            HandheldTokens.Use(HandheldLook.BigType);
            // Today's palette's yellow is far too light for white type; Recent takes its slate.
            var yellow = HandheldFolderSource.ForUser(new HandheldUserFolder("f1", "Couch", "#F0C02E", []));
            var colour = HandheldTokens.LauncherColour(yellow);
            Assert.NotNull(colour);
            Assert.True(HandheldGround.HoldsFloors(colour.Value.ToUInt32()));
            Assert.Equal(HandheldTokens.RecentGround, HandheldTokens.LauncherColour(HandheldFolderSource.ForRecent([1, 2, 3])));
        }
        finally
        {
            HandheldTokens.Use(HandheldLook.Classic);
        }
    }
}

/// <summary>M47 B3: the native Launcher's ring and lift, parameterised so today's look draws exactly what it drew.</summary>
public class LauncherLookParameterTests
{
    [Fact]
    public void ClassicBands_AreTheShadersFormerConstants()
    {
        var bands = LauncherRingBands.Classic;
        Assert.Equal((4f, 18f, 22f), (bands.Inset1, bands.Inset2, bands.Inset3));
        Assert.Equal(System.Numerics.Vector4.One, bands.Band1);
        Assert.Equal(new System.Numerics.Vector4(91f / 255f, 88f / 255f, 217f / 255f, 1f), bands.Band2);
        Assert.Equal(System.Numerics.Vector4.One, bands.Band3);
    }

    [Fact]
    public void OneBand_HasNoSecondOrThird()
    {
        var bands = LauncherRingBands.One(8f, 1f, 1f, 1f);
        Assert.Equal((8f, 8f, 8f), (bands.Inset1, bands.Inset2, bands.Inset3));
        Assert.Equal(System.Numerics.Vector4.One, bands.Band1);
    }

    [Fact]
    public void ASpriteWithNoLiftOfItsOwn_LiftsAsToday()
    {
        var image = new StageImage(new StagePixels(1, 1, [0, 0, 0, 0], false), 0, 0, 1, 1);
        Assert.Equal(LauncherMotion.LiftScale, new LauncherSprite("a", image, LauncherGroup.Home, LauncherTrack.None, true, 0, 0).LiftTo);
        Assert.Equal(1.1f, new LauncherSprite("b", image, LauncherGroup.Home, LauncherTrack.None, true, 0, 0, Lift: 1.1f).LiftTo);
    }
}
