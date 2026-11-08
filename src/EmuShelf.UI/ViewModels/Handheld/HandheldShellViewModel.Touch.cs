using System;
using System.Linq;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

// Touch on the Launcher (M45 H4, the spec's controls table): the view's code-behind finds what was touched and
// calls these. Each one puts the selector where the finger is and then does what A would do there, so touch and
// the pad share one path. Tiles take a tap to select and a second to open (home: TapHomeSlot, folders:
// TapFolderTile); buttons, chips, rows and dock buttons act on the first tap.
public sealed partial class HandheldShellViewModel
{
    // Something with its own handling is open over the Launcher: the companion's drawer, a sheet, naming or move mode.
    private bool IsTouchBlocked => (_companion is { } companion && companion.Overlay != SecondScreenOverlayKind.None) ||
        Sheet is not null || Naming is not null || _move is not null;

    /// <summary>A tap on the folder header's Back or sort chip.</summary>
    public void TapHeader(HandheldHeaderItem item)
    {
        if (IsTouchBlocked || IsMenuOpen || TitlePage is not null || OpenFolder is not { IsLoaded: true } folder)
            return;
        if (item == HandheldHeaderItem.Sort && !folder.CanSort)
            return;
        Zone = HandheldZone.Header;
        folder.FocusedHeaderItem = item;
        UpdateFocus();
        Activate();
    }

    /// <summary>A tap on a dock button: All apps, a pinned app, or the menu.</summary>
    public void TapDockItem(HandheldDockItemViewModel item)
    {
        var index = DockItems.IndexOf(item);
        if (IsTouchBlocked || IsMenuOpen || TitlePage is not null || index < 0)
            return;
        if (OpenFolder is { } folder)
            folder.FocusedHeaderItem = null;
        Zone = HandheldZone.Dock;
        DockIndex = index;
        UpdateFocus();
        Activate();
    }

    /// <summary>A tap on a row of the Start menu.</summary>
    public void TapMenuItem(HandheldMenuItemViewModel item)
    {
        var index = MenuItems.IndexOf(item);
        if (!IsMenuOpen || index < 0)
            return;
        MenuIndex = index;
        UpdateMenuFocus();
        DispatchMenuAction(GamepadAction.Confirm);
    }

    /// <summary>A tap outside an open menu or Title page sheet closes it, as B does.</summary>
    public void TapOutside()
    {
        if (IsMenuOpen)
            CloseMenu();
        else if (TitlePage is { State: not HandheldTitleState.Buttons and not HandheldTitleState.Launching } page)
            ReturnToTitleButtons(page);
    }

    /// <summary>A tap on one of the Title page's buttons (Play, Disc, Achievements).</summary>
    public void TapTitleButton(HandheldTitleButtonViewModel button)
    {
        if (IsTouchBlocked || IsMenuOpen || TitlePage is not { State: HandheldTitleState.Buttons } page)
            return;
        var index = page.Buttons.IndexOf(button);
        if (index < 0)
            return;
        page.FocusButton(index);
        UpdateHints();
        ActivateTitleButton(page);
    }

    /// <summary>A tap on a row of the Title page's disc chooser or actions sheet.</summary>
    public void TapTitleOption(HandheldTitleOptionViewModel option)
    {
        if (IsTouchBlocked || IsMenuOpen || TitlePage is not { IsOptionList: true } page)
            return;
        var index = page.Options.IndexOf(option);
        if (index < 0)
            return;
        page.FocusOption(index);
        page.Options[index].Run();
    }

    /// <summary>A tap on the screen chooser: a screen card, or the "Always use this screen" toggle.</summary>
    public void TapScreenChoice(int index)
    {
        if (IsTouchBlocked || IsMenuOpen || TitlePage is not { State: HandheldTitleState.ScreenChooser } page ||
            index is < HandheldTitlePageViewModel.ScreenTopIndex or > HandheldTitlePageViewModel.ScreenRememberIndex)
            return;
        page.ScreenIndex = index;
        DispatchScreenChooser(page, GamepadAction.Confirm);
    }

    /// <summary>
    /// A long press lifted a home tile and the finger let go without carrying it anywhere: the tile goes back and
    /// its actions sheet opens instead, so touch reaches Pin, Add to folder, Rename and Delete (Move is a row there).
    /// </summary>
    public void ReleaseLongPressInPlace()
    {
        if (_move is not { } move || move.Target != move.Origin)
            return;
        CancelMove();
        OpenActions();
    }

    /// <summary>Whether a long press's lifted tile has been carried to another slot (then letting go drops it).</summary>
    public bool HasCarriedAway => _move is { } move && move.Target != move.Origin;

    /// <summary>A long press on a game inside a folder: select it and open its actions sheet.</summary>
    public void LongPressFolderTile(int index)
    {
        if (IsTouchBlocked || IsMenuOpen || TitlePage is not null || OpenFolder is not { IsLoaded: true } folder ||
            index < 0 || index >= folder.Items.Count)
            return;
        Zone = HandheldZone.Grid;
        folder.FocusedHeaderItem = null;
        SelectInFolder(folder, index);
        OpenActions();
    }

    /// <summary>A swipe inside an open folder scrolls it a screen of columns at a time.</summary>
    private void SwipeFolder(HandheldFolderViewModel folder, int delta)
    {
        if (!folder.HasGames)
            return;
        var lastColumn = HandheldFolderNavigation.ColumnCount(folder.Items.Count) - 1;
        var column = Math.Clamp(folder.FirstVisibleColumn + (delta * FolderVisibleColumns), 0, lastColumn);
        if (column == folder.SelectedIndex / HandheldFolderNavigation.Rows)
        {
            Bumped?.Invoke(delta > 0 ? HandheldDirection.Right : HandheldDirection.Left);
            return;
        }
        Zone = HandheldZone.Grid;
        folder.FocusedHeaderItem = null;
        SelectInFolder(folder, Math.Min(column * HandheldFolderNavigation.Rows, folder.Items.Count - 1));
    }
}
