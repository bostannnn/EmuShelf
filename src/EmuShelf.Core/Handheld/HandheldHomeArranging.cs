namespace EmuShelf.Core.Handheld;

/// <summary>What dropping a carried tile on a slot does in move mode.</summary>
public enum HandheldDropKind
{
    /// <summary>The tile lands there; tiles in the way slide aside toward the nearest empty slot.</summary>
    Place,

    /// <summary>A game dropped on another game: the two become a new folder of yours, named next.</summary>
    MakeFolder,

    /// <summary>A game dropped on one of your folders: it joins the folder, and the pin leaves home.</summary>
    AddToFolder,
}

/// <summary>
/// Move mode's rules on the flat list of home slots (page × 20 + slot), as the mockup has them: the lifted
/// tile leaves an empty slot behind; held over a slot, it either merges (a game onto a game or one of your
/// folders) or takes the slot while the tiles in the way slide aside to the nearest empty one.
/// </summary>
public static class HandheldHomeArranging
{
    /// <summary>The slots with the lifted tile taken out: what everything else is arranged around.</summary>
    public static IReadOnlyList<HandheldSlot> Lift(IReadOnlyList<HandheldSlot> slots, int origin) =>
        slots.Select((slot, index) => index == origin ? HandheldSlot.Empty : slot).ToList();

    public static HandheldDropKind Classify(HandheldSlot carried, HandheldSlot target) => (carried.Kind, target.Kind) switch
    {
        (HandheldSlotKind.Game, HandheldSlotKind.Game) when carried.GameId != target.GameId => HandheldDropKind.MakeFolder,
        (HandheldSlotKind.Game, HandheldSlotKind.UserFolder) => HandheldDropKind.AddToFolder,
        // Settings is foldable like a game (decision 18), but two of it never make a folder: on a game it just lands.
        (HandheldSlotKind.System, HandheldSlotKind.UserFolder) when carried.IsSettings => HandheldDropKind.AddToFolder,
        _ => HandheldDropKind.Place,
    };

    /// <summary>
    /// <paramref name="item"/> at <paramref name="target"/> in <paramref name="lifted"/>: straight in when the slot is
    /// empty, else the run of tiles from it shifts one place toward the nearest empty slot after it (or, failing
    /// that, before it). Null only when there is no empty slot at all.
    /// </summary>
    public static IReadOnlyList<HandheldSlot>? InsertAt(IReadOnlyList<HandheldSlot> lifted, int target, HandheldSlot item)
    {
        var slots = lifted.ToList();
        while (slots.Count <= target)
            slots.Add(HandheldSlot.Empty);
        if (slots[target].IsEmpty)
        {
            slots[target] = item;
            return slots;
        }
        var after = slots.FindIndex(target + 1, slot => slot.IsEmpty);
        if (after >= 0)
        {
            for (var i = after; i > target; i--)
                slots[i] = slots[i - 1];
            slots[target] = item;
            return slots;
        }
        var before = slots.FindLastIndex(target, slot => slot.IsEmpty);
        if (before < 0)
            return null;
        for (var i = before; i < target; i++)
            slots[i] = slots[i + 1];
        slots[target] = item;
        return slots;
    }
}

/// <summary>
/// Keeps a saved home layout true to the library (spec "Reconciliation"): a platform that gained games gets its
/// folder appended with the "New" ribbon; a platform that has none left loses its folder and the slot stays
/// empty; games that left the library are dropped from every folder and pin. Missing files are not removed —
/// the views dim them. The Recent folder is put back if it is gone. Settings is always somewhere, exactly once:
/// on home or in one of your folders; when it is in neither (a layout saved before it existed, a hand-edited file,
/// its folder gone) it is appended to home after the platform folders.
/// </summary>
public static class HandheldHomeReconciler
{
    public static HandheldHomeLayout Reconcile(
        HandheldHomeLayout layout,
        IReadOnlyList<string> platformIds,
        IReadOnlySet<long> gameIds,
        out bool changed)
    {
        var platforms = new HashSet<string>(platformIds, StringComparer.Ordinal);
        var folders = layout.Folders
            .Select(folder => folder with { GameIds = folder.GameIds.Where(gameIds.Contains).Distinct().ToList() })
            .ToList();
        var folderIds = new HashSet<string>(folders.Select(folder => folder.Id), StringComparer.Ordinal);

        // One tile per thing: a second copy of a platform, folder, pin or Recent (a hand-edited file) goes.
        var seen = new HashSet<HandheldSlot>();
        var slots = layout.AllSlots()
            .Select(slot =>
            {
                var keep = slot.Kind switch
                {
                    HandheldSlotKind.PlatformFolder => slot.SystemId is { } id && platforms.Contains(id),
                    HandheldSlotKind.UserFolder => slot.FolderId is { } id && folderIds.Contains(id),
                    HandheldSlotKind.Game => gameIds.Contains(slot.GameId),
                    HandheldSlotKind.System => true,
                    _ => false,
                };
                return keep && seen.Add(slot with { IsNew = false }) ? slot : HandheldSlot.Empty;
            })
            .ToList();

        // A folder of yours with no tile can never be reached again; it is dropped with the layout's next save.
        var placedFolders = slots.Where(slot => slot.Kind == HandheldSlotKind.UserFolder).Select(slot => slot.FolderId!).ToHashSet(StringComparer.Ordinal);
        folders = folders.Where(folder => placedFolders.Contains(folder.Id)).ToList();

        // Settings once: on home wins over a folder; of several folders holding it, the first keeps it.
        var settingsOnHome = slots.Any(slot => slot.IsSettings);
        var settingsHolder = settingsOnHome ? null : folders.FirstOrDefault(folder => folder.HoldsSettings)?.Id;
        folders = folders.Select(folder => folder.HoldsSettings && folder.Id != settingsHolder ? folder with { HoldsSettings = false } : folder).ToList();

        var additions = new List<HandheldSlot>();
        if (platforms.Count > 0 && !slots.Any(slot => slot.Kind == HandheldSlotKind.System && slot.SystemTile == HandheldSystemTile.Recent))
            additions.Add(HandheldSlot.Recent);
        var placedPlatforms = slots.Where(slot => slot.Kind == HandheldSlotKind.PlatformFolder).Select(slot => slot.SystemId!).ToHashSet(StringComparer.Ordinal);
        additions.AddRange(platformIds.Distinct(StringComparer.Ordinal).Where(id => !placedPlatforms.Contains(id))
            .Select(id => HandheldSlot.PlatformFolder(id, isNew: true)));
        if (!settingsOnHome && settingsHolder is null)
            additions.Add(HandheldSlot.Settings);

        // Appended after the last tile on home, so new folders never jump in among the ones you arranged.
        var next = slots.FindLastIndex(slot => !slot.IsEmpty) + 1;
        foreach (var addition in additions)
        {
            while (next < slots.Count && !slots[next].IsEmpty)
                next++;
            if (next < slots.Count)
                slots[next] = addition;
            else
                slots.Add(addition);
            next++;
        }

        var result = HandheldHomeLayout.FromSlots(slots, folders);
        changed = !SameLayout(layout, result);
        return result;
    }

    private static bool SameLayout(HandheldHomeLayout a, HandheldHomeLayout b) =>
        a.AllSlots().SequenceEqual(b.AllSlots()) &&
        a.Folders.Count == b.Folders.Count &&
        a.Folders.Zip(b.Folders).All(pair => pair.First with { GameIds = [] } == pair.Second with { GameIds = [] } &&
            pair.First.GameIds.SequenceEqual(pair.Second.GameIds));
}

/// <summary>Persists the Handheld home layout (portable <c>Settings/handheld-home.json</c>).</summary>
public interface IHandheldHomeLayoutStore
{
    /// <summary>The saved layout, or null when there is none yet (or it could not be read): home starts from the default.</summary>
    HandheldHomeLayout? Load();

    void Save(HandheldHomeLayout layout);
}

/// <summary>A store that keeps nothing: home is the generated default on every start (tests, design previews).</summary>
public sealed class InMemoryHandheldHomeLayoutStore : IHandheldHomeLayoutStore
{
    public HandheldHomeLayout? Saved { get; private set; }

    public int Saves { get; private set; }

    public HandheldHomeLayout? Load() => Saved;

    public void Save(HandheldHomeLayout layout)
    {
        Saved = layout;
        Saves++;
    }
}
