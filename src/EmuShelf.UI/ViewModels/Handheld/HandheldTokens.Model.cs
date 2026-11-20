using Avalonia;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The model view's places (M47 B12, proposals.html, approved 2026-11-15; tokens.js <c>stage.Model…</c>, held in
/// <see cref="BigTypeModelMotion"/>): the medium's box in the middle of the Stage grown <see cref="BigTypeModelMotion.ModelScale"/>, the
/// 3D medium's frame around it, the flat cover standing in the same place, and the multi-letter hint keys (RB, RS) the proposal draws as
/// rounded keys rather than circles. Big Type only; today's look never shows them.
/// </summary>
public static partial class HandheldTokens
{
    /// <summary>
    /// The middle of the medium's corner beside the name (where the model view's medium rises from): the wide Stage's, or on the narrow
    /// one its own corner (M47 B11).
    /// </summary>
    public static Point StageMediumCentre(double canvasWidth) => StageMediumFrame(canvasWidth).Center;

    /// <summary>The model's scale on a canvas: tokens.js <c>ModelScale</c>, or <c>NarrowModelScale</c> on the narrow Stage.</summary>
    public static double StageModelScale(double canvasWidth) => BigTypeModelMotion.ScaleFor((float)canvasWidth);

    /// <summary>The middle of the model view's resting box: across the middle, <c>ModelTop</c> from the top.</summary>
    public static Point StageModelCentre(double canvasWidth)
    {
        var (x, y) = BigTypeModelMotion.Centre((float)canvasWidth, (float)StageMediumHeight);
        return new Point(x, y);
    }

    /// <summary>The 3D medium's frame in the model view: <see cref="StageMediumFrame"/> grown about the model's middle.</summary>
    public static Rect StageModelFrame(double canvasWidth)
    {
        var beside = StageMediumFrame(canvasWidth);
        var centre = StageModelCentre(canvasWidth);
        var width = beside.Width * StageModelScale(canvasWidth);
        var height = beside.Height * StageModelScale(canvasWidth);
        return new Rect(centre.X - (width / 2), centre.Y - (height / 2), width, height);
    }

    /// <summary>The model view's flat cover (a platform with no authored medium): the medium's box grown, in the same middle.</summary>
    public static Rect StageModelCoverBox(double canvasWidth)
    {
        var centre = StageModelCentre(canvasWidth);
        var width = StageMediumWidth * StageModelScale(canvasWidth);
        var height = StageMediumHeight * StageModelScale(canvasWidth);
        return new Rect(centre.X - (width / 2), centre.Y - (height / 2), width, height);
    }

    // The narrow Stage's (M47 B11, the screens swapped): the same pieces on its 1240-wide canvas.
    private const double NarrowStage = BigTypeModelMotion.NarrowCanvasWidth;

    public static Thickness StageNarrowModelFrameMargin => new(StageModelFrame(NarrowStage).X, StageModelFrame(NarrowStage).Y, 0, 0);

    public static double StageNarrowModelFrameWidth => StageModelFrame(NarrowStage).Width;

    public static double StageNarrowModelFrameHeight => StageModelFrame(NarrowStage).Height;

    public static Thickness StageNarrowModelCoverMargin => new(StageModelCoverBox(NarrowStage).X, StageModelCoverBox(NarrowStage).Y, 0, 0);

    public static double StageNarrowModelCoverWidth => StageModelCoverBox(NarrowStage).Width;

    public static double StageNarrowModelCoverHeight => StageModelCoverBox(NarrowStage).Height;

    // The Avalonia Stage's pieces, on its 16:9 canvas.
    public static Thickness StageModelFrameMargin => new(StageModelFrame(1920).X, StageModelFrame(1920).Y, 0, 0);

    public static double StageModelFrameWidth => StageModelFrame(1920).Width;

    public static double StageModelFrameHeight => StageModelFrame(1920).Height;

    public static Thickness StageModelCoverMargin => new(StageModelCoverBox(1920).X, StageModelCoverBox(1920).Y, 0, 0);

    public static double StageModelCoverWidth => StageModelCoverBox(1920).Width;

    public static double StageModelCoverHeight => StageModelCoverBox(1920).Height;

    /// <summary>The flat cover's corner, grown with it.</summary>
    public static CornerRadius StageModelCoverRadius => new(StageCoverRadius.TopLeft * BigTypeModelMotion.ModelScale);

    /// <summary>The no-cover card's title in the model view, grown with it.</summary>
    public static double StageModelNoCoverTitle => StageNoCoverTitle * BigTypeModelMotion.ModelScale;

    public static double StageModelNoCoverTitleLineHeight => Math.Round(StageModelNoCoverTitle * StageCornerLineShare, 2);

    public static Thickness StageModelNoCoverPadding => new(Math.Round(StageModelCoverWidth * 0.12));

    /// <summary>
    /// A hint whose key is more than one letter (RB, RS): tokens.js <c>type.HintKey</c> (the proposal's <c>.hint i.sh</c>), a rounded
    /// key at least as wide as the circle, its letters padded on each side.
    /// </summary>
    public static Thickness StageHintKeyPadding => new(BigTypeModelMotion.HintKeyPadding, 0);

    public static CornerRadius StageHintKeyCorner => new(BigTypeModelMotion.HintKeyRadius);

    /// <summary>
    /// Where the launch's colour grows from in the name view (M47 B12's choice, awaiting the owner): there is no case beside the name any
    /// more, so the circle grows from the middle of the name itself, its box's middle across and its lines' middle up.
    /// </summary>
    public static Point NameTakeoverOrigin(double blockHeight, bool narrow = false)
    {
        // On the narrow Stage the name stands a hint row higher, in its own box (M47 B11).
        var bottom = 1080 - StageNameBottom - (narrow ? StageNarrowHintsRow : 0);
        var height = narrow ? StageNarrowNameHeight : StageNameHeight;
        var width = narrow ? StageNarrowNameWidth : StageNameWidth;
        var block = Math.Min(height, blockHeight);
        return new Point(StageNameLeft + (width / 2), bottom - (block / 2));
    }
}
