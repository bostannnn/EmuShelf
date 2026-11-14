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
        // them no width; a long line, Activity's games, ran under the hints).
        if (HandheldTokens.IsBigType && this.FindControl<Control>("BtHintsShot") is { } hints)
        {
            var lines = new[] { this.FindControl<TextBlock>("BtLine"), this.FindControl<TextBlock>("BtFacts") };
            void Fit()
            {
                foreach (var line in lines)
                {
                    if (line?.Parent is not Control shot || hints.Bounds.Width <= 0 ||
                        hints.TranslatePoint(default, _canvas) is not { } hintsAt || shot.TranslatePoint(default, _canvas) is not { } shotAt)
                    {
                        continue;
                    }
                    // From the line's own left edge to a hint's gap before the hints' first glyph (each shot carries its padding).
                    var max = System.Math.Max(0, hintsAt.X + HandheldTokens.StageShotPad - HandheldTokens.StageHintGap - shotAt.X - HandheldTokens.StageShotPad);
                    if (!line.MaxWidth.Equals(max))
                        line.MaxWidth = max;
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

    // View wiring: the canvas width and the narrow layout's class follow this Stage's screen.
    // Big Type keeps the 16:9 canvas on the narrow screen too, fitted whole, as the mock scales its top screen when the screens
    // swap (M47 B4); today's look lays out a narrow canvas of its own.
    private void ApplyCanvas()
    {
        var narrow = IsNarrow && HandheldTokens.IsClassic;
        _canvas.Width = narrow ? HandheldShellViewModel.NarrowCanvasWidth : HandheldShellViewModel.WideCanvasWidth;
        _canvas.Classes.Set("narrow", narrow);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
