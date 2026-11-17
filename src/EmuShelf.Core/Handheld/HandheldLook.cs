namespace EmuShelf.Core.Handheld;

/// <summary>
/// Handheld mode's look (M47). The Big Type restyle is built behind this switch while today's look stays the default;
/// at B10 Big Type becomes the look and <see cref="Classic"/> is removed with the code that draws it (decision D5).
/// </summary>
public enum HandheldLook
{
    /// <summary>Today's look (HANDHELD-MODE.md decision 16): the default.</summary>
    Classic = 0,

    /// <summary>The Big Type direction (DECISIONS.md, 2026-11-13; the mock in docs/prototypes/handheld-big-type).</summary>
    BigType = 1,
}
