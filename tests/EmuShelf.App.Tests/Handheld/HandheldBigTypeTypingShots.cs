using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M47 B9: the Big Type look's typing and moving as frames to look at, beside the design set (which has no frame of either): a tile held,
// after a trade of places, its Put in sheet, naming a folder (empty, typed, the colours, the other characters), Search (empty, typed, a
// hit focused) and Edit title. Rendered only on request, as the design set is, into EMUSHELF_DESIGN_SNAPSHOT_DIR with
// EMUSHELF_B9_SHOTS=1 and EMUSHELF_DESIGN_LOOK=BigType; a normal test run skips it.
public partial class HandheldShellViewModelTests
{
    private static bool B9Shots => Environment.GetEnvironmentVariable("EMUSHELF_B9_SHOTS") is { Length: > 0 };

    [AvaloniaFact]
    public async Task BigTypeTypingAndMovingShots()
    {
        if (DesignOutput is null || !B9Shots || DesignLook != HandheldLook.BigType)
            return;
        using var rig = await DesignRigAsync();
        var shell = rig.Shell;

        // Pick up and put down: a folder held, then after one trade to the right.
        FocusHome(shell, 1);
        Press(shell, GamepadAction.Actions);
        Press(shell, GamepadAction.NavigateDown, shell.Sheet!.Items.ToList().FindIndex(item => item.Label == "Move"));
        Press(shell, GamepadAction.Confirm);
        Assert.True(shell.IsMoving);
        await DesignPairAsync(rig, "bt9-held", "Big Type: the PS2 folder held (grown, rocking on the compositor, the ring hidden)");
        Press(shell, GamepadAction.NavigateRight);
        await DesignPairAsync(rig, "bt9-held-traded", "Big Type: the held folder after trading places with the one to its right");
        Press(shell, GamepadAction.Cancel);
        Assert.False(shell.IsMoving);
        await DesignPairAsync(rig, "bt9-put-back", "Big Type: B walked the step back: home as it was, the ring on the folder");

        // A pinned game held, and Y: where it can go.
        FocusHome(shell, 11);
        Press(shell, GamepadAction.Actions);
        Press(shell, GamepadAction.NavigateDown, shell.Sheet!.Items.ToList().FindIndex(item => item.Label == "Move"));
        Press(shell, GamepadAction.Confirm);
        Press(shell, GamepadAction.Actions);
        Assert.NotNull(shell.Sheet);
        await DesignPairAsync(rig, "bt9-put-in", "Big Type: Y on a held game: Put it in a folder (the app's; the mock's Y does nothing there)");
        Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.Cancel);
        Assert.False(shell.IsMoving);

        // Naming a folder on an empty slot.
        FocusHome(shell, 15);
        Press(shell, GamepadAction.Confirm);
        Assert.NotNull(shell.Naming);
        await DesignPairAsync(rig, "bt9-naming-empty", "Big Type: Name the folder, nothing typed (the placeholder on the Stage)");
        foreach (var ch in "RACING")
            shell.PressNamingKey(shell.Naming!.KeyRows.SelectMany(row => row.Keys).First(key => key.Value == ch.ToString()));
        await DesignPairAsync(rig, "bt9-naming-typed", "Big Type: naming, \"Racing\" typed (a capital first, then lower case)");
        Press(shell, GamepadAction.NavigateUp, 2);
        await DesignPairAsync(rig, "bt9-naming-colours", "Big Type: naming, up from the top row to the folder's colours (the app's)");
        Press(shell, GamepadAction.NavigateDown);
        Press(shell, GamepadAction.NextPlatform);
        await DesignPairAsync(rig, "bt9-naming-symbols", "Big Type: naming, the next page of characters (the app's, behind the key at the end of the third row)");
        Press(shell, GamepadAction.NextPlatform, 2);
        while (shell.Naming!.Text.Length > 0)
            Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.Naming);

        // Search from the Start menu.
        RunMenuRow(shell, "Search");
        var search = Assert.IsType<HandheldSearchPanel>(shell.Panel);
        await DesignPairAsync(rig, "bt9-search-empty", "Big Type: Search, nothing typed");
        search.Keyboard!.Text = "Mario";
        await search.Pending;
        await DesignPairAsync(rig, "bt9-search-typed", "Big Type: Search, \"Mario\" typed, the hits above the keys");
        Press(shell, GamepadAction.NavigateUp);
        await DesignPairAsync(rig, "bt9-search-hit", "Big Type: Search, up to the nearest hit: the game on the Stage, A Play, B Back");
        Press(shell, GamepadAction.Cancel);
        while (search.Keyboard!.Text.Length > 0)
            Press(shell, GamepadAction.Cancel);
        Press(shell, GamepadAction.Cancel);
        Assert.Null(shell.Panel);
    }
}
