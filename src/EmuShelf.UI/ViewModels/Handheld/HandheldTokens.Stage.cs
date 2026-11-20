// M47 B4: the Stage at rest in the Big Type look (decision 2026-11-14 "M47 B4" in DECISIONS.md).
#pragma warning disable CS8618 // Every token is assigned by UseStageValues(), which UseClassicValues() runs.

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The Stage's tokens that B4 added for the Big Type look (tokens.js <c>type</c> and <c>stage</c>, at the Thor's 1920×1080):
/// the fitted name, the four corners (the label top left, battery and clock top right, the two lines under the name, the hints
/// bottom right), the medium's corner and the missing game's dimmed name. Today's look draws none of these parts (the view
/// shows them only under Big Type), so each has one value. The veils' shape and the ink table live beside the native Stage
/// that also draws them (<see cref="BigTypeVeil"/>, <c>BackdropLegibility</c>).
/// </summary>
public static partial class HandheldTokens
{
    // ===== The name (tokens.js type.DisplayName, stage.NameLeft / NameBottom) =====

    /// <summary>The name's box: left edge, and how far its bottom sits above the screen's.</summary>
    public static double StageNameLeft { get; private set; }

    public static double StageNameBottom { get; private set; }

    /// <summary>The box the name is fitted to (<c>fitBox</c>), and its narrower width when a game's medium is beside it.</summary>
    public static double StageNameWidth { get; private set; }

    public static double StageNameWidthBesideMedium { get; private set; }

    public static double StageNameHeight { get; private set; }

    /// <summary>The name's lines, at most (<c>maxLines</c>); its line height is <c>BigTypeLayout.LineHeight</c> (0.9).</summary>
    public static int StageNameMaxLines { get; private set; }

    /// <summary>The fit's smallest and largest sizes (the mock searches 56 to 840 at Thor scale).</summary>
    public static double StageNameMinSize { get; private set; }

    public static double StageNameMaxSize { get; private set; }

    /// <summary>A game whose file is missing: its name at this opacity (<c>stage.NameDimmedOpacity</c>).</summary>
    public static double StageNameDimmedOpacity { get; private set; }

    /// <summary>
    /// Room below the name's box for its descenders: the native Stage pictures the name's control whole, and a picture stops at
    /// the control's edge. The box itself (and so the fit and the bottom line) is unchanged.
    /// </summary>
    public static double StageNameOverhang { get; private set; }

    public static Thickness StageNameMargin => new(StageNameLeft, 0, 0, StageNameBottom - StageNameOverhang);

    public static double StageNameControlHeight => StageNameHeight + StageNameOverhang;

    // ===== The corners =====

    /// <summary>The console's name (or the item's kind), top left (<c>type.Label</c>, <c>stage.LabelLeft / LabelTop</c>).</summary>
    public static double StageLabel { get; private set; }

    public static Thickness StageLabelMargin { get; private set; }

    /// <summary>The clock, top right (<c>type.Clock</c>, <c>stage.ClockRight / ClockTop</c>), and the battery beside it.</summary>
    public static double StageClock { get; private set; }

    public static double StageBattery { get; private set; }

    public static double StageBatteryOpacity { get; private set; }

    public static Thickness StageStatusMargin { get; private set; }

    /// <summary>The battery sits this far apart from the clock, and this far above the clock's bottom (the mock's padding).</summary>
    public static double StageStatusGap { get; private set; }

    public static Thickness StageBatteryMargin { get; private set; }

    /// <summary>The line under the name (<c>type.Line</c>, <c>stage.LineLeft / LineBottom</c>) and the second line (<c>type.Facts</c>).</summary>
    public static double StageLine { get; private set; }

    public static Thickness StageLineMargin { get; private set; }

    public static double StageFacts { get; private set; }

    /// <summary>
    /// tokens.js <c>stage.LineRowsShared</c> (M47 B14, the app's own): the rows the line and the second line share under the name, a long
    /// one running on at a whole word, each growing down: the line takes at most two, the second line the rest.
    /// </summary>
    public static int StageLineRowsShared { get; private set; }

    public static double StageFactsOpacity { get; private set; }

    public static Thickness StageFactsMargin { get; private set; }

    /// <summary>The hints, bottom right: plain type (<c>type.Hint</c>) after an outlined glyph (<c>type.HintGlyph</c>).</summary>
    public static double StageHint { get; private set; }

    /// <summary>
    /// The glyph's circle inside its stroke (<c>type.HintGlyph.size</c>, which the mock sets as a CSS content box, so the stroke
    /// lies outside it: the circle drawn is 58 + 2 × 5 = 68).
    /// </summary>
    public static double StageHintGlyphSize { get; private set; }

    /// <summary>The glyph's outer size, stroke included, as the mock draws it.</summary>
    public static double StageHintGlyphOuter => StageHintGlyphSize + StageHintGlyphStroke.Left + StageHintGlyphStroke.Right;

    public static CornerRadius StageHintGlyphCorner => new(StageHintGlyphOuter / 2);

    public static Thickness StageHintGlyphStroke { get; private set; }

    public static double StageHintGlyphLetter { get; private set; }

    public static double StageHintGlyphIcon { get; private set; }

    public static double StageHintGap { get; private set; }

    public static double StageHintGlyphGap { get; private set; }

    public static Thickness StageHintsMargin { get; private set; }

    /// <summary>The line height the mock gives the corners' type, as a share of its size (1.15; the hints 1).</summary>
    public static double StageCornerLineShare { get; private set; }

    public static double StageLabelLineHeight => Math.Round(StageLabel * StageCornerLineShare, 2);
    public static double StageClockLineHeight => Math.Round(StageClock * StageCornerLineShare, 2);
    public static double StageBatteryLineHeight => Math.Round(StageBattery * StageCornerLineShare, 2);
    public static double StageLineLineHeight => Math.Round(StageLine * StageCornerLineShare, 2);
    public static double StageFactsLineHeight => Math.Round(StageFacts * StageCornerLineShare, 2);
    public static double StageClockSpacing => Math.Round(StageClock * DisplayLetterSpacingStage, 2);

    /// <summary>The display type's tracking on the Stage (tokens.js <c>face.DisplayLetterSpacing</c>, em): the clock.</summary>
    public static double DisplayLetterSpacingStage { get; private set; }

    /// <summary>
    /// The name's tracking, in pixels: what the mock actually draws. Its −0.02em is declared on the name's box (<c>.word</c>),
    /// whose own size is the page's 16 px, so every letter of the name is drawn −0.32 px apart at half scale: −0.64 here,
    /// whatever the name's size (as B3 found for the folder header). The clock declares it on itself, so it gets the full em.
    /// </summary>
    public static double StageNameSpacing { get; private set; }

    /// <summary>
    /// Where Avalonia sets a corner's type against a browser. The mock gives each corner a line box of 1.15 of its size and a
    /// browser centres the face's ascent and descent in it; Avalonia sets the face's own line, taller, with its text lower in
    /// it. Each corner is moved by what its ink measured against the mock's at Thor scale (M47 B4): positive raises it.
    /// </summary>
    public static double StageLabelRaise { get; private set; }

    public static double StageClockRaise { get; private set; }

    public static double StageLineRaise { get; private set; }

    public static double StageFactsRaise { get; private set; }

    // ===== The medium (stage.MediumRight / MediumTop / MediumWidth / MediumHeight) =====

    /// <summary>The case's box: its right edge's distance from the screen's, its top, its size.</summary>
    public static double StageMediumRight { get; private set; }

    public static double StageMediumTop { get; private set; }

    public static double StageMediumWidth { get; private set; }

    public static double StageMediumHeight { get; private set; }

    /// <summary>A flat cover (no medium for the platform, or none on this GPU) in the case's box.</summary>
    public static Thickness StageCoverMargin => new(0, StageMediumTop, StageMediumRight, 0);

    public static CornerRadius StageCoverRadius { get; private set; }

    /// <summary>A game with no cover at all: the mock's stand-in, its title on a dark slate.</summary>
    public static IBrush StageNoCoverFill { get; private set; }

    public static double StageNoCoverTitle { get; private set; }

    /// <summary>
    /// How much of its frame's height the 3D renderer gives a medium (measured: a case 640 tall in today's 820 box). The native
    /// medium's frame is the case's height divided by this, at today's frame shape, centred on the case's box.
    /// </summary>
    public static double MediumFrameFill { get; private set; }

    /// <summary>Today's medium frame's shape (1400 by 820), kept so a medium is framed the same way in either look.</summary>
    public static double MediumFrameAspect { get; private set; }

    /// <summary>The native medium's frame in Big Type: centred on the case's box, tall enough that the case fills it.</summary>
    public static Rect StageMediumFrame(double canvasWidth)
    {
        var height = StageMediumHeight / MediumFrameFill;
        var width = height * MediumFrameAspect;
        // M47 B11: on the narrow canvas the case's box has a corner of its own (tokens.js stage.NarrowMediumRight / NarrowMediumTop).
        var narrow = canvasWidth <= NarrowCanvas;
        var centreX = canvasWidth - (narrow ? StageNarrowMediumRight : StageMediumRight) - (StageMediumWidth / 2);
        var centreY = (narrow ? StageNarrowMediumTop : StageMediumTop) + (StageMediumHeight / 2);
        return new Rect(centreX - (width / 2), centreY - (height / 2), width, height);
    }

    // ===== Where the native Stage pictures each corner =====

    /// <summary>The room around a corner's type when it is pictured, so no glyph's edge is cut.</summary>
    public static double StageShotPad { get; private set; }

    public static Thickness StageShotPadding => new(StageShotPad);

    // Each corner's box less the picture's room on its anchored sides, moved by its measured raise.
    public static Thickness StageLabelShotMargin => new(StageLabelMargin.Left - StageShotPad, StageLabelMargin.Top - StageShotPad - StageLabelRaise, 0, 0);
    public static Thickness StageStatusShotMargin => new(0, StageStatusMargin.Top - StageShotPad - StageClockRaise, StageStatusMargin.Right - StageShotPad, 0);
    public static Thickness StageLineShotMargin => new(StageLineMargin.Left - StageShotPad, 0, 0, StageLineMargin.Bottom - StageShotPad + StageLineRaise);
    public static Thickness StageFactsShotMargin => new(StageFactsMargin.Left - StageShotPad, 0, 0, StageFactsMargin.Bottom - StageShotPad + StageFactsRaise);
    public static Thickness StageHintsShotMargin => new(0, 0, StageHintsMargin.Right - StageShotPad, StageHintsMargin.Bottom - StageShotPad + StageHintRaise);

    /// <summary>The hints' line is 1 (not 1.15) in the mock, so they are raised by their own measure.</summary>
    public static double StageHintRaise { get; private set; }

    /// <summary>The Avalonia Stage's medium in Big Type: <see cref="StageMediumFrame"/> on the 16:9 canvas.</summary>
    public static Thickness StageMediumFrameMargin => new(StageMediumFrame(1920).X, StageMediumFrame(1920).Y, 0, 0);

    public static double StageMediumFrameWidth => StageMediumFrame(1920).Width;

    public static double StageMediumFrameHeight => StageMediumFrame(1920).Height;

    public static double StageNoCoverTitleLineHeight => Math.Round(StageNoCoverTitle * StageCornerLineShare, 2);

    public static Thickness StageNoCoverPadding => new(Math.Round(StageMediumWidth * 0.12));

    // ===== The narrow Stage (M47 B11, the app's own: tokens.js type.DisplayNameNarrow and stage.Narrow*) =====

    /// <summary>The name's box on the 1240-wide screen (the screens swapped), and beside a game's medium there.</summary>
    public static double StageNarrowNameWidth { get; private set; }

    public static double StageNarrowNameWidthBesideMedium { get; private set; }

    public static double StageNarrowNameHeight { get; private set; }

    /// <summary>The hints take a row of their own on the narrow screen: the name and its two lines stand this much higher.</summary>
    public static double StageNarrowHintsRow { get; private set; }

    /// <summary>The medium's box on the narrow screen: its right edge's distance from the screen's, and its top (its size is the wide one's).</summary>
    public static double StageNarrowMediumRight { get; private set; }

    public static double StageNarrowMediumTop { get; private set; }

    /// <summary>The console's name ends this far before the battery on the narrow screen.</summary>
    public static double StageNarrowLabelGap { get; private set; }

    /// <summary>The launch title's lines at most this wide on the narrow screen; its sizes scale down by the same share.</summary>
    public static double StageNarrowLaunchTitleMaxWidth { get; private set; }

    /// <summary>The name's box width for a Stage on the wide or the narrow screen, beside a medium (a game, a cover candidate) or not.</summary>
    public static double StageNameBoxWidth(bool besideMedium, bool narrow) => narrow
        ? besideMedium ? StageNarrowNameWidthBesideMedium : StageNarrowNameWidth
        : besideMedium ? StageNameWidthBesideMedium : StageNameWidth;

    public static Thickness StageNarrowNameMargin => new(StageNameLeft, 0, 0, StageNameBottom + StageNarrowHintsRow - StageNameOverhang);

    public static double StageNarrowNameControlHeight => StageNarrowNameHeight + StageNameOverhang;

    public static Thickness StageNarrowLineShotMargin => Raised(StageLineShotMargin, StageNarrowHintsRow);

    public static Thickness StageNarrowFactsShotMargin => Raised(StageFactsShotMargin, StageNarrowHintsRow);

    private static Thickness Raised(Thickness margin, double by) => new(margin.Left, margin.Top, margin.Right, margin.Bottom + by);

    public static Thickness StageNarrowCoverMargin => new(0, StageNarrowMediumTop, StageNarrowMediumRight, 0);

    /// <summary>The Avalonia Stage's medium in Big Type on the narrow canvas.</summary>
    public static Thickness StageNarrowMediumFrameMargin => new(StageMediumFrame(NarrowCanvas).X, StageMediumFrame(NarrowCanvas).Y, 0, 0);

    public static double StageNarrowMediumFrameWidth => StageMediumFrame(NarrowCanvas).Width;

    public static double StageNarrowMediumFrameHeight => StageMediumFrame(NarrowCanvas).Height;

    /// <summary>
    /// Where the launch's takeover grows from: tokens.js <c>Launch.takeoverX / takeoverY</c> on the wide Stage (beside the case's middle);
    /// on the narrow one the same point moved with the case to its narrow corner (M47 B11).
    /// </summary>
    public static Point StageTakeoverCentre(bool narrow)
    {
        var wide = new Point(BigTypeLaunchMotion.TakeoverX, BigTypeLaunchMotion.TakeoverY);
        if (!narrow)
            return wide;
        return wide + (StageMediumFrame(NarrowCanvas).Center - StageMediumFrame(1920).Center);
    }

    // The narrow screen's width (HandheldShellViewModel.NarrowCanvasWidth): a Stage canvas narrower than the wide one is the narrow one.
    private const double NarrowCanvas = 1240;

    // ===== The ink =====

    /// <summary>
    /// White: the type on a game's stand-in card with no cover. (The Avalonia Stage's text takes the fallback's dark ink from the
    /// shell, <c>HandheldShellViewModel.StageInk</c>, M47 B5.)
    /// </summary>
    public static IBrush StageBigTypeInk { get; private set; }

    /// <summary>The dark ink for a colour: mixed <see cref="BigTypeVeil.InkMix"/> toward #0B0A18 (tokens.js colour.InkToward / InkMix).</summary>
    public static Color StageDeep(Color colour)
    {
        var deep = BigTypeVeil.Deep(new System.Numerics.Vector3(colour.R, colour.G, colour.B));
        return Color.FromRgb((byte)Math.Round(deep.X), (byte)Math.Round(deep.Y), (byte)Math.Round(deep.Z));
    }

    private static void UseStageValues()
    {
        StageNameLeft = 80;
        StageNameBottom = 240;
        StageNameWidth = 1360;
        StageNameWidthBesideMedium = 1120;
        StageNameHeight = 680;
        StageNameMaxLines = 3;
        StageNameMinSize = 56;
        StageNameMaxSize = 840;
        StageNameDimmedOpacity = 0.5;
        StageNameOverhang = 200;

        StageLabel = 44;
        StageLabelMargin = new Thickness(88, 64, 0, 0);
        StageClock = 48;
        StageBattery = 36;
        StageBatteryOpacity = 0.85;
        StageStatusMargin = new Thickness(0, 64, 88, 0);
        StageStatusGap = 32;
        // Align the battery's smaller type with the clock's baseline.
        StageBatteryMargin = new Thickness(0, 0, 0, 2);
        StageLine = 42;
        StageLineMargin = new Thickness(88, 0, 0, 132);
        StageFacts = 36;
        StageLineRowsShared = 3;
        StageFactsOpacity = 0.82;
        StageFactsMargin = new Thickness(88, 0, 0, 76);
        StageHint = 38;
        StageHintGlyphSize = 58;
        StageHintGlyphStroke = new Thickness(5);
        StageHintGlyphLetter = 28;
        StageHintGlyphIcon = 26;
        StageHintGap = 44;
        StageHintGlyphGap = 16;
        StageHintsMargin = new Thickness(0, 0, 88, 72);
        StageCornerLineShare = 1.15;
        DisplayLetterSpacingStage = -0.02;
        StageNameSpacing = -0.64;
        StageLabelRaise = 7;
        StageClockRaise = 9;
        StageLineRaise = -8;
        StageFactsRaise = -4;
        StageHintRaise = 0;
        StageShotPad = 8;

        StageMediumRight = 248;
        StageMediumTop = 236;
        StageMediumWidth = 392;
        StageMediumHeight = 552;
        StageCoverRadius = new CornerRadius(12);
        StageNoCoverFill = Brush("#3A3950");
        StageNoCoverTitle = 44;
        MediumFrameFill = 640.0 / 820.0;
        MediumFrameAspect = 1400.0 / 820.0;

        StageBigTypeInk = Brush("White");

        StageNarrowNameWidth = 1080;
        StageNarrowNameWidthBesideMedium = 600;
        StageNarrowNameHeight = 584;
        StageNarrowHintsRow = 96;
        StageNarrowMediumRight = 88;
        StageNarrowMediumTop = 200;
        StageNarrowLabelGap = 44;
        StageNarrowLaunchTitleMaxWidth = 1080;
    }
}
