// M47 B3: the Launcher at rest in the Big Type look (decision 2026-11-14 "M47 B3" in DECISIONS.md).
#pragma warning disable CS8618 // Every token is assigned by UseClassicLauncherValues(), which UseClassicValues() runs.

using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The Launcher's tokens that B3 added for the Big Type look: the flat ground, the matte tiles, the one-band ring, the plain
/// type of the folder header and the dock's corners, and the layout of home, a folder and the dock (tokens.js
/// <c>shape</c>, <c>launcher</c> and <c>colour</c>). Each has today's value too, so the views read one token in either look;
/// where today's look has no such thing the value is never drawn (the view shows the Big Type part only under Big Type).
/// </summary>
public static partial class HandheldTokens
{
    // ===== Which look's parts a view shows =====

    /// <summary>Today's look: the views show their Classic parts (read with <c>x:Static</c> when a view is built).</summary>
    public static bool IsClassic => Look == HandheldLook.Classic;

    /// <summary>The Big Type look: the views show their Big Type parts instead.</summary>
    public static bool IsBigType => Look == HandheldLook.BigType;

    // ===== Colour on the Launcher (tokens.js colour) =====

    /// <summary>Type and glyphs on the console's colour (tokens.js <c>colour.OnGround</c>: white).</summary>
    /// <remarks>Not <see cref="OnGround"/>, which is today's dark ink and still draws the sheets, menus and Settings (B7).</remarks>
    public static IBrush OnColour { get; private set; }

    /// <summary>A tile at rest: a folder, an app, a game with no cover, a dock button (tokens.js <c>colour.TileRest</c>).</summary>
    public static IBrush TileRest { get; private set; }

    /// <summary>The focused folder or app tile, with the colour as its ink (tokens.js <c>colour.TileFocus</c>).</summary>
    public static IBrush TileFocus { get; private set; }

    /// <summary>The ring's one band (tokens.js <c>colour.Ring</c>). Today's look draws its violet <see cref="Ring"/> between keylines.</summary>
    public static IBrush RingBand { get; private set; }

    /// <summary>An empty home slot's dot (tokens.js <c>colour.EmptySlotDotOpacity</c>).</summary>
    public static double EmptySlotDotOpacity { get; private set; }

    /// <summary>The focused empty slot's plus.</summary>
    public static double EmptySlotPlusOpacity { get; private set; }

    /// <summary>Recent's ground (tokens.js <c>colour.Recent</c>).</summary>
    public static Color RecentGround { get; private set; }

    /// <summary>The Settings and Activity tiles' ground (tokens.js <c>colour.Settings</c>).</summary>
    public static Color AppGround { get; private set; }

    /// <summary>The ground before anything has been focused (Recent's slate).</summary>
    public static Color DefaultGround => RecentGround;

    /// <summary>How long the ground takes to change to the next focused item's colour (a plain crossfade; B5 brings the flood).</summary>
    public static TimeSpan GroundFade { get; private set; }

    // ===== The ring and the lift (tokens.js shape) =====

    /// <summary>A tile's ring anchor: how far the ring's outer edge sits outside the tile's resting edge (<c>shape.RingOutset</c>).</summary>
    public static Thickness RingOutsetTile { get; private set; }

    /// <summary>A dock button's ring anchor (the mock draws it 18 out, not 26: <c>.dock</c> 44 px buttons, a 62 px ring).</summary>
    public static Thickness RingOutsetDock { get; private set; }

    /// <summary>
    /// How much the ring grows with the focused element's lift. Today's look scales the ring with the 1.13 lift; Big Type's
    /// ring stays at its outset from the resting tile, whatever the lift (the mock's <c>rectOf</c>).
    /// </summary>
    public static double RingLift { get; private set; }

    /// <summary>A focused tile's lift (<c>shape.FocusScale</c>); the views' styles carry the same number.</summary>
    public static double FocusScale { get; private set; }

    /// <summary>A focused dock button's lift (<c>shape.DockFocusScale</c>).</summary>
    public static double DockFocusScale { get; private set; }

    // ===== Layout (tokens.js launcher) =====

    /// <summary>Home's layer: where the grid starts below the canvas's top (Big Type: <c>HomeTop</c> 44 less a slot's margin).</summary>
    public static Thickness HomeLayerMargin { get; private set; }

    /// <summary>A home slot's margin: half the gap between tiles (Big Type: <c>HomePitch</c> 212 − <c>TileHome</c> 176, halved).</summary>
    public static Thickness HomeSlotMargin { get; private set; }

    /// <summary>Where the home grid sits in its layer (today's look centres it; Big Type puts it at <c>HomeTop</c>).</summary>
    public static VerticalAlignment HomeGridAlignment { get; private set; }

    /// <summary>The folder page's clip inside the canvas (Big Type: the whole screen, as the mock's layer).</summary>
    public static Thickness FolderPageMargin { get; private set; }

    /// <summary>The folder's grid inside its page (Big Type: <c>FolderLeft</c> 162, and the first row at <c>FolderTop</c> 208).</summary>
    public static Thickness FolderGridMargin { get; private set; }

    /// <summary>Where a folder's track sits below its header (today's look centres it; Big Type puts it at <c>FolderTop</c>).</summary>
    public static VerticalAlignment FolderTrackAlignment { get; private set; }

    /// <summary>The folder header's box (<c>HeaderLeft</c>, <c>HeaderTop</c>; right edge as far in as the left).</summary>
    public static Thickness HeaderMargin { get; private set; }

    /// <summary>The folder header's height (<c>launcher.HeaderHeight</c>).</summary>
    public static double HeaderHeight { get; private set; }

    /// <summary>The widest the folder's name may be before it is set smaller (the mock fits it to 720).</summary>
    public static double HeaderNameMaxWidth { get; private set; }

    /// <summary>The heading's line height (the mock's 1.15 of its size).</summary>
    public static double HeaderLineHeight { get; private set; }

    /// <summary>The count beside the folder's name (<c>type.HeadingCount.opacity</c>).</summary>
    public static double HeadingCountOpacity { get; private set; }

    /// <summary>The dock's bar: its fill (Big Type: none).</summary>
    public static IBrush DockBarFill { get; private set; }

    /// <summary>The dock's bar: its edge (Big Type: none).</summary>
    public static IBrush DockBarEdge { get; private set; }

    /// <summary>The dock's bar: its height (Big Type: a dock button's, so the buttons sit <c>DockBottom</c> from the edge).</summary>
    public static double DockBarHeight { get; private set; }

    /// <summary>The dock row's corners ("X Swap" at the left, the page or the sort at the right): their inset from the sides.</summary>
    public static Thickness DockCornerMargin { get; private set; }

    /// <summary>The corners' type (the mock's <c>.dside</c>, 15 px at half scale).</summary>
    public static double DockCorner { get; private set; }

    /// <summary>The corners' opacity (the mock's <c>.dside</c>).</summary>
    public static double DockCornerOpacity { get; private set; }

    /// <summary>The outlined circle holding "X" (the mock's <c>.dside i</c>: 22 px, 2 px stroke, 11 px letter, at half scale).</summary>
    public static double DockCornerGlyph { get; private set; }

    public static CornerRadius DockCornerGlyphRadius => new(DockCornerGlyph / 2);

    public static Thickness DockCornerGlyphStroke { get; private set; }

    public static double DockCornerGlyphLetter { get; private set; }

    public static double DockCornerGap { get; private set; }

    // ===== Faces on the tiles =====

    /// <summary>A monogram is set at most this share of its tile's side (tokens.js <c>type.Monogram.maxSize</c>).</summary>
    public static double MonogramMaxShare { get; private set; }

    /// <summary>…and no wider than this share of its tile's width (tokens.js <c>type.Monogram.fitWidth</c>).</summary>
    public static double MonogramFitShare { get; private set; }

    /// <summary>
    /// A glyph's share of its tile's side. The mock's glyphs are drawn in a 44 px box on an 88 px tile, but their strokes fill
    /// about 31 of its 48 units, so what shows is about 36% of the tile; the app's icons fill their box, so they get 36%.
    /// </summary>
    public static double GlyphShare { get; private set; }

    public static double MonogramHome => Math.Round(TileHome * MonogramMaxShare, 2);
    public static double MonogramHomeWidth => Math.Round(TileHome * MonogramFitShare, 2);
    public static double GlyphHome => Math.Round(TileHome * GlyphShare);
    public static double GlyphFolder => Math.Round(TileFolder * GlyphShare);

    /// <summary>The white corner mark on a missing file's tile ("!") and on a folder that is new (a dot): its side.</summary>
    public static double CornerMark { get; private set; }

    public static CornerRadius CornerMarkRadius { get; private set; }

    /// <summary>How far the mark hangs past the tile's top right corner.</summary>
    public static Thickness CornerMarkMargin { get; private set; }

    public static double CornerMarkLetter { get; private set; }

    public static double CornerMarkDot { get; private set; }

    /// <summary>An empty slot's dot, and the focused one's plus (its arm's length and thickness).</summary>
    public static double EmptySlotDot { get; private set; }

    public static double EmptySlotPlus { get; private set; }

    public static CornerRadius EmptySlotDotRadius => new(EmptySlotDot / 2);

    /// <summary>A game with no cover: its title on the tile, at its top left as the mock's stand-in covers carry theirs.</summary>
    public static double TileTitleBigHome { get; private set; }

    public static double TileTitleBigFolder { get; private set; }

    public static double TileTitleBigHomeLine => Math.Round(TileTitleBigHome * 1.15, 1);
    public static double TileTitleBigFolderLine => Math.Round(TileTitleBigFolder * 1.15, 1);
    public static Thickness TileTitleBigHomeMargin => new(Math.Round(TileHome * 0.08));
    public static Thickness TileTitleBigFolderMargin => new(Math.Round(TileFolder * 0.08));

    /// <summary>Display type's tracking, in em (tokens.js <c>face.DisplayLetterSpacing</c>): monograms and headings.</summary>
    public static double DisplayLetterSpacing { get; private set; }

    public static double MonogramHomeSpacing => Math.Round(MonogramHome * DisplayLetterSpacing, 2);

    /// <summary>
    /// The folder header's tracking. The mock sets <c>-.02em</c> on the header's box, whose own size is the page's 16 px, so its
    /// name and count are drawn at −0.32 px at half scale: −0.64 here, not −2.
    /// </summary>
    public static double HeadingSpacing { get; private set; }

    /// <summary>
    /// How far the header's type is raised from the bottom of its box: Avalonia sets a 115 px line lower in its box than a
    /// browser does (measured against the mock: the name's baseline at 153.5, Avalonia's at 168).
    /// </summary>
    public static Thickness HeaderNameShotMargin { get; private set; }

    public static Thickness HeaderCountMargin { get; private set; }

    /// <summary>
    /// How far apart a folder's name and its count stay (<c>launcher.HeaderGap</c>, the mock's <c>.fhead</c> gap, M47 B10): the count's
    /// left margin under Big Type, and a long name is set smaller to keep it.
    /// </summary>
    public static double HeaderGap { get; private set; }

    /// <summary>A dock button's glyph (the mock's 20 px at half scale).</summary>
    public static double GlyphDock { get; private set; }

    /// <summary>An empty folder's line ("This folder is empty"): today's heading size and ink, or Big Type's plain type on the colour.</summary>
    public static double FolderEmptyHeading { get; private set; }

    public static IBrush FolderEmptyInk { get; private set; }

    public static IBrush FolderEmptyHintInk { get; private set; }

    // ===== A Launcher's colour for what is focused =====

    /// <summary>
    /// The Launcher's colour for a folder: a platform's console colour (<see cref="ConsoleColour(HandheldPlatform?)"/>), Recent's
    /// <see cref="RecentGround"/> under Big Type, and your folders' own colour, darkened under Big Type until white type holds the
    /// floors on it (<see cref="HandheldGround.Derive"/>; your folders keep today's palette until naming moves to Big Type).
    /// </summary>
    /// <remarks>The Stage keeps <see cref="FolderColour"/> (B4 decides its colours); this is the Launcher's.</remarks>
    public static Color? LauncherColour(HandheldFolderSource? source)
    {
        if (source is null)
            return null;
        if (source.Platform is { } platform)
            return ConsoleColour(platform);
        if (!IsBigType)
            return ColourOf(source.AccentColor);
        if (source.IsRecent)
            return RecentGround;
        return ColourOf(source.AccentColor) is { } own ? Color.FromUInt32(HandheldGround.Derive(own.ToUInt32() | 0xFF000000)) : null;
    }

    /// <summary>A flat brush of a Launcher colour, grey when there is none.</summary>
    public static IBrush GroundBrush(Color? colour) => colour is { } c ? new ImmutableSolidColorBrush(c) : Brushes.Gray;

    private static void UseClassicLauncherValues()
    {
        OnColour = Brush("White");
        TileRest = Brush("#2BFFFFFF");
        TileFocus = Brush("White");
        RingBand = Brush("White");
        EmptySlotDotOpacity = 0.30;
        EmptySlotPlusOpacity = 0.9;
        RecentGround = Color.Parse("#39465F");
        AppGround = Color.Parse("#4A43B8");
        GroundFade = TimeSpan.FromMilliseconds(200);

        // Today's look: the anchors' outsets and the lift as they were inline before B3.
        RingOutsetTile = new Thickness(-24);
        RingOutsetDock = new Thickness(-16);
        RingLift = HandheldMotion.LiftScale;
        FocusScale = HandheldMotion.LiftScale;
        DockFocusScale = HandheldMotion.LiftScale;

        HomeLayerMargin = new Thickness(0, 48, 0, 0);
        HomeSlotMargin = new Thickness(24, 10);
        HomeGridAlignment = VerticalAlignment.Center;
        FolderPageMargin = new Thickness(32, 0, 0, 0);
        FolderGridMargin = new Thickness(16, 40, 0, 0);
        FolderTrackAlignment = VerticalAlignment.Center;
        HeaderMargin = new Thickness(162, 40, 162, 0);
        HeaderHeight = 136;
        HeaderNameMaxWidth = 720;
        HeaderLineHeight = 115;
        HeadingCountOpacity = 0.45;

        DockBarFill = ClassicDockFill;
        DockBarEdge = ClassicHairline;
        DockBarHeight = 112;
        DockCornerMargin = new Thickness(108, 0, 108, 32);
        DockCorner = 30;
        DockCornerOpacity = 0.8;
        DockCornerGlyph = 44;
        DockCornerGlyphStroke = new Thickness(4);
        DockCornerGlyphLetter = 22;
        DockCornerGap = 14;

        MonogramMaxShare = 0.56;
        MonogramFitShare = 0.76;
        GlyphShare = 0.36;
        GlyphDock = 40;
        HeadingSpacing = 0;
        HeaderNameShotMargin = new Thickness(-24, -24, -24, -10);
        HeaderCountMargin = new Thickness(16, 0, 0, 14);
        HeaderGap = 16;
        CornerMark = 48;
        CornerMarkRadius = new CornerRadius(14);
        CornerMarkMargin = new Thickness(0, -12, -12, 0);
        CornerMarkLetter = 30;
        CornerMarkDot = 16;
        EmptySlotDot = 12;
        EmptySlotPlus = 60;
        TileTitleBigHome = 26;
        TileTitleBigFolder = 30;
        DisplayLetterSpacing = 0;
        FolderEmptyHeading = FolderHeading;
        FolderEmptyInk = OnGround;
        FolderEmptyHintInk = OnGroundMuted;
    }

    /// <summary>B3: the Launcher at rest (home, a folder, the dock) in the Big Type look.</summary>
    private static void UseBigTypeLauncherValues()
    {
        // shape
        TileFolder = 208;
        TileRadius = new CornerRadius(30);
        TileRadiusFolder = new CornerRadius(30);
        DockTile = 88;
        DockRadius = new CornerRadius(22);
        RingStroke = 8;
        RingOutsetTile = new Thickness(-26);
        RingOutsetDock = new Thickness(-18);
        RingRadiusTile = new CornerRadius(50);
        RingRadiusTileFolder = new CornerRadius(50);
        RingRadiusDock = new CornerRadius(38);
        RingLift = 1;
        FocusScale = 1.10;
        DockFocusScale = 1.14;

        // colour
        MissingTileOpacity = 0.45;

        // launcher: home at HomeLeft 108 / HomeTop 44 on a 212 pitch (the 5 × 212 − 36 grid centres to 108 on 1240);
        // a folder at FolderLeft 162 / FolderTop 208 on a 236 pitch; the dock DockBottom 32 from the edge, DockGap apart.
        HomeSlotMargin = new Thickness(18);
        HomeLayerMargin = new Thickness(0, 26, 0, 0);
        HomeGridAlignment = VerticalAlignment.Top;
        FolderPitch = 236;
        FolderRowPitch = 236;
        FolderTrackHeight = 3 * 236;
        FolderPageMargin = new Thickness(0);
        FolderGridMargin = new Thickness(162, 208 - ((236 - 208) / 2), 0, 0);
        FolderTrackAlignment = VerticalAlignment.Top;
        // The app's dock has seven buttons (All apps, five apps, the menu) where the mock draws six, and an open folder's sort
        // ("Most played") is wider than the mock's "Manual": at DockGap 24 with the corners at the grid's edge (108) both corners
        // run into the dock. The gap closes to 16 and the corners move out to the canvas's 48 px edge (decision M47 B3).
        DockGap = 16;
        DockCornerMargin = new Thickness(48, 0, 48, 32);
        DockBarFill = Brushes.Transparent;
        DockBarEdge = Brushes.Transparent;
        DockBarHeight = 88;

        HeaderGap = 32;
        HeaderCountMargin = new Thickness(HeaderGap, 0, 0, 14);

        // type
        FolderHeading = 100;
        HeadingCount = 100;
        FolderEmptyHeading = 44;
        DisplayLetterSpacing = -0.02;
        HeadingSpacing = -0.64;
        FolderEmptyInk = OnColour;
        FolderEmptyHintInk = OnColour;
    }
}
