using System.Numerics;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.Rendering.Stage;

// The idle artwork's round window on the native Stage under the Big Type look (M47 B15, proposals-screens.html option 12, the Porthole),
// from the maths the Avalonia Stage shares (BigTypeScreensMotion): it rises through the hinge when the drift begins, floats, takes the next
// game's artwork through a circle from its bottom and drops back through the hinge on a press. Drawn over the backdrop and its veils, under
// the name and the corners (the page's .art layer).
public sealed partial class StageCompositor
{
    private Layer? _port;
    private Layer? _portUnder;
    private float _portArrive = float.NaN;
    private float _portChange = float.NegativeInfinity;
    private float _portLeave = float.NaN;

    private void ApplyPorthole(StagePortholeCommand command, float now)
    {
        var animate = command.Animate && !_holdStill;
        if (command.Art is not { } art)
        {
            if (_port is not null && float.IsNaN(_portLeave))
                _portLeave = animate ? now : float.NegativeInfinity;
            return;
        }
        var next = new Layer(art, Upload(art.Pixels, repeat: false));
        if (_port is null || !float.IsNaN(_portLeave))
        {
            DeletePorthole();
            _port = next;
            _portArrive = animate ? now : float.NegativeInfinity;
            _portChange = float.NegativeInfinity;
            _portLeave = float.NaN;
            return;
        }
        if (_portUnder is { } older)
            _gl.DeleteTexture(older.Texture);
        _portUnder = animate ? _port : null;
        if (!animate)
            _gl.DeleteTexture(_port.Texture);
        _port = next;
        _portChange = animate ? now : float.NegativeInfinity;
    }

    /// <summary>Draws the window; true while it moves (its float is the resting motion's, drawn while the backdrop runs).</summary>
    private bool DrawPorthole(float now)
    {
        if (_port is not { } port)
            return false;
        var still = _holdStill;
        float drop, scale, opacity;
        var animating = false;
        if (!float.IsNaN(_portLeave))
        {
            var since = now - _portLeave;
            if (still || since >= BigTypeScreensMotion.PortOutTotal())
            {
                DeletePorthole();
                return false;
            }
            (drop, scale, opacity) = BigTypeScreensMotion.PortOut(since);
            animating = true;
        }
        else
        {
            var since = now - _portArrive;
            (drop, scale, opacity) = BigTypeScreensMotion.PortIn(since, still);
            animating = !still && since < BigTypeScreensMotion.PortInMilliseconds;
            drop += BigTypeScreensMotion.PortFloat(float.IsNegativeInfinity(_portArrive) ? 0f : since, still);
        }
        var image = port.Image;
        var centreX = image.X + (image.Width / 2f);
        var centreY = image.Y + (image.Height / 2f) + drop;
        var wipe = BigTypeScreensMotion.PortWipeRadius(now - _portChange, image.Width, still);
        if (_portUnder is { } under && wipe < BigTypeScreensMotion.PortWipeShare * image.Width)
        {
            DrawQuad(under.Texture, image.X, image.Y + drop, image.Width, image.Height, opacity, scale, centreX, centreY, new Vector4(0, 0, 1, 1));
            _clip0 = Circle(centreX, centreY + (image.Height * scale / 2f), wipe * scale, inside: true);
            _clip1 = _clip2 = default;
            DrawQuad(port.Texture, image.X, image.Y + drop, image.Width, image.Height, opacity, scale, centreX, centreY, new Vector4(0, 0, 1, 1));
            _clip0 = default;
            animating = true;
        }
        else
        {
            if (_portUnder is { } done)
                _gl.DeleteTexture(done.Texture);
            _portUnder = null;
            DrawQuad(port.Texture, image.X, image.Y + drop, image.Width, image.Height, opacity, scale, centreX, centreY, new Vector4(0, 0, 1, 1));
        }
        return animating;
    }

    private void DeletePorthole()
    {
        if (_port is { } port)
            _gl.DeleteTexture(port.Texture);
        if (_portUnder is { } under)
            _gl.DeleteTexture(under.Texture);
        _port = _portUnder = null;
        _portLeave = float.NaN;
    }
}
