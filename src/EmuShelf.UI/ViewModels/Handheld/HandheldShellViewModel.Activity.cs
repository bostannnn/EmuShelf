using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EmuShelf.App.Services;

namespace EmuShelf.App.ViewModels;

// The play log in Handheld mode (M46 P4). Activity is an app on the grid beside Settings (amending decision 18): a system
// tile, movable on home, never folded or removed; the Start menu has an Activity row and a game's Y sheet a Play history
// row. Open, it is a panel laid across the screens: the timeline's sessions, the ranking or a game's sessions listed on the
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

    /// <summary>The Activity tile's actions: Open and Move. It has no Remove and no Add to folder: it lives on home.</summary>
    private HandheldSheetViewModel ActivitySheet() =>
        new(HandheldActivityText.Name, "Always on home",
        [
            new HandheldSheetItemViewModel("Open", null, () => _ = OpenActivityAsync()),
            new HandheldSheetItemViewModel("Move", null, StartMove),
        ])
        {
            Swatch = BrushOf(HandheldActivityTileColor),
            SwatchGlyph = HandheldGlyph.Activity,
        };
}
