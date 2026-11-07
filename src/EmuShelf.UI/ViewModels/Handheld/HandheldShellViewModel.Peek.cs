using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace EmuShelf.App.ViewModels;

// The covers that peek out of a focused folder once its flap tilts (Motion spec, "Folder focus"): the first
// three games of the platform, decoded small once per platform and kept for the shell's lifetime, so going
// back to a folder never decodes again. The Stage's big folder and the focused home tile share them.
public sealed partial class HandheldShellViewModel
{
    /// <summary>Peek covers decode at about the big folder's cover width (≈ 200 px).</summary>
    public const int PeekDecodeWidth = 200;

    private readonly Dictionary<string, Bitmap?[]> _peekCovers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _peekLoading = new(StringComparer.Ordinal);
    private HandheldHomeSlotViewModel? _peekSlot;

    private void ApplyPeek(string systemId, HandheldStageViewModel stage)
    {
        if (_peekCovers.TryGetValue(systemId, out var covers))
        {
            stage.PeekA = covers[0];
            stage.PeekB = covers[1];
            stage.PeekC = covers[2];
            return;
        }
        _ = LoadPeekAsync(systemId);
    }

    /// <summary>The focused home folder tile shows the same covers as the Stage's big folder.</summary>
    private void UpdateHomePeek()
    {
        HandheldHomeSlotViewModel? focused = null;
        if (OpenFolder is null && Zone == HandheldZone.Grid && CurrentPage is { } page && _home.Slot < page.Slots.Count)
            focused = page.Slots[_home.Slot];
        if (!ReferenceEquals(focused, _peekSlot) && _peekSlot is { } previous)
            previous.PeekA = previous.PeekB = previous.PeekC = null;
        _peekSlot = focused;
        if (focused?.Platform is { } platform && _peekCovers.TryGetValue(platform.SystemId, out var covers))
        {
            focused.PeekA = covers[0];
            focused.PeekB = covers[1];
            focused.PeekC = covers[2];
        }
    }

    private async Task LoadPeekAsync(string systemId)
    {
        if (!_peekLoading.Add(systemId))
            return;
        var covers = new Bitmap?[3];
        try
        {
            var games = await _library.GetFirstGamesAsync(systemId, covers.Length);
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
            _peekLoading.Remove(systemId);
        }
        if (_disposed)
        {
            foreach (var cover in covers)
                cover?.Dispose();
            return;
        }
        _peekCovers[systemId] = covers;
        if (Stage.Kind == HandheldStageKind.Folder && CurrentFolderStageId() == systemId)
            ApplyPeek(systemId, Stage);
        UpdateHomePeek();
    }

    private string? CurrentFolderStageId()
    {
        if (OpenFolder is { } folder)
            return folder.Platform.SystemId;
        return _layout.SlotAt(_home.Page, _home.Slot).SystemId;
    }

    private void ReleasePeekCovers()
    {
        Stage.PeekA = Stage.PeekB = Stage.PeekC = null;
        if (_peekSlot is { } slot)
            slot.PeekA = slot.PeekB = slot.PeekC = null;
        _peekSlot = null;
        foreach (var covers in _peekCovers.Values)
        {
            foreach (var cover in covers)
                cover?.Dispose();
        }
        _peekCovers.Clear();
    }
}
