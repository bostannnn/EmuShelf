using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The one white ring on the companion's Shelf and Wall (M47 B15): a child of the grid that draws it, placed each frame on B5's path
/// (<see cref="BigTypeLauncherMotion"/>: the stretch between targets, B11's glide under held input, the pop on opening). At rest it
/// breathes on the compositor for the minute after the last press, as the Launcher's ring does (M47 B10a: about its own middle), so a
/// frame drawn from the controls (a design render) never catches it mid-breath.
/// </summary>
internal sealed class HandheldScreenRing
{
    private static readonly SplineEasing EaseInOut = new(0.42, 0, 0.58, 1);
    private readonly Border _ring;
    private bool _breathing;
    private Point _breathCentre;

    public HandheldScreenRing(Panel host)
    {
        _ring = new Border
        {
            BorderBrush = HandheldTokens.RingBand,
            BorderThickness = new Thickness(HandheldTokens.RingStroke),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false,
        };
        host.Children.Add(_ring);
    }

    /// <summary>The ring's path and its rest (no breath of its own: the compositor breathes it).</summary>
    public BigTypeLauncherMotion Motion { get; } = new() { Breathes = false };

    /// <summary>The ring's box as last placed (tests read it).</summary>
    public Rect Placed => _ring.IsVisible ? new Rect(_ring.RenderTransform?.Value.M31 ?? 0, _ring.RenderTransform?.Value.M32 ?? 0, _ring.Width, _ring.Height) : default;

    public void Hide()
    {
        StopBreath();
        _ring.IsVisible = false;
    }

    /// <summary>Places the ring on its way to <paramref name="target"/>; true while it travels or pops.</summary>
    public bool Place(BigTypeRing target, float now, bool still, bool onScreen)
    {
        Motion.HoldStill = still;
        var at = Motion.Ring(target, now, out var opacity, out var moving);
        _ring.IsVisible = true;
        _ring.Width = Math.Max(0, at.Width);
        _ring.Height = Math.Max(0, at.Height);
        _ring.CornerRadius = new CornerRadius(Math.Max(0, at.Radius));
        var shift = new Vector(Math.Round(at.X, 2), Math.Round(at.Y, 2));
        if (_ring.RenderTransform is not TranslateTransform placed || placed.X != shift.X || placed.Y != shift.Y)
            _ring.RenderTransform = new TranslateTransform(shift.X, shift.Y);
        _ring.Opacity = Math.Round(opacity, 3);
        var centre = new Point(at.X + (at.Width / 2), at.Y + (at.Height / 2));
        if (moving || still || !onScreen)
        {
            StopBreath();
        }
        else if (!_breathing || Math.Abs(centre.X - _breathCentre.X) > 0.5 || Math.Abs(centre.Y - _breathCentre.Y) > 0.5)
        {
            StopBreath();
            if (HalfCycles(now, BigTypeMotion.BreathPeriodMilliseconds) is { } count)
            {
                _breathing = true;
                _breathCentre = centre;
                HandheldComposition.Swing(_ring, "Scale", centre, new Vector3D(1, 1, 1),
                    new Vector3D(BigTypeMotion.BreathScale, BigTypeMotion.BreathScale, 1), BigTypeMotion.BreathPeriodMilliseconds / 2, count, EaseInOut);
            }
        }
        return moving;
    }

    private void StopBreath()
    {
        if (_breathing)
            HandheldComposition.Scale(_ring, 1, 1, 1);
        _breathing = false;
    }

    /// <summary>How many half cycles of a swing fit in what is left of the minute after the last press (even, so it ends at rest).</summary>
    public int? HalfCycles(float now, float period)
    {
        var left = StageRest.AfterMilliseconds - (now - Motion.LastInput);
        if (float.IsNaN(left))
            left = StageRest.AfterMilliseconds;
        if (left <= 0)
            return null;
        var halves = (int)Math.Ceiling(left / (period / 2));
        return Math.Max(2, halves + (halves % 2));
    }

    /// <summary>The focused tile's bob on the compositor (B5's: up and back, ease-in-out each way) for what is left of the minute.</summary>
    public void Bob(Control tile, float now, bool still)
    {
        if (still || HalfCycles(now, BigTypeMotion.BobPeriodMilliseconds) is not { } count)
            return;
        HandheldComposition.Swing(tile, "Offset", default, default, new Vector3D(0, -BigTypeMotion.BobRise, 0),
            BigTypeMotion.BobPeriodMilliseconds / 2, count, EaseInOut, relativeToOffset: true);
    }

    /// <summary>Ends a bob: the tile back at its own offset.</summary>
    public static void StopBob(Control tile) =>
        HandheldComposition.OffsetFrames(tile, 1, new LinearEasing(), 0, (0f, default), (1f, default));
}
