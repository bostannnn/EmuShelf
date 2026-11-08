using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace EmuShelf.App.ViewModels;

// The covers that peek out of a focused folder once its flap tilts (Motion spec, "Folder focus"): the first
// three games of the folder, decoded small once per folder (by its key) and kept for the shell's lifetime, so
// going back to a folder never decodes again; an edit to one of your folders reads its covers again. The Stage's
// big folder and the focused home tile share them.
public sealed partial class HandheldShellViewModel
{
    /// <summary>Peek covers decode at about the big folder's cover width (≈ 200 px).</summary>
    public const int PeekDecodeWidth = 200;

    private readonly Dictionary<string, Bitmap?[]> _peekCovers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _peekLoading = new(StringComparer.Ordinal);

    private void ApplyPeek(HandheldFolderSource source, HandheldStageViewModel stage)
    {
        if (_peekCovers.TryGetValue(source.Key, out var covers))
        {
            stage.PeekA = covers[0];
            stage.PeekB = covers[1];
            stage.PeekC = covers[2];
            return;
        }
        _ = LoadPeekAsync(source);
    }

    /// <summary>The focused home folder tile shows the same covers as the Stage's big folder, in both home shapes.</summary>
    private void UpdateHomePeek()
    {
        var homeIndex = HomeIndex;
        foreach (var screen in Screens)
        {
            HandheldHomeSlotViewModel? focused = null;
            var at = screen.Grid.PositionOf(homeIndex);
            if (OpenFolder is null && Zone == HandheldZone.Grid && screen.CurrentPage is { } page && page.Index == at.Page &&
                at.Slot < page.Slots.Count)
            {
                focused = page.Slots[at.Slot];
            }
            if (!ReferenceEquals(focused, screen.PeekSlot) && screen.PeekSlot is { } previous)
                previous.PeekA = previous.PeekB = previous.PeekC = null;
            screen.PeekSlot = focused;
            if (focused?.Folder is { } folder && _peekCovers.TryGetValue(folder.Key, out var covers))
            {
                focused.PeekA = covers[0];
                focused.PeekB = covers[1];
                focused.PeekC = covers[2];
            }
        }
    }

    private async Task LoadPeekAsync(HandheldFolderSource source)
    {
        var key = source.Key;
        if (!_peekLoading.Add(key))
            return;
        var covers = new Bitmap?[3];
        var generation = _peekGeneration;
        try
        {
            var games = source.Kind == HandheldFolderKind.Platform && source.Platform is { } platform
                ? await _library.GetFirstGamesAsync(platform.SystemId, covers.Length)
                : await _library.GetGamesByIdAsync((source.IsRecent ? _recentIds : source.GameIds ?? []).Take(covers.Length).ToList());
            try
            {
                for (var i = 0; i < covers.Length && i < games.Count && !_disposed; i++)
                    covers[i] = await _library.LoadCoverAsync(games[i], PeekDecodeWidth, fullSize: false, default);
            }
            finally
            {
                _library.ReleaseGames(games);
            }
        }
        catch (Exception)
        {
            // Peeking covers are decoration: a failed read leaves the folder closed-looking, never a fault.
        }
        finally
        {
            _peekLoading.Remove(key);
        }
        // A folder edited while its covers were read (or the shell gone) keeps none of them.
        if (_disposed || generation != _peekGeneration || _peekCovers.ContainsKey(key))
        {
            foreach (var cover in covers)
                cover?.Dispose();
            return;
        }
        _peekCovers[key] = covers;
        if (Stage.Kind == HandheldStageKind.Folder && CurrentFolderStageKey() == key)
            ApplyPeek(source, Stage);
        UpdateHomePeek();
    }

    // Bumped whenever covers are dropped, so a read that started before can tell its covers are stale.
    private int _peekGeneration;

    private string? CurrentFolderStageKey()
    {
        if (OpenFolder is { } folder)
            return folder.Source.Key;
        return _move is null && Naming is null ? FolderSourceFor(HomeSlotAt(HomeIndex))?.Key : null;
    }

    /// <summary>Drops one folder's peek covers (its games changed); the next focus reads them again.</summary>
    private void InvalidatePeek(string key)
    {
        _peekGeneration++;
        if (!_peekCovers.Remove(key, out var covers))
            return;
        // Off the Stage and the tile first, then disposed, so no frame can draw a disposed image.
        if (covers.Any(cover => cover is not null && (ReferenceEquals(cover, Stage.PeekA) || ReferenceEquals(cover, Stage.PeekB) || ReferenceEquals(cover, Stage.PeekC))))
            Stage.PeekA = Stage.PeekB = Stage.PeekC = null;
        foreach (var page in Screens.SelectMany(screen => screen.Pages))
        {
            foreach (var tile in page.Slots)
            {
                if (tile.Folder?.Key == key)
                    tile.PeekA = tile.PeekB = tile.PeekC = null;
            }
        }
        foreach (var cover in covers)
            cover?.Dispose();
    }

    private void ReleasePeekCovers()
    {
        _peekGeneration++;
        Stage.PeekA = Stage.PeekB = Stage.PeekC = null;
        foreach (var screen in Screens)
        {
            if (screen.PeekSlot is { } slot)
                slot.PeekA = slot.PeekB = slot.PeekC = null;
            screen.PeekSlot = null;
        }
        foreach (var covers in _peekCovers.Values)
        {
            foreach (var cover in covers)
                cover?.Dispose();
        }
        _peekCovers.Clear();
    }
}
