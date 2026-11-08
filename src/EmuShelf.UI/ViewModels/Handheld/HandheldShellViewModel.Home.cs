using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

// Home as a place you arrange (M45 H4): the saved layout, reconciled with the library on every read; the
// games pinned to it (their entries and tile covers are the shell's); and the pages the Launcher draws, rebuilt
// only where a slot changed, so an edit or a step in move mode re-pictures one page, not all of them.
public sealed partial class HandheldShellViewModel
{
    /// <summary>How many games the Recent folder holds (decision 14).</summary>
    public const int RecentCount = 12;

    private readonly IHandheldHomeLayoutStore _store;
    private IReadOnlyList<long> _recentIds = [];
    private readonly Dictionary<long, HandheldGameEntry> _pins = [];
    private readonly Dictionary<long, Bitmap?> _pinCovers = [];
    private readonly HashSet<long> _pinLoading = [];

    /// <summary>The home layout as it stands (saved after every edit).</summary>
    public HandheldHomeLayout Layout => _layout;

    /// <summary>The focused home slot across all pages (page × 20 + slot).</summary>
    private int HomeIndex => (_home.Page * HandheldHomeLayout.SlotsPerPage) + _home.Slot;

    private static HandheldHomePosition PositionOf(int index) =>
        new(index / HandheldHomeLayout.SlotsPerPage, index % HandheldHomeLayout.SlotsPerPage);

    /// <summary>
    /// Reads the saved layout (or makes the default), reconciles it with the library and saves it back if that
    /// changed anything. The first start saves the default, so the layout survives from then on.
    /// </summary>
    private HandheldHomeLayout LoadLayout(IReadOnlyList<HandheldPlatform> platforms, IReadOnlySet<long> gameIds)
    {
        var platformIds = platforms.Select(platform => platform.SystemId).ToList();
        var saved = _store.Load();
        var layout = HandheldHomeReconciler.Reconcile(saved ?? HandheldHomeLayout.CreateDefault(platformIds), platformIds,
            gameIds, out var changed);
        if (saved is null || changed)
            _store.Save(layout);
        return layout;
    }

    /// <summary>Makes <paramref name="layout"/> home: saves it, redraws what changed and catches up the pins.</summary>
    private void ApplyLayout(HandheldHomeLayout layout)
    {
        _layout = layout;
        _store.Save(layout);
        BuildPages();
        if (_home.Page >= _layout.Pages.Count)
            _home = new HandheldHomePosition(_layout.Pages.Count - 1, _home.Slot);
        ShowPage(_home.Page);
        PrunePins();
        _ = EnsurePinsAsync();
        UpdateFocus();
    }

    /// <summary>The folder a slot opens, as of now; null for a pin, an empty slot, or a platform with no games left.</summary>
    private HandheldFolderSource? FolderSourceFor(HandheldSlot slot) => slot.Kind switch
    {
        HandheldSlotKind.PlatformFolder when slot.SystemId is { } id && _platforms.TryGetValue(id, out var platform) =>
            HandheldFolderSource.ForPlatform(platform),
        HandheldSlotKind.UserFolder when _layout.FindFolder(slot.FolderId) is { } folder => HandheldFolderSource.ForUser(folder),
        HandheldSlotKind.System when slot.SystemTile == HandheldSystemTile.Recent => HandheldFolderSource.ForRecent(_recentIds),
        _ => null,
    };

    // ---- Pages ----

    private void BuildPages()
    {
        var slots = _move?.Preview ?? _layout.AllSlots();
        var pageCount = slots.Count / HandheldHomeLayout.SlotsPerPage;
        for (var page = 0; page < pageCount; page++)
        {
            var built = new List<HandheldHomeSlotViewModel>(HandheldHomeLayout.SlotsPerPage);
            for (var slot = 0; slot < HandheldHomeLayout.SlotsPerPage; slot++)
                built.Add(BuildSlot(page, slot, slots[(page * HandheldHomeLayout.SlotsPerPage) + slot]));
            if (page < Pages.Count)
            {
                // A page whose slots all look the same keeps its view models (and its pictures on the Thor).
                if (Pages[page].Slots.Select(s => s.PictureKey).SequenceEqual(built.Select(s => s.PictureKey)))
                    continue;
                Pages[page] = new HandheldHomePageViewModel(page, built);
            }
            else
            {
                Pages.Add(new HandheldHomePageViewModel(page, built));
            }
        }
        while (Pages.Count > pageCount)
            Pages.RemoveAt(Pages.Count - 1);
        if (PageDots.Count != pageCount)
        {
            PageDots.Clear();
            for (var page = 0; page < pageCount; page++)
                PageDots.Add(new HandheldPageDotViewModel(page));
        }
        PageCount = Pages.Count;
        // The peek covers belong to the focused slot's view model, which may just have been replaced.
        _peekSlot = null;
    }

    private HandheldHomeSlotViewModel BuildSlot(int page, int slot, HandheldSlot content)
    {
        var index = (page * HandheldHomeLayout.SlotsPerPage) + slot;
        var look = HandheldMoveLook.None;
        if (_move is { } move && index == move.Target)
            look = move.Drop == HandheldDropKind.Place ? HandheldMoveLook.Carried : HandheldMoveLook.DropTarget;
        HandheldGameEntry? game = null;
        Bitmap? cover = null;
        if (content.Kind == HandheldSlotKind.Game)
        {
            _pins.TryGetValue(content.GameId, out game);
            _pinCovers.TryGetValue(content.GameId, out cover);
        }
        return new HandheldHomeSlotViewModel(slot, content, FolderSourceFor(content), game, cover, _move is not null, look);
    }

    // ---- Games pinned to home ----

    private IEnumerable<long> PinnedIds() =>
        _layout.AllSlots().Where(slot => slot.Kind == HandheldSlotKind.Game).Select(slot => slot.GameId);

    /// <summary>Reads the entries (and tile covers) of pins that are new to home, then redraws their slots.</summary>
    private async Task EnsurePinsAsync()
    {
        var missing = PinnedIds().Where(id => !_pins.ContainsKey(id) && _pinLoading.Add(id)).ToList();
        if (missing.Count == 0 || _disposed)
            return;
        IReadOnlyList<HandheldGameEntry> games;
        try
        {
            games = await _library.GetGamesByIdAsync(missing);
        }
        catch (Exception)
        {
            foreach (var id in missing)
                _pinLoading.Remove(id);
            return; // the slots keep their placeholder; the next edit tries again
        }
        foreach (var id in missing)
            _pinLoading.Remove(id);
        var pinned = PinnedIds().ToHashSet();
        var kept = new List<HandheldGameEntry>();
        foreach (var game in games)
        {
            if (_disposed || !pinned.Contains(game.Id) || _pins.ContainsKey(game.Id))
                _library.ReleaseGames([game]);
            else
            {
                _pins[game.Id] = game;
                kept.Add(game);
            }
        }
        if (kept.Count == 0)
            return;
        BuildPages();
        ShowPage(_home.Page);
        UpdateFocus();
        foreach (var game in kept)
        {
            Bitmap? cover = null;
            try
            {
                cover = await _library.LoadCoverAsync(game, TileDecodeWidth, fullSize: false, CancellationToken.None);
            }
            catch (Exception)
            {
                // A pin without a decodable cover shows its title instead.
            }
            if (_disposed || !_pins.TryGetValue(game.Id, out var current) || !ReferenceEquals(current, game))
            {
                cover?.Dispose();
                continue;
            }
            _pinCovers[game.Id] = cover;
        }
        BuildPages();
        ShowPage(_home.Page);
        UpdateFocus();
    }

    /// <summary>Releases the entries and covers of games no longer pinned.</summary>
    private void PrunePins(bool all = false)
    {
        var pinned = all ? [] : PinnedIds().ToHashSet();
        foreach (var id in _pins.Keys.Where(id => !pinned.Contains(id)).ToList())
        {
            var game = _pins[id];
            _pins.Remove(id);
            if (_pinCovers.Remove(id, out var cover) && cover is not null)
            {
                if (_stageArtBorrowed && ReferenceEquals(Stage.Art, cover))
                    ReplaceStageArt(null, borrowed: false);
                cover.Dispose();
            }
            // A Title page opened from this pin still uses the entry; it is released when that page closes.
            if (TitlePage?.Entry.Id == id)
                _releaseWithTitle.Add(game);
            else
                _library.ReleaseGames([game]);
        }
    }

    private readonly List<HandheldGameEntry> _releaseWithTitle = [];

    /// <summary>
    /// Re-reads which games the Recent folder holds (a play was just recorded). Its tile, its peek covers and an
    /// open Recent folder follow.
    /// </summary>
    public async Task RefreshRecentAsync()
    {
        IReadOnlyList<long> ids;
        try
        {
            ids = await _library.GetRecentGameIdsAsync(RecentCount);
        }
        catch (Exception)
        {
            return;
        }
        if (_disposed || ids.SequenceEqual(_recentIds))
            return;
        _recentIds = ids;
        InvalidatePeek("r:recent");
        BuildPages();
        ShowPage(_home.Page);
        UpdateFocus();
    }
}
