// M47 B8: the launch and the in-game companion in the Big Type look (decision 2026-11-15 "M47 B8" in DECISIONS.md).

using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The tokens B8 added for the Big Type look: the launch on the Stage (tokens.js <c>stage</c> and <c>type.LaunchStatus</c>) and the
/// in-game companion (tokens.js <c>ingame</c>, the <c>InGame…</c> and <c>SessionTimer</c> types, <c>colour.InGameMix</c> and
/// <c>InGameRestOpacity</c>). Today's look never draws them, so each holds one value in either look. The motion's numbers are in
/// <see cref="BigTypeFolderMotion"/> and <see cref="BigTypeLaunchMotion"/>, shared with the native screens.
/// </summary>
public static partial class HandheldTokens
{
    // ===== The launch, on the Stage (tokens.js stage, type) =====

    /// <summary>The launch title's lines at most this wide (<c>stage.LaunchTitleMaxWidth</c>).</summary>
    public static double LaunchTitleMaxWidth { get; } = 1600;

    /// <summary>The launch title's lines this share of its size apart (<c>stage.LaunchTitleLineHeight</c>).</summary>
    public static double LaunchTitleLineHeight { get; } = 0.92;

    /// <summary>Between the launch title and its status (<c>stage.LaunchGap</c>).</summary>
    public static double LaunchGap { get; } = 36;

    /// <summary>The launch's status line (<c>type.LaunchStatus</c>).</summary>
    public static double LaunchStatus { get; } = 44;

    public static double LaunchStatusOpacity { get; } = 0.9;

    /// <summary>The status line's ink: white at its opacity, carried in the brush (B4: Android cuts text with its own opacity).</summary>
    public static IBrush LaunchStatusInk { get; } = new ImmutableSolidColorBrush(Colors.White, LaunchStatusOpacity);

    /// <summary>
    /// The status line's shot keeps the line's box (the mock's <c>line-height: 1.15</c>) inside its padding: the padding is taken back
    /// by a negative margin so the gap to the title stays <see cref="LaunchGap"/>.
    /// </summary>
    public static Thickness LaunchStatusShotMargin => new(-StageShotPadding.Left, -StageShotPadding.Top, -StageShotPadding.Right, -StageShotPadding.Bottom);

    // ===== The in-game companion (tokens.js ingame, type, colour) =====

    /// <summary><c>colour.InGameMix</c>: the Launcher while a game runs is the console's colour mixed this far toward <c>InkToward</c>.</summary>
    public static double InGameMix { get; } = 0.64;

    /// <summary><c>colour.InGameRestOpacity</c>: the companion resting.</summary>
    public static double InGameRestOpacity { get; } = 0.28;

    /// <summary>The in-game colour for a console's colour.</summary>
    public static Color InGameGround(Color colour)
    {
        var mixed = System.Numerics.Vector3.Lerp(new System.Numerics.Vector3(colour.R, colour.G, colour.B), BigTypeVeil.InkToward, (float)InGameMix);
        return Color.FromRgb((byte)Math.Round(mixed.X), (byte)Math.Round(mixed.Y), (byte)Math.Round(mixed.Z));
    }

    /// <summary><c>type.SessionTimer</c>, <c>InGameFigure</c>, <c>InGameStat</c>, <c>InGameTitle</c>.</summary>
    public static double SessionTimer { get; } = 380;

    public static double InGameFigure { get; } = 224;
    public static double InGameStat { get; } = 64;
    public static double InGameTitle { get; } = 80;

    /// <summary><c>type.InGameLabel</c>: "Now playing", "this session", "unlocked".</summary>
    public static double InGameLabel { get; } = 36;

    public static double InGameLabelOpacity { get; } = 0.75;

    /// <summary><c>type.InGameTab</c>: the tabs, the one not showing at its rest opacity.</summary>
    public static double InGameTab { get; } = 34;

    public static double InGameTabRestOpacity { get; } = 0.45;

    /// <summary><c>type.InGameStatLabel</c>: "in total", "achievements".</summary>
    public static double InGameStatLabel { get; } = 32;

    public static double InGameStatLabelOpacity { get; } = 0.75;

    /// <summary><c>type.InGameRecent</c> and <c>InGameRecentWhen</c>: an unlock and when it was.</summary>
    public static double InGameRecent { get; } = 38;

    public static double InGameRecentWhen { get; } = 32;
    public static double InGameRecentWhenOpacity { get; } = 0.7;

    /// <summary>A text's line box is this share of its size, as the mock's <c>line-height: 1.15</c> (not a tokens.js number: CSS's).</summary>
    public static double InGameLineShare { get; } = 1.15;

    // Places (tokens.js ingame).
    public static double InGameKickerLeft { get; } = 80;
    public static double InGameKickerTop { get; } = 60;
    public static double InGameTitleLeft { get; } = 80;
    public static double InGameTitleTop { get; } = 108;
    public static double InGameTitleMaxWidth { get; } = 1080;
    public static double InGameTabsRight { get; } = 80;
    public static double InGameTabsTop { get; } = 60;
    public static double InGameTabsGap { get; } = 36;
    public static double InGameTimerLeft { get; } = 68;
    public static double InGameTimerTop { get; } = 244;
    public static double InGameLabelLeft { get; } = 88;
    public static double InGameLabelTop { get; } = 660;
    public static double InGameStatsInset { get; } = 80;
    public static double InGameStatsTop { get; } = 736;
    public static double InGameStatGap { get; } = 20;
    public static double InGameFigureLeft { get; } = 72;
    public static double InGameFigureTop { get; } = 264;
    public static double InGameFigureLabelTop { get; } = 544;
    public static double InGameRecentInset { get; } = 80;
    public static double InGameRecentTop { get; } = 676;
    public static double InGameRecentPitch { get; } = 60;
    public static double InGameShotLeft { get; } = 80;
    public static double InGameShotTop { get; } = 256;
    public static double InGameShotPitchX { get; } = 376;
    public static double InGameShotPitchY { get; } = 232;
    public static double InGameShotWidth { get; } = 328;
    public static double InGameShotHeight { get; } = 200;
    public static double InGameShotRadius { get; } = 20;
    public static double InGameShotsLabelTop { get; } = 744;
    public static double InGameBarInset { get; } = 80;
    public static double InGameBarTop { get; } = 868;
    public static double InGameBarRadius { get; } = 6;
    public static double InGameBarTrackOpacity { get; } = 0.2;

    /// <summary>How many photos the Photos tab shows (the mock's six, two rows of three).</summary>
    public const int InGameShotColumns = 3;

    public const int InGameShots = 6;

    /// <summary>The dock row's way back in the companion's corner (the mock's "B to shelf"; the app's game ends in the emulator).</summary>
    public static string InGameWayBack { get; } = "Quit to shelf";

    /// <summary>A Photos tab place's video mark.</summary>
    public static double InGameShotGlyph { get; } = 64;

    // Derived margins, for the views.
    public static Thickness InGameShotsMargin => new(InGameShotLeft, InGameShotTop, 0, 0);
    public static double InGameShotsWidth => InGameShotColumns * InGameShotPitchX;
    public static Thickness InGameRecentWhenMargin => new(InGameStatGap, (InGameRecent - InGameRecentWhen) * InGameLineShare / 2, 0, 0);
    public static Thickness InGameStatLabelMargin => new(0, 0, 0, (InGameStat - InGameStatLabel) * (InGameLineShare - 1) / 2);
    public static Thickness InGameDockMargin => new(0, 0, 0, DockCornerMargin.Bottom);
    public static Thickness InGameCornerMargin => new(0, 0, DockCornerMargin.Right, DockCornerMargin.Bottom + ((DockTile - (DockCorner * InGameLineShare)) / 2));
    public static Thickness InGameRingThickness => new(RingStroke);
    public static TimeSpan InGameTabFade { get; } = TimeSpan.FromMilliseconds(250);
    public static Avalonia.Media.Transformation.TransformOperations InGameDockFocusTransform =>
        Avalonia.Media.Transformation.TransformOperations.Parse(FormattableString.Invariant($"scale({DockFocusScale})"));
    public static Thickness InGameKickerMargin => new(InGameKickerLeft, InGameKickerTop, 0, 0);
    public static Thickness InGameTitleMargin => new(InGameTitleLeft, InGameTitleTop, 0, 0);
    public static Thickness InGameTabsMargin => new(0, InGameTabsTop, InGameTabsRight, 0);
    public static Thickness InGameTimerMargin => new(InGameTimerLeft, InGameTimerTop, 0, 0);
    public static Thickness InGameLabelMargin => new(InGameLabelLeft, InGameLabelTop, 0, 0);
    public static Thickness InGameStatsMargin => new(InGameStatsInset, InGameStatsTop, InGameStatsInset, 0);
    public static Thickness InGameFigureMargin => new(InGameFigureLeft, InGameFigureTop, 0, 0);
    public static Thickness InGameFigureLabelMargin => new(InGameLabelLeft, InGameFigureLabelTop, 0, 0);
    public static Thickness InGameRecentMargin => new(InGameRecentInset, InGameRecentTop, InGameRecentInset, 0);
    public static Thickness InGameShotsLabelMargin => new(InGameLabelLeft, InGameShotsLabelTop, 0, 0);
    public static Thickness InGameBarMargin => new(InGameBarInset, InGameBarTop, InGameBarInset, 0);
    public static CornerRadius InGameShotCorner => new(InGameShotRadius);
    public static CornerRadius InGameBarCorner => new(InGameBarRadius);
    public static double InGameRecentHeight => InGameRecentPitch;
    public static double InGameStatLine => InGameStat * InGameLineShare;
    public static double InGameLabelLine => InGameLabel * InGameLineShare;
    public static double InGameTabLine => InGameTab * InGameLineShare;
    public static double InGameStatLabelLine => InGameStatLabel * InGameLineShare;
    public static double InGameTitleLine => InGameTitle * InGameLineShare;
    public static double SessionTimerLine => SessionTimer * InGameLineShare;
    public static double InGameFigureLine => InGameFigure * InGameLineShare;
    public static double InGameRecentLine => InGameRecent * InGameLineShare;
    public static double InGameRecentWhenLine => InGameRecentWhen * InGameLineShare;
    public static IBrush InGameBarTrack { get; } = new ImmutableSolidColorBrush(Colors.White, InGameBarTrackOpacity);
}
