using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The Big Type Stage's name (M47 B4): the focused item's name fitted into its box as the mock's <c>fitWord</c> does it, by
/// <see cref="BigTypeLayout"/> (words never broken, at most three lines, line height 0.9), set bottom-anchored at the display
/// weight with the display tracking. Drawn in the Stage's ink; the native Stage pictures it whole.
/// </summary>
public sealed class HandheldBigTypeName : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HandheldBigTypeName, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<HandheldBigTypeName, IBrush?>(nameof(Foreground), Brushes.White);

    /// <summary>The name's opacity (a missing game's is dimmed), drawn in the ink rather than on the control, which a picture on Android would cut.</summary>
    public static readonly StyledProperty<double> TextOpacityProperty =
        AvaloniaProperty.Register<HandheldBigTypeName, double>(nameof(TextOpacity), 1);

    /// <summary>The box the name is fitted to; the control may be taller, leaving room below it for descenders.</summary>
    public static readonly StyledProperty<double> BoxHeightProperty =
        AvaloniaProperty.Register<HandheldBigTypeName, double>(nameof(BoxHeight), double.NaN);

    static HandheldBigTypeName()
    {
        AffectsRender<HandheldBigTypeName>(TextProperty, ForegroundProperty, TextOpacityProperty, BoxHeightProperty, TextElement.FontFamilyProperty);
    }

    public double TextOpacity
    {
        get => GetValue(TextOpacityProperty);
        set => SetValue(TextOpacityProperty, value);
    }

    public double BoxHeight
    {
        get => GetValue(BoxHeightProperty);
        set => SetValue(BoxHeightProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    // The last fit, kept while the text, the box and the face stay the same.
    private (string Text, Size Box, FontFamily Family)? _fittedFor;
    private BigTypeName? _fitted;

    /// <summary>The fit for the current text and box (null with no text). Laid out on demand; tests read it.</summary>
    public BigTypeName? Fitted
    {
        get
        {
            var text = Text?.Trim() ?? string.Empty;
            var box = new Size(Bounds.Width, double.IsNaN(BoxHeight) ? Bounds.Height : Math.Min(BoxHeight, Bounds.Height));
            var family = TextElement.GetFontFamily(this);
            if (_fittedFor is { } key && key.Text == text && key.Box == box && Equals(key.Family, family))
                return _fitted;
            _fittedFor = (text, box, family);
            _fitted = text.Length == 0 || box.Width <= 0 || box.Height <= 0 ? null
                : Fit(text, new Typeface(family, FontStyle.Normal, HandheldTokens.DisplayNameWeight), box.Width, box.Height);
            return _fitted;
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Fitted is not { } name)
            return;
        var ink = (Foreground as ISolidColorBrush)?.Color ?? Colors.White;
        var brush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(ink, TextOpacity * ((Foreground as ISolidColorBrush)?.Opacity ?? 1));
        // The box sits at the top of the control; anything below it is room for descenders.
        foreach (var word in name.Words)
            Layout(word.Text, name.Typeface, name.Size, brush).Draw(context, new Point(word.X, word.Top));
    }

    /// <summary>A word as it is drawn: the name's tracking (a fixed −0.64 px, as the mock draws it), no wrapping.</summary>
    private static TextLayout Layout(string text, Typeface typeface, double size, IBrush? brush) =>
        new(text, typeface, size, brush, letterSpacing: HandheldTokens.StageNameSpacing);

    /// <summary>
    /// Fits <paramref name="text"/> into a box of <paramref name="width"/> by <paramref name="height"/>, bottom-anchored: the
    /// words measured once at a reference size, the size and the lines from <see cref="BigTypeLayout.Fit"/>, each line's text
    /// centred in its 0.9 line box as a browser centres a font's ascent and descent in a short line.
    /// </summary>
    public static BigTypeName Fit(string text, Typeface typeface, double width, double height)
    {
        const float reference = 100f;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var measured = words.Select(word => new BigTypeWord(word, (float)Layout(word, typeface, reference, null).WidthIncludingTrailingWhitespace, []))
            .ToArray();
        // A space as a browser adds it between two words: its advance and its share of the tracking.
        var space = (float)(Layout("a a", typeface, reference, null).WidthIncludingTrailingWhitespace -
            Layout("aa", typeface, reference, null).WidthIncludingTrailingWhitespace);
        var fit = BigTypeLayout.Fit(measured, space, reference, (float)width, (float)height, HandheldTokens.StageNameMaxLines,
            (float)HandheldTokens.StageNameMinSize, (float)HandheldTokens.StageNameMaxSize);
        var size = fit.Size;
        var lineHeight = BigTypeLayout.LineHeight * size;
        // The content area a browser centres in the line box: the face's ascent and descent at this size.
        var metrics = typeface.GlyphTypeface.Metrics;
        var em = (double)metrics.DesignEmHeight;
        var ascent = -metrics.Ascent * size / em;
        var descent = metrics.Descent * size / em;
        var probe = Layout("A", typeface, size, null);
        var placed = new List<BigTypeNameWord>(fit.Words.Count);
        foreach (var word in fit.Words)
        {
            var lineTop = height - ((fit.Lines - word.Line) * lineHeight);
            var baseline = lineTop + ((lineHeight - (ascent + descent)) / 2) + ascent;
            placed.Add(new BigTypeNameWord(measured[word.Word].Text, word.X, baseline - probe.Baseline, word.Line));
        }
        return new BigTypeName(text, typeface, size, fit.Lines, placed);
    }
}

/// <summary>A fitted name: its face and size, its line count and each word's place in the box (the word's layout's top left).</summary>
public sealed record BigTypeName(string Text, Typeface Typeface, double Size, int Lines, IReadOnlyList<BigTypeNameWord> Words);

/// <summary>One word of a fitted name: where its layout's top left goes in the box, and its line.</summary>
public sealed record BigTypeNameWord(string Text, double X, double Top, int Line);
