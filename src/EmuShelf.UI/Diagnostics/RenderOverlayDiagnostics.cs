using Avalonia.Controls;
using Avalonia.Rendering;

namespace EmuShelf.App.Diagnostics;

/// <summary>
/// Toggles Avalonia's built-in renderer debug overlays (FPS, render-time graph, layout-time graph,
/// dirty rectangles) on a live <see cref="TopLevel"/>. This is the measurement tool for on-device
/// rendering cost — e.g. finding why the fan ramps up merely scrolling the couch library on a
/// handheld — without adding any bespoke per-frame instrumentation: the numbers are produced and
/// drawn by Avalonia's own render thread, so the overlay itself costs effectively nothing when off.
/// </summary>
/// <remarks>
/// The overlays are a pure diagnostic. On Android there is no keyboard for the usual DevTools F-key
/// toggles, so <c>MainActivity</c> cycles them from an otherwise-unused gamepad button (L3 / left-stick
/// click). Cycling walks a small fixed list of useful combinations rather than exposing the raw flags.
/// <see cref="RendererDebugOverlays.RenderTimeGraph"/> is the ms/frame signal; <see
/// cref="RendererDebugOverlays.DirtyRects"/> shows how much of the screen repaints each frame (the
/// overdraw tell — if the whole grid lights up while scrolling, the item template is the cost);
/// <see cref="RendererDebugOverlays.LayoutTimeGraph"/> catches a layout-bound scroll.
/// </remarks>
public static class RenderOverlayDiagnostics
{
    private static readonly (RendererDebugOverlays Overlays, string Label)[] Modes =
    [
        (RendererDebugOverlays.None, "off"),
        (RendererDebugOverlays.Fps | RendererDebugOverlays.RenderTimeGraph, "fps + render time"),
        (RendererDebugOverlays.Fps | RendererDebugOverlays.RenderTimeGraph | RendererDebugOverlays.DirtyRects,
            "fps + render time + dirty rects"),
        (RendererDebugOverlays.Fps | RendererDebugOverlays.RenderTimeGraph | RendererDebugOverlays.LayoutTimeGraph
            | RendererDebugOverlays.DirtyRects, "all (fps + render + layout + dirty rects)"),
    ];

    // The index of the standard measurement set (fps + render time + dirty rects) within Modes, so
    // SetEnabled and the initial state agree with the cycle order.
    private const int StandardModeIndex = 2;

    private static int _index;

    /// <summary>
    /// Advances to the next overlay combination on <paramref name="topLevel"/>. No-op when it is null
    /// (the view is not attached yet).
    /// </summary>
    /// <returns>A short label for the newly-selected mode, or null when there was no top level.</returns>
    public static string? Cycle(TopLevel? topLevel)
    {
        if (topLevel is null)
            return null;

        _index = (_index + 1) % Modes.Length;
        var (overlays, label) = Modes[_index];
        topLevel.RendererDiagnostics.DebugOverlays = overlays;
        return label;
    }

    /// <summary>
    /// Turns the standard measurement set (FPS + render-time graph + dirty rects) on or off directly,
    /// leaving the cycle position consistent with <see cref="Cycle"/>.
    /// </summary>
    public static void SetEnabled(TopLevel? topLevel, bool enabled)
    {
        if (topLevel is null)
            return;

        _index = enabled ? StandardModeIndex : 0;
        topLevel.RendererDiagnostics.DebugOverlays = Modes[_index].Overlays;
    }
}
