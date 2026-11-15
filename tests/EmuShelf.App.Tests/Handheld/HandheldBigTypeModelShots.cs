using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M47 B12: the model view (proposals.html) as frames to look at, beside the design set (which never switches to it): a disc game (PS2's
// keep case), a jewel case (PS1, three discs), a card (3DS), a game with no cover, a missing file, a long name, a game pinned to home,
// and the name view of the same games. Rendered only on request, as the design set is, into EMUSHELF_DESIGN_SNAPSHOT_DIR with EMUSHELF_B12_SHOTS=1 and
// EMUSHELF_DESIGN_LOOK=BigType; a normal test run skips it. The Avalonia Stage has no GPU path here, so every game stands as its flat
// cover in the medium's place; the 3D media (keep case, jewel case, cartridge, card) are the native Stage's, rendered by
// tools/EmuShelf.Rendering.Preview --stage-model. Held still (Reduce motion on), so each frame is the model at rest.
public partial class HandheldShellViewModelTests
{
    private static bool B12Shots => Environment.GetEnvironmentVariable("EMUSHELF_B12_SHOTS") is { Length: > 0 };

    [AvaloniaFact]
    public async Task BigTypeModelViewShots()
    {
        if (DesignOutput is null || !B12Shots || DesignLook != HandheldLook.BigType)
            return;
        using var rig = await DesignRigAsync();
        var shell = rig.Shell;
        shell.ReduceMotion = true;

        async Task Pair(string title, string name, string what, bool model = true)
        {
            DesignFocusFolder(shell, DesignIndexIn(shell, title));
            shell.SettleStage();
            if (shell.IsModelViewChosen != model)
                Press(shell, GamepadAction.NextPlatform);
            Assert.Equal(model, shell.Stage.IsModelView);
            await DesignPairAsync(rig, name, what);
        }

        // PlayStation 2: a disc in a keep case.
        DesignOpenHome(shell, 1);
        await Pair("Final Fantasy X", "bt12-ps2-name", "Big Type: the name view (the default): the name alone across the whole box, RB Model", model: false);
        await Pair("Final Fantasy X", "bt12-ps2-model", "Big Type: RB, the model view: the medium alone in the middle, the name on the line, RS Turn, RB Name");
        await Pair("Katamari Damacy", "bt12-no-cover-model", "Big Type: the model view of a game with no cover (its card in the medium's place)");
        await Pair("Gran Turismo 4", "bt12-missing-model", "Big Type: the model view of a game whose file is missing (the second line says so)");
        await Pair("Gran Turismo 4", "bt12-missing-name", "Big Type: the same game in the name view (its name dimmed)", model: false);
        await Pair("Jak and Daxter: The Precursor Legacy", "bt12-long-name-model", "Big Type: a long name in the model view (the line)");
        await Pair("Jak and Daxter: The Precursor Legacy", "bt12-long-name-name", "Big Type: the same long name in the name view (the whole box)", model: false);
        DesignCloseAll(shell);

        // PlayStation: a jewel case, a game on three discs.
        DesignOpenHome(shell, 4);
        await Pair("Final Fantasy VII", "bt12-ps1-model", "Big Type: the model view of a PlayStation game (a jewel case; three discs)");
        await Pair("Final Fantasy VII", "bt12-ps1-name", "Big Type: the same game in the name view (its disc on the second line)", model: false);
        DesignCloseAll(shell);

        // Nintendo 3DS: a card.
        DesignOpenHome(shell, 7);
        await Pair("Fire Emblem Awakening", "bt12-3ds-model", "Big Type: the model view of a 3DS game (a card)");
        DesignCloseAll(shell);

        // Home: a pinned game (Ico) switches the view too (the owner, 2026-11-15); the model chosen in the folders shows here at once.
        FocusHome(shell, 11);
        shell.SettleStage();
        Assert.Equal(HandheldStageKind.Game, shell.Stage.Kind);
        Assert.True(shell.Stage.IsModelView);
        await DesignPairAsync(rig, "bt12-home-pinned-model", "Big Type: a game pinned to home in the model view (A Play, RB Name: B does nothing on home)");
        Press(shell, GamepadAction.PreviousPlatform);
        Assert.False(shell.Stage.IsModelView);
        await DesignPairAsync(rig, "bt12-home-pinned-name", "Big Type: the same pinned game in the name view (RB Model; the page turns from the d-pad's edges)");
        shell.ReduceMotion = false;
    }
}
