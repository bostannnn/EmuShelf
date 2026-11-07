using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// One-shot compositor animations for Handheld mode: scale, opacity and offset only, run on the render
/// thread. Every helper ends on a resting value the control's own (Avalonia-side) state agrees with, because
/// a re-hosted control gets a fresh composition visual and falls back to that state (DECISIONS, M45 H0).
/// </summary>
internal static class HandheldComposition
{
    public static CompositionVisual? VisualOf(Visual control) => ElementComposition.GetElementVisual(control);

    /// <summary>Animates the visual's scale about its centre through the given (progress, scale) key frames.</summary>
    public static void Scale(Visual control, double milliseconds, Easing easing, double delayMilliseconds, params (float Progress, double Scale)[] frames) =>
        ScaleAbout(control, new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), milliseconds, easing, delayMilliseconds, frames);

    /// <summary>Scale key frames about a fixed point in the control's own coordinates.</summary>
    public static void ScaleAbout(Visual control, Point centre, double milliseconds, Easing easing, double delayMilliseconds, params (float Progress, double Scale)[] frames)
    {
        if (VisualOf(control) is not { } visual)
            return;
        visual.CenterPoint = new Vector3D(centre.X, centre.Y, 0);
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        foreach (var (progress, scale) in frames)
            animation.InsertKeyFrame(progress, new Vector3D(scale, scale, 1), easing);
        Run(visual, "Scale", animation, milliseconds, delayMilliseconds);
    }

    public static void Scale(Visual control, double from, double to, double milliseconds, Easing? easing = null, double delayMilliseconds = 0) =>
        Scale(control, milliseconds, easing ?? new CubicEaseOut(), delayMilliseconds, (0f, from), (1f, to));

    public static void Opacity(Visual control, double milliseconds, Easing easing, double delayMilliseconds, params (float Progress, double Opacity)[] frames)
    {
        if (VisualOf(control) is not { } visual)
            return;
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        foreach (var (progress, opacity) in frames)
            animation.InsertKeyFrame(progress, (float)opacity, easing);
        Run(visual, "Opacity", animation, milliseconds, delayMilliseconds);
    }

    public static void Opacity(Visual control, double from, double to, double milliseconds, Easing? easing = null, double delayMilliseconds = 0) =>
        Opacity(control, milliseconds, easing ?? new LinearEasing(), delayMilliseconds, (0f, from), (1f, to));

    /// <summary>
    /// Moves the visual from its layout position displaced by <paramref name="from"/> back to where layout put
    /// it (an offset of zero), so the animation always ends where Avalonia thinks the control is.
    /// </summary>
    public static void OffsetFrom(Visual control, Vector from, double milliseconds, Easing easing, double delayMilliseconds = 0) =>
        OffsetFrames(control, milliseconds, easing, delayMilliseconds, (0f, from), (1f, default));

    public static void OffsetFrames(Visual control, double milliseconds, Easing easing, double delayMilliseconds, params (float Progress, Vector Displacement)[] frames)
    {
        if (VisualOf(control) is not { } visual)
            return;
        var rest = visual.Offset;
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        foreach (var (progress, displacement) in frames)
            animation.InsertKeyFrame(progress, new Vector3D(rest.X + displacement.X, rest.Y + displacement.Y, rest.Z), easing);
        Run(visual, "Offset", animation, milliseconds, delayMilliseconds);
    }

    /// <summary>The selector glow's breathing: opacity between <paramref name="low"/> and 1, forever.</summary>
    public static void Breathe(Visual control, double low, double halfCycleMilliseconds)
    {
        if (VisualOf(control) is not { } visual)
            return;
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0f, (float)low);
        animation.InsertKeyFrame(1f, 1f, new SineEaseInOut());
        animation.Duration = TimeSpan.FromMilliseconds(halfCycleMilliseconds);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.Direction = PlaybackDirection.Alternate;
        visual.StartAnimation("Opacity", animation);
    }

    /// <summary>
    /// Slides the visual from where layout put it by <paramref name="travel"/>, linearly, then jumps back and
    /// repeats forever: a seamless drift when the travel is one period of a tiled picture (the Stage's era
    /// backdrop). Call after layout, so the visual's resting offset is known.
    /// </summary>
    public static void Loop(Visual control, Vector travel, double milliseconds)
    {
        if (VisualOf(control) is not { } visual)
            return;
        var rest = visual.Offset;
        var animation = visual.Compositor.CreateVector3DKeyFrameAnimation();
        animation.InsertKeyFrame(0f, rest, new LinearEasing());
        animation.InsertKeyFrame(1f, new Vector3D(rest.X + travel.X, rest.Y + travel.Y, rest.Z), new LinearEasing());
        animation.Duration = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds));
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Offset", animation);
    }

    private static void Run(CompositionVisual visual, string property, KeyFrameAnimation animation, double milliseconds, double delayMilliseconds)
    {
        animation.Duration = TimeSpan.FromMilliseconds(Math.Max(1, milliseconds));
        if (delayMilliseconds > 0)
        {
            animation.DelayTime = TimeSpan.FromMilliseconds(delayMilliseconds);
            animation.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        }
        visual.StartAnimation(property, animation);
    }
}
