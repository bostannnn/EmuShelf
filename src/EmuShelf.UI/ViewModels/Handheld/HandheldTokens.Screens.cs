// M47 B15: the four screens the owner picked from proposals-screens.html on 16 November 2026 (decision "M47 B15" in DECISIONS.md).

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The tokens B15 added for the Big Type look: tokens.js <c>screens</c> (proposals-screens.html, picked 2026-11-16), All apps as the
/// Shelf, every achievement as the Wall, the photo viewer as the Reel and the idle artwork as the Porthole. Today's look never draws
/// them, so each holds one value in either look. Their motion is <see cref="EmuShelf.Rendering.Stage.BigType.BigTypeScreensMotion"/>.
/// </summary>
public static partial class HandheldTokens
{
    // ===== 2 Shelf (All apps), on the Launcher's 1240 x 1080 =====

    public static double ShelfKickerLeft { get; } = 108;
    public static double ShelfKickerTop { get; } = 32;
    public static double ShelfNameLeft { get; } = 104;
    public static double ShelfNameTop { get; } = 68;
    public static double ShelfName { get; } = 92;
    public static double ShelfNameMaxWidth { get; } = 760;
    public static double ShelfCountRight { get; } = 108;
    public static double ShelfCountTop { get; } = 88;
    public static double ShelfCount { get; } = 68;
    public static double ShelfCountOpacity { get; } = 0.45;
    public static double ShelfTop { get; } = 172;
    public static double ShelfHeight { get; } = 816;
    public static double ShelfLeft { get; } = 116;
    public static double ShelfFirstTop { get; } = 28;
    public static double ShelfPitchX { get; } = 212;
    public static double ShelfPitchY { get; } = 196;
    public static double ShelfIcon { get; } = 160;
    public static double ShelfIconRadiusShare { get; } = 0.24;
    public static int ShelfColumns { get; } = 5;
    public static int ShelfRows { get; } = 4;
    public static int ShelfWideColumns { get; } = 8;
    public static double ShelfFocusScale { get; } = 1.12;
    public static double ShelfPush { get; } = 8;
    public static double ShelfRingOutset { get; } = 24;
    public static double ShelfRingRadius { get; } = 56;

    public static Thickness ShelfKickerMargin => new(ShelfKickerLeft, ShelfKickerTop, 0, 0);
    public static Thickness ShelfNameMargin => new(ShelfNameLeft, ShelfNameTop, 0, 0);
    public static Thickness ShelfCountMargin => new(0, ShelfCountTop, ShelfCountRight, 0);
    public static Thickness ShelfClipMargin => new(0, ShelfTop, 0, 0);
    public static CornerRadius ShelfIconCorner => new(ShelfIcon * ShelfIconRadiusShare);

    // ===== 4 Wall (every achievement) =====

    public static double WallKickerLeft { get; } = 80;
    public static double WallKickerTop { get; } = 36;
    public static double WallFigureLeft { get; } = 76;
    public static double WallFigureTop { get; } = 72;
    public static double WallFigure { get; } = 100;
    public static double WallFiltersRight { get; } = 80;
    public static double WallFiltersTop { get; } = 116;
    public static double WallFilter { get; } = 34;
    public static double WallFilterGap { get; } = 28;
    public static double WallFilterCountGap { get; } = 10;
    public static double WallFilterRestOpacity { get; } = 0.45;
    public static double WallFilterCountOpacity { get; } = 0.8;
    public static double WallFilterKey { get; } = 22;
    public static double WallFilterKeyStroke { get; } = 4;
    public static double WallFilterKeyRadius { get; } = 14;
    public static double WallFilterKeyPadX { get; } = 10;
    public static double WallFilterKeyPadY { get; } = 6;
    public static double WallFilterKeyOpacity { get; } = 0.6;
    public static double WallTop { get; } = 196;
    public static double WallHeight { get; } = 504;
    public static double WallLeft { get; } = 74;
    public static double WallFirstTop { get; } = 28;
    public static double WallPitch { get; } = 160;
    public static double WallBadge { get; } = 128;
    public static double WallBadgeRadiusShare { get; } = 0.14;
    public static int WallColumns { get; } = 7;
    public static int WallRows { get; } = 3;
    public static int WallWideColumns { get; } = 11;
    public static double WallFocusScale { get; } = 1.14;
    public static double WallRingOutset { get; } = 20;
    public static double WallRingRadius { get; } = 40;
    public static double WallLockedBrightness { get; } = 0.62;
    public static double WallHardcoreEdge { get; } = 6;
    public static double WallHiddenFill { get; } = 0.16;
    public static double WallHiddenMark { get; } = 70;
    public static double WallNameLeft { get; } = 76;
    public static double WallNameTop { get; } = 700;
    public static double WallName { get; } = 80;
    public static double WallNameMaxWidth { get; } = 1080;
    public static double WallDescLeft { get; } = 80;
    public static double WallDescTop { get; } = 820;
    public static double WallDesc { get; } = 36;
    public static double WallDescOpacity { get; } = 0.9;
    public static double WallMetaTop { get; } = 876;
    public static double WallMeta { get; } = 32;
    public static double WallMetaOpacity { get; } = 0.6;

    public static Thickness WallKickerMargin => new(WallKickerLeft, WallKickerTop, 0, 0);
    public static Thickness WallFigureMargin => new(WallFigureLeft, WallFigureTop, 0, 0);
    public static Thickness WallFiltersMargin => new(0, WallFiltersTop, WallFiltersRight, 0);
    public static Thickness WallFilterKeyPadding => new(WallFilterKeyPadX, WallFilterKeyPadY);
    public static Thickness WallFilterCountMargin => new(WallFilterCountGap, 0, 0, 0);
    public static Thickness WallClipMargin => new(0, WallTop, 0, 0);
    public static Thickness WallNameMargin => new(WallNameLeft, WallNameTop, 0, 0);
    public static Thickness WallDescMargin => new(WallDescLeft, WallDescTop, 0, 0);
    public static Thickness WallMetaMargin => new(WallDescLeft, WallMetaTop, 0, 0);
    public static CornerRadius WallBadgeCorner => new(WallBadge * WallBadgeRadiusShare);

    /// <summary>A hidden achievement's ground: white at <see cref="WallHiddenFill"/>.</summary>
    public static IBrush WallHiddenGround { get; } = new ImmutableSolidColorBrush(Colors.White, WallHiddenFill);

    /// <summary>A hardcore unlock's edge (the page's #E8B84A).</summary>
    public static IBrush WallHardcoreBrush { get; } = new ImmutableSolidColorBrush(Color.Parse("#E8B84A"));

    // ===== The hints on the bottom screen (screens-engine.js hintsBot) =====

    public static double ScreenHintsRight { get; } = 80;
    public static double ScreenHintsBottom { get; } = 48;
    public static double ScreenHint { get; } = 30;
    public static double ScreenHintGap { get; } = 40;
    public static double ScreenHintOpacity { get; } = 0.85;
    public static double ScreenHintKey { get; } = 44;
    public static double ScreenHintKeyStroke { get; } = 4;
    public static double ScreenHintKeyLetter { get; } = 22;
    public static double ScreenHintKeyGap { get; } = 14;
    public static double ScreenHintKeyWidePadX { get; } = 10;
    public static double ScreenHintKeyWideRadius { get; } = 14;

    public static Thickness ScreenHintKeyThickness => new(ScreenHintKeyStroke);
    public static Thickness WallFilterKeyThickness => new(WallFilterKeyStroke);
    public static CornerRadius WallFilterKeyCorner => new(WallFilterKeyRadius);
    public static Thickness ScreenHintsMargin => new(0, 0, ScreenHintsRight, ScreenHintsBottom);
    public static Thickness ScreenHintKeyWidePadding => new(ScreenHintKeyWidePadX, 0);
    public static CornerRadius ScreenHintKeyCorner => new(ScreenHintKey / 2);
    public static CornerRadius ScreenHintKeyWideCorner => new(ScreenHintKeyWideRadius);

    // ===== 9 Reel (the photo viewer) =====

    public static double ReelLeft { get; } = 120;
    public static double ReelTop { get; } = 80;
    public static double ReelWidth { get; } = 1000;
    public static double ReelHeight { get; } = 562;
    public static double ReelRadius { get; } = 20;
    public static double ReelFigureLeft { get; } = 72;
    public static double ReelFigureTop { get; } = 636;
    public static double ReelFigure { get; } = 224;
    public static double ReelFigureMaxWidth { get; } = 652;
    public static double ReelKindLeft { get; } = 748;
    public static double ReelKindTop { get; } = 744;
    public static double ReelKind { get; } = 40;
    public static double ReelTitleTop { get; } = 808;
    public static double ReelTitle { get; } = 32;
    public static double ReelTitleOpacity { get; } = 0.6;
    public static double ReelPlayMarkShare { get; } = 0.22;
    public static double ReelPlayMarkMax { get; } = 168;
    public static double ReelVideoShade { get; } = 0.5;

    public static Thickness ReelVideoMargin => new(0, ReelTop, 0, 0);
    public static Thickness ReelFigureMargin => new(ReelFigureLeft, ReelFigureTop, 0, 0);
    public static Thickness ReelKindMargin => new(ReelKindLeft, ReelKindTop, 0, 0);
    public static Thickness ReelTitleMargin => new(ReelKindLeft, ReelTitleTop, 0, 0);
    public static CornerRadius ReelCorner => new(ReelRadius);

    /// <summary>The video's poster under its play mark: dark at <see cref="ReelVideoShade"/>.</summary>
    public static IBrush ReelVideoShadeBrush { get; } = new ImmutableSolidColorBrush(Color.FromRgb(8, 8, 20), ReelVideoShade);

    /// <summary>The play mark's size on a picture <paramref name="width"/> wide.</summary>
    public static double ReelPlayMark(double width) => System.Math.Min(ReelPlayMarkMax, width * ReelPlayMarkShare);

    // ===== 12 Porthole (the idle artwork), on the Stage =====

    public static double PortLeft { get; } = 956;
    public static double PortTop { get; } = 184;
    public static double PortSize { get; } = 800;
    public static double PortNameMaxWidth { get; } = 840;
    public static double PortNarrowLeft { get; } = 720;
    public static double PortNarrowTop { get; } = 200;
    public static double PortNarrowSize { get; } = 440;
    public static double PortNarrowNameMaxWidth { get; } = 560;
    public static double PortWipeX { get; } = 1340;

    /// <summary>The round window's square on the Stage's canvas (the narrow one when the screens are swapped; app's own there).</summary>
    public static Rect PortRect(bool narrow) => narrow
        ? new Rect(PortNarrowLeft, PortNarrowTop, PortNarrowSize, PortNarrowSize)
        : new Rect(PortLeft, PortTop, PortSize, PortSize);

    // ===== Line boxes: every text here sits in a line box 1.15 of its size, as the page's =====

    public static double ShelfCountLine => ScreenLine(ShelfCount);
    public static double WallFigureLine => ScreenLine(WallFigure);
    public static double WallFilterLine => ScreenLine(WallFilter);
    public static double WallDescLine => ScreenLine(WallDesc);
    public static double WallMetaLine => ScreenLine(WallMeta);
    public static double ReelFigureLine => ScreenLine(ReelFigure);
    public static double ReelKindLine => ScreenLine(ReelKind);
    public static double ReelTitleLine => ScreenLine(ReelTitle);
    public static double ScreenHintLine => ScreenLine(ScreenHint);

    private static double ScreenLine(double size) => System.Math.Round(size * 1.15, 2);

    /// <summary>The name's box beside the window.</summary>
    public static double PortNameWidth(bool narrow) => narrow ? PortNarrowNameMaxWidth : PortNameMaxWidth;
}
