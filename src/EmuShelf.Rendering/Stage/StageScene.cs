using System.Numerics;
using EmuShelf.Rendering.Models;
using EmuShelf.Rendering.Shells;

namespace EmuShelf.Rendering.Stage;

/// <summary>
/// Pixels a 2D layer of the native Stage shows: premultiplied, top row first, exactly as Avalonia and
/// Skia hand them out, so nothing is converted on the way to the GPU: BGRA (desktop) has the texture swizzle
/// red and blue; RGBA (Skia on Android) goes up as it is.
/// </summary>
public sealed record StagePixels(int Width, int Height, byte[] Data, bool IsBgra = true);

/// <summary>A picture placed on the 1920×1080 Stage canvas, in canvas pixels.</summary>
public sealed record StageImage(StagePixels Pixels, float X, float Y, float Width, float Height);

/// <summary>
/// The era backdrop as layers: a static picture under, an optional moving layer, a static picture over.
/// The mover is either a tile repeated across the whole canvas (<see cref="Tile"/>, the cartridge blocks) or
/// one picture translated (<see cref="Quad"/>, the disc's light bar). It travels <see cref="Travel"/> per
/// <see cref="LoopMilliseconds"/>, placed in whole pixels and stepped every <see cref="StepMilliseconds"/>.
/// </summary>
public sealed record StageBackdrop(
    StageImage Under,
    StageImage? Over,
    StagePixels? Tile,
    StageImage? Quad,
    Vector2 Travel,
    float LoopMilliseconds,
    float StepMilliseconds);

/// <summary>A game's physical medium drawn by <see cref="MediaShellRenderer"/> into its own texture.</summary>
public sealed record StageMedium(MediaShell Shell, Vector3 LinearAccent, float X, float Y, float Width, float Height);

/// <summary>
/// Everything a native scene is told, posted from the UI thread and applied on the render thread. A command
/// with a <see cref="RetainKey"/> is state: the host keeps the latest one per key and replays it (as
/// <see cref="ForReplay"/>, without its animation) into a rebuilt context. A command without one is a moment
/// (a pop, a bump) that a rebuilt context does not need.
/// </summary>
public abstract record StageCommand
{
    public virtual string? RetainKey => null;

    /// <summary>True when the command takes its piece away (the host forgets that key).</summary>
    public virtual bool Removes => false;

    public virtual StageCommand ForReplay() => this;
}

/// <summary>A scene a native surface draws on its render thread (the Stage, the Launcher).</summary>
public interface INativeScene : IDisposable
{
    void Apply(StageCommand command, float now);

    /// <summary>Draws a frame into the bound default framebuffer; returns whether something still animates.</summary>
    bool Draw(float now, int surfaceWidth, int surfaceHeight);

    /// <summary>When nothing animates: how soon the scene wants another frame (a stepped drift), or null.</summary>
    float? NextFrameMilliseconds { get; }
}

/// <summary>A new (or no) backdrop; it crossfades over the old one.</summary>
public sealed record StageBackdropCommand(StageBackdrop? Backdrop) : StageCommand
{
    public override string? RetainKey => "backdrop";
}

/// <summary>
/// What the centre shows (a picture, or the medium) and the name plate. <paramref name="Pop"/> plays the
/// focus change: the centre grows from 0.72× on the spring, the plate from 0.94×. Without it the pictures
/// are simply replaced (a cover that finished loading, a folder flap that settled).
/// </summary>
public sealed record StageCentreCommand(StageImage? Art, StageMedium? Medium, StageImage? Plate, bool Pop,
    FolderArt? Folder = null, FolderArtState FolderState = FolderArtState.Closed) : StageCommand
{
    public override string? RetainKey => "centre";
    public override StageCommand ForReplay() => this with { Pop = false };
}

/// <summary>The big folder's flap: closed, peeking or open; it glides there unless told not to.</summary>
public sealed record StageFolderStateCommand(FolderArtState State, bool Animate) : StageCommand
{
    public override string? RetainKey => "folder-state";
    public override StageCommand ForReplay() => this with { Animate = false };
}

/// <summary>The medium's pose, in radians.</summary>
public sealed record StageMediumPoseCommand(float Yaw, float Pitch) : StageCommand
{
    public override string? RetainKey => "pose";
}

/// <summary>The cover printed on the medium (straight-alpha RGBA, as the renderer uploads it).</summary>
public sealed record StageMediumCoverCommand(TextureImage? Cover) : StageCommand
{
    public override string? RetainKey => "cover";
}

/// <summary>A corner pill or toast, by name; null removes it.</summary>
public sealed record StageChromeCommand(string Key, StageImage? Image) : StageCommand
{
    public override string? RetainKey => "chrome:" + Key;
}

/// <summary>
/// The Stage's motion curves, the same numbers as the Avalonia Stage (HANDHELD-MODE.md, Numbers as built):
/// sampled once, evaluated per frame on the render thread.
/// </summary>
public static class StageCurves
{
    public const float CentrePivotX = 960f;
    public const float CentrePivotY = 460f;
    public const float PlatePivotX = 960f;
    public const float PlatePivotY = 940f;
    public const float PopFromScale = 0.72f;
    public const float PlateFromScale = 0.94f;
    public const float PlatePopMilliseconds = 240f;
    public const float BackdropFadeMilliseconds = 450f;

    private static readonly float[] PopSamples = SampleSpring(190, 17);

    /// <summary>How long the centre pop runs (the spring's settling time).</summary>
    public static float PopMilliseconds { get; } = (PopSamples.Length - 1) * 1000f / 240f;

    /// <summary>The centre's scale at <paramref name="milliseconds"/> into a pop: 0.72 → 1 with the spring's overshoot.</summary>
    public static float PopScale(float milliseconds)
    {
        if (milliseconds >= PopMilliseconds)
            return 1f;
        var t = Math.Max(0f, milliseconds) / PopMilliseconds;
        return PopFromScale + ((1f - PopFromScale) * Sample(PopSamples, t));
    }

    /// <summary>The plate's scale: 0.94 → 1 in 240 ms on cubic-bezier(.22, 1.1, .36, 1).</summary>
    public static float PlateScale(float milliseconds)
    {
        if (milliseconds >= PlatePopMilliseconds)
            return 1f;
        var t = Math.Max(0f, milliseconds) / PlatePopMilliseconds;
        return PlateFromScale + ((1f - PlateFromScale) * CubicBezier(0.22f, 1.1f, 0.36f, 1f, t));
    }

    /// <summary>Cubic ease-out, for the backdrop crossfade.</summary>
    public static float FadeIn(float milliseconds)
    {
        var t = Math.Clamp(milliseconds / BackdropFadeMilliseconds, 0f, 1f);
        var u = 1f - t;
        return 1f - (u * u * u);
    }

    /// <summary>A spring's step response from 0 to 1, sampled at 240 Hz until it settles.</summary>
    internal static float[] SampleSpring(double stiffness, double damping)
    {
        var samples = new List<float>();
        double value = 0, velocity = 0, dt = 1 / 240d;
        for (var i = 0; i < 720; i++)
        {
            samples.Add((float)value);
            var acceleration = (-stiffness * (value - 1)) - (damping * velocity);
            velocity += acceleration * dt;
            value += velocity * dt;
            if (i > 20 && Math.Abs(value - 1) < 0.002 && Math.Abs(velocity) < 0.05)
                break;
        }
        samples.Add(1f);
        return samples.ToArray();
    }

    private static float Sample(float[] samples, float t)
    {
        var position = t * (samples.Length - 1);
        var index = Math.Min((int)position, samples.Length - 2);
        var fraction = position - index;
        return samples[index] + ((samples[index + 1] - samples[index]) * fraction);
    }

    /// <summary>CSS-style cubic-bezier(x1, y1, x2, y2) at progress <paramref name="x"/> (solved for t by bisection).</summary>
    internal static float CubicBezier(float x1, float y1, float x2, float y2, float x)
    {
        float lo = 0, hi = 1, t = x;
        for (var i = 0; i < 24; i++)
        {
            t = (lo + hi) / 2;
            var bx = Bezier(x1, x2, t);
            if (bx < x)
                lo = t;
            else
                hi = t;
        }
        return Bezier(y1, y2, t);
    }

    private static float Bezier(float p1, float p2, float t)
    {
        var u = 1 - t;
        return (3 * u * u * t * p1) + (3 * u * t * t * p2) + (t * t * t);
    }
}
