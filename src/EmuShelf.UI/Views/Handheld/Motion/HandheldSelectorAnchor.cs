using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace EmuShelf.App.Views.Handheld;

/// <summary>A view that hosts a travelling selector and wants to know which element is focused.</summary>
internal interface IHandheldSelectorHost
{
    void OnAnchorChanged(Control anchor, bool isFocused);
}

/// <summary>
/// Marks a focusable element (a tile, a chip, a dock button, a Title page button) as a place the travelling
/// selector can go: bound to the same IsFocused as the element's look. The element's <c>Border.ring</c>
/// child says where the frame sits and how round it is; the selector draws the frame itself.
/// </summary>
public static class HandheldSelectorAnchor
{
    public static readonly AttachedProperty<bool> IsFocusedProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsFocused", typeof(HandheldSelectorAnchor));

    static HandheldSelectorAnchor()
    {
        IsFocusedProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            var focused = change.GetNewValue<bool>();
            if (Host(control) is { } host)
            {
                host.OnAnchorChanged(control, focused);
            }
            else if (focused)
            {
                // Bindings run before the element joins the tree: tell the host once it does.
                control.AttachedToVisualTree -= OnAttached;
                control.AttachedToVisualTree += OnAttached;
            }
        });
    }

    public static bool GetIsFocused(Control control) => control.GetValue(IsFocusedProperty);
    public static void SetIsFocused(Control control, bool value) => control.SetValue(IsFocusedProperty, value);

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control control)
            return;
        control.AttachedToVisualTree -= OnAttached;
        if (GetIsFocused(control) && Host(control) is { } host)
            host.OnAnchorChanged(control, true);
    }

    private static IHandheldSelectorHost? Host(Control control)
    {
        foreach (var ancestor in control.GetVisualAncestors())
        {
            if (ancestor is IHandheldSelectorHost host)
                return host;
        }
        return null;
    }
}
