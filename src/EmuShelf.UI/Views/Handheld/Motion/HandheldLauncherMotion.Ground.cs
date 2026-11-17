using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using EmuShelf.App.ViewModels;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

// The Big Type ground on the Avalonia Launcher (M47 B7): a new colour floods in as a circle, as the native Launcher's does (B6's
// Flood, tokens.js Flood), instead of B3's crossfade, so a list that changes the colour while this view has the screen (Settings
// takes its own) does what the mock does. It grows from the middle of the ring's target, or from the middle of the screen when a
// list takes the colour (the mock's focusSet passes no tile). Held still, or while this view is only pictured, the colour simply
// changes.
internal sealed partial class HandheldLauncherMotion
{
    private Border? _ground;
    private Border? _wave;
    private IBrush? _groundShown;
    private IBrush? _waveBrush;
    private long? _waveStart;
    private Point _waveOrigin;

    /// <summary>The ground and the wave the view draws over it (Big Type only).</summary>
    public void AttachGround(Border ground, Border wave)
    {
        _ground = ground;
        _wave = wave;
        // Under Big Type the colour is this motion's to change: no crossfade, no binding.
        ground.Transitions = null;
        ground.Background = _shell?.LauncherGroundBrush;
        _groundShown = ground.Background;
        wave.IsVisible = false;
    }

    private void UpdateGround()
    {
        if (!HandheldTokens.IsBigType || _ground is not { } ground || _wave is not { } wave || _shell is not { } shell)
            return;
        var next = shell.LauncherGroundBrush;
        if (ReferenceEquals(next, _waveBrush ?? _groundShown))
            return;
        if (!_onScreen || Still || _groundShown is null)
        {
            SettleGround(next);
            return;
        }
        // A wave still growing becomes the ground the new one grows over.
        if (_waveBrush is { } growing)
            ground.Background = _groundShown = growing;
        _waveBrush = next;
        wave.Background = next;
        _waveStart = null;
        _waveOrigin = FloodOrigin();
        wave.Clip = new EllipseGeometry(new Rect(_waveOrigin, new Size(0, 0)));
        wave.IsVisible = true;
        RequestTick();
    }

    private void SettleGround(IBrush? brush)
    {
        if (_ground is null || _wave is null)
            return;
        _ground.Background = _groundShown = brush;
        _waveBrush = null;
        _waveStart = null;
        _wave.IsVisible = false;
        _wave.Clip = null;
    }

    private bool StepGround()
    {
        if (_waveBrush is not { } brush || _wave is not { } wave)
            return false;
        _waveStart ??= Stopwatch.GetTimestamp();
        var since = (float)Stopwatch.GetElapsedTime(_waveStart.Value).TotalMilliseconds;
        if (!BigTypeMotion.Flooding(since, Still))
        {
            SettleGround(brush);
            return false;
        }
        // The flood's radius is in the Launcher's canvas units; the wave covers the window, which the canvas is fitted into.
        var scale = CanvasScale();
        var radius = BigTypeMotion.FloodRadiusAt(since) * scale;
        wave.Clip = new EllipseGeometry(new Rect(_waveOrigin.X - radius, _waveOrigin.Y - radius, radius * 2, radius * 2));
        return true;
    }

    // How large one canvas unit is in the window (the canvas sits in a uniform Viewbox).
    private double CanvasScale()
    {
        if (_wave is not { } wave || _canvas.TranslatePoint(new Point(1, 0), wave) is not { } one || _canvas.TranslatePoint(default, wave) is not { } zero)
            return 1;
        var scale = one.X - zero.X;
        return double.IsFinite(scale) && scale > 0 ? scale : 1;
    }

    // The middle of the ring's target, or of the screen while a list has the colour; in the wave's own coordinates.
    private Point FloodOrigin()
    {
        var onCanvas = new Point(_canvas.Bounds.Width / 2, _canvas.Bounds.Height / 2);
        // The shelf coming back after a game floods from the middle (M47 B8, the mock's endPlay).
        var middle = _shell is { MiddleFloodGround: { } back } shell && back == shell.LauncherGround;
        if (!middle && _listKey.Kind == ListKind.None && ResolveTarget() is { } target)
            onCanvas = _selectorLayer.TranslatePoint(target.Rect.Center, _canvas) ?? onCanvas;
        return _wave is { } wave ? _canvas.TranslatePoint(onCanvas, wave) ?? default : default;
    }
}
