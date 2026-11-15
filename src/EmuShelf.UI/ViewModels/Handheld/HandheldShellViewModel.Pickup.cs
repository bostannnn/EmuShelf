using System;
using System.Collections.Generic;
using System.Linq;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// Pick up and put down in the Big Type look (M47 B9, the mock's <c>startMove</c>, <c>moveKey</c> and <c>endMove</c>): the held tile
/// trades places with whatever is in the slot it moves to, across pages too; each step is remembered; A puts it down where it is and
/// B walks every step back. Today's look keeps its own move mode (the tile lifted out, the others sliding aside, a drop that merges).
/// What today's drop can do that a trade cannot (a game into one of your folders, or into a new one; Settings or Activity into a
/// folder) is on Y while the tile is held: the mock's Y does nothing there.
/// </summary>
public partial class HandheldShellViewModel
{
    /// <summary>
    /// Tiles that traded places on home (each from one slot index to another, across pages): the Launcher slides them to their new
    /// slots. Raised after home's pages are rebuilt.
    /// </summary>
    public event Action<IReadOnlyList<(int From, int To)>>? TilesTraded;

    /// <summary>A held tile was put down on this home slot (A, or B after its steps were walked back): its inside squashes and settles.</summary>
    public event Action<int>? TilePutDown;

    /// <summary>A folder named on the Big Type keyboard was made on this home slot: it is born there once the keyboard has gone.</summary>
    public event Action<int>? FolderBorn;

    /// <summary>The home slot (across pages) of the tile held now (Big Type), or −1 when none is held.</summary>
    public int HomeIndexOfHeld => _move is { Trades: true } move ? move.Target : -1;

    /// <summary>The mock's kicker while a tile is held.</summary>
    internal const string MovingKicker = "Moving";

    /// <summary>The mock's line while a tile is held.</summary>
    internal const string MovingLine = "It trades places with what it passes";

    /// <summary>The held tile trades places with whatever is at <paramref name="index"/> (a step of the pad, a shoulder, a drag).</summary>
    private void TradeTo(MoveState move, int index)
    {
        var slots = move.Preview.ToList();
        var from = move.Target;
        (slots[from], slots[index]) = (slots[index], slots[from]);
        move.Path.Add(from);
        move.Preview = slots;
        move.Target = index;
        UpdateMovePreview();
        TilesTraded?.Invoke([(from, index), (index, from)]);
    }

    /// <summary>Puts the held tile down: where it is (A), or with every step walked back so home is as it was (B).</summary>
    private void PutDown(MoveState move, bool back)
    {
        _move = null;
        IsMoving = false;
        if (back)
        {
            // Which slot each tile on home now ends on: the steps walked back in turn (the mock's endMove(undo)).
            var at = Enumerable.Range(0, move.Preview.Count).ToArray();
            var here = move.Target;
            for (var step = move.Path.Count - 1; step >= 0; step--)
            {
                var to = move.Path[step];
                (at[here], at[to]) = (at[to], at[here]);
                here = to;
            }
            var traded = new List<(int From, int To)>();
            for (var slot = 0; slot < at.Length; slot++)
            {
                if (at[slot] != slot)
                    traded.Add((at[slot], slot));
            }
            _home = PositionOf(move.Origin);
            BuildPages();
            ShowPage(_home.Page);
            UpdateFocus();
            if (traded.Count > 0)
                TilesTraded?.Invoke(traded);
            TilePutDown?.Invoke(move.Origin);
            return;
        }
        var slots = move.Preview.ToList();
        slots[move.Target] = move.Carried with { IsNew = false };
        _home = PositionOf(move.Target);
        ApplyLayout(_layout.WithSlots(slots));
        TilePutDown?.Invoke(move.Target);
    }

    // ---- Y while held: into a folder ----

    /// <summary>Whether the held tile can go into a folder: a game always (one of yours, or a new one), Settings or Activity into one of yours.</summary>
    private bool CanPutIn(MoveState move) =>
        move.Carried.Kind == HandheldSlotKind.Game || (move.Carried.IsFoldableApp && _layout.Folders.Count > 0);

    /// <summary>Y while a tile is held: a sheet of where it can go. A row puts it there and the move ends; B goes back to holding it.</summary>
    private void OpenPutInSheet(MoveState move)
    {
        if (!CanPutIn(move))
        {
            Bumped?.Invoke(HandheldDirection.Up);
            return;
        }
        var carried = move.Carried;
        var items = new List<HandheldSheetItemViewModel>();
        if (carried.Kind == HandheldSlotKind.Game)
            items.Add(new("New folder", "With this game in it, on this slot", () => PutInNewFolder(move)));
        foreach (var folder in _layout.Folders)
        {
            var id = folder.Id;
            var already = carried.Kind == HandheldSlotKind.Game && folder.Contains(carried.GameId);
            items.Add(new(folder.Name, already ? "Already in it" : HandheldText.GameCount(folder.GameIds.Count), () => PutInFolder(move, id)));
        }
        var title = carried.IsSettings ? HandheldSettingsText.Name
            : carried.IsActivity ? HandheldActivityText.Name
            : _pins.GetValueOrDefault(carried.GameId)?.Title ?? "Put in";
        ShowSheet(new HandheldSheetViewModel(title, "Put it in a folder", items) { Kicker = PutInKicker });
    }

    /// <summary>The Put in sheet's kicker on the Stage.</summary>
    internal const string PutInKicker = "Put in";

    /// <summary>The held tile goes into one of your folders: its slot is left empty and home keeps the rest as it now stands.</summary>
    private void PutInFolder(MoveState move, string folderId)
    {
        if (!ReferenceEquals(_move, move) || _layout.FindFolder(folderId) is not { } folder)
            return;
        _move = null;
        IsMoving = false;
        var slots = move.Preview.ToList();
        slots[move.Target] = HandheldSlot.Empty;
        InvalidatePeek("u:" + folderId);
        _home = PositionOf(move.Target);
        if (move.Carried.IsFoldableApp)
        {
            ApplyLayout(_layout.WithSlots(slots).WithAppIn(move.Carried.SystemTile, folderId));
            ShowNotice($"{(move.Carried.IsActivity ? HandheldActivityText.Name : HandheldSettingsText.Name)} is in {folder.Name}");
            return;
        }
        ApplyLayout(_layout.WithSlots(slots).WithGameAdded(folderId, move.Carried.GameId));
        ShowNotice(folder.Contains(move.Carried.GameId) ? $"Already in {folder.Name}" : $"Added to {folder.Name}");
    }

    /// <summary>The held game becomes a new folder on its slot: home is put down as it stands, then the folder is named.</summary>
    private void PutInNewFolder(MoveState move)
    {
        if (!ReferenceEquals(_move, move))
            return;
        _move = null;
        IsMoving = false;
        var slots = move.Preview.ToList();
        slots[move.Target] = move.Carried with { IsNew = false };
        _home = PositionOf(move.Target);
        ApplyLayout(_layout.WithSlots(slots));
        StartNaming(isNew: true, move.Target, [move.Carried.GameId], null, removePinFrom: -1, NotePickedSlot);
    }

    // ---- The Stage while a tile is held ----

    /// <summary>
    /// The mock's setFocus while a tile is held: the held tile's own Stage (its name, colour and backdrop, its second line), with
    /// "Moving" as the label and what a step does as the line.
    /// </summary>
    private void ShowHeldStage(MoveState move)
    {
        var slot = move.Carried;
        if (slot.IsSettings)
            ShowSettingsTileStage();
        else if (slot.IsActivity)
            ShowActivityTileStage();
        else if (slot.Kind == HandheldSlotKind.Game && _pins.TryGetValue(slot.GameId, out var pinned))
            ShowGameStage(pinned, _pinCovers.GetValueOrDefault(pinned.Id));
        else if (FolderSourceFor(slot) is { } source)
            ShowFolderStage(source, source.Description);
        else
            ShowStage(HandheldStageKind.EmptySlot, string.Empty, string.Empty);
        BtLabel = MovingKicker;
        BtLine = MovingLine;
    }
}
