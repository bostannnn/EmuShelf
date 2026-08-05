using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace EmuShelf.App.Controls;

/// <summary>
/// A single-line text that scrolls itself (a gentle there-and-back marquee, with pauses at each end)
/// only when it is too wide for its slot, and sits still otherwise. Used for the spotlight hero title
/// so a long game name reads in full without wrapping to a second line. Font family is inherited from
/// the surrounding Gamepad shell; size/weight/foreground are forwarded to the inner text.
///
/// The scroll is driven by a plain timer that writes the transform offset directly, NOT Avalonia's
/// animation API: running an <c>Animation</c> against a bare <c>TranslateTransform</c> makes the
/// transform animator cast it to <c>Visual</c> and throw (InvalidCastException), which crashed the
/// render pass the moment a long title started scrolling.
/// </summary>
public sealed class MarqueeTextBlock : Decorator
{
    private const double PixelsPerSecond = 55;
    private const double StartPauseMs = 800;
    private const double EndPauseMs = 1100;
    private const double TailGap = 12;
    private const double FrameMs = 16;

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MarqueeTextBlock, string?>(nameof(Text));

    public static readonly StyledProperty<double> FontSizeProperty =
        TextBlock.FontSizeProperty.AddOwner<MarqueeTextBlock>();

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        TextBlock.FontWeightProperty.AddOwner<MarqueeTextBlock>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<MarqueeTextBlock>();

    private readonly TextBlock _text;
    private readonly TranslateTransform _transform = new();
    private DispatcherTimer? _timer;
    private long _startTicks;
    private double _distance;
    private double _outStartMs, _outEndMs, _backStartMs, _cycleMs;
    private double _lastTextWidth = double.NaN;
    private double _lastViewWidth = double.NaN;

    /// <summary>True after layout when the text is wider than the slot (so it scrolls). Exposed for tests.</summary>
    internal bool IsOverflowing { get; private set; }

    private bool IsScrolling => _timer is { IsEnabled: true };

    static MarqueeTextBlock()
    {
        ClipToBoundsProperty.OverrideDefaultValue<MarqueeTextBlock>(true);
        AffectsMeasure<MarqueeTextBlock>(TextProperty, FontSizeProperty, FontWeightProperty);
    }

    public MarqueeTextBlock()
    {
        _text = new TextBlock
        {
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = _transform,
        };
        Child = _text;
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            _text.Text = Text;
        else if (change.Property == FontSizeProperty)
            _text.FontSize = FontSize;
        else if (change.Property == FontWeightProperty)
            _text.FontWeight = FontWeight;
        else if (change.Property == ForegroundProperty)
            _text.Foreground = Foreground;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Measure the text at its natural (unclipped) width so overflow can be detected.
        _text.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        var height = _text.DesiredSize.Height;
        var width = double.IsInfinity(availableSize.Width) ? _text.DesiredSize.Width : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var textWidth = _text.DesiredSize.Width;
        _text.Arrange(new Rect(0, 0, Math.Max(textWidth, finalSize.Width), finalSize.Height));
        StartOrStop(textWidth, finalSize.Width);
        return finalSize;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopScroll();
        _timer = null; // fully release on detach; StopScroll keeps the instance for reuse mid-life
        _lastTextWidth = double.NaN;
        _lastViewWidth = double.NaN;
    }

    private void StopScroll()
    {
        _timer?.Stop();
        _transform.X = 0;
    }

    private void StartOrStop(double textWidth, double viewWidth)
    {
        var overflow = textWidth - viewWidth;
        IsOverflowing = overflow > 1 && viewWidth > 0;
        var shouldScroll = IsOverflowing && this.IsAttachedToVisualTree() && IsEffectivelyVisible;

        // Layout can run several passes; if the text width and slot are unchanged and the loop is
        // already in the right state, leave the running scroll alone rather than restart it (which
        // would jump the title back to the start).
        static bool Close(double a, double b) => Math.Abs(a - b) < 0.5;
        if (Close(textWidth, _lastTextWidth) && Close(viewWidth, _lastViewWidth) &&
            shouldScroll == IsScrolling)
            return;
        _lastTextWidth = textWidth;
        _lastViewWidth = viewWidth;

        StopScroll();
        if (!shouldScroll)
            return; // it fits (or isn't shown) — leave it static

        _distance = overflow + TailGap;
        var scrollMs = _distance / PixelsPerSecond * 1000.0;
        _outStartMs = StartPauseMs;
        _outEndMs = _outStartMs + scrollMs;
        _backStartMs = _outEndMs + EndPauseMs;
        _cycleMs = _backStartMs + scrollMs;

        _startTicks = Environment.TickCount64;
        // Reuse one timer instance — the focused game (and so the title) changes on every list step,
        // which would otherwise churn a DispatcherTimer per move.
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(FrameMs), DispatcherPriority.Render, OnTick);
        _timer.Start();
    }

    // One frame of the there-and-back scroll: hold at each end, ease across the middle. Writes the
    // transform offset directly — no layout read, no animator — so it can never re-enter layout.
    private void OnTick(object? sender, EventArgs e)
    {
        // The spotlight can be toggled back to the cover grid without detaching this control, which
        // would leave the timer ticking on a hidden hero. Self-stop when not visible; the next layout
        // pass (when the spotlight is shown again) restarts it.
        if (!IsEffectivelyVisible)
        {
            StopScroll();
            return;
        }

        var elapsed = (Environment.TickCount64 - _startTicks) % (long)Math.Max(1, _cycleMs);
        double offset;
        if (elapsed < _outStartMs)
            offset = 0;
        else if (elapsed < _outEndMs)
            offset = Ease((elapsed - _outStartMs) / (_outEndMs - _outStartMs)) * _distance;
        else if (elapsed < _backStartMs)
            offset = _distance;
        else
            offset = (1 - Ease((elapsed - _backStartMs) / (_cycleMs - _backStartMs))) * _distance;

        _transform.X = -offset;
    }

    // Smoothstep, so the title eases in and out at each end rather than jerking.
    private static double Ease(double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        return p * p * (3 - 2 * p);
    }
}
