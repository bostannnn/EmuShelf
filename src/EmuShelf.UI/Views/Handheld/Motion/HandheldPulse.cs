using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.VisualTree;
using EmuShelf.Rendering.Stage.BigType;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// A key that pulses each time it is pressed (M47 B9, the mock's <c>pulse</c>: from 0.95 to rest on the Land curve), bound to a count
/// of presses. On the compositor only, so it never reaches a picture or a design frame; held still (the Launcher's <c>still</c>
/// class) it does nothing.
/// </summary>
public static class HandheldPulse
{
    public static readonly AttachedProperty<int> CountProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("Count", typeof(HandheldPulse));

    private const int Samples = 12;

    static HandheldPulse()
    {
        CountProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (change.GetNewValue<int>() <= change.GetOldValue<int>() || !control.IsAttachedToVisualTree() || HeldStill(control))
                return;
            var frames = new (float Progress, double Scale)[Samples + 1];
            for (var i = 0; i <= Samples; i++)
            {
                var progress = (float)i / Samples;
                frames[i] = (progress, BigTypeTypingMotion.KeyPulse(progress * BigTypeTypingMotion.KeyPressMilliseconds));
            }
            var centre = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
            HandheldComposition.ScaleAbout(control, centre, BigTypeTypingMotion.KeyPressMilliseconds, new LinearEasing(), 0, frames);
        });
    }

    public static int GetCount(Control control) => control.GetValue(CountProperty);
    public static void SetCount(Control control, int value) => control.SetValue(CountProperty, value);

    private static bool HeldStill(Control control) =>
        control.GetVisualAncestors().OfType<Panel>().Any(panel => panel.Classes.Contains("still"));
}
