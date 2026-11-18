namespace EmuShelf.Core.Handheld;

/// <summary>
/// Handheld presentation stored in settings. Keep the numeric values stable for older settings.
/// </summary>
public enum HandheldLook
{
    /// <summary>The original handheld presentation.</summary>
    Classic = 0,

    /// <summary>The Big Type direction (DECISIONS.md, 2026-11-13; the mock in docs/prototypes/handheld-big-type).</summary>
    BigType = 1,
}
