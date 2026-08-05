using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace EmuShelf.App.Controls;

/// <summary>
/// A single-line text that scrolls itself (a gentle there-and-back marquee, with pauses at each end)
/// only when it is too wide for its slot, and sits still otherwise. Used for the spotlight hero title
/// so a long game name reads in full without wrapping to a second line. Font family is inherited from
/// the surrounding Gamepad shell; size/weight/foreground are forwarded to the inner text.
/// </summary>
public sealed class MarqueeTextBlock : Decorator
{
    private const double PixelsPerSecond = 55;
    private const double StartPauseSeconds = 0.8;
    private const double EndPauseSeconds = 1.1;
    private const double TailGap = 12;

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
    private CancellationTokenSource? _animation;
    private double _lastTextWidth = double.NaN;
    private double _lastViewWidth = double.NaN;

    /// <summary>True after layout when the text is wider than the slot (so it scrolls). Exposed for tests.</summary>
    internal bool IsOverflowing { get; private set; }

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
        _animation?.Cancel();
        _animation = null;
        _transform.X = 0;
        _lastTextWidth = double.NaN;
        _lastViewWidth = double.NaN;
    }

    private void StartOrStop(double textWidth, double viewWidth)
    {
        var overflow = textWidth - viewWidth;
        IsOverflowing = overflow > 1 && viewWidth > 0;
        var shouldScroll = IsOverflowing && this.IsAttachedToVisualTree() && IsEffectivelyVisible;

        // Layout can run several passes; if the text width and slot are unchanged and the loop is
        // already in the right state, leave the running animation alone rather than restart it (which
        // would jump the title back to the start).
        static bool Close(double a, double b) => Math.Abs(a - b) < 0.5;
        if (Close(textWidth, _lastTextWidth) && Close(viewWidth, _lastViewWidth) &&
            shouldScroll == (_animation is not null))
            return;
        _lastTextWidth = textWidth;
        _lastViewWidth = viewWidth;

        _animation?.Cancel();
        _animation = null;
        _transform.X = 0;

        if (!shouldScroll)
            return; // it fits (or isn't shown) — leave it static

        var distance = overflow + TailGap;
        var scroll = distance / PixelsPerSecond;
        var total = StartPauseSeconds + scroll + EndPauseSeconds + scroll;

        double Cue(double seconds) => seconds / total;

        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(total),
            IterationCount = IterationCount.Infinite,
            Easing = new SineEaseInOut(),
            Children =
            {
                Frame(0, 0),
                Frame(Cue(StartPauseSeconds), 0),
                Frame(Cue(StartPauseSeconds + scroll), -distance),
                Frame(Cue(StartPauseSeconds + scroll + EndPauseSeconds), -distance),
                Frame(1, 0),
            },
        };

        var cts = new CancellationTokenSource();
        _animation = cts;
        _ = animation.RunAsync(_transform, cts.Token);
    }

    private static KeyFrame Frame(double cue, double x) => new()
    {
        Cue = new Cue(cue),
        Setters = { new Setter(TranslateTransform.XProperty, x) },
    };
}
