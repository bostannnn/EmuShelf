// M47 B1: one home for Handheld mode's design values (decision 2026-11-13 "M47 B1" in DECISIONS.md).
#pragma warning disable CS8618 // Every token is assigned by UseClassicValues(), which the static constructor runs.

using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Handheld mode's design tokens: every colour, type size, weight, corner radius and design size the five Handheld views
/// and the code behind them draw with. XAML reads them with <c>{x:Static vm:HandheldTokens.Name}</c>; code reads the same
/// properties, so there is one source of truth.
/// </summary>
/// <remarks>
/// Names say what a value is for, never what it is. Where a role matches one in the Big Type table
/// (the Big Type mock's tokens.js, beside docs/prototypes/handheld-big-type/index.html), the name is that table's. A name that starts with <c>Classic</c> exists only
/// in today's look (frosted pills, the name plate, shadows, the selector's keylines and glow, gradients) and goes when
/// today's look is removed (M47 B10).
/// The look is chosen once, when Handheld mode starts and before its views are built: <see cref="Use"/> assigns every token
/// for that look. Views read tokens when they are built, so a look never changes under a running Handheld mode.
/// </remarks>
public static partial class HandheldTokens
{
    static HandheldTokens() => Use(HandheldLook.BigType);

    /// <summary>The look whose values the tokens hold.</summary>
    public static HandheldLook Look { get; private set; }

    /// <summary>
    /// Gives every token its value for <paramref name="look"/>. Called when Handheld mode starts, before its views exist.
    /// Today's values are the base; Big Type then assigns only the ones that differ (<see cref="UseBigTypeValues"/>).
    /// </summary>
    public static void Use(HandheldLook look)
    {
        Look = look;
        UseClassicValues();
        if (look == HandheldLook.BigType)
            UseBigTypeValues();
    }

    // ===== A console's colour =====

    /// <summary>
    /// A console's colour in the look the tokens hold: today's look takes its accent (<c>GameSystem.AccentColor</c>); Big
    /// Type takes its approved ground (<c>GameSystem.GroundColor</c>), or, for a system the table does not cover, its
    /// accent darkened until white type holds the floors (<see cref="HandheldGround.Derive"/>).
    /// </summary>
    /// <returns>The colour, or null for no platform or a colour that does not parse.</returns>
    public static Color? ConsoleColour(HandheldPlatform? platform) => ConsoleColour(platform, Look);

    /// <summary><see cref="ConsoleColour(HandheldPlatform?)"/> for a given look (tests ask without switching the tokens).</summary>
    internal static Color? ConsoleColour(HandheldPlatform? platform, HandheldLook look) => platform is null ? null
        : look == HandheldLook.BigType ? Ground(platform.GroundColor, platform.AccentColor)
        : ColourOf(platform.AccentColor);

    /// <summary><see cref="ConsoleColour(HandheldPlatform?)"/> as a brush, grey when there is no colour (as every Handheld view drew a missing one).</summary>
    public static IBrush ConsoleBrush(HandheldPlatform? platform) => BrushOf(ConsoleColour(platform));

    /// <summary>A folder's colour: a platform's is its console's (<see cref="ConsoleColour(HandheldPlatform?)"/>); yours, Recent's and Search's are their own.</summary>
    public static Color? FolderColour(HandheldFolderSource? source) =>
        source?.Platform is { } platform ? ConsoleColour(platform) : ColourOf(source?.AccentColor);

    /// <summary><see cref="FolderColour"/> as a brush, grey when there is no colour.</summary>
    public static IBrush FolderBrush(HandheldFolderSource? source) => BrushOf(FolderColour(source));

    /// <summary>A colour from its stored text (one of your folders' colours); the same in either look.</summary>
    /// <returns>The colour, or null when there is none or it does not parse.</returns>
    public static Color? ColourOf(string? text) => Color.TryParse(text, out var colour) ? colour : null;

    /// <summary><see cref="ColourOf"/> as a brush, grey when there is no colour.</summary>
    public static IBrush BrushOf(string? text) => BrushOf(ColourOf(text));

    private static IBrush BrushOf(Color? colour) => colour is { } c ? new SolidColorBrush(c) : Brushes.Gray;

    /// <summary>Big Type's ground: the approved colour, else the accent darkened until it holds the floors, else null.</summary>
    internal static Color? Ground(string? ground, string? accent)
    {
        if (HandheldGround.TryParse(ground, out var approved))
            return Color.FromUInt32(approved);
        return ColourOf(accent) is { } colour ? Color.FromUInt32(HandheldGround.Derive(colour.ToUInt32() | 0xFF000000)) : null;
    }

    private static IBrush Brush(string colour) => new ImmutableSolidColorBrush(Color.Parse(colour));

    // ===== Launcher: ground and type =====
    // The Launcher's light ground and the ink on it (the Panel view and All apps' light look share them).

    /// <summary>The Launcher's ground gradient, top.</summary>
    public static Color ClassicGroundTop { get; private set; }

    /// <summary>The Launcher's ground gradient, bottom.</summary>
    public static Color ClassicGroundBottom { get; private set; }

    /// <summary>Type on the Launcher: headings, labels, rows.</summary>
    public static IBrush OnGround { get; private set; }

    /// <summary>Secondary type on the Launcher: notes, counts, subtitles, hints.</summary>
    public static IBrush OnGroundMuted { get; private set; }

    /// <summary>A row's label while it cannot be used.</summary>
    public static IBrush OnGroundDisabled { get; private set; }

    /// <summary>A menu or option row's label at rest; also All apps' light type.</summary>
    public static IBrush MenuRowInk { get; private set; }

    /// <summary>A panel's status line.</summary>
    public static IBrush OnGroundSoft { get; private set; }

    /// <summary>Glyphs on chips, buttons and the dock's system buttons.</summary>
    public static IBrush Glyph { get; private set; }

    /// <summary>A row's chevrons at rest.</summary>
    public static IBrush Chevron { get; private set; }

    /// <summary>Type and glyphs on a filled colour: the accent, a face, a swatch.</summary>
    public static IBrush OnAccent { get; private set; }

    /// <summary>Secondary type on the accent (the setup button's detail).</summary>
    public static IBrush OnAccentMuted { get; private set; }

    /// <summary>The scrim under a sheet, the menu and a chooser.</summary>
    public static IBrush Scrim { get; private set; }

    /// <summary>Warning type and glyphs.</summary>
    public static IBrush Warning { get; private set; }

    /// <summary>A destructive row's label.</summary>
    public static IBrush Danger { get; private set; }

    /// <summary>The trophy glyph on a light surface.</summary>
    public static IBrush Trophy { get; private set; }

    /// <summary>The trophy on a dark backdrop, and the achievement bar.</summary>
    public static IBrush TrophyBright { get; private set; }

    /// <summary>A progress bar's track.</summary>
    public static IBrush ProgressTrack { get; private set; }

    /// <summary>A game whose file is missing, on a tile.</summary>
    public static double MissingTileOpacity { get; private set; }

    /// <summary>Behind a cover until it loads, and a game with no cover.</summary>
    public static IBrush CoverPlaceholder { get; private set; }

    /// <summary>Behind a panel row's thumbnail.</summary>
    public static IBrush ThumbPlaceholder { get; private set; }

    /// <summary>A home page's dot.</summary>
    public static IBrush PageDot { get; private set; }

    /// <summary>A switch's track while off.</summary>
    public static IBrush SwitchTrack { get; private set; }

    /// <summary>A switch's knob.</summary>
    public static IBrush SwitchKnobFill { get; private set; }

    /// <summary>The round glyph of a button hint (A, B, X, Y).</summary>
    public static IBrush HintGlyphFill { get; private set; }

    /// <summary>The letter on a button hint's glyph.</summary>
    public static IBrush HintGlyphInk { get; private set; }

    /// <summary>A folder's monogram or glyph on its colour.</summary>
    public static IBrush OnFolder { get; private set; }

    // ===== Launcher: today's chrome =====
    // Values that exist only in today's look: the indigo accent, white surfaces, hairlines, gradients, shadows.

    /// <summary>Today's one accent: a focused row, the Play button, a switch that is on, the page indicator.</summary>
    public static IBrush ClassicAccent { get; private set; }

    /// <summary>A panel's primary row label, a deeper accent on its tint.</summary>
    public static IBrush ClassicAccentInk { get; private set; }

    /// <summary>The accent's tint: the save row, a primary row, the Done key.</summary>
    public static IBrush ClassicAccentTint { get; private set; }

    /// <summary>The outline that keeps a row's place while the rail has the pad, and a lit empty slot.</summary>
    public static IBrush ClassicAccentOutline { get; private set; }

    /// <summary>The hairline round the save row and a primary row.</summary>
    public static IBrush ClassicAccentHairline { get; private set; }

    /// <summary>A panel row's second line while the row is focused.</summary>
    public static IBrush ClassicRowDetailOnAccent { get; private set; }

    /// <summary>The raised white surface: chips, cards, keys, buttons, rows, sheets.</summary>
    public static IBrush ClassicSurface { get; private set; }

    /// <summary>The screen chooser's card.</summary>
    public static IBrush ClassicSheetSurface { get; private set; }

    /// <summary>The name field's fill.</summary>
    public static IBrush ClassicFieldFill { get; private set; }

    /// <summary>The name field's edge at rest.</summary>
    public static IBrush ClassicFieldEdge { get; private set; }

    /// <summary>The plate behind a platform's icon in a Settings row.</summary>
    public static IBrush ClassicIconPlate { get; private set; }

    /// <summary>The hairline round every white surface.</summary>
    public static IBrush ClassicHairline { get; private set; }

    /// <summary>The white edge round the New ribbon, a colour chip and the merge thumbnail.</summary>
    public static IBrush ClassicEdge { get; private set; }

    /// <summary>The frosted pill of a home page's edge hint.</summary>
    public static IBrush ClassicEdgeHintFill { get; private set; }

    /// <summary>The dock's frosted pill.</summary>
    public static IBrush ClassicDockFill { get; private set; }

    /// <summary>An empty slot's edge.</summary>
    public static IBrush ClassicEmptySlotEdge { get; private set; }

    /// <summary>An empty slot's fill.</summary>
    public static IBrush ClassicEmptySlotFill { get; private set; }

    /// <summary>An empty slot lit as a landing place in move mode.</summary>
    public static IBrush ClassicLandingFill { get; private set; }

    /// <summary>A tile about to take the carried game.</summary>
    public static IBrush ClassicDropFill { get; private set; }

    /// <summary>The Add games slot's edge.</summary>
    public static IBrush ClassicAddGamesEdge { get; private set; }

    /// <summary>The Add games slot's fill.</summary>
    public static IBrush ClassicAddGamesFill { get; private set; }

    /// <summary>A switch's track while off, in a focused row.</summary>
    public static IBrush ClassicSwitchTrackFocused { get; private set; }

    /// <summary>A switch's track while on, in a focused row (its knob takes the accent).</summary>
    public static IBrush ClassicSwitchOnFocused { get; private set; }

    /// <summary>The round plate of the setup button's glyph.</summary>
    public static IBrush ClassicStartGlyphFill { get; private set; }

    /// <summary>The Settings face's gradient, top.</summary>
    public static Color ClassicSettingsFaceTop { get; private set; }

    /// <summary>The Settings face's gradient, bottom.</summary>
    public static Color ClassicSettingsFaceBottom { get; private set; }

    /// <summary>The Activity face's gradient, top.</summary>
    public static Color ClassicActivityFaceTop { get; private set; }

    /// <summary>The Activity face's gradient, bottom.</summary>
    public static Color ClassicActivityFaceBottom { get; private set; }

    /// <summary>The Title page's colour wash: where the mask is full.</summary>
    public static Color ClassicWashOpaque { get; private set; }

    /// <summary>The Title page's colour wash: where the mask has faded out.</summary>
    public static Color ClassicWashClear { get; private set; }

    /// <summary>A pre-rendered shadow's colour unless it says otherwise.</summary>
    public static Color ClassicShadow { get; private set; }

    /// <summary>The merge thumbnail's shadow.</summary>
    public static Color ClassicDropShadow { get; private set; }

    /// <summary>A lifted folder tile's deeper shadow.</summary>
    public static Color ClassicLiftShadow { get; private set; }

    /// <summary>The dock pill's shadow.</summary>
    public static Color ClassicDockShadow { get; private set; }

    /// <summary>The glow that breathes under the selector.</summary>
    public static Color ClassicRingGlow { get; private set; }

    /// <summary>The New ribbon's shadow.</summary>
    public static BoxShadows ClassicRibbonShadow { get; private set; }

    /// <summary>A key's shadow.</summary>
    public static BoxShadows ClassicKeyShadow { get; private set; }

    /// <summary>A chip's and the selected Settings section's shadow.</summary>
    public static BoxShadows ClassicRaisedShadow { get; private set; }

    /// <summary>A Title page button's shadow.</summary>
    public static BoxShadows ClassicButtonShadow { get; private set; }

    /// <summary>The shadow under an accent fill (Play, a focused Settings row).</summary>
    public static BoxShadows ClassicAccentShadow { get; private set; }

    /// <summary>A screen card's shadow.</summary>
    public static BoxShadows ClassicCardShadow { get; private set; }

    /// <summary>The Title page's cover shadow.</summary>
    public static BoxShadows ClassicCoverShadow { get; private set; }

    /// <summary>A centred card's shadow (the menu, a chooser, a confirmation).</summary>
    public static BoxShadows ClassicSheetShadow { get; private set; }

    /// <summary>The actions sheet's shadow.</summary>
    public static BoxShadows ClassicBottomSheetShadow { get; private set; }

    /// <summary>The actions sheet's cover thumbnail shadow.</summary>
    public static BoxShadows ClassicThumbShadow { get; private set; }

    /// <summary>The actions sheet's folder swatch shadow.</summary>
    public static BoxShadows ClassicSwatchShadow { get; private set; }

    /// <summary>The setup button's shadow.</summary>
    public static BoxShadows ClassicStartShadow { get; private set; }

    /// <summary>The ring round a focused colour chip.</summary>
    public static BoxShadows ClassicChipFocusRing { get; private set; }

    /// <summary>A home tile's pre-rendered shadow: its corner.</summary>
    public static double ClassicHomeTileShadowRadius { get; private set; }

    /// <summary>A home tile's pre-rendered shadow: its blur.</summary>
    public static double ClassicHomeTileShadowBlur { get; private set; }

    /// <summary>The merge thumbnail's shadow: its corner.</summary>
    public static double ClassicCarriedShadowRadius { get; private set; }

    /// <summary>The merge thumbnail's shadow: its blur.</summary>
    public static double ClassicCarriedShadowBlur { get; private set; }

    /// <summary>A folder tile's shadows: their corner.</summary>
    public static double ClassicFolderTileShadowRadius { get; private set; }

    /// <summary>A folder tile's resting shadow: its blur.</summary>
    public static double ClassicFolderTileShadowBlur { get; private set; }

    /// <summary>A lifted folder tile's deeper shadow: its blur.</summary>
    public static double ClassicLiftShadowBlur { get; private set; }

    /// <summary>The dock pill's shadow: its corner.</summary>
    public static double ClassicDockShadowRadius { get; private set; }

    /// <summary>The dock pill's shadow: its blur.</summary>
    public static double ClassicDockShadowBlur { get; private set; }

    /// <summary>The selector's glow: its blur.</summary>
    public static double ClassicRingGlowBlur { get; private set; }

    // ===== The selector =====
    // The one ring that travels between focused elements, and the corners it takes at each kind of element.

    /// <summary>The selector's band.</summary>
    public static IBrush Ring { get; private set; }

    /// <summary>The white keylines either side of today's band.</summary>
    public static IBrush ClassicRingKeyline { get; private set; }

    /// <summary>The band's width.</summary>
    public static double RingStroke { get; private set; }

    /// <summary>Each keyline's width.</summary>
    public static double ClassicRingKeylineWidth { get; private set; }

    /// <summary>The band's width as a border.</summary>
    public static Thickness RingStrokeThickness => new(RingStroke);

    /// <summary>A keyline's width as a border.</summary>
    public static Thickness ClassicRingKeylineThickness => new(ClassicRingKeylineWidth);

    /// <summary>How far in from a ring anchor's outer corner its innermost border sits.</summary>
    public static double ClassicRingAnchorInset { get; private set; }

    /// <summary>The ring round a home tile.</summary>
    public static CornerRadius RingRadiusTile { get; private set; }

    /// <summary>The ring round a game in a folder.</summary>
    public static CornerRadius RingRadiusTileFolder { get; private set; }

    /// <summary>The ring round a dock tile.</summary>
    public static CornerRadius RingRadiusDock { get; private set; }

    /// <summary>The ring round a header chip (Back, sort).</summary>
    public static CornerRadius RingRadiusChip { get; private set; }

    /// <summary>The ring round a Title page button.</summary>
    public static CornerRadius RingRadiusButton { get; private set; }

    /// <summary>The ring round a screen card.</summary>
    public static CornerRadius RingRadiusCard { get; private set; }

    /// <summary>The ring round the remember row.</summary>
    public static CornerRadius RingRadiusRow { get; private set; }

    /// <summary>A ring anchor's band corner.</summary>
    public static CornerRadius ClassicRingBandRadiusTileFolder => new(Math.Max(0, RingRadiusTileFolder.TopLeft - ClassicRingKeylineWidth));

    /// <summary>A ring anchor's inner corner.</summary>
    public static CornerRadius ClassicRingInnerRadiusTileFolder => new(Math.Max(0, RingRadiusTileFolder.TopLeft - ClassicRingAnchorInset));

    /// <summary>A ring anchor's band corner.</summary>
    public static CornerRadius ClassicRingBandRadiusDock => new(Math.Max(0, RingRadiusDock.TopLeft - ClassicRingKeylineWidth));

    /// <summary>A ring anchor's inner corner.</summary>
    public static CornerRadius ClassicRingInnerRadiusDock => new(Math.Max(0, RingRadiusDock.TopLeft - ClassicRingAnchorInset));

    /// <summary>A ring anchor's band corner.</summary>
    public static CornerRadius ClassicRingBandRadiusChip => new(Math.Max(0, RingRadiusChip.TopLeft - ClassicRingKeylineWidth));

    /// <summary>A ring anchor's inner corner.</summary>
    public static CornerRadius ClassicRingInnerRadiusChip => new(Math.Max(0, RingRadiusChip.TopLeft - ClassicRingAnchorInset));

    /// <summary>A ring anchor's band corner.</summary>
    public static CornerRadius ClassicRingBandRadiusButton => new(Math.Max(0, RingRadiusButton.TopLeft - ClassicRingKeylineWidth));

    /// <summary>A ring anchor's inner corner.</summary>
    public static CornerRadius ClassicRingInnerRadiusButton => new(Math.Max(0, RingRadiusButton.TopLeft - ClassicRingAnchorInset));

    /// <summary>A ring anchor's band corner.</summary>
    public static CornerRadius ClassicRingBandRadiusCard => new(Math.Max(0, RingRadiusCard.TopLeft - ClassicRingKeylineWidth));

    /// <summary>A ring anchor's inner corner.</summary>
    public static CornerRadius ClassicRingInnerRadiusCard => new(Math.Max(0, RingRadiusCard.TopLeft - ClassicRingAnchorInset));

    /// <summary>A ring anchor's band corner.</summary>
    public static CornerRadius ClassicRingBandRadiusRow => new(Math.Max(0, RingRadiusRow.TopLeft - ClassicRingKeylineWidth));

    /// <summary>A ring anchor's inner corner.</summary>
    public static CornerRadius ClassicRingInnerRadiusRow => new(Math.Max(0, RingRadiusRow.TopLeft - ClassicRingAnchorInset));

    // ===== Launcher: shape and size =====
    // Tiles, the dock, keys, rows and the controls in them.

    /// <summary>A home tile's side.</summary>
    public static double TileHome { get; private set; }

    /// <summary>A game tile's side in an open folder.</summary>
    public static double TileFolder { get; private set; }

    /// <summary>An open folder's column pitch.</summary>
    public static double FolderPitch { get; private set; }

    /// <summary>An open folder's row pitch.</summary>
    public static double FolderRowPitch { get; private set; }

    /// <summary>An open folder's three rows.</summary>
    public static double FolderTrackHeight { get; private set; }

    /// <summary>A dock tile's side.</summary>
    public static double DockTile { get; private set; }

    /// <summary>The gap between dock tiles.</summary>
    public static double DockGap { get; private set; }

    /// <summary>A key's side.</summary>
    public static double Key { get; private set; }

    /// <summary>A home page dot's width.</summary>
    public static double PageDotWidth { get; private set; }

    /// <summary>A home page dot's height.</summary>
    public static double PageDotHeight { get; private set; }

    /// <summary>The gap between home page dots.</summary>
    public static double PageDotGap { get; private set; }

    /// <summary>The current-page pill's width.</summary>
    public static double PageIndicatorWidth { get; private set; }

    /// <summary>A button hint's round glyph on the Launcher.</summary>
    public static double HintGlyph { get; private set; }

    /// <summary>A switch's width.</summary>
    public static double SwitchWidth { get; private set; }

    /// <summary>A switch's height.</summary>
    public static double SwitchHeight { get; private set; }

    /// <summary>A switch's knob.</summary>
    public static double SwitchKnob { get; private set; }

    /// <summary>The Title page's achievement bar.</summary>
    public static double ProgressHeight { get; private set; }

    /// <summary>The launch bar.</summary>
    public static double LaunchProgressHeight { get; private set; }

    /// <summary>A home tile: a pinned game, Settings, Activity.</summary>
    public static CornerRadius TileRadius { get; private set; }

    /// <summary>A game tile in an open folder.</summary>
    public static CornerRadius TileRadiusFolder { get; private set; }

    /// <summary>An empty slot, Add games, and a tile about to take the carried game.</summary>
    public static CornerRadius SlotRadius { get; private set; }

    /// <summary>The merge thumbnail.</summary>
    public static CornerRadius CarriedTileRadius { get; private set; }

    /// <summary>A dock tile.</summary>
    public static CornerRadius DockRadius { get; private set; }

    /// <summary>A key.</summary>
    public static CornerRadius KeyRadius { get; private set; }

    /// <summary>A home page dot and the current-page pill.</summary>
    public static CornerRadius PageDotRadius { get; private set; }

    /// <summary>The colour dot beside an open folder's name.</summary>
    public static CornerRadius FolderDotRadius { get; private set; }

    /// <summary>A button hint's round glyph on the Launcher.</summary>
    public static CornerRadius HintGlyphRadius { get; private set; }

    /// <summary>A row in the actions sheet or a choice list.</summary>
    public static CornerRadius SheetRowRadius { get; private set; }

    /// <summary>A row in the menu or an option list.</summary>
    public static CornerRadius MenuRowRadius { get; private set; }

    /// <summary>A Settings or panel row.</summary>
    public static CornerRadius RowRadius { get; private set; }

    /// <summary>A Settings section.</summary>
    public static CornerRadius RailRowRadius { get; private set; }

    /// <summary>The name field.</summary>
    public static CornerRadius FieldRadius { get; private set; }

    /// <summary>A colour chip.</summary>
    public static CornerRadius ColourChipRadius { get; private set; }

    /// <summary>A switch's track.</summary>
    public static CornerRadius SwitchRadius { get; private set; }

    /// <summary>A switch's knob.</summary>
    public static CornerRadius SwitchKnobRadius { get; private set; }

    /// <summary>A screen card in the screen chooser.</summary>
    public static CornerRadius ScreenCardRadius { get; private set; }

    /// <summary>The Title page's cover, and the cover that lifts off at launch.</summary>
    public static CornerRadius TitleCoverRadius { get; private set; }

    /// <summary>The Title page's achievement bar.</summary>
    public static CornerRadius ProgressRadius { get; private set; }

    /// <summary>The launch bar.</summary>
    public static CornerRadius LaunchProgressRadius { get; private set; }

    /// <summary>The remember row's check box.</summary>
    public static CornerRadius CheckboxRadius { get; private set; }

    /// <summary>The actions sheet's cover thumbnail or folder swatch.</summary>
    public static CornerRadius SwatchRadius { get; private set; }

    /// <summary>The setup wizard's Continue button.</summary>
    public static CornerRadius StartButtonRadius { get; private set; }

    /// <summary>The round plate of the Continue button's glyph.</summary>
    public static CornerRadius StartGlyphRadius { get; private set; }

    /// <summary>The plate behind a platform's icon in a Settings row.</summary>
    public static CornerRadius IconPlateRadius { get; private set; }

    /// <summary>A panel row's thumbnail.</summary>
    public static CornerRadius ThumbRadius { get; private set; }

    /// <summary>A panel's filter chip.</summary>
    public static CornerRadius FilterRadius { get; private set; }

    /// <summary>The New ribbon.</summary>
    public static CornerRadius ClassicRibbonRadius { get; private set; }

    /// <summary>A home page's edge hint.</summary>
    public static CornerRadius ClassicEdgeHintRadius { get; private set; }

    /// <summary>A header chip (Back, sort).</summary>
    public static CornerRadius ClassicChipRadius { get; private set; }

    /// <summary>A Title page button.</summary>
    public static CornerRadius ClassicButtonRadius { get; private set; }

    /// <summary>The remember row's chip.</summary>
    public static CornerRadius ClassicRememberRadius { get; private set; }

    /// <summary>The plus badge on a tile about to take the carried game.</summary>
    public static CornerRadius ClassicMergeBadgeRadius { get; private set; }

    /// <summary>The dock's pill.</summary>
    public static CornerRadius ClassicDockPillRadius { get; private set; }

    /// <summary>A centred card: the menu, a chooser, a confirmation.</summary>
    public static CornerRadius ClassicSheetRadius { get; private set; }

    /// <summary>The screen chooser's card.</summary>
    public static CornerRadius ClassicChooserRadius { get; private set; }

    /// <summary>The actions sheet.</summary>
    public static CornerRadius ClassicBottomSheetRadius { get; private set; }

    // ===== Launcher: type =====
    // Sizes and weights. A name matches the Big Type table where the role is the same.

    /// <summary>Settings' and a panel's heading.</summary>
    public static double Heading { get; private set; }

    /// <summary>Headings and titles.</summary>
    public static FontWeight HeadingWeight { get; private set; }

    /// <summary>An open folder's name, and its empty state's heading.</summary>
    public static double FolderHeading { get; private set; }

    /// <summary>The count beside a folder's name.</summary>
    public static double HeadingCount { get; private set; }

    /// <summary>A secondary line under a heading or a card's title.</summary>
    public static double Note { get; private set; }

    /// <summary>A centred card's title (the menu, a chooser, a confirmation).</summary>
    public static double SheetTitle { get; private set; }

    /// <summary>The actions sheet's title.</summary>
    public static double BottomSheetTitle { get; private set; }

    /// <summary>The screen chooser's question.</summary>
    public static double ChooserTitle { get; private set; }

    /// <summary>The screen chooser's title line.</summary>
    public static double ChooserSubtitle { get; private set; }

    /// <summary>A screen card's name.</summary>
    public static double ScreenCardTitle { get; private set; }

    /// <summary>A Settings row's label.</summary>
    public static double Row { get; private set; }

    /// <summary>Rows, chips, buttons, keys and filters.</summary>
    public static FontWeight RowWeight { get; private set; }

    /// <summary>A Settings row's value, a sheet row's note.</summary>
    public static double RowValue { get; private set; }

    /// <summary>A panel row's second line.</summary>
    public static double RowDetail { get; private set; }

    /// <summary>A Settings section's title.</summary>
    public static double RailRow { get; private set; }

    /// <summary>A Settings section's status line.</summary>
    public static double RailRowStatus { get; private set; }

    /// <summary>An actions sheet or choice row's label.</summary>
    public static double SheetRow { get; private set; }

    /// <summary>A menu or option row's label.</summary>
    public static double MenuRow { get; private set; }

    /// <summary>An option row's note.</summary>
    public static double MenuRowNote { get; private set; }

    /// <summary>A menu row's state (Sounds on or off).</summary>
    public static double MenuRowState { get; private set; }

    /// <summary>A header chip's label (Back, sort, remember).</summary>
    public static double Chip { get; private set; }

    /// <summary>The Title page's facts and achievement line.</summary>
    public static double Facts { get; private set; }

    /// <summary>The Title page's crumb line.</summary>
    public static double Crumb { get; private set; }

    /// <summary>The Title page's title.</summary>
    public static double TitleName { get; private set; }

    /// <summary>The Title page's platform line.</summary>
    public static double TitlePlatform { get; private set; }

    /// <summary>A Title page button's label.</summary>
    public static double TitleButton { get; private set; }

    /// <summary>A Title page button's note.</summary>
    public static double TitleButtonNote { get; private set; }

    /// <summary>The launch panel's heading.</summary>
    public static double LaunchHeading { get; private set; }

    /// <summary>The launch panel's detail line.</summary>
    public static double LaunchDetail { get; private set; }

    /// <summary>The wide screen's page name.</summary>
    public static double PageLabel { get; private set; }

    /// <summary>A home game's title when it has no cover.</summary>
    public static double TileTitleHome { get; private set; }

    /// <summary>A folder game's title when it has no cover.</summary>
    public static double TileTitleFolder { get; private set; }

    /// <summary>The merge thumbnail's title when it has no cover.</summary>
    public static double TileTitleCarried { get; private set; }

    /// <summary>The Title page cover's title when it has no cover.</summary>
    public static double TileTitleCover { get; private set; }

    /// <summary>A title set in place of a cover, and Add games.</summary>
    public static FontWeight TileTitleWeight { get; private set; }

    /// <summary>The Add games slot's label.</summary>
    public static double AddGames { get; private set; }

    /// <summary>The New ribbon.</summary>
    public static double Ribbon { get; private set; }

    /// <summary>The New ribbon.</summary>
    public static FontWeight RibbonWeight { get; private set; }

    /// <summary>A platform's monogram on the actions sheet's swatch.</summary>
    public static double SwatchMonogram { get; private set; }

    /// <summary>A platform's monogram on the actions sheet's swatch.</summary>
    public static FontWeight SwatchMonogramWeight { get; private set; }

    /// <summary>A key's letter.</summary>
    public static double KeyLabel { get; private set; }

    /// <summary>A wide key's label.</summary>
    public static double KeyWide { get; private set; }

    /// <summary>A middle-width key's label.</summary>
    public static double KeyMid { get; private set; }

    /// <summary>The space key's label.</summary>
    public static double KeySpace { get; private set; }

    /// <summary>The folder name as typed.</summary>
    public static double NamingField { get; private set; }

    /// <summary>A text row's value as typed on the keyboard.</summary>
    public static double KeyboardField { get; private set; }

    /// <summary>What a field or the colour chips are for.</summary>
    public static double FieldLabel { get; private set; }

    /// <summary>A button hint's label on the Launcher.</summary>
    public static double Hint { get; private set; }

    /// <summary>The letter on a button hint's glyph.</summary>
    public static double HintGlyphLetter { get; private set; }

    /// <summary>The letter on a hint glyph (the Title page, the Stage, the companion).</summary>
    public static FontWeight HintGlyphWeight { get; private set; }

    /// <summary>The letter on a hint glyph under a keyboard.</summary>
    public static FontWeight KeyboardHintGlyphWeight { get; private set; }

    /// <summary>The Continue button's label.</summary>
    public static double StartLabel { get; private set; }

    /// <summary>The Continue button's detail line.</summary>
    public static double StartDetail { get; private set; }

    /// <summary>A panel's filter chip.</summary>
    public static double Filter { get; private set; }

    /// <summary>What a panel is doing or what happened.</summary>
    public static double PanelStatus { get; private set; }

    /// <summary>A panel's empty state.</summary>
    public static double PanelEmpty { get; private set; }

    /// <summary>A platform folder's monogram.</summary>
    public static double Monogram { get; private set; }

    /// <summary>A platform folder's monogram.</summary>
    public static FontWeight MonogramWeight { get; private set; }

    // ===== Folders =====
    // A folder's art: the back plate, the covers peeking out, the flap.

    /// <summary>A folder's back plate.</summary>
    public static CornerRadius FolderBackRadius { get; private set; }

    /// <summary>A folder's front flap.</summary>
    public static CornerRadius FolderFlapRadius { get; private set; }

    /// <summary>A cover peeking out of a folder.</summary>
    public static CornerRadius PeekCoverRadius { get; private set; }

    /// <summary>The shade that darkens a folder's back plate.</summary>
    public static IBrush ClassicFolderBackShade { get; private set; }

    /// <summary>The flap's gloss, top.</summary>
    public static Color ClassicFlapGlossTop { get; private set; }

    /// <summary>The flap's gloss, faded out.</summary>
    public static Color ClassicFlapGlossClear { get; private set; }

    /// <summary>The flap's top edge catching the light.</summary>
    public static IBrush ClassicFlapEdge { get; private set; }

    /// <summary>A folder's shadow.</summary>
    public static Color ClassicFolderShadow { get; private set; }

    /// <summary>A folder's shadow: its corner.</summary>
    public static double ClassicFolderShadowRadius { get; private set; }

    /// <summary>A folder's shadow: its blur.</summary>
    public static double ClassicFolderShadowBlur { get; private set; }

    // ===== Stage =====
    // The top screen: the backdrop, the centre art, the cards, the corners.

    /// <summary>The Stage's plain ground, top (apps, empty slots, the menu).</summary>
    public static Color ClassicStageGroundTop { get; private set; }

    /// <summary>The Stage's plain ground, bottom.</summary>
    public static Color ClassicStageGroundBottom { get; private set; }

    /// <summary>Type on the Stage's cards and corners.</summary>
    public static IBrush StageInk { get; private set; }

    /// <summary>Secondary type and glyphs on the Stage.</summary>
    public static IBrush StageInkMuted { get; private set; }

    /// <summary>A card's description and status.</summary>
    public static IBrush StageInkSoft { get; private set; }

    /// <summary>The status corner's Wi-Fi and charging glyphs.</summary>
    public static IBrush StageGlyph { get; private set; }

    /// <summary>Type over fan art.</summary>
    public static IBrush OnHero { get; private set; }

    /// <summary>Why the idle drift shows this game, over fan art.</summary>
    public static IBrush OnHeroMuted { get; private set; }

    /// <summary>The line under the title over fan art.</summary>
    public static IBrush OnHeroSoft { get; private set; }

    /// <summary>The darkening across fan art: at the left.</summary>
    public static Color ClassicHeroVeilLeft { get; private set; }

    /// <summary>The darkening across fan art: the middle.</summary>
    public static Color ClassicHeroVeilMid { get; private set; }

    /// <summary>The darkening across fan art: faded out.</summary>
    public static Color ClassicHeroVeilClear { get; private set; }

    /// <summary>The darkening up from the foot of fan art.</summary>
    public static Color ClassicHeroVeilFoot { get; private set; }

    /// <summary>The shadow under the logo or title over fan art.</summary>
    public static Color ClassicHeroTextShadow { get; private set; }

    /// <summary>That shadow's blur.</summary>
    public static double ClassicHeroTextShadowBlur { get; private set; }

    /// <summary>A corner pill's frosted fill.</summary>
    public static IBrush ClassicPillFill { get; private set; }

    /// <summary>A corner pill's and a message's shadow.</summary>
    public static BoxShadows ClassicPillShadow { get; private set; }

    /// <summary>A corner pill and a message.</summary>
    public static CornerRadius ClassicPillRadius { get; private set; }

    /// <summary>A message's fill.</summary>
    public static IBrush ClassicToastFill { get; private set; }

    /// <summary>A failure's fill.</summary>
    public static IBrush ClassicToastErrorFill { get; private set; }

    /// <summary>A failure's edge.</summary>
    public static IBrush ClassicToastErrorEdge { get; private set; }

    /// <summary>The name plate's fill.</summary>
    public static IBrush ClassicPlateFill { get; private set; }

    /// <summary>The name plate's shadow.</summary>
    public static BoxShadows ClassicPlateShadow { get; private set; }

    /// <summary>The name plate.</summary>
    public static CornerRadius ClassicPlateRadius { get; private set; }

    /// <summary>The centre art's shadow.</summary>
    public static BoxShadows ClassicStageArtShadow { get; private set; }

    /// <summary>The no-cover card's shadow.</summary>
    public static BoxShadows ClassicStagePlaceholderShadow { get; private set; }

    /// <summary>An app's icon card's shadow.</summary>
    public static BoxShadows ClassicStageAppShadow { get; private set; }

    /// <summary>The Settings tile's shadow.</summary>
    public static BoxShadows ClassicStageSettingsShadow { get; private set; }

    /// <summary>The Activity tile's shadow.</summary>
    public static BoxShadows ClassicStageActivityShadow { get; private set; }

    /// <summary>A Settings or panel card's shadow.</summary>
    public static BoxShadows ClassicStageCardShadow { get; private set; }

    /// <summary>The quiet glyph card's shadow.</summary>
    public static BoxShadows ClassicStageGlyphCardShadow { get; private set; }

    /// <summary>A fanned search result's shadow.</summary>
    public static BoxShadows ClassicFanCoverShadow { get; private set; }

    /// <summary>The status box inside a card.</summary>
    public static IBrush ClassicCardInsetFill { get; private set; }

    /// <summary>Behind a panel card's picture.</summary>
    public static IBrush ClassicPictureFill { get; private set; }

    /// <summary>A panel card's progress track.</summary>
    public static IBrush CardProgressTrack { get; private set; }

    /// <summary>The edge of the cover that lands at launch.</summary>
    public static IBrush ClassicLaunchCardEdge { get; private set; }

    /// <summary>The edge of the cover that lands at launch: its width.</summary>
    public static double ClassicLaunchCardEdgeWidth { get; private set; }

    /// <summary>That edge as a border.</summary>
    public static Thickness ClassicLaunchCardEdgeThickness => new(ClassicLaunchCardEdgeWidth);

    /// <summary>The centre art and the no-cover card.</summary>
    public static CornerRadius StageArtRadius { get; private set; }

    /// <summary>An app's icon card, the Settings and Activity tiles, the quiet glyph card.</summary>
    public static CornerRadius StageAppRadius { get; private set; }

    /// <summary>A Settings or panel card.</summary>
    public static CornerRadius StageCardRadius { get; private set; }

    /// <summary>The glyph tile in a card's eyebrow.</summary>
    public static CornerRadius StageCardGlyphRadius { get; private set; }

    /// <summary>The status box inside a card.</summary>
    public static CornerRadius CardInsetRadius { get; private set; }

    /// <summary>A panel card's picture.</summary>
    public static CornerRadius PanelPictureRadius { get; private set; }

    /// <summary>A fanned search result.</summary>
    public static CornerRadius FanCoverRadius { get; private set; }

    /// <summary>A panel card's progress bar.</summary>
    public static CornerRadius CardProgressRadius { get; private set; }

    /// <summary>The cover that lands at launch.</summary>
    public static CornerRadius LaunchCardRadius { get; private set; }

    /// <summary>A button hint's round glyph on the Stage.</summary>
    public static CornerRadius StageHintGlyphRadius { get; private set; }

    /// <summary>A button hint's round glyph on the Stage.</summary>
    public static double StageHintGlyph { get; private set; }

    /// <summary>The cover that lands at launch: its width.</summary>
    public static double LaunchCardWidth { get; private set; }

    /// <summary>The cover that lands at launch: its height.</summary>
    public static double LaunchCardHeight { get; private set; }

    /// <summary>Type in the Stage's corners: status, achievements, hints, messages.</summary>
    public static double StageCorner { get; private set; }

    /// <summary>Type in the Stage's corners.</summary>
    public static FontWeight StageCornerWeight { get; private set; }

    /// <summary>A two-line message's line height.</summary>
    public static double StageCornerLineHeight { get; private set; }

    /// <summary>The focused item's name.</summary>
    public static double DisplayName { get; private set; }

    /// <summary>The focused item's name.</summary>
    public static FontWeight DisplayNameWeight { get; private set; }

    /// <summary>The line under the focused item's name.</summary>
    public static double Line { get; private set; }

    /// <summary>The title set in type over fan art (no logo).</summary>
    public static double HeroTitle { get; private set; }

    /// <summary>The title under a logo over fan art.</summary>
    public static double HeroLogoTitle { get; private set; }

    /// <summary>Why the idle drift shows this game.</summary>
    public static double HeroEyebrow { get; private set; }

    /// <summary>The achievement and meta line over fan art.</summary>
    public static double HeroLine { get; private set; }

    /// <summary>Type over fan art.</summary>
    public static FontWeight HeroWeight { get; private set; }

    /// <summary>The no-cover card's title.</summary>
    public static double StageCoverTitle { get; private set; }

    /// <summary>What a card is about, beside its glyph.</summary>
    public static double CardEyebrow { get; private set; }

    /// <summary>A Settings card's title.</summary>
    public static double CardTitle { get; private set; }

    /// <summary>A panel card's title.</summary>
    public static double PanelCardTitle { get; private set; }

    /// <summary>A card's value.</summary>
    public static double CardValue { get; private set; }

    /// <summary>A card's description.</summary>
    public static double CardDescription { get; private set; }

    /// <summary>A card's description: its line height.</summary>
    public static double CardDescriptionLineHeight { get; private set; }

    /// <summary>A card's status box.</summary>
    public static double CardStatus { get; private set; }

    /// <summary>A card's eyebrow, title and value.</summary>
    public static FontWeight CardWeight { get; private set; }

    // ===== Companion =====
    // The bottom screen while a game runs: a dark ground, its own ring, tabs, the dock, the layers.

    /// <summary>The companion's ground, top.</summary>
    public static Color ClassicCompanionGroundTop { get; private set; }

    /// <summary>The companion's ground, at 58%.</summary>
    public static Color ClassicCompanionGroundMid { get; private set; }

    /// <summary>The companion's ground, bottom.</summary>
    public static Color ClassicCompanionGroundBottom { get; private set; }

    /// <summary>The companion's selector band.</summary>
    public static IBrush CompanionRing { get; private set; }

    /// <summary>The companion selector's glow.</summary>
    public static BoxShadows ClassicCompanionRingGlow { get; private set; }

    /// <summary>The selector's glow over All apps' light look.</summary>
    public static BoxShadows ClassicCompanionRingGlowLight { get; private set; }

    /// <summary>The companion band's width.</summary>
    public static double CompanionRingStroke { get; private set; }

    /// <summary>The companion ring's outer keyline.</summary>
    public static double ClassicCompanionRingOuterKeyline { get; private set; }

    /// <summary>The companion ring's inner keyline.</summary>
    public static double ClassicCompanionRingInnerKeyline { get; private set; }

    /// <summary>The companion band as a border.</summary>
    public static Thickness CompanionRingStrokeThickness => new(CompanionRingStroke);

    /// <summary>The outer keyline as a border.</summary>
    public static Thickness ClassicCompanionRingOuterKeylineThickness => new(ClassicCompanionRingOuterKeyline);

    /// <summary>The inner keyline as a border.</summary>
    public static Thickness ClassicCompanionRingInnerKeylineThickness => new(ClassicCompanionRingInnerKeyline);

    /// <summary>From a companion ring's outer corner to its inner keyline (outer keyline and band).</summary>
    public static double ClassicCompanionRingInset { get; private set; }

    /// <summary>The ring round the companion's big panel.</summary>
    public static CornerRadius CompanionRingRadiusPanel { get; private set; }

    /// <summary>The ring round a companion dock tile.</summary>
    public static CornerRadius CompanionRingRadiusDock { get; private set; }

    /// <summary>The ring round a badge.</summary>
    public static CornerRadius CompanionRingRadiusBadge { get; private set; }

    /// <summary>The ring round an app in All apps.</summary>
    public static CornerRadius CompanionRingRadiusApp { get; private set; }

    /// <summary>The band's corner.</summary>
    public static CornerRadius ClassicCompanionRingBandRadiusPanel => new(Math.Max(0, CompanionRingRadiusPanel.TopLeft - ClassicCompanionRingOuterKeyline));

    /// <summary>The inner keyline's corner.</summary>
    public static CornerRadius ClassicCompanionRingInnerRadiusPanel => new(Math.Max(0, CompanionRingRadiusPanel.TopLeft - ClassicCompanionRingInset));

    /// <summary>The band's corner.</summary>
    public static CornerRadius ClassicCompanionRingBandRadiusDock => new(Math.Max(0, CompanionRingRadiusDock.TopLeft - ClassicCompanionRingOuterKeyline));

    /// <summary>The inner keyline's corner.</summary>
    public static CornerRadius ClassicCompanionRingInnerRadiusDock => new(Math.Max(0, CompanionRingRadiusDock.TopLeft - ClassicCompanionRingInset));

    /// <summary>The band's corner.</summary>
    public static CornerRadius ClassicCompanionRingBandRadiusBadge => new(Math.Max(0, CompanionRingRadiusBadge.TopLeft - ClassicCompanionRingOuterKeyline));

    /// <summary>The inner keyline's corner.</summary>
    public static CornerRadius ClassicCompanionRingInnerRadiusBadge => new(Math.Max(0, CompanionRingRadiusBadge.TopLeft - ClassicCompanionRingInset));

    /// <summary>The band's corner.</summary>
    public static CornerRadius ClassicCompanionRingBandRadiusApp => new(Math.Max(0, CompanionRingRadiusApp.TopLeft - ClassicCompanionRingOuterKeyline));

    /// <summary>The inner keyline's corner.</summary>
    public static CornerRadius ClassicCompanionRingInnerRadiusApp => new(Math.Max(0, CompanionRingRadiusApp.TopLeft - ClassicCompanionRingInset));

    /// <summary>Type on the companion's ground.</summary>
    public static IBrush CompanionInk { get; private set; }

    /// <summary>Secondary type on the companion.</summary>
    public static IBrush CompanionInkMuted { get; private set; }

    /// <summary>The quietest type and glyphs on the companion.</summary>
    public static IBrush CompanionInkQuiet { get; private set; }

    /// <summary>Tabs at rest, hints, a panel's message.</summary>
    public static IBrush CompanionInkSoft { get; private set; }

    /// <summary>The chosen tab's fill.</summary>
    public static IBrush CompanionTabOn { get; private set; }

    /// <summary>The chosen tab's type and glyph.</summary>
    public static IBrush CompanionTabOnInk { get; private set; }

    /// <summary>A tile at rest on the companion: All apps, an app face, a badge, the cover's ground.</summary>
    public static IBrush CompanionTileRest { get; private set; }

    /// <summary>A glyph on a play or close button over a photo.</summary>
    public static IBrush CompanionGlyph { get; private set; }

    /// <summary>All apps' glyph in the companion's dock.</summary>
    public static IBrush CompanionDockGlyph { get; private set; }

    /// <summary>An empty dock slot's edge.</summary>
    public static IBrush CompanionEmptySlotEdge { get; private set; }

    /// <summary>The rule between All apps and the pinned apps.</summary>
    public static IBrush CompanionDivider { get; private set; }

    /// <summary>The rule above the selected badge.</summary>
    public static IBrush CompanionRule { get; private set; }

    /// <summary>A button hint's round glyph on the companion.</summary>
    public static IBrush CompanionHintGlyphFill { get; private set; }

    /// <summary>The achievement ring's track.</summary>
    public static IBrush CompanionProgressTrack { get; private set; }

    /// <summary>Achievement progress, and a hardcore badge's edge.</summary>
    public static IBrush AchievementGold { get; private set; }

    /// <summary>The Now playing dot.</summary>
    public static IBrush CompanionLive { get; private set; }

    /// <summary>The Now playing label.</summary>
    public static IBrush CompanionLiveInk { get; private set; }

    /// <summary>All apps' ground while a game runs.</summary>
    public static IBrush CompanionDrawerGround { get; private set; }

    /// <summary>The badge grid's ground.</summary>
    public static IBrush CompanionLayerGround { get; private set; }

    /// <summary>A hint over a photo.</summary>
    public static IBrush CompanionOnPhoto { get; private set; }

    /// <summary>The photo viewer's ground.</summary>
    public static IBrush CompanionViewerGround { get; private set; }

    /// <summary>The photo viewer's status and caption.</summary>
    public static IBrush CompanionViewerInk { get; private set; }

    /// <summary>The photo viewer's loading bar track.</summary>
    public static IBrush CompanionViewerTrack { get; private set; }

    /// <summary>The companion at rest.</summary>
    public static IBrush CompanionRestGround { get; private set; }

    /// <summary>A tab at rest.</summary>
    public static IBrush ClassicCompanionTabFill { get; private set; }

    /// <summary>A round header button (Refresh, Close).</summary>
    public static IBrush ClassicCompanionChipFill { get; private set; }

    /// <summary>The Now playing pill.</summary>
    public static IBrush ClassicCompanionLiveFill { get; private set; }

    /// <summary>The big panel's fill.</summary>
    public static IBrush ClassicCompanionPanelFill { get; private set; }

    /// <summary>The big panel's edge.</summary>
    public static IBrush ClassicCompanionPanelEdge { get; private set; }

    /// <summary>The companion dock's pill.</summary>
    public static IBrush ClassicCompanionDockFill { get; private set; }

    /// <summary>The companion dock's pill edge.</summary>
    public static IBrush ClassicCompanionDockEdge { get; private set; }

    /// <summary>The pill behind the panel's corner hint.</summary>
    public static IBrush ClassicCompanionHintPill { get; private set; }

    /// <summary>A status floating over the badge grid.</summary>
    public static IBrush ClassicCompanionStatusFill { get; private set; }

    /// <summary>That status's edge.</summary>
    public static IBrush ClassicCompanionStatusEdge { get; private set; }

    /// <summary>The shade over a video's still.</summary>
    public static IBrush ClassicCompanionVideoShade { get; private set; }

    /// <summary>A play button over a photo or in the viewer.</summary>
    public static IBrush CompanionPlayFill { get; private set; }

    /// <summary>A play button's edge.</summary>
    public static IBrush CompanionPlayEdge { get; private set; }

    /// <summary>A photo's page dot.</summary>
    public static IBrush CompanionPhotoDotFill { get; private set; }

    /// <summary>The current photo's page dot.</summary>
    public static IBrush CompanionPhotoDotCurrentFill { get; private set; }

    /// <summary>The viewer's close button and hint glyphs.</summary>
    public static IBrush CompanionViewerButtonFill { get; private set; }

    /// <summary>A shade over a photo: faded out.</summary>
    public static Color ClassicCompanionCaptionClear { get; private set; }

    /// <summary>The caption's shade at the foot of a photo.</summary>
    public static Color ClassicCompanionCaptionShade { get; private set; }

    /// <summary>The viewer's shade under its title.</summary>
    public static Color ClassicCompanionViewerTopShade { get; private set; }

    /// <summary>The viewer's shade under its caption.</summary>
    public static Color ClassicCompanionViewerFootShade { get; private set; }

    /// <summary>Now playing's cover shadow.</summary>
    public static BoxShadows ClassicCompanionCoverShadow { get; private set; }

    /// <summary>A tab.</summary>
    public static CornerRadius CompanionTabRadius { get; private set; }

    /// <summary>A small tab (a filter).</summary>
    public static CornerRadius CompanionTabSmallRadius { get; private set; }

    /// <summary>A round header button.</summary>
    public static CornerRadius CompanionChipRadius { get; private set; }

    /// <summary>A button hint's round glyph on the companion.</summary>
    public static CornerRadius CompanionHintGlyphRadius { get; private set; }

    /// <summary>A badge's face.</summary>
    public static CornerRadius CompanionBadgeRadius { get; private set; }

    /// <summary>Now playing's cover.</summary>
    public static CornerRadius CompanionCoverRadius { get; private set; }

    /// <summary>The Now playing pill.</summary>
    public static CornerRadius CompanionLiveRadius { get; private set; }

    /// <summary>The big panel.</summary>
    public static CornerRadius CompanionPanelRadius { get; private set; }

    /// <summary>A recent unlock's badge.</summary>
    public static CornerRadius CompanionUnlockRadius { get; private set; }

    /// <summary>The play button over a photo.</summary>
    public static CornerRadius CompanionPlayRadius { get; private set; }

    /// <summary>A photo's page dot.</summary>
    public static CornerRadius CompanionPhotoDotRadius { get; private set; }

    /// <summary>The panel's corner hint and the badge grid's status.</summary>
    public static CornerRadius CompanionHintPillRadius { get; private set; }

    /// <summary>The companion dock's pill.</summary>
    public static CornerRadius ClassicCompanionDockPillRadius { get; private set; }

    /// <summary>The rule between All apps and the pinned apps.</summary>
    public static CornerRadius CompanionDividerRadius { get; private set; }

    /// <summary>An app's face in All apps.</summary>
    public static CornerRadius CompanionAppRadius { get; private set; }

    /// <summary>The cover at rest.</summary>
    public static CornerRadius CompanionRestCoverRadius { get; private set; }

    /// <summary>The viewer's play button.</summary>
    public static CornerRadius CompanionViewerPlayRadius { get; private set; }

    /// <summary>The viewer's close button.</summary>
    public static CornerRadius CompanionViewerCloseRadius { get; private set; }

    /// <summary>A hint glyph in the viewer.</summary>
    public static CornerRadius CompanionViewerGlyphRadius { get; private set; }

    /// <summary>A tab's label.</summary>
    public static double CompanionTab { get; private set; }

    /// <summary>A small tab's label.</summary>
    public static double CompanionTabSmall { get; private set; }

    /// <summary>A round header button's label.</summary>
    public static double CompanionChip { get; private set; }

    /// <summary>A button hint's label, LB / RB, a notice.</summary>
    public static double CompanionHint { get; private set; }

    /// <summary>The running game's title (now playing, and at rest).</summary>
    public static double CompanionTitle { get; private set; }

    /// <summary>Now playing's platform and play time.</summary>
    public static double CompanionLine { get; private set; }

    /// <summary>The Now playing label.</summary>
    public static double CompanionLiveLabel { get; private set; }

    /// <summary>A layer's heading (Achievements, All apps).</summary>
    public static double CompanionHeading { get; private set; }

    /// <summary>The line beside a layer's heading.</summary>
    public static double CompanionSummary { get; private set; }

    /// <summary>The progress ring's percentage.</summary>
    public static double CompanionFigure { get; private set; }

    /// <summary>The unlocked count.</summary>
    public static double CompanionStat { get; private set; }

    /// <summary>The points line.</summary>
    public static double CompanionStatLabel { get; private set; }

    /// <summary>Recently unlocked, and its empty line.</summary>
    public static double CompanionSection { get; private set; }

    /// <summary>A recent unlock's title.</summary>
    public static double CompanionItemTitle { get; private set; }

    /// <summary>When an unlock was earned, the selected badge's points.</summary>
    public static double CompanionItemMeta { get; private set; }

    /// <summary>A panel's message (no set, loading, not linked).</summary>
    public static double CompanionMessage { get; private set; }

    /// <summary>A panel's message: its line height.</summary>
    public static double CompanionMessageLineHeight { get; private set; }

    /// <summary>A photo's caption.</summary>
    public static double CompanionCaption { get; private set; }

    /// <summary>An empty panel's or filter's heading.</summary>
    public static double CompanionEmptyTitle { get; private set; }

    /// <summary>An empty panel's or filter's line.</summary>
    public static double CompanionEmptyText { get; private set; }

    /// <summary>A status over the badge grid.</summary>
    public static double CompanionStatus { get; private set; }

    /// <summary>The selected badge's title.</summary>
    public static double CompanionDetailTitle { get; private set; }

    /// <summary>The selected badge's description.</summary>
    public static double CompanionDetailText { get; private set; }

    /// <summary>The selected badge's description: its line height.</summary>
    public static double CompanionDetailLineHeight { get; private set; }

    /// <summary>An app's name in All apps.</summary>
    public static double CompanionAppLabel { get; private set; }

    /// <summary>How to wake the companion.</summary>
    public static double CompanionRestHint { get; private set; }

    /// <summary>The viewer's title.</summary>
    public static double CompanionViewerTitle { get; private set; }

    /// <summary>The viewer's status.</summary>
    public static double CompanionViewerStatus { get; private set; }

    /// <summary>The viewer's caption and hint labels.</summary>
    public static double CompanionViewerCaption { get; private set; }

    /// <summary>The letter on a viewer hint's glyph.</summary>
    public static double CompanionViewerGlyphLetter { get; private set; }

    /// <summary>A photo's page dot: its height, and its width unless current.</summary>
    public static double CompanionPhotoDot { get; private set; }

    /// <summary>The current photo's page dot.</summary>
    public static double CompanionPhotoDotCurrentWidth { get; private set; }

    /// <summary>The companion's labels, lines and hints.</summary>
    public static FontWeight CompanionWeight { get; private set; }

    /// <summary>The companion's titles, headings and figures.</summary>
    public static FontWeight CompanionStrongWeight { get; private set; }

    /// <summary>Today's look: every token's value as Handheld mode has drawn it since M45.</summary>
    // ===== The face =====
    // Today's look sets no typeface and takes the system's (and keeps taking it: Face is null and nothing is applied). Big
    // Type sets M PLUS Rounded 1c on every Handheld view's root, where every text inherits it, with three weights
    // (tokens.js face: WeightText 500, WeightStrong 700, WeightDisplay 900). Each role's weight token above takes one of them.

    /// <summary>
    /// Where the Handheld face is: App.axaml's <c>EmuHandheldFontFamily</c> (M47 R3). The name after <c>#</c> is the one inside
    /// the files; Google Fonts' display name does not resolve. A test holds the two declarations equal.
    /// </summary>
    public const string FaceSource = "avares://EmuShelf.UI/Assets/Fonts/MPLUSRounded1c#Rounded Mplus 1c";

    /// <summary>The Handheld typeface, or null to set none (today's look: the system's face, as before M47).</summary>
    public static FontFamily? Face { get; private set; }

    /// <summary>Text with no role of its own (tokens.js <c>face.WeightText</c>); what a view's root sets with <see cref="Face"/>.</summary>
    public static FontWeight WeightText { get; private set; }

    /// <summary>Labels, lines, rows and hints (tokens.js <c>face.WeightStrong</c>).</summary>
    public static FontWeight WeightStrong { get; private set; }

    /// <summary>Names, headings, monograms and figures (tokens.js <c>face.WeightDisplay</c>).</summary>
    public static FontWeight WeightDisplay { get; private set; }

    /// <summary>
    /// Sets the look's face on a Handheld view's root, so every text under it inherits it: <see cref="Face"/> at
    /// <see cref="WeightText"/>. In today's look there is no face and the root is left exactly as it was.
    /// </summary>
    public static void ApplyFace(TemplatedControl root)
    {
        if (Face is not { } face)
            return;
        root.FontFamily = face;
        root.FontWeight = WeightText;
    }

    private static void UseClassicValues()
    {
        // The face: none (the system's), and the weights only Big Type's roots use.
        Face = null;
        WeightText = FontWeight.Medium;
        WeightStrong = FontWeight.Bold;
        WeightDisplay = FontWeight.Black;

        // Launcher: ground and type
        ClassicGroundTop = Color.Parse("#FAFBFD");
        ClassicGroundBottom = Color.Parse("#E4E8EF");
        OnGround = Brush("#1C2033");
        OnGroundMuted = Brush("#5B6176");
        OnGroundDisabled = Brush("#8A90A3");
        MenuRowInk = Brush("#1E2235");
        OnGroundSoft = Brush("#4B5274");
        Glyph = Brush("#4B5274");
        Chevron = Brush("#8A90A3");
        OnAccent = Brush("White");
        OnAccentMuted = Brush("#D9FFFFFF");
        Scrim = Brush("#521C2033");
        Warning = Brush("#B4531A");
        Danger = Brush("#C23B3B");
        Trophy = Brush("#C98A12");
        TrophyBright = Brush("#F2AE2E");
        ProgressTrack = Brush("#29465088");
        MissingTileOpacity = 0.4;
        CoverPlaceholder = Brush("#D5D9E4");
        ThumbPlaceholder = Brush("#E4E8EF");
        PageDot = Brush("#38465088");
        SwitchTrack = Brush("#D5D9E4");
        SwitchKnobFill = Brush("#FFFFFF");
        HintGlyphFill = Brush("#4B5274");
        HintGlyphInk = Brush("White");
        OnFolder = Brush("White");

        // Launcher: today's chrome
        ClassicAccent = Brush("#5B58D9");
        ClassicAccentInk = Brush("#3F3CB8");
        ClassicAccentTint = Brush("#E9E8FB");
        ClassicAccentOutline = Brush("#805B58D9");
        ClassicAccentHairline = Brush("#335B58D9");
        ClassicRowDetailOnAccent = Brush("#E4E3FA");
        ClassicSurface = Brush("#FFFFFF");
        ClassicSheetSurface = Brush("#F5F6F9");
        ClassicFieldFill = Brush("#F2F4F8");
        ClassicFieldEdge = Brush("#29465088");
        ClassicIconPlate = Brush("#EEF0F5");
        ClassicHairline = Brush("#141C2033");
        ClassicEdge = Brush("White");
        ClassicEdgeHintFill = Brush("#B3FFFFFF");
        ClassicDockFill = Brush("#C7FFFFFF");
        ClassicEmptySlotEdge = Brush("#29465088");
        ClassicEmptySlotFill = Brush("#40FFFFFF");
        ClassicLandingFill = Brush("#265B58D9");
        ClassicDropFill = Brush("#335B58D9");
        ClassicAddGamesEdge = Brush("#995B58D9");
        ClassicAddGamesFill = Brush("#D9FFFFFF");
        ClassicSwitchTrackFocused = Brush("#7F7CE6");
        ClassicSwitchOnFocused = Brush("#FFFFFF");
        ClassicStartGlyphFill = Brush("#33FFFFFF");
        ClassicSettingsFaceTop = Color.Parse("#6865DE");
        ClassicSettingsFaceBottom = Color.Parse("#5450CF");
        ClassicActivityFaceTop = Color.Parse("#33AE9F");
        ClassicActivityFaceBottom = Color.Parse("#26918A");
        ClassicWashOpaque = Color.Parse("#FF000000");
        ClassicWashClear = Color.Parse("#00000000");
        ClassicShadow = Color.Parse("#24000000");
        ClassicDropShadow = Color.Parse("#40000000");
        ClassicLiftShadow = Color.Parse("#33000000");
        ClassicDockShadow = Color.Parse("#14000000");
        ClassicRingGlow = Color.Parse("#B05B58D9");
        ClassicRibbonShadow = BoxShadows.Parse("0 3 8 0 #405B58D9");
        ClassicKeyShadow = BoxShadows.Parse("0 2 6 0 #12000000");
        ClassicRaisedShadow = BoxShadows.Parse("0 2 8 0 #0F000000");
        ClassicButtonShadow = BoxShadows.Parse("0 4 14 0 #14000000");
        ClassicAccentShadow = BoxShadows.Parse("0 6 18 0 #455B58D9");
        ClassicCardShadow = BoxShadows.Parse("0 6 20 0 #14000000");
        ClassicCoverShadow = BoxShadows.Parse("0 16 40 0 #2E000000");
        ClassicSheetShadow = BoxShadows.Parse("0 20 48 0 #33000000");
        ClassicBottomSheetShadow = BoxShadows.Parse("0 24 56 0 #40000000");
        ClassicThumbShadow = BoxShadows.Parse("0 4 12 0 #26000000");
        ClassicSwatchShadow = BoxShadows.Parse("0 4 12 0 #1F000000");
        ClassicStartShadow = BoxShadows.Parse("0 10 24 0 #405B58D9");
        ClassicChipFocusRing = BoxShadows.Parse("0 0 0 6 #5B58D9");
        ClassicHomeTileShadowRadius = 40;
        ClassicHomeTileShadowBlur = 16;
        ClassicCarriedShadowRadius = 28;
        ClassicCarriedShadowBlur = 14;
        ClassicFolderTileShadowRadius = 56;
        ClassicFolderTileShadowBlur = 20;
        ClassicLiftShadowBlur = 32;
        ClassicDockShadowRadius = 40;
        ClassicDockShadowBlur = 20;
        ClassicRingGlowBlur = 28;

        // The selector
        Ring = Brush("#5B58D9");
        ClassicRingKeyline = Brush("White");
        RingStroke = 14;
        ClassicRingKeylineWidth = 4;
        ClassicRingAnchorInset = 16;
        RingRadiusTile = new CornerRadius(68);
        RingRadiusTileFolder = new CornerRadius(80);
        RingRadiusDock = new CornerRadius(40);
        RingRadiusChip = new CornerRadius(44);
        RingRadiusButton = new CornerRadius(64);
        RingRadiusCard = new CornerRadius(60);
        RingRadiusRow = new CornerRadius(56);

        // Launcher: shape and size
        TileHome = 176;
        TileFolder = 232;
        FolderPitch = 264;
        FolderRowPitch = 256;
        FolderTrackHeight = 768;
        DockTile = 80;
        DockGap = 32;
        Key = 100;
        PageDotWidth = 18;
        PageDotHeight = 16;
        PageDotGap = 12;
        PageIndicatorWidth = 52;
        HintGlyph = 36;
        SwitchWidth = 88;
        SwitchHeight = 48;
        SwitchKnob = 36;
        ProgressHeight = 12;
        LaunchProgressHeight = 8;
        TileRadius = new CornerRadius(40);
        TileRadiusFolder = new CornerRadius(56);
        SlotRadius = new CornerRadius(44);
        CarriedTileRadius = new CornerRadius(28);
        DockRadius = new CornerRadius(24);
        KeyRadius = new CornerRadius(24);
        PageDotRadius = new CornerRadius(8);
        FolderDotRadius = new CornerRadius(10);
        HintGlyphRadius = new CornerRadius(18);
        SheetRowRadius = new CornerRadius(20);
        MenuRowRadius = new CornerRadius(16);
        RowRadius = new CornerRadius(24);
        RailRowRadius = new CornerRadius(24);
        FieldRadius = new CornerRadius(28);
        ColourChipRadius = new CornerRadius(44);
        SwitchRadius = new CornerRadius(24);
        SwitchKnobRadius = new CornerRadius(18);
        ScreenCardRadius = new CornerRadius(40);
        TitleCoverRadius = new CornerRadius(32);
        ProgressRadius = new CornerRadius(6);
        LaunchProgressRadius = new CornerRadius(4);
        CheckboxRadius = new CornerRadius(12);
        SwatchRadius = new CornerRadius(26);
        StartButtonRadius = new CornerRadius(24);
        StartGlyphRadius = new CornerRadius(26);
        IconPlateRadius = new CornerRadius(14);
        ThumbRadius = new CornerRadius(16);
        FilterRadius = new CornerRadius(28);
        ClassicRibbonRadius = new CornerRadius(16);
        ClassicEdgeHintRadius = new CornerRadius(20);
        ClassicChipRadius = new CornerRadius(28);
        ClassicButtonRadius = new CornerRadius(44);
        ClassicRememberRadius = new CornerRadius(36);
        ClassicMergeBadgeRadius = new CornerRadius(26);
        ClassicDockPillRadius = new CornerRadius(40);
        ClassicSheetRadius = new CornerRadius(40);
        ClassicChooserRadius = new CornerRadius(48);
        ClassicBottomSheetRadius = new CornerRadius(48);

        // Launcher: type
        Heading = 34;
        HeadingWeight = FontWeight.SemiBold;
        FolderHeading = 32;
        HeadingCount = 24;
        Note = 24;
        SheetTitle = 32;
        BottomSheetTitle = 36;
        ChooserTitle = 36;
        ChooserSubtitle = 26;
        ScreenCardTitle = 32;
        Row = 28;
        RowWeight = FontWeight.SemiBold;
        RowValue = 24;
        RowDetail = 22;
        RailRow = 28;
        RailRowStatus = 21;
        SheetRow = 30;
        MenuRow = 28;
        MenuRowNote = 24;
        MenuRowState = 26;
        Chip = 26;
        Facts = 26;
        Crumb = 26;
        TitleName = 48;
        TitlePlatform = 28;
        TitleButton = 32;
        TitleButtonNote = 24;
        LaunchHeading = 40;
        LaunchDetail = 26;
        PageLabel = 28;
        TileTitleHome = 20;
        TileTitleFolder = 24;
        TileTitleCarried = 16;
        TileTitleCover = 32;
        TileTitleWeight = FontWeight.SemiBold;
        AddGames = 22;
        Ribbon = 20;
        RibbonWeight = FontWeight.Bold;
        SwatchMonogram = 30;
        SwatchMonogramWeight = FontWeight.Bold;
        KeyLabel = 36;
        KeyWide = 30;
        KeyMid = 28;
        KeySpace = 28;
        NamingField = 40;
        KeyboardField = 36;
        FieldLabel = 26;
        Hint = 24;
        HintGlyphLetter = 20;
        HintGlyphWeight = FontWeight.SemiBold;
        KeyboardHintGlyphWeight = FontWeight.Bold;
        StartLabel = 28;
        StartDetail = 20;
        Filter = 24;
        PanelStatus = 26;
        PanelEmpty = 30;
        Monogram = 112;
        MonogramWeight = FontWeight.SemiBold;

        // Folders
        FolderBackRadius = new CornerRadius(76);
        FolderFlapRadius = new CornerRadius(72);
        PeekCoverRadius = new CornerRadius(14);
        ClassicFolderBackShade = Brush("#47141830");
        ClassicFlapGlossTop = Color.Parse("#5CFFFFFF");
        ClassicFlapGlossClear = Color.Parse("#00FFFFFF");
        ClassicFlapEdge = Brush("#73FFFFFF");
        ClassicFolderShadow = Color.Parse("#33000000");
        ClassicFolderShadowRadius = 72;
        ClassicFolderShadowBlur = 28;

        // Stage
        ClassicStageGroundTop = Color.Parse("#F5F6F9");
        ClassicStageGroundBottom = Color.Parse("#E1E5EC");
        StageInk = Brush("#1C2033");
        StageInkMuted = Brush("#5B6176");
        StageInkSoft = Brush("#4B5274");
        StageGlyph = Brush("#4B5274");
        OnHero = Brush("White");
        OnHeroMuted = Brush("#D9FFFFFF");
        OnHeroSoft = Brush("#E6FFFFFF");
        ClassicHeroVeilLeft = Color.Parse("#9E080A14");
        ClassicHeroVeilMid = Color.Parse("#2E080A14");
        ClassicHeroVeilClear = Color.Parse("#00080A14");
        ClassicHeroVeilFoot = Color.Parse("#8C080A14");
        ClassicHeroTextShadow = Color.Parse("#8C000000");
        ClassicHeroTextShadowBlur = 26;
        ClassicPillFill = Brush("#E6FFFFFF");
        ClassicPillShadow = BoxShadows.Parse("0 4 16 0 #14000000");
        ClassicPillRadius = new CornerRadius(32);
        ClassicToastFill = Brush("#F2FFFFFF");
        ClassicToastErrorFill = Brush("#FFFFF4EC");
        ClassicToastErrorEdge = Brush("#59B4531A");
        ClassicPlateFill = Brush("#F2FFFFFF");
        ClassicPlateShadow = BoxShadows.Parse("0 6 24 0 #14000000");
        ClassicPlateRadius = new CornerRadius(32);
        ClassicStageArtShadow = BoxShadows.Parse("0 28 64 0 #33000000");
        ClassicStagePlaceholderShadow = BoxShadows.Parse("0 28 64 0 #29000000");
        ClassicStageAppShadow = BoxShadows.Parse("0 24 56 0 #2E000000");
        ClassicStageSettingsShadow = BoxShadows.Parse("0 24 56 0 #405B58D9");
        ClassicStageActivityShadow = BoxShadows.Parse("0 24 56 0 #4026918A");
        ClassicStageCardShadow = BoxShadows.Parse("0 24 56 0 #1F000000");
        ClassicStageGlyphCardShadow = BoxShadows.Parse("0 16 40 0 #1F000000");
        ClassicFanCoverShadow = BoxShadows.Parse("0 12 28 0 #2E000000");
        ClassicCardInsetFill = Brush("#F2F4F8");
        ClassicPictureFill = Brush("#EEF0F5");
        CardProgressTrack = Brush("#E4E8EF");
        ClassicLaunchCardEdge = Brush("#D9FFFFFF");
        ClassicLaunchCardEdgeWidth = 8;
        StageArtRadius = new CornerRadius(24);
        StageAppRadius = new CornerRadius(96);
        StageCardRadius = new CornerRadius(56);
        StageCardGlyphRadius = new CornerRadius(16);
        CardInsetRadius = new CornerRadius(20);
        PanelPictureRadius = new CornerRadius(28);
        FanCoverRadius = new CornerRadius(20);
        CardProgressRadius = new CornerRadius(8);
        LaunchCardRadius = new CornerRadius(28);
        StageHintGlyphRadius = new CornerRadius(20);
        StageHintGlyph = 40;
        LaunchCardWidth = 448;
        LaunchCardHeight = 632;
        StageCorner = 26;
        StageCornerWeight = FontWeight.SemiBold;
        StageCornerLineHeight = 34;
        DisplayName = 52;
        DisplayNameWeight = FontWeight.SemiBold;
        Line = 28;
        HeroTitle = 96;
        HeroLogoTitle = 40;
        HeroEyebrow = 30;
        HeroLine = 32;
        HeroWeight = FontWeight.SemiBold;
        StageCoverTitle = 40;
        CardEyebrow = 30;
        CardTitle = 64;
        PanelCardTitle = 60;
        CardValue = 44;
        CardDescription = 32;
        CardDescriptionLineHeight = 44;
        CardStatus = 26;
        CardWeight = FontWeight.SemiBold;

        // Companion
        ClassicCompanionGroundTop = Color.Parse("#2B3050");
        ClassicCompanionGroundMid = Color.Parse("#1B1E30");
        ClassicCompanionGroundBottom = Color.Parse("#13151F");
        CompanionRing = Brush("#7C86FF");
        ClassicCompanionRingGlow = BoxShadows.Parse("0 0 44 0 #8C7C86FF");
        ClassicCompanionRingGlowLight = BoxShadows.Parse("0 0 32 0 #665B58D9");
        CompanionRingStroke = 9;
        ClassicCompanionRingOuterKeyline = 4;
        ClassicCompanionRingInnerKeyline = 5;
        ClassicCompanionRingInset = 13;
        CompanionRingRadiusPanel = new CornerRadius(54);
        CompanionRingRadiusDock = new CornerRadius(42);
        CompanionRingRadiusBadge = new CornerRadius(46);
        CompanionRingRadiusApp = new CornerRadius(48);
        CompanionInk = Brush("#EEF0FA");
        CompanionInkMuted = Brush("#A9AFCB");
        CompanionInkQuiet = Brush("#8A90AE");
        CompanionInkSoft = Brush("#C9CDE2");
        CompanionTabOn = Brush("#EEF0FA");
        CompanionTabOnInk = Brush("#1B1E30");
        CompanionTileRest = Brush("#24FFFFFF");
        CompanionGlyph = Brush("White");
        CompanionDockGlyph = Brush("#DDE1F2");
        CompanionEmptySlotEdge = Brush("#4DAAB0D2");
        CompanionDivider = Brush("#1FFFFFFF");
        CompanionRule = Brush("#14FFFFFF");
        CompanionHintGlyphFill = Brush("#33FFFFFF");
        CompanionProgressTrack = Brush("#1FFFFFFF");
        AchievementGold = Brush("#F0B03A");
        CompanionLive = Brush("#6FD691");
        CompanionLiveInk = Brush("#9FE3B4");
        CompanionDrawerGround = Brush("#181B2A");
        CompanionLayerGround = Brush("#1B1E30");
        CompanionOnPhoto = Brush("#E3E6F5");
        CompanionViewerGround = Brush("#000000");
        CompanionViewerInk = Brush("#B9C0CA");
        CompanionViewerTrack = Brush("#26303D");
        CompanionRestGround = Brush("#06070B");
        ClassicCompanionTabFill = Brush("#14FFFFFF");
        ClassicCompanionChipFill = Brush("#1AFFFFFF");
        ClassicCompanionLiveFill = Brush("#1F6FD691");
        ClassicCompanionPanelFill = Brush("#12FFFFFF");
        ClassicCompanionPanelEdge = Brush("#12FFFFFF");
        ClassicCompanionDockFill = Brush("#14FFFFFF");
        ClassicCompanionDockEdge = Brush("#0FFFFFFF");
        ClassicCompanionHintPill = Brush("#59000000");
        ClassicCompanionStatusFill = Brush("#F2262A40");
        ClassicCompanionStatusEdge = Brush("#26FFFFFF");
        ClassicCompanionVideoShade = Brush("#66000000");
        CompanionPlayFill = Brush("#8C000000");
        CompanionPlayEdge = Brush("#99FFFFFF");
        CompanionPhotoDotFill = Brush("#4DFFFFFF");
        CompanionPhotoDotCurrentFill = Brush("White");
        CompanionViewerButtonFill = Brush("#33FFFFFF");
        ClassicCompanionCaptionClear = Color.Parse("#00000000");
        ClassicCompanionCaptionShade = Color.Parse("#99000000");
        ClassicCompanionViewerTopShade = Color.Parse("#C0000000");
        ClassicCompanionViewerFootShade = Color.Parse("#D9000000");
        ClassicCompanionCoverShadow = BoxShadows.Parse("0 12 28 0 #73000000");
        CompanionTabRadius = new CornerRadius(40);
        CompanionTabSmallRadius = new CornerRadius(32);
        CompanionChipRadius = new CornerRadius(36);
        CompanionHintGlyphRadius = new CornerRadius(20);
        CompanionBadgeRadius = new CornerRadius(28);
        CompanionCoverRadius = new CornerRadius(16);
        CompanionLiveRadius = new CornerRadius(16);
        CompanionPanelRadius = new CornerRadius(36);
        CompanionUnlockRadius = new CornerRadius(20);
        CompanionPlayRadius = new CornerRadius(66);
        CompanionPhotoDotRadius = new CornerRadius(7);
        CompanionHintPillRadius = new CornerRadius(28);
        ClassicCompanionDockPillRadius = new CornerRadius(40);
        CompanionDividerRadius = new CornerRadius(2);
        CompanionAppRadius = new CornerRadius(30);
        CompanionRestCoverRadius = new CornerRadius(18);
        CompanionViewerPlayRadius = new CornerRadius(32);
        CompanionViewerCloseRadius = new CornerRadius(16);
        CompanionViewerGlyphRadius = new CornerRadius(9);
        CompanionTab = 32;
        CompanionTabSmall = 28;
        CompanionChip = 26;
        CompanionHint = 24;
        CompanionTitle = 40;
        CompanionLine = 26;
        CompanionLiveLabel = 24;
        CompanionHeading = 44;
        CompanionSummary = 28;
        CompanionFigure = 56;
        CompanionStat = 36;
        CompanionStatLabel = 24;
        CompanionSection = 28;
        CompanionItemTitle = 32;
        CompanionItemMeta = 24;
        CompanionMessage = 32;
        CompanionMessageLineHeight = 44;
        CompanionCaption = 26;
        CompanionEmptyTitle = 36;
        CompanionEmptyText = 26;
        CompanionStatus = 24;
        CompanionDetailTitle = 34;
        CompanionDetailText = 26;
        CompanionDetailLineHeight = 34;
        CompanionAppLabel = 26;
        CompanionRestHint = 28;
        CompanionViewerTitle = 15;
        CompanionViewerStatus = 13;
        CompanionViewerCaption = 11;
        CompanionViewerGlyphLetter = 10;
        CompanionPhotoDot = 14;
        CompanionPhotoDotCurrentWidth = 40;
        CompanionWeight = FontWeight.SemiBold;
        CompanionStrongWeight = FontWeight.Bold;

        UseClassicLauncherValues();
        UseStageValues();
        UseListValues();
    }

    /// <summary>
    /// The Big Type values that differ from today's (M47 B2 onward), assigned after <see cref="UseClassicValues"/>.
    /// B2: the face and its weights (a console's colour comes from <see cref="ConsoleColour(HandheldPlatform?)"/>, which reads
    /// <see cref="Look"/>). Layouts, sizes and the other colours keep today's values until B3 and B4 give them theirs.
    /// </summary>
    private static void UseBigTypeValues()
    {
        // The face (tokens.js face), and each role's weight from tokens.js type.
        Face = new FontFamily(FaceSource);
        HeadingWeight = WeightDisplay;          // Heading, SheetTitle
        RowWeight = WeightStrong;               // Row, RowValue, RailRow, Key
        TileTitleWeight = WeightStrong;
        RibbonWeight = WeightStrong;
        SwatchMonogramWeight = WeightDisplay;   // Monogram
        HintGlyphWeight = WeightStrong;         // Hint
        KeyboardHintGlyphWeight = WeightStrong;
        MonogramWeight = WeightDisplay;         // Monogram
        StageCornerWeight = WeightStrong;       // Label, Line, Facts, Battery
        DisplayNameWeight = WeightDisplay;      // DisplayName
        HeroWeight = WeightStrong;
        CardWeight = WeightStrong;
        CompanionWeight = WeightStrong;         // InGameLabel
        CompanionStrongWeight = WeightDisplay;  // SessionTimer, InGameFigure, InGameStat

        // B3: the Launcher at rest.
        UseBigTypeLauncherValues();

        // B7: the lists.
        UseBigTypeListValues();
    }
}
