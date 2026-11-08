using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// Scrolling lists on the Handheld screens never show a row cut in half (owner, 2026-11-08: All apps sliced its top row and
/// the selector ring at the scroller's top edge, and a half row under the hints at the bottom). A list's viewport is sized
/// to a whole number of rows (<see cref="FitWholeRows"/>) and has no vertical padding. With rows of one pitch, bringing the
/// focused row into view then moves the list by whole rows, so the edges always fall between rows; a drag or the wheel is
/// settled back onto whole rows (<see cref="KeepOnWholeRows"/>).
/// </summary>
internal static class HandheldScrolling
{
    /// <summary>
    /// Sizes <paramref name="scroller"/> (a scroll viewer, or a list that scrolls itself) to the most whole rows of
    /// <paramref name="pitch"/> that fit in <paramref name="available"/>, less one <paramref name="trailingGap"/> (a spacing
    /// after the last row that need not show).
    /// </summary>
    public static void FitWholeRows(Control scroller, double available, double pitch, double trailingGap = 0)
    {
        if (pitch <= 0 || available <= 0)
            return;
        var rows = Math.Max(1, Math.Floor((available + trailingGap) / pitch));
        var height = (rows * pitch) - trailingGap;
        if (double.IsNaN(scroller.Height) || Math.Abs(scroller.Height - height) > 0.5)
            scroller.Height = height;
    }

    /// <summary>The height of one row of <paramref name="items"/> (any laid-out one: the rows are one height), or 0.</summary>
    public static double RowPitch(ItemsControl items) =>
        items.GetRealizedContainers().FirstOrDefault() is Control row ? row.Bounds.Height : 0;

    /// <summary>
    /// Settles <paramref name="scroller"/> on whole rows of <paramref name="pitch"/> whenever something other than the focus
    /// moves it: a drag lands on the nearest row when it (and its inertia) ends, and the wheel goes on to the next row in the
    /// direction it turned. Focus moves already land on whole rows and pass through untouched.
    /// </summary>
    public static void KeepOnWholeRows(ScrollViewer scroller, Func<double> pitch)
    {
        var dragging = false;
        scroller.AddHandler(InputElement.ScrollGestureEvent, (_, _) => dragging = true, handledEventsToo: true);
        scroller.AddHandler(InputElement.ScrollGestureEndedEvent, (_, _) =>
        {
            dragging = false;
            Settle(scroller, pitch(), Math.Round);
        }, handledEventsToo: true);
        scroller.ScrollChanged += (_, e) =>
        {
            if (!dragging && e.OffsetDelta.Y != 0)
                Settle(scroller, pitch(), e.OffsetDelta.Y > 0 ? Math.Ceiling : Math.Floor);
        };
    }

    private static void Settle(ScrollViewer scroller, double pitch, Func<double, double> toRow)
    {
        if (pitch <= 0)
            return;
        var y = scroller.Offset.Y;
        var rows = y / pitch;
        // Already on a row (within a rounding hair): nothing to do, which also ends the loop a settle starts.
        if (Math.Abs(rows - Math.Round(rows)) < 0.01)
            return;
        var max = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
        var target = Math.Clamp(toRow(rows) * pitch, 0, max);
        if (Math.Abs(target - y) > 0.5)
            scroller.Offset = new Vector(scroller.Offset.X, target);
    }

    /// <summary>
    /// For a list whose scroller does not clip (so the focused item's ring and glow can reach past the edge): every row not
    /// wholly inside the viewport is hidden, so nothing shows outside it but that ring.
    /// </summary>
    public static void HideRowsOutside(ScrollViewer scroller, ItemsControl items)
    {
        if (!items.IsEffectivelyVisible)
            return;
        var height = scroller.Viewport.Height;
        for (var index = 0; index < items.ItemCount; index++)
        {
            if (items.ContainerFromIndex(index) is not Control container)
                continue;
            var inside = height > 0 && container.TranslatePoint(default, scroller) is { } at &&
                         at.Y >= -0.5 && at.Y + container.Bounds.Height <= height + 0.5;
            container.Opacity = inside ? 1 : 0;
            container.IsHitTestVisible = inside;
        }
    }
}
