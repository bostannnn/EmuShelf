using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Hotkeys;
using EmuShelf.Core.Launching;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// Controller-native Hotkeys overlay: the D-pad focus model <see cref="GamepadHotkeysViewModel"/>
/// layers on the shared <see cref="EmulatorSettingsViewModel"/> hotkey feature (Apply-to-all, Install
/// Steam template, per-emulator Apply / Revert, the support matrix and per-emulator status) without
/// leaving Gamepad mode or opening any Desktop window. Final controller <em>feel</em> — the Steam
/// keyboard, gamescope — needs real Deck acceptance and is out of scope for these headless tests.
/// </summary>
public sealed class GamepadHotkeysOverlayTests
{
    [AvaloniaFact]
    public void Opens_WithApplyToAllFocused()
    {
        var (vm, _, _, _) = Build([Emu("duckstation", "DuckStation"), Emu("pcsx2", "PCSX2")]);

        Assert.Equal(GamepadHotkeyTargetKind.ApplyAll, vm.FocusedKind);
        Assert.True(vm.IsApplyAllFocused);
        Assert.Null(vm.FocusedRow);
    }

    [AvaloniaFact]
    public void Dpad_WalksGlobalsThenPerEmulatorApplyRevert_AndClampsAtBothEnds()
    {
        var (vm, _, _, _) = Build([Emu("duckstation", "DuckStation"), Emu("pcsx2", "PCSX2")]);
        var duck = vm.Emulators[0];
        var pcsx2 = vm.Emulators[1];

        Assert.Equal(GamepadHotkeyTargetKind.ApplyAll, vm.FocusedKind);

        vm.MoveFocus(1);
        Assert.Equal(GamepadHotkeyTargetKind.InstallSteam, vm.FocusedKind);
        Assert.True(vm.IsInstallFocused);
        Assert.False(vm.IsApplyAllFocused);

        vm.MoveFocus(1);
        Assert.Equal(GamepadHotkeyTargetKind.EmulatorApply, vm.FocusedKind);
        Assert.Same(duck, vm.FocusedRow);
        Assert.True(duck.IsApplyFocused);

        vm.MoveFocus(1);
        Assert.Equal(GamepadHotkeyTargetKind.EmulatorRevert, vm.FocusedKind);
        Assert.Same(duck, vm.FocusedRow);
        Assert.True(duck.IsRevertFocused);
        Assert.False(duck.IsApplyFocused);

        vm.MoveFocus(1);
        Assert.Equal(GamepadHotkeyTargetKind.EmulatorApply, vm.FocusedKind);
        Assert.Same(pcsx2, vm.FocusedRow);

        vm.MoveFocus(1);
        Assert.Equal(GamepadHotkeyTargetKind.EmulatorRevert, vm.FocusedKind);
        Assert.Same(pcsx2, vm.FocusedRow);

        // Down past the last target clamps rather than wrapping.
        vm.MoveFocus(1);
        Assert.Equal(GamepadHotkeyTargetKind.EmulatorRevert, vm.FocusedKind);
        Assert.Same(pcsx2, vm.FocusedRow);

        // Up past the first target clamps on Apply-to-all.
        for (var step = 0; step < 10; step++)
            vm.MoveFocus(-1);
        Assert.Equal(GamepadHotkeyTargetKind.ApplyAll, vm.FocusedKind);
        Assert.Null(vm.FocusedRow);
    }

    [AvaloniaFact]
    public void ActivateApplyToAll_AppliesEveryEmulatorInTurn()
    {
        var (vm, applied, reverted, _) = Build(
            [Emu("duckstation", "DuckStation"), Emu("pcsx2", "PCSX2"), Emu("rpcs3", "RPCS3")]);

        // Apply-to-all is the opening focus, so A fires it immediately.
        vm.Activate();

        Assert.Equal(["duckstation", "pcsx2", "rpcs3"], applied);
        Assert.Empty(reverted);
        Assert.False(vm.IsBusy);
    }

    [AvaloniaFact]
    public void ActivatePerEmulatorApply_AppliesOnlyThatEmulator()
    {
        var (vm, applied, reverted, _) = Build([Emu("duckstation", "DuckStation"), Emu("pcsx2", "PCSX2")]);

        // ApplyAll(0) → Install(1) → duck.Apply(2) → duck.Revert(3) → pcsx2.Apply(4).
        vm.MoveFocus(4);
        Assert.Equal(GamepadHotkeyTargetKind.EmulatorApply, vm.FocusedKind);
        Assert.Same(vm.Emulators[1], vm.FocusedRow);

        vm.Activate();

        Assert.Equal(["pcsx2"], applied);
        Assert.Empty(reverted);
    }

    [AvaloniaFact]
    public void ActivatePerEmulatorRevert_RevertsOnlyThatEmulator()
    {
        var (vm, applied, reverted, _) = Build([Emu("duckstation", "DuckStation"), Emu("pcsx2", "PCSX2")]);

        vm.MoveFocus(3); // duck.Revert
        Assert.Equal(GamepadHotkeyTargetKind.EmulatorRevert, vm.FocusedKind);
        Assert.Same(vm.Emulators[0], vm.FocusedRow);

        vm.Activate();

        Assert.Equal(["duckstation"], reverted);
        Assert.Empty(applied);
    }

    [AvaloniaFact]
    public void NonOperableEmulator_IsInTheMatrixButNeverAFocusTarget()
    {
        var (vm, _, _, _) = Build([Emu("duckstation", "DuckStation"), Emu("rpcs3", "RPCS3", operable: false)]);
        var rpcs3 = vm.Emulators.Single(row => row.EmulatorId == "rpcs3");

        // It still appears in the matrix so its status reads, it just cannot be operated.
        Assert.False(rpcs3.CanOperate);
        Assert.Contains(vm.Emulators, row => row.EmulatorId == "rpcs3");

        // Walking to the very bottom never lands the ring on the non-operable row.
        for (var step = 0; step < 12; step++)
        {
            Assert.NotSame(rpcs3, vm.FocusedRow);
            vm.MoveFocus(1);
        }
        Assert.NotSame(rpcs3, vm.FocusedRow);
        Assert.Equal(GamepadHotkeyTargetKind.EmulatorRevert, vm.FocusedKind);
        Assert.Equal("duckstation", vm.FocusedRow!.EmulatorId);
    }

    [AvaloniaFact]
    public void ActivateInstallSteamTemplate_SurfacesTheInstallerResult()
    {
        // A resolver that finds no Steam returns SteamNotFound, so the test writes no files.
        var installer = new SteamInputTemplateInstaller(resolveSteamRoot: () => null);
        var (vm, _, _, _) = Build([Emu("duckstation", "DuckStation")], installer);

        Assert.False(vm.HasSteamTemplateStatus);

        vm.MoveFocus(1); // Install Steam template
        Assert.Equal(GamepadHotkeyTargetKind.InstallSteam, vm.FocusedKind);
        vm.Activate();

        Assert.True(vm.HasSteamTemplateStatus);
        Assert.Contains("Steam", vm.SteamTemplateStatus, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void ControllerMapping_ListsHoldSelectPlusFaceButtonToKey()
    {
        var (vm, _, _, _) = Build([Emu("duckstation", "DuckStation")]);

        Assert.Equal(5, vm.ControllerMapping.Count);
        Assert.Equal("Select + Square", vm.ControllerMapping[0].Combo);
        Assert.Contains("R", vm.ControllerMapping[0].Action);
        Assert.Contains(vm.ControllerMapping, row => row.Combo == "Select + Start" && row.Action.Contains("F8"));
        Assert.False(string.IsNullOrWhiteSpace(vm.SchemeSummary));

        // The per-action cells expose the check / dash the matrix renders.
        Assert.Equal("✓", vm.Emulators[0].Rewind!.Mark);
    }

    [AvaloniaFact]
    public void PerEmulatorStatus_IncludingNeedsTheEmulatorClosed_IsReadableInTheMatrix()
    {
        var running = new HotkeyEmulatorSnapshot(
            "pcsx2",
            "PCSX2",
            [new HotkeyActionLine(HotkeyAction.CloseGame, "Close game", "F8", IsAvailable: true)],
            "PCSX2 is running — close it first, then apply.",
            HotkeyRowTone.Warning,
            CanOperate: true);
        var (vm, _, _, _) = Build([Emu("duckstation", "DuckStation"), running]);

        var row = vm.Emulators.Single(candidate => candidate.EmulatorId == "pcsx2");
        Assert.Equal("PCSX2 is running — close it first, then apply.", row.StatusText);
        Assert.Equal(HotkeyRowTone.Warning, row.StatusTone);
    }

    [AvaloniaFact]
    public void Dispose_ClearsFocusOffTheSharedRowsAndUnsubscribes()
    {
        var (vm, _, _, settings) = Build([Emu("duckstation", "DuckStation")]);
        vm.MoveFocus(2); // duck.Apply
        var duck = vm.Emulators[0];
        Assert.True(duck.IsApplyFocused);

        var raisedAfterDispose = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GamepadHotkeysViewModel.SteamTemplateStatus))
                raisedAfterDispose = true;
        };

        vm.Dispose();

        Assert.False(duck.IsApplyFocused);
        Assert.False(duck.IsRevertFocused);
        // Unsubscribed: a later settings change no longer drives the disposed overlay's notifications.
        settings.SteamTemplateStatus = "changed after dispose";
        Assert.False(raisedAfterDispose);
    }

    private static HotkeyEmulatorSnapshot Emu(string id, string name, bool operable = true)
    {
        HotkeyAction[] all =
        [
            HotkeyAction.Rewind, HotkeyAction.FastForward, HotkeyAction.SaveState,
            HotkeyAction.LoadState, HotkeyAction.CloseGame,
        ];
        var lines = all
            .Select(action => new HotkeyActionLine(action, action.ToString(), operable ? "R" : "n/a", operable))
            .ToArray();
        return new HotkeyEmulatorSnapshot(
            id,
            name,
            operable ? lines : [],
            operable
                ? "Recommended hotkeys aren't applied yet."
                : "EmuShelf couldn't find this emulator's configuration directory.",
            operable ? HotkeyRowTone.Info : HotkeyRowTone.Muted,
            CanOperate: operable);
    }

    private static (GamepadHotkeysViewModel Vm, List<string> Applied, List<string> Reverted, EmulatorSettingsViewModel Settings)
        Build(IReadOnlyList<HotkeyEmulatorSnapshot> emulators, SteamInputTemplateInstaller? installer = null)
    {
        var applied = new List<string>();
        var reverted = new List<string>();
        HotkeyEmulatorSnapshot Find(string id) => emulators.First(snapshot => snapshot.EmulatorId == id);

        var context = new HotkeySettingsContext(
            emulators,
            (id, _) => { applied.Add(id); return Task.FromResult(Find(id)); },
            (id, _) => { reverted.Add(id); return Task.FromResult(Find(id)); },
            "EmuShelf writes a uniform keyboard scheme into each emulator: R rewinds, L fast-forwards, "
                + "F2 saves, F4 loads, and F8 closes the game.");

        var settings = new EmulatorSettingsViewModel(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(system => system.Id, _ => (EmulatorConfiguration?)null, StringComparer.Ordinal),
            new NullEmulatorConfigurationStore(),
            new NullDialogService(),
            hotkeys: context,
            steamTemplateInstaller: installer);

        return (new GamepadHotkeysViewModel(settings), applied, reverted, settings);
    }
}
