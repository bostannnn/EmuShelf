// M47 B7: the lists in the Big Type look (decision 2026-11-14 "M47 B7" in DECISIONS.md).
#pragma warning disable CS8618 // Every token is assigned by UseListValues(), which UseClassicValues() runs.

using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The lists' tokens that B7 added for the Big Type look: where the Y sheet, the Start menu, Settings and a panel sit on the
/// Launcher, their rows' pitch, focus and ring, the switch, and the grid stepping back under a sheet (tokens.js <c>launcher</c>,
/// <c>shape</c> and <c>colour</c>). Today's look never draws them (it keeps its cards), so they hold one value in either look. The
/// type sizes the lists share with today's look (<see cref="Row"/>, <see cref="RowValue"/>, <see cref="RailRow"/>,
/// <see cref="SheetTitle"/>, <see cref="Heading"/>), the switch's size and <see cref="Danger"/> take Big Type's values under Big
/// Type (<see cref="UseBigTypeListValues"/>).
/// </summary>
public static partial class HandheldTokens
{
    // ===== Rows (tokens.js launcher) =====

    /// <summary>The Y sheet's and the menu's row pitch (<c>launcher.RowPitch</c>).</summary>
    public static double RowPitch { get; private set; }

    /// <summary>A panel's row pitch (<c>launcher.RowPitchPanel</c>).</summary>
    public static double RowPitchPanel { get; private set; }

    /// <summary>Settings' row pitch, the rail's too (<c>launcher.RowPitchSettings</c>).</summary>
    public static double RowPitchSettings { get; private set; }

    /// <summary>How many of a panel's rows are in view (<c>launcher.PanelRowsVisible</c>).</summary>
    public static int PanelRowsVisible { get; private set; }

    /// <summary>A row is its pitch less this high (<c>launcher.RowGap</c>).</summary>
    public static double RowGap { get; private set; }

    /// <summary>Between a row's label and its value (<c>launcher.RowValueGap</c>).</summary>
    public static double RowValueGap { get; private set; }

    /// <summary>How far the focused row moves right (<c>launcher.RowFocusShift</c>).</summary>
    public static double RowFocusShift { get; private set; }

    /// <summary>The ring beside a row (<c>launcher.RowRingOutsetX</c>) and above and below it (<c>RowRingOutsetY</c>).</summary>
    public static double RowRingOutsetX { get; private set; }

    public static double RowRingOutsetY { get; private set; }

    /// <summary>A Settings row inside an opened one (tokens.js <c>launcher.RowGroupIndent</c>, the app's own indent).</summary>
    public static double RowGroupIndent { get; private set; }

    public static Thickness RowGroupMargin => new(RowGroupIndent, 0, 0, 0);

    /// <summary>
    /// The smallest a rail name or row label is set to fit its room, as a share of its size (tokens.js <c>launcher.LabelFitMin</c>, not
    /// the mock's); past it the label ends in an ellipsis.
    /// </summary>
    public static double LabelFitMin { get; private set; }

    /// <summary>The most of a row its value takes (tokens.js <c>launcher.RowValueMaxShare</c>, not the mock's).</summary>
    public static double RowValueMaxShare { get; private set; }

    public static double RowHeight => RowPitch - RowGap;
    public static double RowHeightPanel => RowPitchPanel - RowGap;
    public static double RowHeightSettings => RowPitchSettings - RowGap;
    public static Thickness RowSpacing => new(0, 0, 0, RowGap);
    public static Thickness RowRingMargin => new(-RowRingOutsetX, -RowRingOutsetY);
    public static Thickness RowValueMargin => new(RowValueGap, 0, 0, 0);

    // ===== The Y sheet and the menu =====

    public static double SheetLeft { get; private set; }
    public static double SheetTitleTop { get; private set; }
    public static double SheetTop { get; private set; }
    public static double SheetWidth { get; private set; }

    /// <summary>The most rows a sheet shows at once; a longer one scrolls (<c>launcher.SheetRowsVisible</c>).</summary>
    public static int SheetRowsVisible { get; private set; }

    public static Thickness SheetTitleMargin => new(SheetLeft, SheetTitleTop, 0, 0);
    public static Thickness SheetRowsMargin => new(SheetLeft, SheetTop, 0, 0);
    public static double SheetRowsHeight => SheetRowsVisible * RowPitch;

    // ===== Settings =====

    public static double SettingsLeft { get; private set; }
    public static double SettingsHeadTop { get; private set; }
    public static double SettingsHeadMaxWidth { get; private set; }
    public static double SettingsTop { get; private set; }
    public static double RailWidth { get; private set; }
    public static double SettingsRowsLeft { get; private set; }
    public static double SettingsRowsWidth { get; private set; }

    /// <summary>How many of Settings' rows (and sections) are in view (<c>launcher.SettingsRowsVisible</c>).</summary>
    public static int SettingsRowsVisible { get; private set; }

    public static double SettingsRowsHeight => SettingsRowsVisible * RowPitchSettings;
    public static Thickness SettingsHeadMargin => new(SettingsLeft, SettingsHeadTop, 0, 0);
    public static Thickness RailMargin => new(SettingsLeft, SettingsTop, 0, 0);
    public static Thickness SettingsRowsMargin => new(SettingsRowsLeft, SettingsTop, 0, 0);

    // ===== A panel =====

    public static double PanelLeft { get; private set; }
    public static double PanelHeadTop { get; private set; }
    public static double PanelHeadHeight { get; private set; }
    public static double PanelNameMaxWidth { get; private set; }

    /// <summary>The count's bottom sits this far above the name's (tokens.js <c>launcher.PanelCountRaise</c>, the mock's padding).</summary>
    public static double PanelCountRaise { get; private set; }

    public static double PanelRowsTop { get; private set; }
    public static double PanelRowsInset { get; private set; }
    public static double PanelWidth { get; private set; }

    public static Thickness PanelHeadMargin => new(PanelLeft, PanelHeadTop, PanelLeft, 0);
    public static Thickness PanelRowsMargin => new(PanelLeft, PanelRowsTop, 0, 0);
    public static Thickness PanelRowsPadding => new(0, PanelRowsInset);

    /// <summary>The window the rows scroll in: whole rows, its inset above and below.</summary>
    public static double PanelRowsHeight => (PanelRowsVisible * RowPitchPanel) + (2 * PanelRowsInset);

    /// <summary>The progress bar sits between the heading and the rows.</summary>
    public static Thickness PanelProgressMargin => new(PanelLeft, PanelHeadTop + PanelHeadHeight + ((PanelRowsTop - PanelHeadTop - PanelHeadHeight - ProgressHeight) / 2), PanelLeft, 0);

    /// <summary>The panel's count (tokens.js <c>type.PanelCount</c>) and its opacity.</summary>
    public static double PanelCount { get; private set; }

    public static double PanelCountOpacity { get; private set; }

    /// <summary>The count's line box (the mock's 1.15 of its size), and how far its bottom sits above the heading box's.</summary>
    public static double PanelCountLine => Math.Round(PanelCount * HeadingLineShare, 2);

    public static Thickness PanelCountMargin => new(RowValueGap, 0, 0, PanelCountRaise);

    /// <summary>A sheet's title rises less far than a heading (tokens.js <c>HeadingIn.sheetFromEm</c>).</summary>
    public static double HeadingInSheetFromEm => BigTypeListMotion.HeadingInSheetFromEm;

    // ===== Cover search's candidates (the mock's: a folder's tiles, four to a row) =====

    /// <summary>Candidates to a row (tokens.js <c>launcher.FolderColumnsVisible</c>).</summary>
    public static int CoverColumns { get; private set; }

    /// <summary>Where the candidates' grid starts in from the panel's rows (tokens.js <c>launcher.FolderLeft</c>).</summary>
    public static double FolderLeft { get; private set; }

    public static Thickness CoverGridMargin => new(FolderLeft - PanelLeft, 0, 0, 0);

    /// <summary>Rows of candidates in view (tokens.js <c>launcher.CoverRowsVisible</c>); more scroll a row at a time.</summary>
    public static int CoverRowsVisible { get; private set; }

    public static double CoverGridHeight => CoverRowsVisible * FolderPitch;

    /// <summary>A candidate's slot: the folder's tile and the gap to the next (<c>FolderPitch</c> − <c>TileFolder</c>).</summary>
    public static Thickness CoverTileMargin => new(0, 0, FolderPitch - TileFolder, FolderPitch - TileFolder);

    // ===== Rows' type and colour (tokens.js type, colour) =====

    /// <summary>A row's value (<c>type.RowValue.opacity</c>).</summary>
    public static double RowValueOpacity { get; private set; }

    /// <summary>A row at rest, a locked one (an achievement not yet earned) at rest, and focused (<c>colour</c>).</summary>
    public static double RowRestOpacity { get; private set; }

    public static double RowLockedOpacity { get; private set; }

    public static double RowLockedFocusOpacity { get; private set; }

    /// <summary>A heading's line, as a share of its size (the mock's 1.15).</summary>
    public static double HeadingLineShare { get; private set; }

    // ===== The switch (tokens.js shape) =====

    public static double SwitchStroke { get; private set; }
    public static double SwitchKnobInset { get; private set; }
    public static double SwitchKnobTravel { get; private set; }
    public static double SwitchOnKnobScale { get; private set; }
    public static double SwitchOffOpacity { get; private set; }
    public static CornerRadius SwitchTrackRadius => new(SwitchHeight / 2);
    public static CornerRadius SwitchKnobRound => new(SwitchKnob / 2);
    public static Thickness SwitchStrokeThickness => new(SwitchStroke);

    /// <summary>The knob's inset from the inside of the track's stroke (the mock's <c>.sw i</c>), centred across it.</summary>
    public static Thickness SwitchKnobMargin => new(SwitchKnobInset, 0, 0, 0);

    // ===== Under a sheet (tokens.js shape.UnderSheetScale, colour.UnderSheetOpacity) =====

    public static double UnderSheetScale { get; private set; }
    public static double UnderSheetOpacity { get; private set; }

    // ===== Motion the lists' styles carry (tokens.js motion; the numbers are BigTypeListMotion's) =====

    /// <summary>tokens.js <c>Land</c>, for a style's transition.</summary>
    public static Easing LandEasing => new SplineEasing(0.2, 1.5, 0.35, 1);

    /// <summary>tokens.js <c>Scroll.easing</c>.</summary>
    public static Easing ScrollEasing => new SplineEasing(0.2, 1.15, 0.3, 1);

    public static TimeSpan RowFocusMove => TimeSpan.FromMilliseconds(BigTypeListMotion.RowFocusMilliseconds);
    public static TimeSpan RowFocusFade => TimeSpan.FromMilliseconds(BigTypeListMotion.RowFocusFadeMilliseconds);
    public static TimeSpan SwitchMove => TimeSpan.FromMilliseconds(BigTypeListMotion.SwitchMilliseconds);
    public static TimeSpan SwitchFade => TimeSpan.FromMilliseconds(BigTypeListMotion.SwitchFadeMilliseconds);
    public static TimeSpan ListScroll => TimeSpan.FromMilliseconds(BigTypeListMotion.ScrollMilliseconds);

    /// <summary>The focused row, moved over (<c>RowFocusShift</c>); a row at rest.</summary>
    public static ITransform RowFocusTransform => Translate(RowFocusShift);

    public static ITransform RowRestTransform => Translate(0);

    /// <summary>A switch that is on: its knob across (<c>SwitchKnobTravel</c>) and a little larger (<c>SwitchOnKnobScale</c>).</summary>
    public static ITransform SwitchOnTransform
    {
        get
        {
            var builder = new TransformOperations.Builder(2);
            builder.AppendTranslate(SwitchKnobTravel, 0);
            builder.AppendScale(SwitchOnKnobScale, SwitchOnKnobScale);
            return builder.Build();
        }
    }

    public static ITransform SwitchOffTransform
    {
        get
        {
            var builder = new TransformOperations.Builder(2);
            builder.AppendTranslate(0, 0);
            builder.AppendScale(1, 1);
            return builder.Build();
        }
    }

    private static ITransform Translate(double x)
    {
        var builder = new TransformOperations.Builder(1);
        builder.AppendTranslate(x, 0);
        return builder.Build();
    }

    // ===== Today's look: values that kept their size when Big Type took the list roles =====

    /// <summary>The Title page's option sheet's heading (today's <see cref="SheetTitle"/>; B8 brings the Title page).</summary>
    public static double TitleOptionsTitle { get; private set; }

    /// <summary>The ring round the Title page's "Remember" chip (today's <see cref="RingRadiusRow"/>).</summary>
    public static CornerRadius RingRadiusTitleChip { get; private set; }

    /// <summary>A panel's heading while it types (today's <see cref="Heading"/>; B9 brings the keyboard).</summary>
    public static double ClassicPanelHeading { get; private set; }

    private static void UseListValues()
    {
        RowPitch = 88;
        RowPitchPanel = 92;
        RowPitchSettings = 104;
        PanelRowsVisible = 7;
        RowGap = 16;
        RowValueGap = 28;
        RowFocusShift = 10;
        RowRingOutsetX = 28;
        RowRingOutsetY = 12;
        RowGroupIndent = 48;
        LabelFitMin = 0.75;
        RowValueMaxShare = 0.45;

        SheetLeft = 140;
        SheetTitleTop = 60;
        SheetTop = 208;
        SheetWidth = 960;
        SheetRowsVisible = 8;

        SettingsLeft = 80;
        SettingsHeadTop = 44;
        SettingsHeadMaxWidth = 1080;
        SettingsTop = 224;
        RailWidth = 300;
        SettingsRowsLeft = 472;
        SettingsRowsWidth = 688;
        SettingsRowsVisible = 7;

        PanelLeft = 80;
        PanelHeadTop = 40;
        PanelHeadHeight = 136;
        PanelNameMaxWidth = 680;
        PanelCountRaise = 12;
        PanelRowsTop = 200;
        PanelRowsInset = 16;
        PanelWidth = 1080;
        CoverColumns = 4;
        CoverRowsVisible = 2;
        FolderLeft = 162;
        PanelCount = 68;
        PanelCountOpacity = 0.45;

        RowValueOpacity = 0.85;
        RowRestOpacity = 0.58;
        RowLockedOpacity = 0.34;
        RowLockedFocusOpacity = 0.8;
        HeadingLineShare = 1.15;

        SwitchStroke = 5;
        SwitchKnobInset = 6;
        SwitchKnobTravel = 36;
        SwitchOnKnobScale = 1.15;
        SwitchOffOpacity = 0.65;

        UnderSheetScale = 0.95;
        UnderSheetOpacity = 0.12;

        // Today's sizes, kept for the parts B7 does not redraw.
        TitleOptionsTitle = 32;
        RingRadiusTitleChip = new CornerRadius(56);
        ClassicPanelHeading = 34;
    }

    /// <summary>B7: the list roles' Big Type values (tokens.js <c>type</c>, <c>shape</c>, <c>colour</c>).</summary>
    private static void UseBigTypeListValues()
    {
        Row = 40;
        RowValue = 32;
        RailRow = 46;
        SheetTitle = 80;
        Heading = 100;
        SwitchWidth = 84;
        SwitchHeight = 48;
        SwitchKnob = 26;
        RingRadiusRow = new CornerRadius(32);
        Danger = Brush("#FFC9BD");
    }
}
