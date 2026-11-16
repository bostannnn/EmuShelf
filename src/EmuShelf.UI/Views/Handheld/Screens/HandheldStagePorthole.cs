using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The idle artwork as the Porthole (M47 B15, proposals-screens.html option 12, the owner's pick of 16 November 2026), on the Avalonia
/// Stage: the console's backdrop stays and the game's artwork sits in a round window to the right of its name. It rises through the hinge
/// when the drift begins (<see cref="BigTypeScreensMotion.PortIn"/>), floats, takes the next game's artwork through a circle growing from
/// its bottom (<see cref="BigTypeScreensMotion.PortWipeRadius"/>) and drops back through the hinge when a press wakes the drift
/// (<see cref="BigTypeScreensMotion.PortOut"/>). The float runs on the compositor, so a frame drawn from the controls (a design render, the
/// native Stage's picture of this view) never catches it mid-float. The native Stage draws the same from the same maths.
/// </summary>
public sealed class HandheldStagePorthole : Control
{
    public static readonly StyledProperty<Bitmap?> ArtProperty = AvaloniaProperty.Register<HandheldStagePorthole, Bitmap?>(nameof(Art));
    public static readonly StyledProperty<bool> NarrowProperty = AvaloniaProperty.Register<HandheldStagePorthole, bool>(nameof(Narrow));

    private static readonly SplineEasing EaseInOut = new(0.42, 0, 0.58, 1);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Bitmap? _shown;
    private Bitmap? _under;
    private float _arriveStart = float.NaN;
    private float _changeStart = float.NegativeInfinity;
    private float _leaveStart = float.NaN;
    private bool _ticking;
    private bool _floating;

    static HandheldStagePorthole() => AffectsRender<HandheldStagePorthole>(NarrowProperty);

    public HandheldStagePorthole()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
    }

    /// <summary>The artwork in the window; null drops the window back through the hinge.</summary>
    public Bitmap? Art { get => GetValue(ArtProperty); set => SetValue(ArtProperty, value); }

    /// <summary>Drawn for the 1240-wide Stage (the screens swapped).</summary>
    public bool Narrow { get => GetValue(NarrowProperty); set => SetValue(NarrowProperty, value); }

    private float Now => (float)_clock.Elapsed.TotalMilliseconds;

    private bool Still => !HandheldRollingText.GetRolls(this);

    /// <summary>Whether the window shows (tests read it).</summary>
    internal bool IsShowing => _shown is not null && float.IsNaN(_leaveStart);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ArtProperty)
            return;
        var now = Now;
        var still = Still;
        var next = change.GetNewValue<Bitmap?>();
        if (next is null)
        {
            if (_shown is not null && float.IsNaN(_leaveStart))
                _leaveStart = still ? float.NegativeInfinity : now;
            StopFloat();
        }
        else if (_shown is null || !float.IsNaN(_leaveStart))
        {
            // The drift began: the window rises through the hinge with the first game's artwork.
            _shown = next;
            _under = null;
            _leaveStart = float.NaN;
            _arriveStart = still ? float.NegativeInfinity : now;
            _changeStart = float.NegativeInfinity;
        }
        else if (!ReferenceEquals(next, _shown))
        {
            // The next game: its artwork grows inside the window through a circle from the window's bottom.
            _under = _shown;
            _shown = next;
            _changeStart = still ? float.NegativeInfinity : now;
        }
        Request();
    }

    private Rect Window => HandheldTokens.PortRect(Narrow);

    public override void Render(DrawingContext context)
    {
        if (_shown is not { } shown)
            return;
        var now = Now;
        var still = Still;
        var window = Window;
        float drop, scale, opacity;
        var moving = false;
        if (!float.IsNaN(_leaveStart))
        {
            var since = now - _leaveStart;
            (drop, scale, opacity) = BigTypeScreensMotion.PortOut(since, still);
            if (since >= BigTypeScreensMotion.PortOutTotal(still))
            {
                // Gone: the bitmaps were the shell's (it lets them go); the window holds none of them.
                _shown = _under = null;
                return;
            }
            moving = true;
        }
        else
        {
            var since = now - _arriveStart;
            (drop, scale, opacity) = BigTypeScreensMotion.PortIn(since, still);
            moving |= !still && since < BigTypeScreensMotion.PortInMilliseconds;
            if (!moving && !_floating && !still && IsEffectivelyVisible)
            {
                // Started after this pass (a render pass changes nothing).
                _floating = true;
                Avalonia.Threading.Dispatcher.UIThread.Post(StartFloat, Avalonia.Threading.DispatcherPriority.Background);
            }
        }
        var size = window.Width * scale;
        var centre = new Point(window.Center.X, window.Center.Y + drop);
        var box = new Rect(centre.X - (size / 2), centre.Y - (size / 2), size, size);
        using (context.PushOpacity(opacity))
        using (context.PushGeometryClip(new EllipseGeometry(box)))
        {
            var wipe = BigTypeScreensMotion.PortWipeRadius(now - _changeStart, (float)window.Width, still);
            var wiping = _under is not null && wipe < BigTypeScreensMotion.PortWipeShare * window.Width;
            if (wiping)
            {
                context.DrawImage(_under!, Cover(_under!, box), box);
                var radius = wipe * scale;
                var bottom = new Point(box.Center.X, box.Bottom);
                using (context.PushGeometryClip(new EllipseGeometry(new Rect(bottom.X - radius, bottom.Y - radius, radius * 2, radius * 2))))
                    context.DrawImage(shown, Cover(shown, box), box);
                moving = true;
            }
            else
            {
                _under = null;
                context.DrawImage(shown, Cover(shown, box), box);
            }
        }
        if (moving)
            Request();
    }

    // The part of the artwork that fills the round window (UniformToFill, centred: the page's canvas is square).
    private static Rect Cover(Bitmap image, Rect box)
    {
        var size = image.Size;
        var k = Math.Max(box.Width / size.Width, box.Height / size.Height);
        var w = box.Width / k;
        var h = box.Height / k;
        return new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h);
    }

    // The float: from 10 px down to 14 px up and back, 5 s each way, on the compositor for the minute the resting motion lives.
    private void StartFloat()
    {
        if (!_floating || _shown is null)
            return;
        HandheldComposition.Swing(this, "Offset", default, new Vector3D(0, BigTypeScreensMotion.PortFloatFrom, 0),
            new Vector3D(0, BigTypeScreensMotion.PortFloatTo, 0), BigTypeScreensMotion.PortFloatMilliseconds,
            (int)(EmuShelf.Rendering.Stage.StageRest.AfterMilliseconds / BigTypeScreensMotion.PortFloatMilliseconds), EaseInOut, relativeToOffset: true);
    }

    private void StopFloat()
    {
        if (_floating)
            HandheldScreenRing.StopBob(this);
        _floating = false;
    }

    private void Request()
    {
        if (_ticking || TopLevel.GetTopLevel(this) is not { } top)
        {
            if (!_ticking)
                InvalidateVisual();
            return;
        }
        _ticking = true;
        top.RequestAnimationFrame(_ =>
        {
            _ticking = false;
            InvalidateVisual();
        });
    }
}
