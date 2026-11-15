using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// A list's heading in the Big Type look (M47 B7): the Y sheet's and the menu's title, "Settings", a panel's name. One line set at
/// <see cref="Size"/>, made smaller to fit <see cref="MaxTextWidth"/> (the mock's <c>setHead</c>), its line box
/// <see cref="HandheldTokens.HeadingLineShare"/> of its size with the face's ascent and descent centred in it as a browser does.
/// Each time it is shown, or its text changes, its letters land one after another (the mock's <c>lettersIn</c>, tokens.js
/// <c>HeadingIn</c>): each rises <see cref="FromEm"/> tilted, on the Land curve. While the view is held still or only pictured
/// (<see cref="HandheldRollingText.RollsProperty"/>) the heading is simply there.
/// </summary>
public sealed class HandheldBigTypeHeading : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HandheldBigTypeHeading, string?>(nameof(Text));

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<HandheldBigTypeHeading, double>(nameof(Size), 100);

    public static readonly StyledProperty<double> MaxTextWidthProperty =
        AvaloniaProperty.Register<HandheldBigTypeHeading, double>(nameof(MaxTextWidth), double.PositiveInfinity);

    /// <summary>Tracking in em of the size drawn (the mock's <c>.disp</c>); NaN uses <see cref="FixedSpacing"/> instead.</summary>
    public static readonly StyledProperty<double> SpacingEmProperty =
        AvaloniaProperty.Register<HandheldBigTypeHeading, double>(nameof(SpacingEm), double.NaN);

    /// <summary>Tracking in px whatever the size (a panel's heading inherits the header box's, as the folder's does).</summary>
    public static readonly StyledProperty<double> FixedSpacingProperty =
        AvaloniaProperty.Register<HandheldBigTypeHeading, double>(nameof(FixedSpacing));

    public static readonly StyledProperty<double> FromEmProperty =
        AvaloniaProperty.Register<HandheldBigTypeHeading, double>(nameof(FromEm), BigTypeListMotion.HeadingInFromEm);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<HandheldBigTypeHeading, IBrush?>(nameof(Foreground), Brushes.White);

    static HandheldBigTypeHeading()
    {
        AffectsMeasure<HandheldBigTypeHeading>(TextProperty, SizeProperty, MaxTextWidthProperty, SpacingEmProperty, FixedSpacingProperty);
        AffectsRender<HandheldBigTypeHeading>(ForegroundProperty);
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double MaxTextWidth { get => GetValue(MaxTextWidthProperty); set => SetValue(MaxTextWidthProperty, value); }
    public double SpacingEm { get => GetValue(SpacingEmProperty); set => SetValue(SpacingEmProperty, value); }
    public double FixedSpacing { get => GetValue(FixedSpacingProperty); set => SetValue(FixedSpacingProperty, value); }
    public double FromEm { get => GetValue(FromEmProperty); set => SetValue(FromEmProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private Fitted? _fitted;
    private long? _arrival;
    private bool _ticking;

    /// <summary>The heading as drawn at rest: its size after fitting, its line box, and each letter's place.</summary>
    internal Fitted? Layout => _fitted ??= Fit();

    /// <summary>One letter: its text, where its layout is drawn, the middle it turns about.</summary>
    internal readonly record struct Letter(string Text, Point Origin, Point Centre);

    internal sealed record Fitted(Typeface Typeface, double Size, double Spacing, double Width, double LineHeight, IReadOnlyList<Letter> Letters);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == SizeProperty || change.Property == MaxTextWidthProperty ||
            change.Property == SpacingEmProperty || change.Property == FixedSpacingProperty || change.Property == FontFamilyHost)
        {
            _fitted = null;
        }
        if (change.Property == TextProperty)
            Arrive();
    }

    // The face is the look's (the view's root carries it); a change of the inherited family is read at the next fit.
    private static readonly AvaloniaProperty FontFamilyHost = TextElement.FontFamilyProperty;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Arrive();
    }

    /// <summary>The letters land again from the start (the heading was shown, or says something new).</summary>
    public void Arrive()
    {
        if (!IsEffectivelyVisible || !HandheldRollingText.GetRolls(this) || string.IsNullOrEmpty(Text))
        {
            _arrival = null;
            InvalidateVisual();
            return;
        }
        _arrival = Stopwatch.GetTimestamp();
        InvalidateVisual();
        RequestFrame();
    }

    /// <summary>
    /// The heading as the native Launcher draws it (M47 B8: a folder's name landing on the native Launcher): one atlas of its distinct
    /// letters and each letter's place, the control's top left at <paramref name="origin"/> on the Launcher's canvas. Null with no text.
    /// </summary>
    internal BigTypeLetters? Letters(Point origin)
    {
        if (Layout is not { } fitted || string.IsNullOrEmpty(Text))
            return null;
        var placed = new List<HandheldLetterAtlas.Placed>(fitted.Letters.Count);
        for (var i = 0; i < fitted.Letters.Count; i++)
            placed.Add(new HandheldLetterAtlas.Placed(fitted.Letters[i].Text, fitted.Letters[i].Origin, i));
        return HandheldLetterAtlas.Build(Text, placed, fitted.Typeface, fitted.Size, origin);
    }

    /// <summary>Whether the letters are still landing (the native feed pictures the heading whole once they have).</summary>
    internal bool IsArriving => _arrival is not null;

    protected override Size MeasureOverride(Size availableSize) =>
        Layout is { } fitted ? new Size(fitted.Width, fitted.LineHeight) : default;

    public override void Render(DrawingContext context)
    {
        if (Layout is not { } fitted || Foreground is not { } brush)
            return;
        var since = _arrival is { } start ? (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds : float.PositiveInfinity;
        var still = !HandheldRollingText.GetRolls(this);
        for (var i = 0; i < fitted.Letters.Count; i++)
        {
            var letter = fitted.Letters[i];
            var (em, degrees, opacity) = BigTypeListMotion.HeadingLetter(i, since, (float)FromEm, still);
            if (opacity <= 0f)
                continue;
            using var layout = LetterLayout(letter.Text, fitted, brush);
            if (em == 0f && degrees == 0f && opacity >= 1f)
            {
                layout.Draw(context, letter.Origin);
                continue;
            }
            var turn = Matrix.CreateTranslation(-letter.Centre.X, -letter.Centre.Y) * Matrix.CreateRotation(degrees * Math.PI / 180) *
                Matrix.CreateTranslation(letter.Centre.X, letter.Centre.Y + (em * fitted.Size));
            using (context.PushOpacity(opacity))
            using (context.PushTransform(turn))
                layout.Draw(context, letter.Origin);
        }
        if (_arrival is not null && since >= BigTypeListMotion.HeadingInTotal(fitted.Letters.Count))
            _arrival = null;
    }

    private void RequestFrame()
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            InvalidateVisual();
            if (_arrival is not null)
                RequestFrame();
        });
    }

    private static TextLayout LetterLayout(string text, Fitted fitted, IBrush? brush) =>
        new(text, fitted.Typeface, fitted.Size, brush, letterSpacing: fitted.Spacing);

    private Fitted? Fit()
    {
        if (string.IsNullOrEmpty(Text))
            return null;
        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, HandheldTokens.HeadingWeight);
        var size = Size;
        double Spacing(double at) => double.IsNaN(SpacingEm) ? FixedSpacing : SpacingEm * at;
        using (var natural = new TextLayout(Text, typeface, size, null, letterSpacing: Spacing(size)))
        {
            var width = natural.WidthIncludingTrailingWhitespace;
            if (width > MaxTextWidth && width > 0)
                size = Math.Floor(size * MaxTextWidth / width * 100) / 100;
        }
        var spacing = Spacing(size);
        var lineHeight = HandheldTokens.HeadingLineShare * size;
        // The content area a browser centres in the line box: the face's ascent and descent at this size.
        var metrics = typeface.GlyphTypeface.Metrics;
        var em = (double)metrics.DesignEmHeight;
        var ascent = -metrics.Ascent * size / em;
        var descent = metrics.Descent * size / em;
        var baseline = ((lineHeight - (ascent + descent)) / 2) + ascent;
        using var line = new TextLayout(Text, typeface, size, null, letterSpacing: spacing);
        var top = baseline - line.Baseline;
        var letters = new List<Letter>(Text.Length);
        var elements = StringInfo.GetTextElementEnumerator(Text);
        while (elements.MoveNext())
        {
            var text = elements.GetTextElement();
            if (string.IsNullOrWhiteSpace(text))
                continue;
            var index = elements.ElementIndex;
            var x = line.HitTestTextPosition(index).X;
            var advance = line.HitTestTextPosition(index + text.Length).X - x;
            // A letter turns about the middle of its own box: its advance, the line box's height (the mock's inline-block span).
            letters.Add(new Letter(text, new Point(x, top), new Point(x + (advance / 2), lineHeight / 2)));
        }
        return new Fitted(typeface, size, spacing, line.WidthIncludingTrailingWhitespace, lineHeight, letters);
    }
}
