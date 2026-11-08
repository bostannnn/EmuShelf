namespace EmuShelf.Core.Handheld;

/// <summary>What a home slot holds.</summary>
public enum HandheldSlotKind
{
    Empty,

    /// <summary>A platform folder (by system id): made for you, always holding every game of the platform.</summary>
    PlatformFolder,

    /// <summary>One of your folders (by folder id): a name, a colour and the games you put in it.</summary>
    UserFolder,

    /// <summary>A game pinned straight to home (by game id). A pin is a reference, never a move (decision 12).</summary>
    Game,

    /// <summary>A tile EmuShelf provides (by <see cref="HandheldSystemTile"/>), such as the Recent folder.</summary>
    System,
}

/// <summary>The tiles EmuShelf itself puts on home.</summary>
public enum HandheldSystemTile
{
    /// <summary>The Recent folder (decision 14): the last twelve games played, newest first.</summary>
    Recent,
}

/// <summary>
/// One slot on a Handheld home page: empty, or a reference to what it shows. <see cref="IsNew"/> marks a
/// platform folder that reconciliation added since you last opened it (the "New" ribbon).
/// </summary>
public sealed record HandheldSlot(
    HandheldSlotKind Kind,
    string? SystemId = null,
    string? FolderId = null,
    long GameId = 0,
    HandheldSystemTile SystemTile = HandheldSystemTile.Recent,
    bool IsNew = false)
{
    public static HandheldSlot Empty { get; } = new(HandheldSlotKind.Empty);

    public static HandheldSlot PlatformFolder(string systemId, bool isNew = false) =>
        new(HandheldSlotKind.PlatformFolder, SystemId: systemId, IsNew: isNew);

    public static HandheldSlot UserFolder(string folderId) => new(HandheldSlotKind.UserFolder, FolderId: folderId);

    public static HandheldSlot Game(long gameId) => new(HandheldSlotKind.Game, GameId: gameId);

    public static HandheldSlot Recent { get; } = new(HandheldSlotKind.System, SystemTile: HandheldSystemTile.Recent);

    public bool IsEmpty => Kind == HandheldSlotKind.Empty;

    /// <summary>Whether the slot opens as a folder (platform, yours, or Recent).</summary>
    public bool IsFolder => Kind is HandheldSlotKind.PlatformFolder or HandheldSlotKind.UserFolder ||
        (Kind == HandheldSlotKind.System && SystemTile == HandheldSystemTile.Recent);

    /// <summary>Whether two slots show the same thing (the ribbon aside).</summary>
    public bool SameTileAs(HandheldSlot other) => this with { IsNew = false } == other with { IsNew = false };
}

/// <summary>
/// One of your folders: a name, a colour (#RRGGBB) and the games in it, in the order they were added
/// (its Manual order), plus the sort chip it was left on. Folders hold references; deleting one never
/// touches its games.
/// </summary>
public sealed record HandheldUserFolder(
    string Id,
    string Name,
    string Color,
    IReadOnlyList<long> GameIds,
    HandheldFolderSort Sort = HandheldFolderSort.Manual)
{
    public bool Contains(long gameId) => GameIds.Contains(gameId);
}

/// <summary>One 5×4 page of home slots; always exactly <see cref="HandheldHomeLayout.SlotsPerPage"/> long.</summary>
public sealed record HandheldHomePage(IReadOnlyList<HandheldSlot> Slots);

/// <summary>
/// The Handheld home screen: pages of 5×4 slots and your folders. Home is a place with slots (spec decision
/// 11): every page is full length, unused slots are <see cref="HandheldSlot.Empty"/>, and there are always at
/// least <see cref="MinimumPages"/> pages (40 slots). Immutable: every edit returns a new layout.
/// </summary>
public sealed class HandheldHomeLayout
{
    public const int Columns = 5;
    public const int Rows = 4;
    public const int SlotsPerPage = Columns * Rows;
    public const int MinimumPages = 2;

    /// <summary>Recent's own colour: a quiet slate, so it reads as EmuShelf's and not a platform's.</summary>
    public const string RecentColor = "#6B7A9C";

    /// <summary>The colour chips offered for your folders (the mockup's six tints).</summary>
    public static IReadOnlyList<string> FolderColors { get; } =
        ["#EF79AC", "#FF9A44", "#3BBD79", "#4FA3F7", "#9B7BEA", "#F0C02E"];

    public HandheldHomeLayout(IReadOnlyList<HandheldHomePage> pages, IReadOnlyList<HandheldUserFolder>? folders = null)
    {
        if (pages.Count == 0)
            throw new ArgumentException("A home layout has at least one page.", nameof(pages));
        if (pages.Any(page => page.Slots.Count != SlotsPerPage))
            throw new ArgumentException($"Every page has exactly {SlotsPerPage} slots.", nameof(pages));
        Pages = pages;
        Folders = folders ?? [];
    }

    public IReadOnlyList<HandheldHomePage> Pages { get; }

    public IReadOnlyList<HandheldUserFolder> Folders { get; }

    public int SlotCount => Pages.Count * SlotsPerPage;

    public HandheldSlot SlotAt(int page, int slot) => Pages[page].Slots[slot];

    /// <summary>A slot by its place across all pages (page × 20 + slot).</summary>
    public HandheldSlot SlotAt(int index) => Pages[index / SlotsPerPage].Slots[index % SlotsPerPage];

    /// <summary>Every slot across all pages, page by page.</summary>
    public IReadOnlyList<HandheldSlot> AllSlots() => Pages.SelectMany(page => page.Slots).ToList();

    public HandheldUserFolder? FindFolder(string? id) =>
        id is null ? null : Folders.FirstOrDefault(folder => string.Equals(folder.Id, id, StringComparison.Ordinal));

    /// <summary>The first slot (across all pages) showing a tile like <paramref name="tile"/>, or -1.</summary>
    public int IndexOf(HandheldSlot tile)
    {
        var slots = AllSlots();
        for (var i = 0; i < slots.Count; i++)
        {
            if (!slots[i].IsEmpty && slots[i].SameTileAs(tile))
                return i;
        }
        return -1;
    }

    /// <summary>The first empty slot across all pages, or -1 when home is full.</summary>
    public int FirstEmptySlot()
    {
        var slots = AllSlots();
        for (var i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsEmpty)
                return i;
        }
        return -1;
    }

    /// <summary>Whether the game is pinned straight to home.</summary>
    public bool IsPinned(long gameId) => IndexOf(HandheldSlot.Game(gameId)) >= 0;

    /// <summary>
    /// Builds a layout from a flat list of slots: padded with empty slots to whole pages, never fewer than
    /// <see cref="MinimumPages"/>, and with empty pages past that minimum trimmed off the end.
    /// </summary>
    public static HandheldHomeLayout FromSlots(IEnumerable<HandheldSlot> slots, IEnumerable<HandheldUserFolder>? folders = null)
    {
        var list = slots.ToList();
        var lastUsed = list.FindLastIndex(slot => !slot.IsEmpty);
        var pageCount = Math.Max(MinimumPages, (lastUsed + SlotsPerPage) / SlotsPerPage);
        var pages = new List<HandheldHomePage>(pageCount);
        for (var page = 0; page < pageCount; page++)
        {
            var pageSlots = new HandheldSlot[SlotsPerPage];
            for (var slot = 0; slot < SlotsPerPage; slot++)
            {
                var index = page * SlotsPerPage + slot;
                pageSlots[slot] = index < list.Count ? list[index] : HandheldSlot.Empty;
            }
            pages.Add(new HandheldHomePage(pageSlots));
        }
        return new HandheldHomeLayout(pages, folders?.ToList());
    }

    /// <summary>
    /// The layout a new user starts with: the Recent folder, then one platform folder per platform that has
    /// games, in the order given (the navigation order), filling each page row by row, then empty slots to
    /// the end of the last page. An empty library gets only the empty slots.
    /// </summary>
    public static HandheldHomeLayout CreateDefault(IEnumerable<string> systemIds)
    {
        var folders = systemIds
            .Distinct(StringComparer.Ordinal)
            .Select(id => HandheldSlot.PlatformFolder(id))
            .ToList();
        IEnumerable<HandheldSlot> slots = folders.Count == 0 ? [] : folders.Prepend(HandheldSlot.Recent);
        return FromSlots(slots);
    }

    // ---- Edits (each returns a new layout) ----

    /// <summary>Puts <paramref name="slot"/> at <paramref name="index"/>, growing home by a page if it is past the end.</summary>
    public HandheldHomeLayout WithSlot(int index, HandheldSlot slot)
    {
        var slots = AllSlots().ToList();
        while (slots.Count <= index)
            slots.Add(HandheldSlot.Empty);
        slots[index] = slot;
        return FromSlots(slots, Folders);
    }

    public HandheldHomeLayout WithSlots(IReadOnlyList<HandheldSlot> slots) => FromSlots(slots, Folders);

    public HandheldHomeLayout WithFolder(HandheldUserFolder folder)
    {
        var folders = Folders.ToList();
        var index = folders.FindIndex(existing => string.Equals(existing.Id, folder.Id, StringComparison.Ordinal));
        if (index >= 0)
            folders[index] = folder;
        else
            folders.Add(folder);
        return new HandheldHomeLayout(Pages, folders);
    }

    /// <summary>Deletes one of your folders and its tile. The games are untouched: a folder only holds references.</summary>
    public HandheldHomeLayout WithoutFolder(string folderId)
    {
        var slots = AllSlots()
            .Select(slot => slot.Kind == HandheldSlotKind.UserFolder && slot.FolderId == folderId ? HandheldSlot.Empty : slot)
            .ToList();
        return FromSlots(slots, Folders.Where(folder => folder.Id != folderId));
    }

    /// <summary>Makes a new folder of yours at <paramref name="index"/> holding <paramref name="gameIds"/>.</summary>
    public HandheldHomeLayout WithNewFolder(int index, string folderId, string name, string color, IEnumerable<long> gameIds) =>
        WithFolder(new HandheldUserFolder(folderId, name, color, gameIds.Distinct().ToList()))
            .WithSlot(index, HandheldSlot.UserFolder(folderId));

    /// <summary>Adds a game to the end of one of your folders (its Manual order); a game already there stays put.</summary>
    public HandheldHomeLayout WithGameAdded(string folderId, long gameId) =>
        FindFolder(folderId) is not { } folder || folder.Contains(gameId)
            ? this
            : WithFolder(folder with { GameIds = folder.GameIds.Append(gameId).ToList() });

    public HandheldHomeLayout WithGameRemoved(string folderId, long gameId) =>
        FindFolder(folderId) is not { } folder || !folder.Contains(gameId)
            ? this
            : WithFolder(folder with { GameIds = folder.GameIds.Where(id => id != gameId).ToList() });

    /// <summary>Pins a game to the first empty slot; unchanged (and false) when it is pinned already or home is full.</summary>
    public HandheldHomeLayout WithPin(long gameId, out int index)
    {
        index = IndexOf(HandheldSlot.Game(gameId));
        if (index >= 0)
            return this;
        index = FirstEmptySlot();
        return index < 0 ? this : WithSlot(index, HandheldSlot.Game(gameId));
    }

    public HandheldHomeLayout WithoutPin(long gameId) =>
        WithSlots(AllSlots().Select(slot => slot.Kind == HandheldSlotKind.Game && slot.GameId == gameId ? HandheldSlot.Empty : slot).ToList());

    /// <summary>Takes the "New" ribbon off a slot (its folder was opened or moved).</summary>
    public HandheldHomeLayout WithRibbonCleared(int index)
    {
        if (index < 0 || index >= SlotCount || !SlotAt(index).IsNew)
            return this;
        return WithSlot(index, SlotAt(index) with { IsNew = false });
    }

    /// <summary>A fresh id for a new folder of yours.</summary>
    public static string NewFolderId() => "u-" + Guid.NewGuid().ToString("N")[..12];
}
