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
public sealed partial class HandheldBigTypeName : Control
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<HandheldBigTypeName, string?>(nameof(Text));

    public static readonly StyledProperty<bool> IsGameTitleProperty =
        AvaloniaProperty.Register<HandheldBigTypeName, bool>(nameof(IsGameTitle));

    public bool IsGameTitle
    {
        get => GetValue(IsGameTitleProperty);
        set => SetValue(IsGameTitleProperty, value);
    }

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<HandheldBigTypeName, IBrush?>(nameof(Foreground), Brushes.White);

    /// <summary>The name's opacity (a missing game's is dimmed), drawn in the ink rather than on the control, which a picture on Android would cut.</summary>
    public static readonly StyledProperty<double> TextOpacityProperty =
        AvaloniaProperty.Register<HandheldBigTypeName, double>(nameof(TextOpacity), 1);

    /// <summary>The box the name is fitted to; the control may be taller, leaving room below it for descenders.</summary>
    public static readonly StyledProperty<double> BoxHeightProperty =
        AvaloniaProperty.Register<HandheldBigTypeName, double>(nameof(BoxHeight), double.NaN);

    /// <summary>The name is a word being typed (M47 B9): only new letters drop in, a deleted one falls off the end.</summary>
    public static readonly StyledProperty<bool> IsTypedProperty =
        AvaloniaProperty.Register<HandheldBigTypeName, bool>(nameof(IsTyped));

    /// <summary>The typed word is empty and the name shows its placeholder (no letter drops in for it).</summary>
    public static readonly StyledProperty<bool> IsPlaceholderProperty =
        AvaloniaProperty.Register<HandheldBigTypeName, bool>(nameof(IsPlaceholder));

    static HandheldBigTypeName()
    {
        AffectsRender<HandheldBigTypeName>(TextProperty, IsGameTitleProperty, IsTypedProperty, ForegroundProperty, TextOpacityProperty, BoxHeightProperty, TextElement.FontFamilyProperty);
    }

    public bool IsTyped
    {
        get => GetValue(IsTypedProperty);
        set => SetValue(IsTypedProperty, value);
    }

    public bool IsPlaceholder
    {
        get => GetValue(IsPlaceholderProperty);
        set => SetValue(IsPlaceholderProperty, value);
    }

    // ---- The typed word (M47 B9, the mock's typeWord) ----

    /// <summary>How the typed word last changed (the native Stage moves its letters from it); null for any other name.</summary>
    public BigTypeTyped? TypedChange { get; private set; }

    // The word as last typed ("" for the placeholder), null while not typing; the size it was fitted at; the letter deleted, if any.
    private string? _typedText;
    private double _typedSize;
    private string? _ghost;

    // On screen: the typed word's letters moving since the change, and the name before typing leaving upward.
    private (BigTypeName Name, long Start, BigTypeTyped Change, string? Ghost)? _typing;
    private readonly List<(BigTypeName Name, long Start)> _leavingUp = [];

    /// <summary>
    /// The typed word changed (or typing began or ended): what is new, what was deleted and the size before, as the mock's typeWord works
    /// them out (the letters after the part both words share drop in; the one deleted falls off the end).
    /// </summary>
    private void NoteTyped(BigTypeName? before)
    {
        _ghost = null;
        if (!IsTyped)
        {
            TypedChange = null;
            _typedText = null;
            return;
        }
        var now = IsPlaceholder ? string.Empty : Text?.Trim() ?? string.Empty;
        var fitted = Fitted;
        var anchor = new Point(0, double.IsNaN(BoxHeight) ? Bounds.Height : Math.Min(BoxHeight, Bounds.Height));
        if (_typedText is not { } was)
        {
            // Typing began: the name there leaves upward; the placeholder is simply there, a word drops in whole.
            TypedChange = new BigTypeTyped(now.Length == 0 ? int.MaxValue : 0, 0f, (float)anchor.X, (float)anchor.Y, Begins: true);
        }
        else
        {
            var common = 0;
            while (common < was.Length && common < now.Length && was[common] == now[common])
                common++;
            // The placeholder never drops in (the mock's typeWord animates only typed letters).
            var fresh = now.Length == 0 ? int.MaxValue : now.Take(common).Count(ch => !char.IsWhiteSpace(ch));
            if (now.Length < was.Length && now.Length > 0 && !char.IsWhiteSpace(was[^1]))
                _ghost = was[^1].ToString();
            TypedChange = new BigTypeTyped(fresh, (float)_typedSize, (float)anchor.X, (float)anchor.Y, Begins: false);
        }
        _typedText = now;
        _typedSize = fitted?.Size ?? 0;
        if (before is not null && TypedChange.Begins)
            _leavingUp.Add((before, Stopwatch.GetTimestamp()));
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
    private (string Text, Size Box, FontFamily Family, bool GameTitle, string Language)? _fittedFor;
    private BigTypeName? _fitted;

    /// <summary>The fit for the current text and box (null with no text). Laid out on demand; tests read it.</summary>
    public BigTypeName? Fitted
    {
        get
        {
            var text = Text?.Trim() ?? string.Empty;
            var box = new Size(Bounds.Width, double.IsNaN(BoxHeight) ? Bounds.Height : Math.Min(BoxHeight, Bounds.Height));
            var family = TextElement.GetFontFamily(this);
            var compose = IsGameTitle && !IsTyped;
            var language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var key = (text, box, family, compose, language);
            if (_fittedFor == key)
                return _fitted;
            _fittedFor = key;
            var face = new Typeface(family, FontStyle.Normal, HandheldTokens.DisplayNameWeight);
            _fitted = text.Length == 0 || box.Width <= 0 || box.Height <= 0 ? null
                : compose ? FitGameTitle(text, face, box.Width, box.Height, language)
                : Fit(text, face, box.Width, box.Height);
            return _fitted;
        }
    }

    /// <summary>Where the next name's letters arrive from (the move that brought it); set before the text changes.</summary>
    public BigTypeEntrance Entrance { get; set; }

    // The letters in flight on screen: the name arriving (and when), and the names leaving (each with when and which way).
    private (BigTypeName Name, long Start, BigTypeEntrance Entrance, bool Quick)? _arriving;
    private readonly List<(BigTypeName Name, long Start, BigTypeEntrance Exit, bool Quick)> _leaving = [];
    private BigTypeName? _shown;
    private bool _ticking;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsTypedProperty && change.GetNewValue<bool>() && _typedText is null && _shown is not null)
        {
            // Typing began over a name with the same text (a rename of the folder in focus): the word still takes its place.
            OnNameChanged();
            return;
        }
        if (change.Property != TextProperty)
            return;
        OnNameChanged();
    }

    private void OnNameChanged()
    {
        var before = _shown;
        var wasTyped = _typedText is not null;
        NoteTyped(before);
        var moves = before is not null && HandheldRollingText.GetRolls(this) && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not null;
        _arriving = null;
        if (TypedChange is { } typed)
        {
            // The typed word stays one layer: its own letters move; only a name there before typing leaves (upward).
            _leaving.Clear();
            _typing = moves && Fitted is { } word ? (word, Stopwatch.GetTimestamp(), typed, _ghost) : null;
            if (!moves)
                _leavingUp.Clear();
            if (moves)
                RequestFrame();
            return;
        }
        _typing = null;
        _leavingUp.Clear();
        if (!moves)
        {
            _leaving.Clear();
            return;
        }
        var now = Stopwatch.GetTimestamp();
        // M47 B11: within a run of the last name (a held direction, quick taps) the new one follows in the short form and the names
        // already leaving go at once, so nothing queues or stacks.
        var run = HandheldInputRun.Of(this);
        if (run)
            _leaving.Clear();
        _leaving.Add((before!, now, Entrance, run));
        if (Fitted is { } next)
            _arriving = (next, now, Entrance, run);
        // The word typed leaves as a name does (the mock's setWord after closeKb).
        _ = wasTyped;
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
            Layout(word.Text, name.Typeface, word.Size, brush).Draw(context, new Point(word.X, word.Top));
    }

    /// <summary>The letters in flight, each its own layout at its place with its pose; false once nothing moves.</summary>
    private bool DrawMoving(DrawingContext context, IBrush brush)
    {
        if (DrawTyping(context, brush))
            return true;
        _leaving.RemoveAll(leaving => Since(leaving.Start) >=
            (leaving.Quick ? BigTypeMotion.RunDepartureMilliseconds() : BigTypeMotion.DepartureMilliseconds(LetterCount(leaving.Name))));
        if (_arriving is { } a && Since(a.Start) >= (a.Quick ? BigTypeMotion.RunArrivalMilliseconds() : BigTypeMotion.ArrivalMilliseconds(LetterCount(a.Name))))
            _arriving = null;
        if (_arriving is null && _leaving.Count == 0)
            return false;
        foreach (var (name, start, exit, quick) in _leaving)
        {
            var count = LetterCount(name);
            foreach (var letter in LettersOf(name))
            {
                var leave = quick ? BigTypeMotion.DepartInRun(exit, Since(start)) : BigTypeMotion.Depart(exit, letter.Order, count, Since(start));
                if (leave is { } pose)
                    DrawLetter(context, name, letter, pose, brush);
            }
        }
        if (_arriving is { } arriving)
        {
            _shown = arriving.Name;
            var count = LetterCount(arriving.Name);
            foreach (var letter in LettersOf(arriving.Name))
            {
                var pose = arriving.Quick ? BigTypeMotion.ArriveInRun(arriving.Entrance, Since(arriving.Start))
                    : BigTypeMotion.Arrive(arriving.Entrance, letter.Order, count, Since(arriving.Start));
                DrawLetter(context, arriving.Name, letter, pose, brush);
            }
        }
        else
        {
            // Only leaving letters: the name they gave way to is at rest.
            _shown = Fitted;
            if (_shown is { } resting)
            {
                foreach (var word in resting.Words)
                    Layout(word.Text, resting.Typeface, word.Size, brush).Draw(context, new Point(word.X, word.Top));
            }
        }
        return true;
    }

    private static void DrawLetter(DrawingContext context, BigTypeName name, PlacedLetter letter, BigTypeLetterPose pose, IBrush brush)
    {
        if (pose.Opacity <= 0f)
            return;
        // The native compositor measures motion in the title's em, including for subtitle glyphs.
        var size = (float)name.Size;
        var turn = Matrix.CreateTranslation(-letter.Centre.X, -letter.Centre.Y) * Matrix.CreateRotation(pose.Degrees * Math.PI / 180) *
            Matrix.CreateTranslation(letter.Centre.X + (pose.X * size), letter.Centre.Y + (pose.Y * size));
        using (context.PushOpacity(pose.Opacity))
        using (context.PushTransform(turn))
        {
            using var layout = Layout(letter.Text, name.Typeface, letter.Size, brush);
            layout.Draw(context, letter.Origin);
        }
    }

    /// <summary>
    /// The typed word on screen (M47 B9): the name before it leaving upward, the new letters dropping in, the deleted one falling off the
    /// end, the whole word easing to its new size about its box's bottom left. False once nothing moves.
    /// </summary>
    private bool DrawTyping(DrawingContext context, IBrush brush)
    {
        var still = !HandheldRollingText.GetRolls(this);
        _leavingUp.RemoveAll(up => Since(up.Start) >= BigTypeTypingMotion.NameOutMilliseconds(LetterCount(up.Name), still));
        if (_typing is { } t && TypingDone(t, still))
            _typing = null;
        if (_typing is null && _leavingUp.Count == 0)
            return false;
        foreach (var (name, start) in _leavingUp)
        {
            foreach (var letter in LettersOf(name))
            {
                if (BigTypeTypingMotion.NameOut(letter.Order, Since(start), still) is { } pose)
                    DrawLetter(context, name, letter, pose, brush);
            }
        }
        var word = _typing?.Name ?? Fitted;
        _shown = Fitted;
        if (word is null)
            return true;
        var since = _typing is { } typing ? Since(typing.Start) : float.MaxValue;
        var change = _typing?.Change;
        var size = change is { FromSize: > 0f } c ? BigTypeTypingMotion.TypedSize(c.FromSize, (float)word.Size, since, still) : (float)word.Size;
        var scale = size / (float)word.Size;
        var anchor = new Point(change?.AnchorX ?? 0, change?.AnchorY ?? 0);
        using (context.PushTransform(Matrix.CreateTranslation(-anchor.X, -anchor.Y) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(anchor.X, anchor.Y)))
        {
            foreach (var letter in LettersOf(word))
            {
                var pose = change is { } typed && letter.Order >= typed.FreshFrom
                    ? BigTypeTypingMotion.TypedIn(letter.Order - typed.FreshFrom, since, still)
                    : BigTypeLetterPose.Rest;
                DrawLetter(context, word, letter, pose, brush);
            }
            if (_typing is { Ghost: { } ghost } && GhostOf(word, ghost) is { } placed && BigTypeTypingMotion.TypedOut(since, still) is { } fall)
                DrawLetter(context, word, placed, fall, brush);
        }
        return true;
    }

    private static bool TypingDone((BigTypeName Name, long Start, BigTypeTyped Change, string? Ghost) typing, bool still)
    {
        var since = Since(typing.Start);
        var fresh = typing.Change.FreshFrom == int.MaxValue ? 0 : Math.Max(0, LetterCount(typing.Name) - typing.Change.FreshFrom);
        return since >= Math.Max(Math.Max(BigTypeTypingMotion.TypedInMilliseconds(fresh, still),
            typing.Ghost is null || still ? 0f : BigTypeTypingMotion.TypedLetterOutMilliseconds),
            typing.Change.FromSize > 0f && !still ? BigTypeTypingMotion.TypedResizeMilliseconds : 0f);
    }

    /// <summary>The deleted letter, placed right after the word's last letter (the mock's ghost at the end of the last word).</summary>
    private static PlacedLetter? GhostOf(BigTypeName word, string ghost)
    {
        if (word.Words.Count == 0)
            return null;
        var last = word.Words[^1];
        using var layout = Layout(last.Text, word.Typeface, word.Size, null);
        using var glyph = Layout(ghost, word.Typeface, word.Size, null);
        var origin = new Point(last.X + layout.WidthIncludingTrailingWhitespace, last.Top);
        return new PlacedLetter(ghost, origin, new Point(origin.X + (glyph.WidthIncludingTrailingWhitespace / 2), origin.Y + (glyph.Height / 2)), -1, word.Size);
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
            if (_arriving is not null || _leaving.Count > 0 || _typing is not null || _leavingUp.Count > 0)
                RequestFrame();
        });
    }

    // ---- Letters ----

    /// <summary>One letter of a fitted name in the control: its text (a grapheme), where its layout is drawn, its middle, its order.</summary>
    private readonly record struct PlacedLetter(string Text, Point Origin, Point Centre, int Order, double Size);

    private static int LetterCount(BigTypeName name) => LettersOf(name).Count;

    private static readonly ConditionalWeakTableCache LetterCache = new();

    /// <summary>Every letter of a fitted name (spaces left out), placed where the word's own layout puts it, in reading order.</summary>
    private static IReadOnlyList<PlacedLetter> LettersOf(BigTypeName name) => LetterCache.Get(name, () =>
    {
        var letters = new List<PlacedLetter>(name.Text.Length);
        foreach (var word in name.Words)
        {
            using var layout = Layout(word.Text, name.Typeface, word.Size, null);
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
                letters.Add(new PlacedLetter(text, origin, new Point(origin.X + (advance / 2), origin.Y + (layout.Height / 2)), letters.Count, word.Size));
            }
        }
        return letters;
    });

    // Per display: the narrow and wide layouts fit text differently. The bound includes all cached RGBA atlases.
    private const int AtlasBudget = 8 * 1024 * 1024;
    private readonly LinkedList<(AtlasKey Key, BigTypeLetters Value)> _atlases = [];
    private int _atlasBytes;
    private readonly record struct AtlasKey(string Text, Size Box, Typeface Face, Point Origin, double Opacity, bool GameTitle, string Language);

    /// <summary>
    /// The fitted name as the native Stage draws it (M47 B6, R1's route): each distinct letter rasterised once, in white, in an ink-tight
    /// cell of one atlas, and every letter placed where this control draws it at rest, the control's top left at <paramref name="origin"/>
    /// on the Stage canvas. Null with no name. Runs on the UI thread (it renders a bitmap).
    /// </summary>
    public BigTypeLetters? Letters(Point origin)
    {
        if (Fitted is not { } name)
            return null;
        var key = new AtlasKey(name.Text, _fittedFor!.Value.Box, name.Typeface, origin, TextOpacity, _fittedFor.Value.GameTitle, _fittedFor.Value.Language);
        if (!IsTyped)
        {
            for (var node = _atlases.First; node is not null; node = node.Next)
            {
                if (node.Value.Key != key)
                    continue;
                _atlases.Remove(node);
                _atlases.AddFirst(node);
                return node.Value.Value;
            }
        }
        var placed = LettersOf(name).Select(letter => new HandheldLetterAtlas.Placed(letter.Text, letter.Origin, letter.Order, letter.Size)).ToList();
        // A typed word's deleted letter goes with it, placed after its last letter, to fall off the end (M47 B9).
        if (_ghost is { } ghost && GhostOf(name, ghost) is { } fall)
            placed.Add(new HandheldLetterAtlas.Placed(fall.Text, fall.Origin, -1));
        var letters = HandheldLetterAtlas.Build(name.Text, placed, name.Typeface, name.Size, origin, (float)TextOpacity);
        if (!IsTyped && letters is not null && letters.Atlas.Data.Length <= AtlasBudget)
        {
            _atlases.AddFirst((key, letters));
            _atlasBytes += letters.Atlas.Data.Length;
            while (_atlasBytes > AtlasBudget || _atlases.Count > 16)
            {
                _atlasBytes -= _atlases.Last!.Value.Value.Atlas.Data.Length;
                _atlases.RemoveLast();
            }
        }
        return letters;
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
    /// <summary>
    /// The fit's smallest size for a box <paramref name="width"/> wide: the mock's (<see cref="HandheldTokens.StageNameMinSize"/>) for the
    /// wide Stage's boxes, smaller by as much as a box is narrower than the wide Stage's narrowest (M47 B11: the narrow Stage beside a
    /// game's case is about half as wide, and a single long word must still fit it).
    /// </summary>
    public static double StageNameMinSize(double width) =>
        HandheldTokens.StageNameMinSize * Math.Min(1, width / HandheldTokens.StageNameWidthBesideMedium);

    public static BigTypeName Fit(string text, Typeface typeface, double width, double height)
        => FitParts(text, text.Split(' ', StringSplitOptions.RemoveEmptyEntries), typeface, width, height,
            HandheldTokens.StageNameMaxLines, StageNameMinSize(width), HandheldTokens.StageNameMaxSize);

    private static BigTypeName FitParts(string text, string[] words, Typeface typeface, double width, double height,
        int maxLines, double minSize, double maxSize)
    {
        const float reference = 100f;
        double Measure(string value)
        {
            using var layout = Layout(value, typeface, reference, null);
            return layout.WidthIncludingTrailingWhitespace;
        }

        var measured = words.Select(word => new BigTypeWord(word, (float)Measure(word), [])).ToArray();
        var space = (float)(Measure("a a") - Measure("aa"));
        var fit = BigTypeLayout.Fit(measured, space, reference, (float)width, (float)height, maxLines,
            (float)minSize, (float)maxSize);
        var size = fit.Size;
        var lineHeight = BigTypeLayout.LineHeight * size;
        // The content area a browser centres in the line box: the face's ascent and descent at this size.
        var metrics = typeface.GlyphTypeface.Metrics;
        var em = (double)metrics.DesignEmHeight;
        var ascent = -metrics.Ascent * size / em;
        var descent = metrics.Descent * size / em;
        using var probe = Layout("A", typeface, size, null);
        var placed = new List<BigTypeNameWord>(fit.Words.Count);
        foreach (var word in fit.Words)
        {
            var lineTop = height - ((fit.Lines - word.Line) * lineHeight);
            var baseline = lineTop + ((lineHeight - (ascent + descent)) / 2) + ascent;
            placed.Add(new BigTypeNameWord(measured[word.Word].Text, word.X, baseline - probe.Baseline, word.Line, size));
        }
        return new BigTypeName(text, typeface, size, fit.Lines, placed);
    }
}

/// <summary>A fitted name: its face and size, its line count and each word's place in the box (the word's layout's top left).</summary>
public sealed record BigTypeName(string Text, Typeface Typeface, double Size, int Lines, IReadOnlyList<BigTypeNameWord> Words)
{
    public double BlockHeight => Words.DistinctBy(word => word.Line).Sum(word => word.Size * BigTypeLayout.LineHeight);
}

/// <summary>One word of a fitted name: where its layout's top left goes in the box, and its line.</summary>
public sealed record BigTypeNameWord(string Text, double X, double Top, int Line, double Size);
