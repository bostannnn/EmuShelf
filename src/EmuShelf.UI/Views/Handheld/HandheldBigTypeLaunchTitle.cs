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
/// The launch's title on the Stage in the Big Type look (M47 B8, the mock's <c>.launch .lw</c>): the game's title at the size its length
/// gives it (tokens.js <c>type.LaunchTitle</c>), its words in centred lines at most <see cref="HandheldTokens.LaunchTitleMaxWidth"/>
/// wide, <see cref="HandheldTokens.LaunchTitleLineHeight"/> of its size apart. Its letters land as the takeover starts (the mock's
/// <c>lettersIn(lw, .8)</c>), timed from <see cref="Started"/>, the press that launched; held still or only pictured it is simply there.
/// The native Stage draws the same letters from <see cref="Letters"/>.
/// </summary>
public sealed class HandheldBigTypeLaunchTitle : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HandheldBigTypeLaunchTitle, string?>(nameof(Text));

    /// <summary>When the press that launched landed (a <see cref="Stopwatch"/> timestamp); 0: no launch, at rest.</summary>
    public static readonly StyledProperty<long> StartedProperty =
        AvaloniaProperty.Register<HandheldBigTypeLaunchTitle, long>(nameof(Started));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<HandheldBigTypeLaunchTitle, IBrush?>(nameof(Foreground), Brushes.White);

    /// <summary>
    /// The lines' width at most: the wide Stage's (tokens.js <c>stage.LaunchTitleMaxWidth</c>), or the narrow one's (M47 B11,
    /// <c>stage.NarrowLaunchTitleMaxWidth</c>); the title's sizes scale with it.
    /// </summary>
    public static readonly StyledProperty<double> MaxLineWidthProperty =
        AvaloniaProperty.Register<HandheldBigTypeLaunchTitle, double>(nameof(MaxLineWidth), HandheldTokens.LaunchTitleMaxWidth);

    static HandheldBigTypeLaunchTitle()
    {
        AffectsMeasure<HandheldBigTypeLaunchTitle>(TextProperty, MaxLineWidthProperty);
        AffectsRender<HandheldBigTypeLaunchTitle>(ForegroundProperty, StartedProperty);
    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public long Started { get => GetValue(StartedProperty); set => SetValue(StartedProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double MaxLineWidth { get => GetValue(MaxLineWidthProperty); set => SetValue(MaxLineWidthProperty, value); }

    private Fitted? _fitted;
    private bool _ticking;

    internal readonly record struct Letter(string Text, Point Origin, Point Centre, int Order);

    internal sealed record Fitted(Typeface Typeface, double Size, double Spacing, double Width, double Height, IReadOnlyList<Letter> Letters);

    /// <summary>The title as drawn at rest: its size, its box, each letter's place.</summary>
    internal Fitted? Layout => _fitted ??= Fit();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == TextElement.FontFamilyProperty || change.Property == MaxLineWidthProperty)
            _fitted = null;
        if (change.Property == StartedProperty || change.Property == TextProperty)
            RequestFrame();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Layout is { } fitted ? new Size(fitted.Width, fitted.Height) : default;

    private float Since => Started == 0 ? float.PositiveInfinity : (float)Stopwatch.GetElapsedTime(Started).TotalMilliseconds;

    public override void Render(DrawingContext context)
    {
        if (Layout is not { } fitted || Foreground is not { } brush)
            return;
        var since = Since;
        var still = !HandheldRollingText.GetRolls(this);
        foreach (var letter in fitted.Letters)
        {
            var (em, degrees, opacity) = BigTypeLaunchMotion.TitleLetter(letter.Order, since, still);
            if (opacity <= 0f)
                continue;
            using var layout = new TextLayout(letter.Text, fitted.Typeface, fitted.Size, brush, letterSpacing: fitted.Spacing);
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
    }

    private void RequestFrame()
    {
        InvalidateVisual();
        if (_ticking || Started == 0 || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            InvalidateVisual();
            if (Layout is { } fitted && Since < BigTypeLaunchMotion.TakeoverAt + BigTypeListMotion.HeadingInTotal(fitted.Letters.Count))
                RequestFrame();
        });
    }

    /// <summary>The title as the native Stage draws it: one atlas of its letters, the control's top left at <paramref name="origin"/>.</summary>
    internal BigTypeLetters? Letters(Point origin)
    {
        if (Layout is not { } fitted || string.IsNullOrEmpty(Text))
            return null;
        var placed = new List<HandheldLetterAtlas.Placed>(fitted.Letters.Count);
        foreach (var letter in fitted.Letters)
            placed.Add(new HandheldLetterAtlas.Placed(letter.Text, letter.Origin, letter.Order));
        return HandheldLetterAtlas.Build(Text, placed, fitted.Typeface, fitted.Size, origin);
    }

    /// <summary>
    /// Sets the title: words in lines no wider than the maximum (a word wider than it has a line of its own), each line centred, the
    /// face's ascent and descent centred in each line's box as a browser does.
    /// </summary>
    private Fitted? Fit()
    {
        if (string.IsNullOrEmpty(Text))
            return null;
        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, HandheldTokens.DisplayNameWeight);
        var max = MaxLineWidth;
        var size = BigTypeLaunchMotion.TitleSize(Text) * (max / HandheldTokens.LaunchTitleMaxWidth);
        var spacing = HandheldTokens.DisplayLetterSpacing * size;
        var lineHeight = HandheldTokens.LaunchTitleLineHeight * size;
        double Measure(string text)
        {
            using var layout = new TextLayout(text, typeface, size, null, letterSpacing: spacing);
            return layout.WidthIncludingTrailingWhitespace;
        }
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = current.Length == 0 ? word : $"{current} {word}";
            if (current.Length > 0 && Measure(next) > max)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = next;
            }
        }
        if (current.Length > 0)
            lines.Add(current);
        var widths = lines.ConvertAll(Measure);
        var width = 0d;
        foreach (var w in widths)
            width = Math.Max(width, w);
        var metrics = typeface.GlyphTypeface.Metrics;
        var em = (double)metrics.DesignEmHeight;
        var ascent = -metrics.Ascent * size / em;
        var descent = metrics.Descent * size / em;
        var baselineInLine = ((lineHeight - (ascent + descent)) / 2) + ascent;
        var letters = new List<Letter>(Text.Length);
        for (var i = 0; i < lines.Count; i++)
        {
            using var line = new TextLayout(lines[i], typeface, size, null, letterSpacing: spacing);
            var left = (width - widths[i]) / 2;
            var top = (i * lineHeight) + baselineInLine - line.Baseline;
            var elements = StringInfo.GetTextElementEnumerator(lines[i]);
            while (elements.MoveNext())
            {
                var text = elements.GetTextElement();
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var index = elements.ElementIndex;
                var x = line.HitTestTextPosition(index).X;
                var advance = line.HitTestTextPosition(index + text.Length).X - x;
                letters.Add(new Letter(text, new Point(left + x, top), new Point(left + x + (advance / 2), (i * lineHeight) + (lineHeight / 2)), letters.Count));
            }
        }
        return new Fitted(typeface, size, spacing, width, lines.Count * lineHeight, letters);
    }
}
