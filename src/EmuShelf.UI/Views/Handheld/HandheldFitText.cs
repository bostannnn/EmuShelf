using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// A list's label in the Big Type look (M47 B7): set at <see cref="Size"/>, or smaller to fit its room as the mock's <c>setHead</c>
/// fits a heading (size × room ÷ natural width), but no smaller than <see cref="HandheldTokens.LabelFitMin"/> of it; past that it
/// ends in an ellipsis. No label is cut mid-letter or runs into what ends its row.
/// </summary>
public sealed class HandheldFitText : TextBlock
{
    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<HandheldFitText, double>(nameof(Size), 40);

    static HandheldFitText() => AffectsMeasure<HandheldFitText>(SizeProperty);

    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <summary>The label's size when it fits (the list's type token).</summary>
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    /// <summary>The size a text is set at to fit <paramref name="room"/> when it is <paramref name="natural"/> wide at <paramref name="size"/>.</summary>
    public static double Fit(double size, double natural, double room, double minShare) =>
        natural <= room || natural <= 0 || !double.IsFinite(room) ? size : Math.Max(size * minShare, size * room / natural);

    protected override Size MeasureOverride(Size availableSize)
    {
        var fitted = Size;
        if (!string.IsNullOrEmpty(Text) && double.IsFinite(availableSize.Width))
        {
            var room = Math.Max(0, availableSize.Width - Padding.Left - Padding.Right);
            using var natural = new TextLayout(Text, new Typeface(FontFamily, FontStyle, FontWeight, FontStretch), Size, null,
                letterSpacing: LetterSpacing);
            fitted = Fit(Size, natural.WidthIncludingTrailingWhitespace, room, HandheldTokens.LabelFitMin);
        }
        if (!FontSize.Equals(fitted))
            SetCurrentValue(FontSizeProperty, fitted);
        return base.MeasureOverride(availableSize);
    }
}

/// <summary>
/// A list row's line (M47 B7): its label at the left and what ends it (a value or a switch) at the right, centred across. The ending
/// takes at most <see cref="HandheldTokens.RowValueMaxShare"/> of the row and the label the rest, so neither ever runs into the other.
/// </summary>
public sealed class HandheldRowLine : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = availableSize.Width;
        var endWidth = 0d;
        var height = 0d;
        for (var i = 1; i < Children.Count; i++)
        {
            var end = Children[i];
            end.Measure(new Size(double.IsFinite(width) ? width * HandheldTokens.RowValueMaxShare : width, availableSize.Height));
            endWidth = Math.Max(endWidth, end.DesiredSize.Width);
            height = Math.Max(height, end.DesiredSize.Height);
        }
        if (Children.Count > 0)
        {
            var label = Children[0];
            label.Measure(new Size(double.IsFinite(width) ? Math.Max(0, width - endWidth) : width, availableSize.Height));
            height = Math.Max(height, label.DesiredSize.Height);
            return new Size(double.IsFinite(width) ? width : label.DesiredSize.Width + endWidth, height);
        }
        return new Size(double.IsFinite(width) ? width : endWidth, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var endWidth = 0d;
        for (var i = 1; i < Children.Count; i++)
        {
            var end = Children[i];
            var size = end.DesiredSize;
            end.Arrange(new Rect(finalSize.Width - size.Width, (finalSize.Height - size.Height) / 2, size.Width, size.Height));
            endWidth = Math.Max(endWidth, size.Width);
        }
        if (Children.Count > 0)
        {
            var label = Children[0];
            var width = Math.Max(0, finalSize.Width - endWidth);
            label.Arrange(new Rect(0, (finalSize.Height - label.DesiredSize.Height) / 2, width, label.DesiredSize.Height));
        }
        return finalSize;
    }
}
