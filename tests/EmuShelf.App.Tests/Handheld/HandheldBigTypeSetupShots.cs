using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

// M47 B14: the Big Type look's empty library and setup as frames to look at, beside the design set (whose empty-library frame is the
// only one of them): the empty library, each of setup's questions, the folder chosen, the count climbing, "All set", and a scan that finds
// nothing. Rendered only on request, as the design set is, into EMUSHELF_DESIGN_SNAPSHOT_DIR with EMUSHELF_B14_SHOTS=1 and
// EMUSHELF_DESIGN_LOOK=BigType; a normal test run skips it. A test library only: nothing is scanned.
public partial class HandheldShellViewModelTests
{
    private static bool B14Shots => Environment.GetEnvironmentVariable("EMUSHELF_B14_SHOTS") is { Length: > 0 };

    private static void DressForSetup(FakeLibrary library)
    {
        library.MoreImportSystems.Add(new HandheldImportSystem("playstation2", "PlayStation 2", "PS2", "#2B5BC7"));
        library.MoreImportSystems.Add(new HandheldImportSystem("gamecube", "GameCube", "GC", "#5B3FA0"));
        library.MoreImportSystems.Add(new HandheldImportSystem("nds", "Nintendo DS", "DS", "#4A4A66"));
        library.SetupEmulators["playstation2"] = new HandheldSetupEmulator("playstation2", "NetherSX2", true);
        library.SetupEmulators["gamecube"] = new HandheldSetupEmulator("gamecube", "Dolphin", false);
    }

    private static async Task WaitForShot(Func<bool> done)
    {
        for (var i = 0; i < 600 && !done(); i++)
            await Task.Delay(5);
        Assert.True(done());
    }

    [AvaloniaFact]
    public async Task BigTypeEmptyLibraryAndSetupShots()
    {
        if (DesignOutput is null || !B14Shots || DesignLook != HandheldLook.BigType)
            return;
        using (var rig = await DesignRigAsync(empty: true))
        {
            var shell = rig.Shell;
            var library = rig.Data.Library;
            DressForSetup(library);
            var gate = new TaskCompletionSource();
            library.SetupImport = async (_, _, found) =>
            {
                found(9);
                await gate.Task;
                found(71);
                return new HandheldSetupImport(71, null);
            };
            await DesignPairAsync(rig, "bt14-empty-library", "Big Type: no games: No games yet, A starts setup, or the menu's Add games");

            Press(shell, GamepadAction.Confirm);
            var setup = Assert.IsType<HandheldSetupPanel>(shell.Panel);
            // "All set" is held while it is pictured (the device holds it 1.1 s).
            setup.DoneHold = TimeSpan.FromMinutes(1);
            await DesignPairAsync(rig, "bt14-setup-1-where", "Big Type: setup 1 of 4, Where are your games? (Choose a folder…)");
            Press(shell, GamepadAction.Confirm);
            await DesignPairAsync(rig, "bt14-setup-1-system", "Big Type: setup 1 of 4, the folder picked: which system its games are for (the app's own question)");
            Press(shell, GamepadAction.Confirm);
            await DesignPairAsync(rig, "bt14-setup-1-chosen", "Big Type: setup 1 of 4, the folder chosen (PS2 · PlayStation 2), A goes on, Y removes it");
            Press(shell, GamepadAction.Confirm);
            await WaitForShot(() => setup.Step == HandheldSetupStep.Emulators);
            await DesignPairAsync(rig, "bt14-setup-2-emulators", "Big Type: setup 2 of 4, Emulators: what will run the games, Found");
            Press(shell, GamepadAction.Confirm);
            await DesignPairAsync(rig, "bt14-setup-3-achievements", "Big Type: setup 3 of 4, Achievements? Connect RetroAchievements");
            Press(shell, GamepadAction.NavigateDown);
            Press(shell, GamepadAction.Confirm);
            await WaitForShot(() => shell.Stage.Name == "9 games");
            await DesignPairAsync(rig, "bt14-setup-4-counting", "Big Type: setup 4 of 4, the count climbing as the PS2 folder is read");
            gate.SetResult();
            await WaitForShot(() => setup.IsDone);
            await DesignPairAsync(rig, "bt14-setup-4-all-set", "Big Type: setup 4 of 4, 71 games, All set (home comes back 1.1 s later)");
        }

        using (var rig = await DesignRigAsync(empty: true))
        {
            var shell = rig.Shell;
            DressForSetup(rig.Data.Library);
            Press(shell, GamepadAction.Confirm);
            var setup = Assert.IsType<HandheldSetupPanel>(shell.Panel);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            await WaitForShot(() => setup.Step == HandheldSetupStep.Emulators);
            Press(shell, GamepadAction.Confirm);
            Press(shell, GamepadAction.Confirm);
            await WaitForShot(() => shell.Stage.Line.StartsWith("No games found", StringComparison.Ordinal));
            await DesignPairAsync(rig, "bt14-setup-4-nothing", "Big Type: setup 4 of 4, a folder with no games: it says so, B goes back to choose another");
        }
    }
}
