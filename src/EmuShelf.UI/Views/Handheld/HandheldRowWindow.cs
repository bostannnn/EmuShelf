using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// A Big Type list longer than its window (M47 B7): the rows move up and down by whole rows so the focused one is in view, as the
/// mock's panel does (<c>P.scroll</c>): the window starts at the focused row when it is above it and ends at it when it is below.
/// Set on the list's items control with its <see cref="VisibleProperty"/> rows and <see cref="PitchProperty"/>; the list's
/// parent clips it. The move rides the control's <c>RenderTransform</c> transition (tokens.js <c>Scroll</c>, set by its style).
/// </summary>
public static class HandheldRowWindow
{
    public static readonly AttachedProperty<int> FocusProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("Focus", typeof(HandheldRowWindow), -1);

    public static readonly AttachedProperty<int> VisibleProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("Visible", typeof(HandheldRowWindow), int.MaxValue);

    public static readonly AttachedProperty<double> PitchProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("Pitch", typeof(HandheldRowWindow));

    /// <summary>The first row in view.</summary>
    public static readonly AttachedProperty<int> FirstProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("First", typeof(HandheldRowWindow));

    static HandheldRowWindow()
    {
        FocusProperty.Changed.AddClassHandler<Control>((control, _) => Update(control));
        VisibleProperty.Changed.AddClassHandler<Control>((control, _) => Update(control));
        PitchProperty.Changed.AddClassHandler<Control>((control, _) => Update(control));
    }

    public static int GetFocus(Control control) => control.GetValue(FocusProperty);
    public static void SetFocus(Control control, int value) => control.SetValue(FocusProperty, value);
    public static int GetVisible(Control control) => control.GetValue(VisibleProperty);
    public static void SetVisible(Control control, int value) => control.SetValue(VisibleProperty, value);
    public static double GetPitch(Control control) => control.GetValue(PitchProperty);
    public static void SetPitch(Control control, double value) => control.SetValue(PitchProperty, value);
    public static int GetFirst(Control control) => control.GetValue(FirstProperty);

    /// <summary>The first row in view once <paramref name="focus"/> is: unchanged while it is in view.</summary>
    public static int First(int first, int focus, int visible)
    {
        if (focus < 0 || visible <= 0)
            return 0;
        if (focus < first)
            return focus;
        if (focus > first + visible - 1)
            return focus - visible + 1;
        return first;
    }

    /// <summary>A new list in the window starts at its top, with no move.</summary>
    public static void Reset(Control control)
    {
        var transitions = control.Transitions;
        control.Transitions = null;
        control.SetValue(FirstProperty, 0);
        Place(control, First(0, GetFocus(control), GetVisible(control)));
        control.Transitions = transitions;
    }

    private static void Update(Control control)
    {
        var first = First(control.GetValue(FirstProperty), GetFocus(control), GetVisible(control));
        Place(control, first);
    }

    private static void Place(Control control, int first)
    {
        control.SetValue(FirstProperty, first);
        var offset = -first * GetPitch(control);
        if (control.RenderTransform is ITransform current && Math.Abs(current.Value.M32 - offset) < 0.01)
            return;
        var builder = new TransformOperations.Builder(1);
        builder.AppendTranslate(0, offset);
        control.RenderTransform = builder.Build();
    }
}
