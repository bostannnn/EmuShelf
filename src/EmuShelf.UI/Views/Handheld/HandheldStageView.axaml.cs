using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmuShelf.App.Controls;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views.Handheld;

namespace EmuShelf.App.Views;

/// <summary>
/// Handheld mode's top screen. A pure view onto <c>HandheldShellViewModel</c>; its motion (centre pop, folder
/// flap, launch landing) is <see cref="HandheldStageMotion"/>. Where the platform has a native Stage
/// (<see cref="HandheldNativeStage"/>), <see cref="HandheldNativeStageFeed"/> pictures this view's pieces for it.
/// </summary>
public partial class HandheldStageView : UserControl
{
    /// <summary>
    /// Raised once the native Stage has everything this view shows: after it was shown again, or moved to the other
    /// screen's canvas (M45 H5), so the platform can bring that screen into view.
    /// </summary>
    public event System.Action? NativeCaughtUp;

    public HandheldStageView()
        : this(HandheldNativeStage.Host, pinnedNarrow: null)
    {
    }

    /// <summary>
    /// A Stage drawn by <paramref name="native"/> (if any). <paramref name="pinnedNarrow"/> keeps it on one screen's
    /// canvas (the 1240×1080 one when true): the Android head keeps one Stage per display, so a swap only changes
    /// which one is in front (M45 H5). Null follows the swap (one Stage that moves).
    /// </summary>
    public HandheldStageView(IHandheldNativeStage? native, bool? pinnedNarrow)
    {
        PinnedNarrow = pinnedNarrow;
        InitializeComponent();
        // View wiring: the look's typeface on the root, which every text here inherits (M47 B2; none in today's look).
        HandheldTokens.ApplyFace(this);
        _canvas = this.FindControl<Grid>("StageCanvas")!;
        // View wiring (M47 B7): under Big Type the two lines under the name end before the hints' corner, in an ellipsis (the mock sets
        // them no width; a long line, Activity's games, ran under the hints). On the narrow screen (M47 B11) the hints have a row of their
        // own, so the lines run to the hints' right margin instead, and the console's name ends a gap before the battery and the clock.
        if (HandheldTokens.IsBigType && this.FindControl<Control>("BtHintsShot") is { } hints)
        {
            var lines = new[] { this.FindControl<TextBlock>("BtLine"), this.FindControl<TextBlock>("BtFacts") };
            var label = this.FindControl<TextBlock>("BtLabel");
            var status = this.FindControl<Control>("BtStatusShot");
            void Fit()
            {
                var narrow = _canvas.Classes.Contains("btnarrow");
                foreach (var line in lines)
                {
                    if (line?.Parent is not Control shot || hints.Bounds.Width <= 0 ||
                        hints.TranslatePoint(default, _canvas) is not { } hintsAt || shot.TranslatePoint(default, _canvas) is not { } shotAt)
                    {
                        continue;
                    }
                    // From the line's own left edge to a hint's gap before the hints' first glyph (each shot carries its padding); on the
                    // narrow screen to the hints' right edge.
                    var end = narrow ? hintsAt.X + hints.Bounds.Width - HandheldTokens.StageShotPad : hintsAt.X + HandheldTokens.StageShotPad - HandheldTokens.StageHintGap;
                    var max = System.Math.Max(0, end - shotAt.X - HandheldTokens.StageShotPad);
                    if (!line.MaxWidth.Equals(max))
                        line.MaxWidth = max;
                }
                // A message on the line that runs to two lines (M47 B10) grows down into the second line's row, which gives way, rather
                // than up into the name's descenders (M47 B11; found on the narrow screen, the same on the wide one).
                // Any other long text (M47 B14) runs on at a whole word rather than end in an ellipsis. The line and the second line share
                // three rows under the name (stage.LineRowsShared): their own two and the one free below them, which is all the room
                // there is above the screen's edge. The line takes at most two, growing down into the second line's row; the second line
                // takes the rest, moved down a row of its own under a line of two, or growing down into the free row under a line of one.
                // Neither reaches up into the name. Only text past its rows ends in an ellipsis, at a whole word.
                if (lines[0] is { } message && message.Parent is Control messageShot && lines[1] is { } facts && facts.Parent is Control factsShot)
                {
                    var isMessage = message.Classes.Contains("message");
                    var lineMax = HandheldTokens.StageLineRowsShared - 1;
                    if (message.MaxLines != lineMax)
                        message.MaxLines = lineMax;
                    var lineRows = message.TextLayout.TextLines.Count;
                    var lineBy = 0d;
                    for (var row = 1; row < lineRows; row++)
                        lineBy += message.TextLayout.TextLines[row].Height;
                    var lineMargin = narrow ? HandheldTokens.StageNarrowLineShotMargin : HandheldTokens.StageLineShotMargin;
                    var factsMargin = narrow ? HandheldTokens.StageNarrowFactsShotMargin : HandheldTokens.StageFactsShotMargin;
                    if (lineBy > 0)
                    {
                        var at = new Thickness(lineMargin.Left, lineMargin.Top, lineMargin.Right, lineMargin.Bottom - lineBy);
                        if (messageShot.Margin != at)
                            messageShot.Margin = at;
                    }
                    else
                    {
                        messageShot.ClearValue(MarginProperty);
                    }
                    if (isMessage && lineRows > 1)
                    {
                        if (factsShot.Opacity != 0)
                            factsShot.Opacity = 0;
                        factsShot.ClearValue(MarginProperty);
                    }
                    else
                    {
                        factsShot.ClearValue(OpacityProperty);
                        var factsMax = System.Math.Max(1, HandheldTokens.StageLineRowsShared - lineRows);
                        if (facts.MaxLines != factsMax)
                            facts.MaxLines = factsMax;
                        // Under a line of two it moves down by a row of its own (the line's row is taller, but the rows overlap by their
                        // leading, as B7 set them); under a line of one, its own second row grows down into the free row.
                        var factsRows = facts.TextLayout.TextLines;
                        var factsBy = lineRows > 1 && factsRows.Count > 0 ? factsRows[0].Height
                            : factsRows.Count > 1 ? factsRows[1].Height : 0;
                        if (factsBy > 0)
                        {
                            var at = new Thickness(factsMargin.Left, factsMargin.Top, factsMargin.Right, factsMargin.Bottom - factsBy);
                            if (factsShot.Margin != at)
                                factsShot.Margin = at;
                        }
                        else
                        {
                            factsShot.ClearValue(MarginProperty);
                        }
                    }
                }
                if (label?.Parent is Control labelShot && status is not null)
                {
                    var max = double.PositiveInfinity;
                    if (narrow && status.Bounds.Width > 0 && status.TranslatePoint(default, _canvas) is { } statusAt &&
                        labelShot.TranslatePoint(default, _canvas) is { } labelAt)
                    {
                        max = System.Math.Max(0, statusAt.X + HandheldTokens.StageShotPad - HandheldTokens.StageNarrowLabelGap - labelAt.X - HandheldTokens.StageShotPad);
                    }
                    if (!label.MaxWidth.Equals(max))
                        label.MaxWidth = max;
                }
            }
            hints.LayoutUpdated += (_, _) => Fit();
        }
        // View wiring only: a GPU path that cannot start tells the Stage to keep flat covers.
        foreach (var name in new[] { "Medium", "BtMedium" })
        {
            if (this.FindControl<Media3DControl>(name) is { } medium)
            {
                medium.InitializationFailed += (_, _) =>
                {
                    if (DataContext is HandheldShellViewModel shell)
                        shell.Stage.IsMediumUnavailable = true;
                };
            }
        }
        var motion = new HandheldStageMotion(this, () => IsNarrow);
        // On a platform with a native Stage this view stays laid out but invisible, and lends it its pieces.
        var feed = native is { } host ? new HandheldNativeStageFeed(this, motion, host, () => IsNarrow) : null;
        if (feed is not null)
            feed.CaughtUp += () => NativeCaughtUp?.Invoke();
        DataContextChanged += (_, _) =>
        {
            if (_shell is { } old)
                old.PropertyChanged -= OnShellChanged;
            _shell = DataContext as HandheldShellViewModel;
            if (_shell is { } shell)
                shell.PropertyChanged += OnShellChanged;
            ApplyCanvas();
            motion.Attach(_shell);
            feed?.Attach(_shell);
        };
        ApplyCanvas();
        // Any touch on the Stage counts as activity, and the first one after the idle drift only wakes it (M45 H6d).
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_shell?.WakeOnTouch() == true)
                e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private readonly Grid _canvas;
    private HandheldShellViewModel? _shell;

    /// <summary>The screen this Stage is kept on (the narrow one when true), or null when it follows the swap.</summary>
    public bool? PinnedNarrow { get; }

    /// <summary>Drawn for the 1240×1080 screen: its pinned screen, or the one the Stage is on now.</summary>
    public bool IsNarrow => PinnedNarrow ?? _shell?.IsSwapped == true;

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HandheldShellViewModel.IsSwapped) && PinnedNarrow is null)
            ApplyCanvas();
    }

    // View wiring: the canvas width and the narrow layout's class follow this Stage's screen, in either look. Today's look moves its
    // pieces with "narrow"; Big Type lays its Stage out again for the narrow screen with "btnarrow" (M47 B11; B4 had shown the 16:9
    // canvas whole, letterboxed).
    private void ApplyCanvas()
    {
        _canvas.Width = IsNarrow ? HandheldShellViewModel.NarrowCanvasWidth : HandheldShellViewModel.WideCanvasWidth;
        _canvas.Classes.Set("narrow", IsNarrow && HandheldTokens.IsClassic);
        _canvas.Classes.Set("btnarrow", IsNarrow && HandheldTokens.IsBigType);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
