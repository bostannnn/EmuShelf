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
/// A big name on the companion's Big Type layers (M47 B15, proposals-screens.html's <c>letters()</c>): the focused app's name on the Shelf,
/// the focused achievement's on the Wall. One line at <see cref="Size"/>, made smaller to fit <see cref="MaxTextWidth"/>. When its text
/// changes the old letters leave (<see cref="BigTypeScreensMotion.LetterOut"/>) and the new ones land from <see cref="Side"/>
/// (<see cref="BigTypeScreensMotion.LetterIn"/>); after a press within a run of the one before (<see cref="Quick"/>) both take the
/// short form. Held still (<see cref="HandheldRollingText.RollsProperty"/> false) the new name is simply there.
/// </summary>
public sealed class HandheldScreenHeading : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<HandheldScreenHeading, string?>(nameof(Text));
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<HandheldScreenHeading, double>(nameof(Size), 80);
    public static readonly StyledProperty<double> MaxTextWidthProperty =
        AvaloniaProperty.Register<HandheldScreenHeading, double>(nameof(MaxTextWidth), double.PositiveInfinity);
    public static readonly StyledProperty<BigTypeScreenSide> SideProperty = AvaloniaProperty.Register<HandheldScreenHeading, BigTypeScreenSide>(nameof(Side));
    public static readonly StyledProperty<bool> QuickProperty = AvaloniaProperty.Register<HandheldScreenHeading, bool>(nameof(Quick));
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<HandheldScreenHeading, IBrush?>(nameof(Foreground), HandheldTokens.OnColour);

    static HandheldScreenHeading() => AffectsMeasure<HandheldScreenHeading>(TextProperty, SizeProperty, MaxTextWidthProperty);

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double MaxTextWidth { get => GetValue(MaxTextWidthProperty); set => SetValue(MaxTextWidthProperty, value); }
    public BigTypeScreenSide Side { get => GetValue(SideProperty); set => SetValue(SideProperty, value); }
    public bool Quick { get => GetValue(QuickProperty); set => SetValue(QuickProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private sealed record Fitted(Typeface Typeface, double Size, double Spacing, double Width, double LineHeight, IReadOnlyList<(string Text, Point Origin, Point Centre)> Letters);

    private Fitted? _fitted;
    private (Fitted Fitted, BigTypeScreenSide Side, bool Quick, long Start)? _ghost;
    private (BigTypeScreenSide Side, bool Quick, long Start)? _arrival;
    private bool _ticking;

    /// <summary>The size the name is drawn at after fitting (tests read it).</summary>
    internal double FittedSize => (_fitted ??= Fit())?.Size ?? 0;

    /// <summary>Whether letters are still arriving or leaving.</summary>
    internal bool IsMoving => _arrival is not null || _ghost is not null;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            var before = _fitted;
            _fitted = null;
            var moves = HandheldRollingText.GetRolls(this) && IsEffectivelyVisible && TopLevel.GetTopLevel(this) is not null;
            var now = Stopwatch.GetTimestamp();
            _ghost = moves && before is not null ? (before, Side, Quick, now) : null;
            _arrival = moves && !string.IsNullOrEmpty(Text) ? (Side, Quick, now) : null;
            InvalidateVisual();
            RequestFrame();
        }
        else if (change.Property == SizeProperty || change.Property == MaxTextWidthProperty || change.Property == TextElement.FontFamilyProperty)
        {
            _fitted = null;
            InvalidateVisual();
        }
    }

    /// <summary>The letters land again from below (the layer opened).</summary>
    public void Arrive()
    {
        _ghost = null;
        _arrival = HandheldRollingText.GetRolls(this) && !string.IsNullOrEmpty(Text) ? (BigTypeScreenSide.Up, false, Stopwatch.GetTimestamp()) : null;
        InvalidateVisual();
        RequestFrame();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var fitted = _fitted ??= Fit();
        return fitted is null ? default : new Size(fitted.Width, fitted.LineHeight);
    }

    public override void Render(DrawingContext context)
    {
        if (Foreground is not { } brush)
            return;
        var still = !HandheldRollingText.GetRolls(this);
        if (_ghost is { } ghost)
        {
            var since = Since(ghost.Start);
            var any = false;
            for (var i = 0; i < ghost.Fitted.Letters.Count; i++)
            {
                if (BigTypeScreensMotion.LetterOut(i, ghost.Side, since, ghost.Quick, still) is not { } pose)
                    continue;
                any = true;
                Draw(context, ghost.Fitted, i, pose, brush);
            }
            if (!any && since > 0f)
                _ghost = null;
        }
        if ((_fitted ??= Fit()) is not { } fitted)
            return;
        var arrivalSince = _arrival is { } arrival ? Since(arrival.Start) : float.PositiveInfinity;
        for (var i = 0; i < fitted.Letters.Count; i++)
        {
            var pose = _arrival is { } a
                ? BigTypeScreensMotion.LetterIn(i, fitted.Letters.Count, a.Side, arrivalSince, a.Quick, still)
                : BigTypeLetterPose.Rest;
            Draw(context, fitted, i, pose, brush);
        }
        if (_arrival is { } done && (still || arrivalSince >= (done.Quick ? BigTypeMotion.RunInMilliseconds
                : BigTypeScreensMotion.LettersInTotal(fitted.Letters.Count))))
            _arrival = null;
    }

    private static float Since(long start) => (float)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private static void Draw(DrawingContext context, Fitted fitted, int index, BigTypeLetterPose pose, IBrush brush)
    {
        if (pose.Opacity <= 0f)
            return;
        var (text, origin, centre) = fitted.Letters[index];
        using var layout = new TextLayout(text, fitted.Typeface, fitted.Size, brush, letterSpacing: fitted.Spacing);
        if (pose == BigTypeLetterPose.Rest)
        {
            layout.Draw(context, origin);
            return;
        }
        var turn = Matrix.CreateTranslation(-centre.X, -centre.Y) * Matrix.CreateRotation(pose.Degrees * Math.PI / 180) *
            Matrix.CreateTranslation(centre.X + (pose.X * fitted.Size), centre.Y + (pose.Y * fitted.Size));
        using (context.PushOpacity(pose.Opacity))
        using (context.PushTransform(turn))
            layout.Draw(context, origin);
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
            if (IsMoving)
                RequestFrame();
        });
    }

    // One line at Size, smaller to fit MaxTextWidth (the page's letters() with o.max); its line box 1.15 of the size, the face's
    // ascent and descent centred in it as a browser does.
    private Fitted? Fit()
    {
        if (string.IsNullOrEmpty(Text))
            return null;
        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, HandheldTokens.HeadingWeight);
        var size = Size;
        var spacingEm = HandheldTokens.DisplayLetterSpacing;
        using (var natural = new TextLayout(Text, typeface, size, null, letterSpacing: spacingEm * size))
        {
            var width = natural.WidthIncludingTrailingWhitespace;
            if (width > MaxTextWidth && width > 0)
                size = Math.Floor(size * MaxTextWidth / width * 100) / 100;
        }
        var spacing = spacingEm * size;
        var lineHeight = HandheldTokens.HeadingLineShare * size;
        var metrics = typeface.GlyphTypeface.Metrics;
        var em = (double)metrics.DesignEmHeight;
        var ascent = -metrics.Ascent * size / em;
        var descent = metrics.Descent * size / em;
        var baseline = ((lineHeight - (ascent + descent)) / 2) + ascent;
        using var line = new TextLayout(Text, typeface, size, null, letterSpacing: spacing);
        var top = baseline - line.Baseline;
        var letters = new List<(string, Point, Point)>(Text.Length);
        var elements = StringInfo.GetTextElementEnumerator(Text);
        while (elements.MoveNext())
        {
            var text = elements.GetTextElement();
            if (string.IsNullOrWhiteSpace(text))
                continue;
            var index = elements.ElementIndex;
            var x = line.HitTestTextPosition(index).X;
            var advance = line.HitTestTextPosition(index + text.Length).X - x;
            letters.Add((text, new Point(x, top), new Point(x + (advance / 2), lineHeight / 2)));
        }
        return new Fitted(typeface, size, spacing, line.WidthIncludingTrailingWhitespace, lineHeight, letters);
    }
}
