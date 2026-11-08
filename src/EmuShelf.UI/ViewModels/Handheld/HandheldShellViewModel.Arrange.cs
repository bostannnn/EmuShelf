using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

// Arranging home (M45 H4): the actions sheet (Y), move mode, naming and recolouring your folders, pinning,
// and the short notices that say what an edit did. Every edit goes through ApplyLayout, which saves it.
// A game can be in several places at once (decision 12): folders and pins are references, so every "Remove"
// names the place it means, and none of them touches the library or the game's files.
public sealed partial class HandheldShellViewModel
{
    /// <summary>The open actions sheet (or a confirmation), drawn over the Launcher; null when none.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsSheetOpen))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsLauncherOverlayOpen))]
    public partial HandheldSheetViewModel? Sheet { get; private set; }

    public bool IsSheetOpen => Sheet is not null;

    /// <summary>The naming panel (a new folder, or renaming one of yours); null when none.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsNaming))]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsLauncherOverlayOpen))]
    public partial HandheldNamingViewModel? Naming { get; private set; }

    public bool IsNaming => Naming is not null;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    [CommunityToolkit.Mvvm.ComponentModel.NotifyPropertyChangedFor(nameof(IsLauncherOverlayOpen))]
    public partial bool IsMoving { get; private set; }

    /// <summary>
    /// Something the Avalonia Launcher draws is open (the menu, a sheet, naming, move mode): on the Thor the native
    /// Launcher hands the screen back to it while this is true.
    /// </summary>
    public bool IsLauncherOverlayOpen => IsMenuOpen || Sheet is not null || Naming is not null || IsMoving;

    partial void OnIsMenuOpenChanged(bool value) => OnPropertyChanged(nameof(IsLauncherOverlayOpen));

    // ---- Notices: a short line on the Stage that says what an edit did, then the app's status comes back ----

    private string? _appStatus;
    private string? _notice;
    private DispatcherTimer? _noticeTimer;

    public static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(3);

    /// <summary>Mirrors the app's status line; it clears itself when the app's message is dismissed.</summary>
    public void ShowStatus(string? message)
    {
        _appStatus = message;
        // A new message from the app (a launch, a failure) matters more than an edit's notice: it shows at once.
        if (_notice is not null && !string.IsNullOrWhiteSpace(message))
        {
            _noticeTimer?.Stop();
            _notice = null;
        }
        if (_notice is null)
            StatusMessage = message;
    }

    /// <summary>The last notice an edit showed (tests read it; the Stage shows <see cref="StatusMessage"/>).</summary>
    public string? Notice => _notice;

    private void ShowNotice(string text)
    {
        _notice = text;
        StatusMessage = text;
        if (_disposed)
            return;
        if (_noticeTimer is null)
        {
            _noticeTimer = new DispatcherTimer { Interval = NoticeDuration };
            _noticeTimer.Tick += (_, _) =>
            {
                _noticeTimer?.Stop();
                _notice = null;
                StatusMessage = _appStatus;
            };
        }
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    // ---- The actions sheet (Y) ----

    private void OpenActions()
    {
        if (Zone != HandheldZone.Grid)
        {
            Bumped?.Invoke(HandheldDirection.Up);
            return;
        }
        if (OpenFolder is { } folder)
        {
            if (folder.SelectedTile is { } tile)
                ShowSheet(GameSheet(tile.Entry, folder));
            return;
        }
        if (SlotSheet(HomeIndex) is { } sheet)
            ShowSheet(sheet);
    }

    private void ShowSheet(HandheldSheetViewModel sheet)
    {
        Sheet = sheet;
        UpdateHints();
    }

    private void CloseSheet()
    {
        Sheet = null;
        UpdateHints();
    }

    private bool DispatchSheetAction(HandheldSheetViewModel sheet, GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp when sheet.Index > 0:
                sheet.Focus(sheet.Index - 1);
                break;
            case GamepadAction.NavigateDown when sheet.Index < sheet.Items.Count - 1:
                sheet.Focus(sheet.Index + 1);
                break;
            case GamepadAction.NavigateUp:
            case GamepadAction.NavigateDown:
            case GamepadAction.NavigateLeft:
            case GamepadAction.NavigateRight:
                Bumped?.Invoke(ToDirection(action));
                break;
            case GamepadAction.Confirm when sheet.FocusedItem is { } item:
                RunSheetItem(item);
                break;
            case GamepadAction.Cancel:
            case GamepadAction.Actions:
            case GamepadAction.Menu:
                CloseSheet();
                break;
        }
        return true;
    }

    /// <summary>Runs a sheet row (a tap, or A on it): the sheet closes first; a row may open the next sheet.</summary>
    public void RunSheetItem(HandheldSheetItemViewModel item)
    {
        if (Sheet is not { } sheet || !sheet.Items.Contains(item))
            return;
        CloseSheet();
        item.Run();
    }

    private HandheldSheetViewModel? SlotSheet(int index)
    {
        var slot = _layout.SlotAt(index);
        var page = (index / HandheldHomeLayout.SlotsPerPage) + 1;
        if (slot.IsEmpty)
        {
            return new HandheldSheetViewModel("Empty slot", $"Page {page}",
            [
                new HandheldSheetItemViewModel("New folder here", null, () => StartNaming(isNew: true, index, [], null, -1)),
            ]);
        }
        if (slot.Kind == HandheldSlotKind.Game)
            return _pins.TryGetValue(slot.GameId, out var game) ? GameSheet(game, folder: null) : null;
        if (FolderSourceFor(slot) is not { } source)
            return null;
        var items = new List<HandheldSheetItemViewModel>
        {
            new("Open", null, Activate),
            new("Move", null, StartMove),
        };
        if (source.IsUser && source.FolderId is { } folderId)
        {
            items.Add(new("Rename or recolour…", null, () => StartRename(folderId)));
            items.Add(new("Delete folder", "Games stay in the library", () => ConfirmDelete(folderId)));
        }
        var subtitle = source.Kind switch
        {
            HandheldFolderKind.Recent => $"Made for you · the last {HandheldText.GameCount(source.GameCount)} you played",
            _ => source.Description,
        };
        return new HandheldSheetViewModel(source.Name, subtitle, items);
    }

    /// <summary>One sheet for a game wherever it is focused: on home (a pin), inside a folder, or on its Title page.</summary>
    private HandheldSheetViewModel GameSheet(HandheldGameEntry game, HandheldFolderViewModel? folder)
    {
        var items = new List<HandheldSheetItemViewModel>
        {
            new("Title page", null, () => OpenTitle(game)),
        };
        if (folder is null)
            items.Add(new("Move", "Drop on a game to make a folder", StartMove));
        items.AddRange(PlaceItems(game, folder?.Source));
        return new HandheldSheetViewModel(game.Title, game.SystemName, items);
    }

    /// <summary>The rows that change where a game is: Add to folder…, Pin / Remove from home, Remove from this folder.</summary>
    private IEnumerable<HandheldSheetItemViewModel> PlaceItems(HandheldGameEntry game, HandheldFolderSource? folder)
    {
        yield return new("Add to folder…", null, () => ShowSheet(AddToFolderSheet(game)));
        if (_layout.IsPinned(game.Id))
            yield return new("Remove from home", "Stays in the library", () => RemoveFromHome(game.Id));
        else
            yield return new("Pin to home", null, () => PinToHome(game.Id));
        if (folder is { IsUser: true, FolderId: { } folderId })
            yield return new($"Remove from {folder.Name}", "Stays in the library", () => RemoveFromFolder(folderId, game.Id));
    }

    private HandheldSheetViewModel AddToFolderSheet(HandheldGameEntry game)
    {
        var items = new List<HandheldSheetItemViewModel>
        {
            new("New folder", "With this game in it", () => NewFolderWith(game.Id)),
        };
        foreach (var folder in _layout.Folders)
        {
            var id = folder.Id;
            var already = folder.Contains(game.Id);
            items.Add(new(folder.Name, already ? "Already in it" : null, () => AddToFolder(id, game.Id)));
        }
        return new HandheldSheetViewModel("Add to folder", game.Title, items);
    }

    private void ConfirmDelete(string folderId)
    {
        if (_layout.FindFolder(folderId) is not { } folder)
            return;
        ShowSheet(new HandheldSheetViewModel($"Delete \"{folder.Name}\"?",
            $"Its {HandheldText.GameCount(folder.GameIds.Count)} stay in the library and in their platform folders",
            [
                new HandheldSheetItemViewModel("Delete folder", null, () => DeleteFolder(folderId)),
                new HandheldSheetItemViewModel("Keep it", null, () => { }),
            ]));
    }

    // ---- Edits ----

    /// <summary>Pins a game to the first empty slot on home (a reference: it stays in its platform folder).</summary>
    public void PinToHome(long gameId)
    {
        if (_layout.IsPinned(gameId))
        {
            ShowNotice("Already on the home screen");
            return;
        }
        var layout = _layout.WithPin(gameId, out var index);
        if (index < 0)
        {
            ShowNotice("The home screen is full");
            return;
        }
        ApplyLayout(layout);
        ShowNotice($"Pinned to home, page {(index / HandheldHomeLayout.SlotsPerPage) + 1}");
    }

    public void RemoveFromHome(long gameId)
    {
        if (!_layout.IsPinned(gameId))
            return;
        // The Title page of a pin that leaves home closes with it, as in the mockup.
        if (TitlePage?.Entry.Id == gameId && OpenFolder is null)
            CloseTitle();
        ApplyLayout(_layout.WithoutPin(gameId));
        ShowNotice("Removed from home. The game is still in its platform folder.");
    }

    public void AddToFolder(string folderId, long gameId)
    {
        if (_layout.FindFolder(folderId) is not { } folder)
            return;
        if (folder.Contains(gameId))
        {
            ShowNotice($"Already in {folder.Name}");
            return;
        }
        InvalidatePeek("u:" + folderId);
        ApplyLayout(_layout.WithGameAdded(folderId, gameId));
        ShowNotice($"Added to {folder.Name}");
        RefreshOpenUserFolder(folderId);
    }

    public void RemoveFromFolder(string folderId, long gameId)
    {
        if (_layout.FindFolder(folderId) is not { } folder)
            return;
        // The game's Title page belongs to the folder it just left; the folder is read again behind it.
        if (TitlePage?.Entry.Id == gameId)
            CloseTitle();
        InvalidatePeek("u:" + folderId);
        ApplyLayout(_layout.WithGameRemoved(folderId, gameId));
        ShowNotice($"Removed from {folder.Name}. The game is still in the library.");
        RefreshOpenUserFolder(folderId);
    }

    /// <summary>Deletes one of your folders. Its games are only references: nothing leaves the library.</summary>
    public void DeleteFolder(string folderId)
    {
        if (_layout.FindFolder(folderId) is not { } folder)
            return;
        if (OpenFolder?.Source.FolderId == folderId)
            CloseFolder();
        InvalidatePeek("u:" + folderId);
        ApplyLayout(_layout.WithoutFolder(folderId));
        ShowNotice($"Deleted \"{folder.Name}\". The games are still in the library.");
    }

    /// <summary>An open folder of yours shows its new contents (a game added or removed from inside it).</summary>
    private void RefreshOpenUserFolder(string folderId)
    {
        if (OpenFolder is not { } open || open.Source.FolderId != folderId || _layout.FindFolder(folderId) is not { } folder)
            return;
        open.Source = HandheldFolderSource.ForUser(folder) with { Sort = open.Sort };
        _ = RefreshOpenFolderAsync();
    }

    private void NewFolderWith(long gameId)
    {
        var index = _layout.FirstEmptySlot();
        if (index < 0)
        {
            ShowNotice("The home screen is full");
            return;
        }
        StartNaming(isNew: true, index, [gameId], null, -1);
    }

    // ---- Move mode ----

    private sealed class MoveState(HandheldSlot carried, int origin, IReadOnlyList<HandheldSlot> lifted)
    {
        public HandheldSlot Carried { get; } = carried;
        public int Origin { get; } = origin;
        public IReadOnlyList<HandheldSlot> Lifted { get; } = lifted;
        public int Target { get; set; } = origin;
        public HandheldDropKind Drop { get; set; }
        public IReadOnlyList<HandheldSlot> Preview { get; set; } = lifted;
    }

    private MoveState? _move;

    /// <summary>Lifts the focused home tile (Y → Move, or a long press); it follows the selector until dropped.</summary>
    public void StartMove()
    {
        if (OpenFolder is not null || TitlePage is not null || _move is not null)
            return;
        var origin = HomeIndex;
        var carried = _layout.SlotAt(origin);
        if (carried.IsEmpty)
            return;
        Zone = HandheldZone.Grid;
        _move = new MoveState(carried, origin, HandheldHomeArranging.Lift(_layout.AllSlots(), origin));
        IsMoving = true;
        UpdateMovePreview();
    }

    private void UpdateMovePreview()
    {
        if (_move is not { } move)
            return;
        move.Drop = HandheldHomeArranging.Classify(move.Carried, move.Lifted[move.Target]);
        move.Preview = move.Drop == HandheldDropKind.Place
            ? HandheldHomeArranging.InsertAt(move.Lifted, move.Target, move.Carried) ?? move.Lifted
            : move.Lifted;
        _home = PositionOf(move.Target);
        BuildPages();
        ShowPage(_home.Page);
        UpdateFocus();
    }

    /// <summary>Carries the lifted tile to a slot (a drag, or the pad).</summary>
    public void CarryTo(int index)
    {
        if (_move is not { } move || index < 0 || index >= move.Lifted.Count || index == move.Target)
            return;
        move.Target = index;
        UpdateMovePreview();
    }

    private bool DispatchMoveAction(MoveState move, GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp:
            case GamepadAction.NavigateDown:
            case GamepadAction.NavigateLeft:
            case GamepadAction.NavigateRight:
                var direction = ToDirection(action);
                var (position, outcome) = HandheldHomeNavigation.Move(PositionOf(move.Target), direction,
                    move.Lifted.Count / HandheldHomeLayout.SlotsPerPage);
                if (outcome == HandheldMoveOutcome.Moved)
                    CarryTo((position.Page * HandheldHomeLayout.SlotsPerPage) + position.Slot);
                else
                    Bumped?.Invoke(direction); // the dock is no place to drop a tile
                break;
            case GamepadAction.NextPlatform:
            case GamepadAction.PreviousPlatform:
                var delta = action == GamepadAction.NextPlatform ? 1 : -1;
                var page = (move.Target / HandheldHomeLayout.SlotsPerPage) + delta;
                if (page < 0 || page >= move.Lifted.Count / HandheldHomeLayout.SlotsPerPage)
                    Bumped?.Invoke(delta > 0 ? HandheldDirection.Right : HandheldDirection.Left);
                else
                    CarryTo((page * HandheldHomeLayout.SlotsPerPage) + (move.Target % HandheldHomeLayout.SlotsPerPage));
                break;
            case GamepadAction.Confirm:
                Drop();
                break;
            case GamepadAction.Cancel:
                CancelMove();
                break;
        }
        return true;
    }

    /// <summary>Drops the carried tile where it is: it lands, merges into a new folder, or joins one of yours.</summary>
    public void Drop()
    {
        if (_move is not { } move)
            return;
        _move = null;
        IsMoving = false;
        var target = move.Lifted[move.Target];
        switch (move.Drop)
        {
            case HandheldDropKind.AddToFolder when target.FolderId is { } folderId && _layout.FindFolder(folderId) is { } folder:
                InvalidatePeek("u:" + folderId);
                _home = PositionOf(move.Target);
                ApplyLayout(_layout.WithSlots(move.Lifted).WithGameAdded(folderId, move.Carried.GameId));
                // The pin leaves home either way; a game already in the folder is not added twice.
                ShowNotice(folder.Contains(move.Carried.GameId) ? $"Already in {folder.Name}" : $"Added to {folder.Name}");
                return;
            case HandheldDropKind.MakeFolder:
                // Home stays as it was until the folder is named; Done replaces both pins with the folder.
                _home = PositionOf(move.Target);
                BuildPages();
                ShowPage(_home.Page);
                StartNaming(isNew: true, move.Target, [target.GameId, move.Carried.GameId], null, move.Origin);
                return;
            default:
                var slots = move.Preview.ToList();
                var landed = slots.IndexOf(move.Carried);
                if (landed >= 0)
                    slots[landed] = move.Carried with { IsNew = false };
                _home = PositionOf(move.Target);
                ApplyLayout(_layout.WithSlots(slots));
                if (move.Target != move.Origin)
                    ShowNotice("Moved");
                return;
        }
    }

    /// <summary>B in move mode: the tile goes back where it was and home is unchanged.</summary>
    public void CancelMove()
    {
        if (_move is not { } move)
            return;
        _move = null;
        IsMoving = false;
        _home = PositionOf(move.Origin);
        BuildPages();
        ShowPage(_home.Page);
        UpdateFocus();
    }

    // ---- Touch (the Launcher's view wiring calls these) ----

    /// <summary>A tap on a home slot: the first selects it, a tap on the selected one opens it.</summary>
    public void TapHomeSlot(int page, int slot)
    {
        if (!CanTouchHome())
            return;
        var position = new HandheldHomePosition(page, slot);
        if (Zone == HandheldZone.Grid && position == _home)
        {
            Activate();
            return;
        }
        Zone = HandheldZone.Grid;
        _home = position;
        ShowPage(page);
        UpdateFocus();
    }

    /// <summary>A long press on a home tile lifts it into move mode; a drag then carries it (<see cref="CarryTo"/>).</summary>
    public void LongPressHomeSlot(int page, int slot)
    {
        if (!CanTouchHome())
            return;
        Zone = HandheldZone.Grid;
        _home = new HandheldHomePosition(page, slot);
        ShowPage(page);
        UpdateFocus();
        StartMove();
    }

    /// <summary>A swipe across home turns the page.</summary>
    public void SwipePage(int delta)
    {
        if (_move is not null)
            DispatchMoveAction(_move, delta > 0 ? GamepadAction.NextPlatform : GamepadAction.PreviousPlatform);
        else if (CanTouchHome())
            TurnPage(delta);
    }

    /// <summary>A tap on a game inside the open folder: select it, or open its Title page if it is selected.</summary>
    public void TapFolderTile(int index)
    {
        if (OpenFolder is not { IsLoaded: true } folder || TitlePage is not null || IsLauncherOverlayOpen ||
            index < 0 || index >= folder.Items.Count)
            return;
        if (Zone == HandheldZone.Grid && folder.SelectedIndex == index)
        {
            Activate();
            return;
        }
        Zone = HandheldZone.Grid;
        SelectInFolder(folder, index);
    }

    private bool CanTouchHome() => OpenFolder is null && TitlePage is null && !IsMenuOpen && Sheet is null && Naming is null &&
        _move is null && IsLoaded;

    /// <summary>A tap on a colour chip while naming.</summary>
    public void ChooseNamingColor(int index)
    {
        if (Naming is not { } naming)
            return;
        naming.MoveTo(HandheldNamingZone.Colours);
        naming.ChooseColor(index);
        UpdateHints();
    }

    // ---- Naming and recolouring your folders ----

    private void StartNaming(bool isNew, int slotIndex, IReadOnlyList<long> gameIds, string? folderId, int removePinFrom)
    {
        var existing = _layout.FindFolder(folderId);
        // A new folder takes the next colour in turn, so folders made one after another look different.
        var color = existing?.Color ?? HandheldHomeLayout.FolderColors[_layout.Folders.Count % HandheldHomeLayout.FolderColors.Count];
        var naming = new HandheldNamingViewModel(isNew, existing?.Name ?? HandheldNamingViewModel.DefaultName, color, slotIndex,
            gameIds, folderId, removePinFrom);
        naming.NameChanged += () => UpdateNamingStage(naming);
        Naming = naming;
        UpdateFocus();
    }

    private void StartRename(string folderId)
    {
        var index = _layout.IndexOf(HandheldSlot.UserFolder(folderId));
        if (index >= 0)
            StartNaming(isNew: false, index, [], folderId, -1);
    }

    private bool DispatchNamingAction(HandheldNamingViewModel naming, GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.NavigateUp:
            case GamepadAction.NavigateDown:
            case GamepadAction.NavigateLeft:
            case GamepadAction.NavigateRight:
                var moved = naming.Zone == HandheldNamingZone.Colours
                    ? action switch
                    {
                        GamepadAction.NavigateLeft => naming.MoveColor(-1),
                        GamepadAction.NavigateRight => naming.MoveColor(1),
                        GamepadAction.NavigateDown => MoveToKeys(naming),
                        _ => false,
                    }
                    : action switch
                    {
                        GamepadAction.NavigateLeft => naming.MoveKey(0, -1),
                        GamepadAction.NavigateRight => naming.MoveKey(0, 1),
                        GamepadAction.NavigateUp => naming.MoveKey(-1, 0),
                        _ => naming.MoveKey(1, 0),
                    };
                if (!moved)
                    Bumped?.Invoke(ToDirection(action));
                UpdateHints();
                break;
            case GamepadAction.Confirm when naming.Zone == HandheldNamingZone.Colours:
                naming.ChooseColor(naming.FocusedColor);
                break;
            case GamepadAction.Confirm:
                PressNamingKey(naming.FocusedKey);
                break;
            case GamepadAction.Cancel:
                // B deletes, as on the mockup's keyboard; with nothing left to delete it leaves without saving.
                if (!naming.DeleteLast())
                    CancelNaming();
                break;
            case GamepadAction.Actions:
                if (!naming.Type(" "))
                    Bumped?.Invoke(HandheldDirection.Right);
                break;
            case GamepadAction.Search:
            case GamepadAction.Menu:
                FinishNaming();
                break;
        }
        UpdateHints();
        return true;
    }

    private static bool MoveToKeys(HandheldNamingViewModel naming)
    {
        naming.MoveTo(HandheldNamingZone.Keys);
        return true;
    }

    /// <summary>A key of the naming keyboard: A on it, or a tap.</summary>
    public void PressNamingKey(HandheldKeyViewModel key)
    {
        if (Naming is not { } naming)
            return;
        naming.FocusKey(key);
        var done = key.Kind switch
        {
            HandheldKeyKind.Character => naming.Type(key.Label),
            HandheldKeyKind.Space => naming.Type(" "),
            HandheldKeyKind.Delete => naming.DeleteLast(),
            _ => true,
        };
        if (key.Kind == HandheldKeyKind.Done)
            FinishNaming();
        else if (!done)
            Bumped?.Invoke(HandheldDirection.Right);
        UpdateHints();
    }

    /// <summary>Done: makes the folder (or saves the new name and colour) and says so.</summary>
    public void FinishNaming()
    {
        if (Naming is not { } naming)
            return;
        Naming = null;
        if (naming.IsNew)
        {
            // The slot is empty, or (a merge) holds one of the folder's own games; a library refresh while naming
            // may have put something else there, and then the folder takes the first free slot instead.
            var slotIndex = naming.SlotIndex;
            var there = slotIndex < _layout.SlotCount ? _layout.SlotAt(slotIndex) : HandheldSlot.Empty;
            if (!there.IsEmpty && !(there.Kind == HandheldSlotKind.Game && naming.GameIds.Contains(there.GameId)))
                slotIndex = _layout.FirstEmptySlot() is >= 0 and var free ? free : _layout.SlotCount;
            var id = HandheldHomeLayout.NewFolderId();
            var layout = _layout.WithNewFolder(slotIndex, id, naming.Name, naming.Color, naming.GameIds);
            if (naming.RemovePinFrom >= 0 && naming.RemovePinFrom != slotIndex &&
                layout.SlotAt(naming.RemovePinFrom) is { Kind: HandheldSlotKind.Game } pin && naming.GameIds.Contains(pin.GameId))
                layout = layout.WithSlot(naming.RemovePinFrom, HandheldSlot.Empty);
            var elsewhere = OpenFolder is not null || slotIndex / HandheldHomeLayout.SlotsPerPage != _home.Page;
            if (OpenFolder is null)
            {
                Zone = HandheldZone.Grid;
                _home = PositionOf(slotIndex);
            }
            ApplyLayout(layout);
            ShowNotice(elsewhere
                ? $"Made \"{naming.Name}\" on page {(slotIndex / HandheldHomeLayout.SlotsPerPage) + 1}"
                : $"Made \"{naming.Name}\"");
            return;
        }
        if (_layout.FindFolder(naming.FolderId) is { } folder)
        {
            ApplyLayout(_layout.WithFolder(folder with { Name = naming.Name, Color = naming.Color }));
            ShowNotice($"Saved \"{naming.Name}\"");
            return;
        }
        UpdateFocus();
    }

    /// <summary>B while naming: nothing is made or changed.</summary>
    public void CancelNaming()
    {
        if (Naming is null)
            return;
        Naming = null;
        UpdateFocus();
    }

    /// <summary>The Stage shows the folder being named, its name following every key without replaying the pop.</summary>
    private void UpdateNamingStage(HandheldNamingViewModel naming)
    {
        if (!ReferenceEquals(Naming, naming) || Stage.Kind != HandheldStageKind.Folder)
            return;
        Stage.Title = naming.Name;
        Stage.AccentBrush = naming.ColorBrush;
    }

    private void ShowNamingStage(HandheldNamingViewModel naming)
    {
        var count = naming.IsNew
            ? naming.GameIds.Count
            : _layout.FindFolder(naming.FolderId)?.GameIds.Count ?? 0;
        ShowStage(HandheldStageKind.Folder, naming.Name, $"Your folder · {HandheldText.GameCount(count)}");
        Stage.Monogram = string.Empty;
        Stage.FolderGlyph = HandheldGlyph.Star;
        Stage.AccentBrush = naming.ColorBrush;
    }

    private void AddArrangeHints()
    {
        if (Naming is { } naming)
        {
            Hints.Add(new HandheldHint("A", naming.Zone == HandheldNamingZone.Colours ? "Colour" : "Type"));
            Hints.Add(new HandheldHint("B", naming.Text.Length > 0 ? "Delete" : "Cancel"));
            Hints.Add(new HandheldHint("Y", "Space"));
            Hints.Add(new HandheldHint("X", "Done"));
            return;
        }
        if (Sheet is not null)
        {
            Hints.Add(new HandheldHint("A", "Select"));
            Hints.Add(new HandheldHint("B", "Close"));
            return;
        }
        if (_move is { } move)
        {
            Hints.Add(new HandheldHint("A", move.Drop switch
            {
                HandheldDropKind.MakeFolder => "Make folder",
                HandheldDropKind.AddToFolder => "Add",
                _ => "Drop",
            }));
            Hints.Add(new HandheldHint("B", "Put back"));
        }
    }

    /// <summary>What the Stage says while a tile is carried: what it is, and what A would do here.</summary>
    private void ShowMoveStage(MoveState move)
    {
        var target = move.Lifted[move.Target];
        var name = FolderSourceFor(move.Carried)?.Name ??
            (_pins.TryGetValue(move.Carried.GameId, out var game) ? game.Title : "Tile");
        var line = move.Drop switch
        {
            HandheldDropKind.MakeFolder when _pins.TryGetValue(target.GameId, out var other) => $"Drop on {other.Title} to make a folder",
            HandheldDropKind.MakeFolder => "Drop here to make a folder",
            HandheldDropKind.AddToFolder => $"Drop into {FolderSourceFor(target)?.Name ?? "the folder"}",
            _ => $"Moving · page {(move.Target / HandheldHomeLayout.SlotsPerPage) + 1}",
        };
        if (move.Carried.Kind == HandheldSlotKind.Game && _pins.TryGetValue(move.Carried.GameId, out var carried))
        {
            ShowGameStage(carried, _pinCovers.GetValueOrDefault(carried.Id));
            Stage.Subtitle = line;
            return;
        }
        if (FolderSourceFor(move.Carried) is { } source)
        {
            ShowFolderStage(source, line);
            return;
        }
        ShowStage(HandheldStageKind.EmptySlot, name, line);
    }
}
