using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The Big Type Stage's name (M47 B4): the focused item's name fitted into its box as the mock's <c>fitWord</c> does it, by
/// <see cref="BigTypeLayout"/> (words never broken, at most three lines, line height 0.9), set bottom-anchored at the display
/// weight with the display tracking. Drawn in the Stage's ink.
/// </summary>
/// <remarks>
/// M47 B6: the native Stage no longer pictures it whole. <see cref="Letters"/> sets the same fit as one atlas of its distinct letters
/// and every letter's place, and the native Stage moves each letter (move 2). On screen (the desktop, the Avalonia fallback) this control
/// moves its letters the same way when the text changes, from <see cref="Entrance"/>, by <see cref="BigTypeMotion"/>; while it is only
/// pictured or the device asks for stillness (<see cref="HandheldRollingText.RollsProperty"/> false) it simply swaps.
/// </remarks>
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

    /// <summary>Where the next name's letters arrive from (the move that brought it); set before the text changes.</summary>
    public BigTypeEntrance Entrance { get; set; }

    // The letters in flight on screen: the name arriving (and when), and the names leaving (each with when and which way).
    private (BigTypeName Name, long Start, BigTypeEntrance Entrance)? _arriving;
    private readonly List<(BigTypeName Name, long Start, BigTypeEntrance Exit)> _leaving = [];
    private BigTypeName? _shown;
    private bool _ticking;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != TextProperty)
            return;
        var before = _shown;
        _arriving = null;
        if (before is null || !HandheldRollingText.GetRolls(this) || !IsEffectivelyVisible || TopLevel.GetTopLevel(this) is null)
        {
            _leaving.Clear();
            return;
        }
        var now = Stopwatch.GetTimestamp();
        _leaving.Add((before, now, Entrance));
        if (Fitted is { } next)
            _arriving = (next, now, Entrance);
        RequestFrame();
    }

    public override void Render(DrawingContext context)
    {
        var ink = (Foreground as ISolidColorBrush)?.Color ?? Colors.White;
        var brush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(ink, TextOpacity * ((Foreground as ISolidColorBrush)?.Opacity ?? 1));
        if (DrawMoving(context, brush))
            return;
        _shown = Fitted;
        if (_shown is not { } name)
            return;
        // The box sits at the top of the control; anything below it is room for descenders.
        foreach (var word in name.Words)
            Layout(word.Text, name.Typeface, name.Size, brush).Draw(context, new Point(word.X, word.Top));
    }

    /// <summary>The letters in flight, each its own layout at its place with its pose; false once nothing moves.</summary>
    private bool DrawMoving(DrawingContext context, IBrush brush)
    {
        _leaving.RemoveAll(leaving => Since(leaving.Start) >= BigTypeMotion.DepartureMilliseconds(LetterCount(leaving.Name)));
        if (_arriving is { } a && Since(a.Start) >= BigTypeMotion.ArrivalMilliseconds(LetterCount(a.Name)))
            _arriving = null;
        if (_arriving is null && _leaving.Count == 0)
            return false;
        foreach (var (name, start, exit) in _leaving)
        {
            var count = LetterCount(name);
            foreach (var letter in LettersOf(name))
            {
                if (BigTypeMotion.Depart(exit, letter.Order, count, Since(start)) is { } pose)
                    DrawLetter(context, name, letter, pose, brush);
            }
        }
        if (_arriving is { } arriving)
        {
            _shown = arriving.Name;
            var count = LetterCount(arriving.Name);
            foreach (var letter in LettersOf(arriving.Name))
                DrawLetter(context, arriving.Name, letter, BigTypeMotion.Arrive(arriving.Entrance, letter.Order, count, Since(arriving.Start)), brush);
        }
        else
        {
            // Only leaving letters: the name they gave way to is at rest.
            _shown = Fitted;
            if (_shown is { } resting)
            {
                foreach (var word in resting.Words)
                    Layout(word.Text, resting.Typeface, resting.Size, brush).Draw(context, new Point(word.X, word.Top));
            }
        }
        return true;
    }

    private static void DrawLetter(DrawingContext context, BigTypeName name, PlacedLetter letter, BigTypeLetterPose pose, IBrush brush)
    {
        if (pose.Opacity <= 0f)
            return;
        var size = (float)name.Size;
        var turn = Matrix.CreateTranslation(-letter.Centre.X, -letter.Centre.Y) * Matrix.CreateRotation(pose.Degrees * Math.PI / 180) *
            Matrix.CreateTranslation(letter.Centre.X + (pose.X * size), letter.Centre.Y + (pose.Y * size));
        using (context.PushOpacity(pose.Opacity))
        using (context.PushTransform(turn))
        {
            using var layout = Layout(letter.Text, name.Typeface, name.Size, brush);
            layout.Draw(context, letter.Origin);
        }
    }

    private static float Since(long start) => (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private void RequestFrame()
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            InvalidateVisual();
            if (_arriving is not null || _leaving.Count > 0)
                RequestFrame();
        });
    }

    // ---- Letters ----

    /// <summary>One letter of a fitted name in the control: its text (a grapheme), where its layout is drawn, its middle, its order.</summary>
    private readonly record struct PlacedLetter(string Text, Point Origin, Point Centre, int Order);

    private static int LetterCount(BigTypeName name) => LettersOf(name).Count;

    private static readonly ConditionalWeakTableCache LetterCache = new();

    /// <summary>Every letter of a fitted name (spaces left out), placed where the word's own layout puts it, in reading order.</summary>
    private static IReadOnlyList<PlacedLetter> LettersOf(BigTypeName name) => LetterCache.Get(name, () =>
    {
        var letters = new List<PlacedLetter>(name.Text.Length);
        foreach (var word in name.Words)
        {
            using var layout = Layout(word.Text, name.Typeface, name.Size, null);
            var elements = StringInfo.GetTextElementEnumerator(word.Text);
            while (elements.MoveNext())
            {
                var text = elements.GetTextElement();
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                var index = elements.ElementIndex;
                var x = layout.HitTestTextPosition(index).X;
                var advance = layout.HitTestTextPosition(index + text.Length).X - x;
                var origin = new Point(word.X + x, word.Top);
                letters.Add(new PlacedLetter(text, origin, new Point(origin.X + (advance / 2), origin.Y + (layout.Height / 2)), letters.Count));
            }
        }
        return letters;
    });

    /// <summary>
    /// The fitted name as the native Stage draws it (M47 B6, R1's route): each distinct letter rasterised once, in white, in an ink-tight
    /// cell of one atlas, and every letter placed where this control draws it at rest, the control's top left at <paramref name="origin"/>
    /// on the Stage canvas. Null with no name. Runs on the UI thread (it renders a bitmap).
    /// </summary>
    public BigTypeLetters? Letters(Point origin)
    {
        if (Fitted is not { } name)
            return null;
        var placed = LettersOf(name).Select(letter => new HandheldLetterAtlas.Placed(letter.Text, letter.Origin, letter.Order)).ToList();
        return HandheldLetterAtlas.Build(name.Text, placed, name.Typeface, name.Size, origin, (float)TextOpacity);
    }

    /// <summary>A small cache of each fitted name's placed letters (a name is laid out again only when its fit changes).</summary>
    private sealed class ConditionalWeakTableCache
    {
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<BigTypeName, IReadOnlyList<PlacedLetter>> _table = [];

        public IReadOnlyList<PlacedLetter> Get(BigTypeName name, Func<IReadOnlyList<PlacedLetter>> make) =>
            _table.GetValue(name, _ => make());
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
