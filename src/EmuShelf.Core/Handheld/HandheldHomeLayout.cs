namespace EmuShelf.Core.Handheld;

/// <summary>What a home slot holds. H4 adds user folders, game pins and system tiles.</summary>
public enum HandheldSlotKind
{
    Empty,
    PlatformFolder,
}

/// <summary>One slot on a Handheld home page: empty, or a reference to what it shows.</summary>
public sealed record HandheldSlot(HandheldSlotKind Kind, string? SystemId = null)
{
    public static HandheldSlot Empty { get; } = new(HandheldSlotKind.Empty);

    public static HandheldSlot PlatformFolder(string systemId) => new(HandheldSlotKind.PlatformFolder, systemId);

    public bool IsEmpty => Kind == HandheldSlotKind.Empty;
}

/// <summary>One 5×4 page of home slots; always exactly <see cref="HandheldHomeLayout.SlotsPerPage"/> long.</summary>
public sealed record HandheldHomePage(IReadOnlyList<HandheldSlot> Slots);

/// <summary>
/// The Handheld home screen: pages of 5×4 slots. Home is a place with slots (spec decision 11), so every
/// page is full length and unused slots are <see cref="HandheldSlot.Empty"/> rather than absent. In H1 the
/// layout is always the generated default; H4 makes it arrangeable and persists it.
/// </summary>
public sealed class HandheldHomeLayout
{
    public const int Columns = 5;
    public const int Rows = 4;
    public const int SlotsPerPage = Columns * Rows;

    public HandheldHomeLayout(IReadOnlyList<HandheldHomePage> pages)
    {
        if (pages.Count == 0)
            throw new ArgumentException("A home layout has at least one page.", nameof(pages));
        if (pages.Any(page => page.Slots.Count != SlotsPerPage))
            throw new ArgumentException($"Every page has exactly {SlotsPerPage} slots.", nameof(pages));
        Pages = pages;
    }

    public IReadOnlyList<HandheldHomePage> Pages { get; }

    public HandheldSlot SlotAt(int page, int slot) => Pages[page].Slots[slot];

    /// <summary>
    /// The layout a new user starts with: one platform folder per platform that has games, in the order
    /// given (the navigation order), filling each page row by row, then empty slots to the end of the last
    /// page. An empty library still gets one page of empty slots.
    /// </summary>
    public static HandheldHomeLayout CreateDefault(IEnumerable<string> systemIds)
    {
        var folders = systemIds
            .Distinct(StringComparer.Ordinal)
            .Select(HandheldSlot.PlatformFolder)
            .ToList();
        var pageCount = Math.Max(1, (folders.Count + SlotsPerPage - 1) / SlotsPerPage);
        var pages = new List<HandheldHomePage>(pageCount);
        for (var page = 0; page < pageCount; page++)
        {
            var slots = new HandheldSlot[SlotsPerPage];
            for (var slot = 0; slot < SlotsPerPage; slot++)
            {
                var index = page * SlotsPerPage + slot;
                slots[slot] = index < folders.Count ? folders[index] : HandheldSlot.Empty;
            }
            pages.Add(new HandheldHomePage(slots));
        }
        return new HandheldHomeLayout(pages);
    }
}
