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
/// The overlays are a pure diagnostic and are off by default. On Android there is no keyboard for the
/// usual DevTools F-key toggles, so <c>MainActivity</c> advances them with a deliberate <em>triple</em>
/// click of the otherwise-unused L3 (left-stick) button — three quick clicks in a row, so a stray stick
/// press can never turn them on. Nothing is auto-enabled, in Debug or Release. Each triple-click walks a
/// small fixed list of useful combinations rather than exposing the raw flags, and <see cref="Cycle"/>
/// gates the matching <see cref="PerfTrace"/> logcat sampler on the same switch so a clean library shows
/// neither the panel nor the log.
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

    private static int _index;

    /// <summary>
    /// Advances to the next overlay combination on <paramref name="topLevel"/> and starts or stops the
    /// <see cref="PerfTrace"/> logcat sampler to match (running for any active mode, stopped at "off"), so
    /// both halves of the diagnostic move together. No-op when <paramref name="topLevel"/> is null (the
    /// view is not attached yet).
    /// </summary>
    /// <returns>A short label for the newly-selected mode, or null when there was no top level.</returns>
    public static string? Cycle(TopLevel? topLevel)
    {
        if (topLevel is null)
            return null;

        _index = (_index + 1) % Modes.Length;
        var (overlays, label) = Modes[_index];
        topLevel.RendererDiagnostics.DebugOverlays = overlays;

        if (overlays == RendererDebugOverlays.None)
            PerfTrace.StopSampling();
        else
            PerfTrace.StartSampling();

        return label;
    }
}
