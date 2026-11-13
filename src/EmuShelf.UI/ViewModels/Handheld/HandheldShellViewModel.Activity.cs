using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.ViewModels;

// The play log in Handheld mode (M46 P4). Activity is an app on the grid beside Settings (amending decision 18): a system
// tile, movable and foldable, never removed; the Start menu has an Activity row and a game's Y sheet a Play history
// row. Like Settings it can be put in one of your folders (first inside it, after Settings), and taken back out to home with
// Move to home. Open, it is a panel laid across the screens: the timeline's sessions, the ranking or a game's sessions listed on the
// Launcher, and on the Stage the focused day's total and games, or the focused game's stats.
public sealed partial class HandheldShellViewModel
{
    /// <summary>The Activity tile's colour: a teal of its own, so it reads as EmuShelf's and not a platform's or yours.</summary>
    public const string HandheldActivityTileColor = "#26918A";

    /// <summary>
    /// Opens the play log on its timeline, or on one game's page (<paramref name="gameId"/>). Nothing happens when the head
    /// has no play log (a test fake) or Settings is open.
    /// </summary>
    public async Task OpenActivityAsync(long? gameId = null)
    {
        if (_disposed || Settings is not null || _library.CreateActivity() is not { } activity)
            return;
        var panel = new HandheldActivityPanel(activity);
        OpenPanel(panel, _ => _library.CloseActivityAsync(activity));
        if (!ReferenceEquals(Panel, panel))
            return;
        try
        {
            await activity.LoadAsync(gameId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ShowNotice($"Could not read the play log: {ex.Message}", HandheldGlyph.Alert);
        }
    }

    /// <summary>The Stage for the focused Activity tile: its glyph on its teal, with a plate.</summary>
    private void ShowActivityTileStage(string? subtitle = null) =>
        ShowStage(HandheldStageKind.ActivityTile, HandheldActivityText.Name, subtitle ?? HandheldActivityText.Subtitle);

    /// <summary>
    /// The Activity tile's actions, as Settings has them: Open, Move, Add to folder… on home; Open and Move to home inside a
    /// folder. It has no Remove: it can be moved and folded, never lost.
    /// </summary>
    private HandheldSheetViewModel ActivitySheet(HandheldUserFolder? inFolder)
    {
        var items = new List<HandheldSheetItemViewModel> { new("Open", null, () => _ = OpenActivityAsync()) };
        if (inFolder is null)
        {
            items.Add(new("Move", "Drop on one of your folders to put it there", StartMove));
            if (_layout.Folders.Count > 0)
                items.Add(new("Add to folder…", null, () => ShowSheet(ActivityToFolderSheet())));
        }
        else
        {
            items.Add(new("Move to home", "Out of this folder, onto home", MoveActivityToHome));
        }
        var subtitle = inFolder is null ? "Always on home or in one of your folders" : $"In {inFolder.Name}";
        return new HandheldSheetViewModel(HandheldActivityText.Name, subtitle, items)
        {
            Swatch = BrushOf(HandheldActivityTileColor),
            SwatchGlyph = HandheldGlyph.Activity,
        };
    }

    private HandheldSheetViewModel ActivityToFolderSheet() =>
        new("Add to folder", HandheldActivityText.Name, _layout.Folders.Select(folder =>
        {
            var id = folder.Id;
            return new HandheldSheetItemViewModel(folder.Name, null, () => PutActivityIn(id));
        }))
        {
            Swatch = BrushOf(HandheldActivityTileColor),
            SwatchGlyph = HandheldGlyph.Activity,
        };

    /// <summary>Puts Activity in one of your folders (Y → Add to folder…, or dropped on it in move mode).</summary>
    public void PutActivityIn(string folderId)
    {
        if (_layout.FindFolder(folderId) is not { } folder)
            return;
        ApplyLayout(_layout.WithActivityIn(folderId));
        ShowNotice($"Activity is in {folder.Name}");
        RefreshOpenUserFolder(folderId);
    }

    /// <summary>Takes Activity out of its folder onto home's first empty slot.</summary>
    public void MoveActivityToHome()
    {
        if (_layout.ActivityFolder is not { } folder)
            return;
        ApplyLayout(_layout.WithActivityOnHome(-1, out var placed));
        ShowNotice($"Activity is on home, page {(placed / SlotsPerPage) + 1}");
        RefreshOpenUserFolder(folder.Id);
    }
}
