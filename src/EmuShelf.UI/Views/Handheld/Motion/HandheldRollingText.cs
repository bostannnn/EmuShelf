using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// A text whose characters roll when it changes (M47 B5, the mock's move 5): each character that changed leaves upward and the
/// new one rises in on the landing curve, each clipped to its own cell; characters that stayed do not move. At rest, and whenever
/// <see cref="RollsProperty"/> is false (the native screen pictures this view, the device asks for stillness), it draws exactly
/// as a <see cref="TextBlock"/> and simply swaps. The timing is <see cref="BigTypeMotion"/>'s, shared with the native screens.
/// </summary>
public sealed class HandheldRollingText : TextBlock
{
    /// <summary>Whether texts under this element roll (inherited); the views turn it off while they are only pictured or held still.</summary>
    public static readonly AttachedProperty<bool> RollsProperty =
        AvaloniaProperty.RegisterAttached<HandheldRollingText, Control, bool>("Rolls", defaultValue: true, inherits: true);

    public static bool GetRolls(Control control) => control.GetValue(RollsProperty);

    public static void SetRolls(Control control, bool value) => control.SetValue(RollsProperty, value);

    protected override Type StyleKeyOverride => typeof(TextBlock);

    private bool _shown;
    private Point _shownOrigin;
    private string _shownText = string.Empty;
    private (TextLayout Layout, StageText Cells, Point Origin)? _old;
    private StageText? _cells;
    private long _start;
    private bool _ticking;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TextProperty)
            return;
        var before = _shownText;
        var after = Text ?? string.Empty;
        EndRoll();
        // Within a run of presses (a held direction, quick taps; M47 B11) it swaps in place, so a roll never restarts on a roll.
        if (HandheldInputRun.Of(this) || !_shown || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is null || !BigTypeMotion.Rolls(before, after, holdStill: !GetRolls(this)))
            return;
        // The text block lets its own layout of the old text go; the roll draws one of its own, laid out the same way.
        var layout = CreateTextLayout(before);
        // Laying out the old text fills the text block's cache of shaped runs with it, after the block had already let its cache
        // go for the new text: its next layout then drew the old text (the Stage's line kept an earlier line, M47 B7). Let it go again.
        InvalidateTextLayout();
        if (Cells(layout, before, _shownOrigin) is not { } cells)
        {
            layout.Dispose();
            return;
        }
        _old = (layout, cells, _shownOrigin);
        _start = Stopwatch.GetTimestamp();
        // A change in the middle of a roll simply swaps.
        _shown = false;
        RequestFrame();
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        var layout = TextLayout;
        if (_old is { } old && Pieces(layout, origin, old) is { } pieces)
        {
            foreach (var piece in pieces)
            {
                using (context.PushClip(new Rect(piece.ClipX, piece.ClipY, piece.ClipWidth, piece.ClipHeight)))
                using (context.PushOpacity(piece.Opacity))
                using (context.PushTransform(Matrix.CreateTranslation(0, piece.OffsetY)))
                {
                    if (piece.Source == BigTypeRollSource.New)
                        base.RenderTextLayout(context, origin);
                    else
                        old.Layout.Draw(context, old.Origin);
                }
            }
            return;
        }
        EndRoll();
        base.RenderTextLayout(context, origin);
        _shown = true;
        _shownOrigin = origin;
        _shownText = Text ?? string.Empty;
    }

    private IReadOnlyList<BigTypeRollPiece>? Pieces(TextLayout layout, Point origin, (TextLayout Layout, StageText Cells, Point Origin) old)
    {
        _cells ??= Cells(layout, Text ?? string.Empty, origin);
        if (_cells is not { } cells)
            return null;
        var since = (float)Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
        return BigTypeRollDraw.Pieces(old.Cells, cells, 0f, 0f, (float)Bounds.Width, (float)Bounds.Height, since);
    }

    private void EndRoll()
    {
        _old?.Layout.Dispose();
        _old = null;
        _cells = null;
    }

    // Each character's window in this control: its advance across from where the text is drawn, the control's height down.
    private StageText? Cells(TextLayout layout, string text, Point origin)
    {
        if (text.Length == 0 || Bounds.Height < 1)
            return null;
        var cells = new List<StageTextCell>(text.Length);
        try
        {
            for (var i = 0; i < text.Length; i++)
            {
                var rect = layout.HitTestTextRange(i, 1).FirstOrDefault();
                cells.Add(new StageTextCell(text[i], (float)(origin.X + rect.X), 0f, (float)rect.Width, (float)Bounds.Height, (float)FontSize));
            }
        }
        catch (Exception)
        {
            return null; // a text the layout cannot measure character by character simply swaps
        }
        return new StageText(cells);
    }

    private void RequestFrame()
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            if (_old is not { } old)
                return;
            InvalidateVisual();
            // A roll that is not being drawn (the text went out of sight) still ends on time.
            if (Stopwatch.GetElapsedTime(_start).TotalMilliseconds > BigTypeMotion.RollMilliseconds(old.Cells.Cells.Count + (Text?.Length ?? 0)) + 100)
            {
                EndRoll();
                return;
            }
            RequestFrame();
        });
    }
}
