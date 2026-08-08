using EmuShelf.Core.Hotkeys;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Azahar;
using EmuShelf.Integrations.Emulators.Dolphin;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.Pcsx2;
using EmuShelf.Integrations.Emulators.Ppsspp;
using EmuShelf.Integrations.Emulators.RetroArch;
using EmuShelf.Integrations.Emulators.Rpcs3;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class HotkeyConfiguratorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("emushelf-hotkeys").FullName;

    private string BackupRoot => Path.Combine(_root, "backups");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ---- DuckStation ---------------------------------------------------------------------------

    [Fact]
    public void DuckStation_Apply_WritesKeys_EnablesRewind_ClearsF4Conflict_AndBacksUp()
    {
        Write("settings.ini",
            "[Main]", "SettingsVersion = 3", "RewindEnable = false",
            "[Hotkeys]",
            "FastForward = SDL-0/Back & SDL-0/B",
            "SaveSelectedSaveState = SDL-0/Back & SDL-0/Y",
            "SelectNextSaveStateSlot = Keyboard/F4");

        var result = new DuckStationHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Changed, result.Status);
        Assert.All(result.Bindings, binding => Assert.Equal(HotkeyBindingStatus.Bound, binding.Status));

        var document = Read("settings.ini");
        Assert.Equal("Keyboard/F8", document.GetValue("Hotkeys", "PowerOff"));
        Assert.Equal("Keyboard/R", document.GetValue("Hotkeys", "Rewind"));
        Assert.Equal("Keyboard/L", document.GetValue("Hotkeys", "FastForward"));
        Assert.Equal("Keyboard/F2", document.GetValue("Hotkeys", "SaveSelectedSaveState"));
        Assert.Equal("Keyboard/F4", document.GetValue("Hotkeys", "LoadSelectedSaveState"));
        Assert.Equal("true", document.GetValue("Main", "RewindEnable"));
        // SelectNextSaveStateSlot defaulted to F4, which load state now claims, so it is unbound.
        Assert.Null(document.GetValue("Hotkeys", "SelectNextSaveStateSlot"));
        Assert.True(BackupExists());
    }

    [Fact]
    public void DuckStation_Preview_ReportsChanges_ButWritesNothing()
    {
        Write("settings.ini", "[Main]", "SettingsVersion = 3", "RewindEnable = false", "[Hotkeys]", "FastForward = SDL-0/Back & SDL-0/B");

        var result = new DuckStationHotkeyConfigurator(_root, BackupRoot).Preview(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Changed, result.Status);
        Assert.NotEmpty(result.Changes);
        // Untouched on disk.
        Assert.Equal("false", Read("settings.ini").GetValue("Main", "RewindEnable"));
        Assert.False(BackupExists());
    }

    [Fact]
    public void DuckStation_ReApply_IsUnchanged()
    {
        Write("settings.ini", "[Main]", "SettingsVersion = 3", "RewindEnable = false", "[Hotkeys]");
        var configurator = new DuckStationHotkeyConfigurator(_root, BackupRoot);

        configurator.Apply(HotkeyProfile.Default);
        var second = configurator.Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Unchanged, second.Status);
    }

    [Fact]
    public void DuckStation_UnsupportedSettingsVersion_IsRefused()
    {
        Write("settings.ini", "[Main]", "SettingsVersion = 99", "[Hotkeys]");

        var result = new DuckStationHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.UnsupportedFormat, result.Status);
        Assert.Null(Read("settings.ini").GetValue("Hotkeys", "PowerOff"));
    }

    [Fact]
    public void DuckStation_MissingFile_IsConfigurationNotFound()
    {
        var result = new DuckStationHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.ConfigurationNotFound, result.Status);
    }

    [Fact]
    public void DuckStation_Revert_RestoresTheOriginalFile()
    {
        Write("settings.ini", "[Main]", "SettingsVersion = 3", "RewindEnable = false", "[Hotkeys]", "FastForward = Keyboard/Tab");
        var configurator = new DuckStationHotkeyConfigurator(_root, BackupRoot);
        configurator.Apply(HotkeyProfile.Default);

        var revert = configurator.Revert();

        Assert.Equal(HotkeyApplyStatus.Changed, revert.Status);
        var document = Read("settings.ini");
        Assert.Equal("false", document.GetValue("Main", "RewindEnable"));
        Assert.Equal("Keyboard/Tab", document.GetValue("Hotkeys", "FastForward"));
        Assert.Null(document.GetValue("Hotkeys", "PowerOff"));
    }

    // ---- PCSX2 ---------------------------------------------------------------------------------

    [Fact]
    public void Pcsx2_Apply_WritesKeys_ClearsExactConflict_KeepsChords_AndReportsRewindUnsupported()
    {
        Write(Path.Combine("inis", "PCSX2.ini"),
            "[UI]", "SettingsVersion = 1",
            "[Hotkeys]",
            "ShutdownVM = SDL-0/Back & SDL-0/Start",
            "SaveStateToSlot = SDL-0/Back & SDL-0/FaceNorth",
            "LoadStateFromSlot = SDL-0/Back & SDL-0/FaceSouth",
            "HoldTurbo = SDL-0/Back & SDL-0/FaceEast",
            "ToggleFrameLimit = Keyboard/F4",
            "GSDumpSingleFrame = Keyboard/Shift & Keyboard/F8");

        var result = new Pcsx2HotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Changed, result.Status);
        var document = Read(Path.Combine("inis", "PCSX2.ini"));
        Assert.Equal("Keyboard/F8", document.GetValue("Hotkeys", "ShutdownVM"));
        Assert.Equal("Keyboard/F2", document.GetValue("Hotkeys", "SaveStateToSlot"));
        Assert.Equal("Keyboard/F4", document.GetValue("Hotkeys", "LoadStateFromSlot"));
        Assert.Equal("Keyboard/L", document.GetValue("Hotkeys", "HoldTurbo"));
        // ToggleFrameLimit held plain F4, which load state now claims, so it is unbound.
        Assert.Null(document.GetValue("Hotkeys", "ToggleFrameLimit"));
        // A Shift+F8 chord is a different value than plain F8, so close game leaves it untouched.
        Assert.Equal("Keyboard/Shift & Keyboard/F8", document.GetValue("Hotkeys", "GSDumpSingleFrame"));

        var rewind = result.Bindings.Single(binding => binding.Action == HotkeyAction.Rewind);
        Assert.Equal(HotkeyBindingStatus.Unsupported, rewind.Status);
    }

    [Fact]
    public void Pcsx2_DescribeSupport_MarksOnlyRewindUnsupported()
    {
        Write(Path.Combine("inis", "PCSX2.ini"), "[UI]", "SettingsVersion = 1", "[Hotkeys]");

        var support = new Pcsx2HotkeyConfigurator(_root, BackupRoot).DescribeSupport(HotkeyProfile.Default);

        Assert.False(support.Single(item => item.Action == HotkeyAction.Rewind).IsSupported);
        Assert.True(support.Single(item => item.Action == HotkeyAction.SaveState).IsSupported);
    }

    // ---- Dolphin -------------------------------------------------------------------------------

    [Fact]
    public void Dolphin_Apply_WritesKeyboardTokens_KeepsDeviceAndUnrelated_ReportsRewindUnsupported()
    {
        Write(Path.Combine("Config", "Hotkeys.ini"),
            "[Hotkeys]",
            "Device = SDL/0/DualSense Wireless Controller",
            "General/Exit = @(Back+Start)",
            "General/Take Screenshot = `DInput/0/Keyboard Mouse:F9`");

        var result = new DolphinHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Changed, result.Status);
        var document = Read(Path.Combine("Config", "Hotkeys.ini"));
        Assert.Equal($"`{DolphinHotkeyConfigurator.KeyboardDevice}:F8`", document.GetValue("Hotkeys", "General/Exit"));
        Assert.Equal($"`{DolphinHotkeyConfigurator.KeyboardDevice}:L`", document.GetValue("Hotkeys", "Emulation Speed/Disable Emulation Speed Limit"));
        Assert.Equal($"`{DolphinHotkeyConfigurator.KeyboardDevice}:F2`", document.GetValue("Hotkeys", "Save State/Save to Selected Slot"));
        Assert.Equal($"`{DolphinHotkeyConfigurator.KeyboardDevice}:F4`", document.GetValue("Hotkeys", "Load State/Load from Selected Slot"));
        // The controller Device line and an unrelated keyboard binding (F9) are left untouched.
        Assert.Equal("SDL/0/DualSense Wireless Controller", document.GetValue("Hotkeys", "Device"));
        Assert.Equal("`DInput/0/Keyboard Mouse:F9`", document.GetValue("Hotkeys", "General/Take Screenshot"));

        var rewind = result.Bindings.Single(binding => binding.Action == HotkeyAction.Rewind);
        Assert.Equal(HotkeyBindingStatus.Unsupported, rewind.Status);
    }

    [Fact]
    public void Dolphin_Apply_Binds_WithoutADeviceLine()
    {
        // Fully-qualified keyboard tokens resolve regardless of the [Hotkeys] Device line.
        Write(Path.Combine("Config", "Hotkeys.ini"), "[Hotkeys]");

        var result = new DolphinHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Changed, result.Status);
        Assert.Equal($"`{DolphinHotkeyConfigurator.KeyboardDevice}:F8`", Read(Path.Combine("Config", "Hotkeys.ini")).GetValue("Hotkeys", "General/Exit"));
    }

    [Fact]
    public void Dolphin_Apply_ClearsBarewordSlotDefaultsThatCollideWithOurKeys()
    {
        Write(Path.Combine("Config", "Hotkeys.ini"),
            "[Hotkeys]",
            "Device = SDL/0/DualSense Wireless Controller",
            "Load State/Load State Slot 1 = F1",
            "Load State/Load State Slot 2 = F2",
            "Load State/Load State Slot 4 = F4",
            "Load State/Load State Slot 8 = F8",
            "Save State/Save State Slot 2 = @(Shift+F2)");

        new DolphinHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        var document = Read(Path.Combine("Config", "Hotkeys.ini"));
        // Our fully-qualified keys are written.
        Assert.Equal($"`{DolphinHotkeyConfigurator.KeyboardDevice}:F2`", document.GetValue("Hotkeys", "Save State/Save to Selected Slot"));
        Assert.Equal($"`{DolphinHotkeyConfigurator.KeyboardDevice}:F4`", document.GetValue("Hotkeys", "Load State/Load from Selected Slot"));
        // Bareword slot loads on the keys we claim (F2/F4/F8) are unbound so a key can't do two things.
        Assert.Null(document.GetValue("Hotkeys", "Load State/Load State Slot 2"));
        Assert.Null(document.GetValue("Hotkeys", "Load State/Load State Slot 4"));
        Assert.Null(document.GetValue("Hotkeys", "Load State/Load State Slot 8"));
        // A slot on another key stays, and Shift+F2 is a different input so it stays too.
        Assert.Equal("F1", document.GetValue("Hotkeys", "Load State/Load State Slot 1"));
        Assert.Equal("@(Shift+F2)", document.GetValue("Hotkeys", "Save State/Save State Slot 2"));
    }

    // ---- PPSSPP --------------------------------------------------------------------------------

    [Fact]
    public void Ppsspp_Apply_WritesKeyboardCodes_AndSupportsClose()
    {
        Write("controls.ini",
            "[ControlMapping]",
            "Fast-forward = 1-61,20-4036", "Pause = 1-111", "Rewind = 1-67");

        var result = new PpssppHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Changed, result.Status);
        Assert.All(result.Bindings, binding => Assert.Equal(HotkeyBindingStatus.Bound, binding.Status));
        var controls = Read("controls.ini");
        Assert.Equal("1-46", controls.GetValue("ControlMapping", "Rewind"));
        Assert.Equal("1-40", controls.GetValue("ControlMapping", "Fast-forward"));
        Assert.Equal("1-132", controls.GetValue("ControlMapping", "Save State"));
        Assert.Equal("1-134", controls.GetValue("ControlMapping", "Load State"));
        Assert.Equal("1-138", controls.GetValue("ControlMapping", "Exit App"));
    }

    [Fact]
    public void Ppsspp_ReApply_IsUnchanged()
    {
        Write("controls.ini", "[ControlMapping]", "Rewind = 1-67");
        var configurator = new PpssppHotkeyConfigurator(_root, BackupRoot);

        configurator.Apply(HotkeyProfile.Default);
        var second = configurator.Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Unchanged, second.Status);
    }

    [Fact]
    public void Ppsspp_MissingFile_IsConfigurationNotFound()
    {
        var result = new PpssppHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.ConfigurationNotFound, result.Status);
    }

    // ---- RetroArch -----------------------------------------------------------------------------

    [Fact]
    public void RetroArch_Apply_SetsKeyboardKeys_EnablesRewind_ClearsControllerButtons()
    {
        Write("retroarch.cfg",
            "input_enable_hotkey_btn = \"7\"",
            "input_exit_emulator = \"escape\"",
            "input_exit_emulator_btn = \"6\"",
            "input_rewind = \"r\"",
            "input_rewind_btn = \"3\"",
            "input_hold_fast_forward = \"l\"",
            "input_hold_fast_forward_btn = \"0\"",
            "input_save_state = \"f2\"",
            "input_save_state_btn = \"2\"",
            "input_load_state = \"f4\"",
            "input_load_state_btn = \"1\"",
            "rewind_enable = \"false\"");

        var result = new RetroArchHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Changed, result.Status);
        Assert.All(result.Bindings, binding => Assert.Equal(HotkeyBindingStatus.Bound, binding.Status));
        var document = Read("retroarch.cfg");
        Assert.Equal("\"f8\"", document.GetValue(null, "input_exit_emulator"));
        Assert.Equal("\"r\"", document.GetValue(null, "input_rewind"));
        Assert.Equal("\"l\"", document.GetValue(null, "input_hold_fast_forward"));
        Assert.Equal("\"f2\"", document.GetValue(null, "input_save_state"));
        Assert.Equal("\"f4\"", document.GetValue(null, "input_load_state"));
        Assert.Equal("\"true\"", document.GetValue(null, "rewind_enable"));
        // The controller hotkey buttons a previous version wrote are cleared to "nul".
        Assert.Equal("\"nul\"", document.GetValue(null, "input_enable_hotkey_btn"));
        Assert.Equal("\"nul\"", document.GetValue(null, "input_exit_emulator_btn"));
        Assert.Equal("\"nul\"", document.GetValue(null, "input_rewind_btn"));
        Assert.Equal("\"nul\"", document.GetValue(null, "input_hold_fast_forward_btn"));
        Assert.Equal("\"nul\"", document.GetValue(null, "input_save_state_btn"));
        Assert.Equal("\"nul\"", document.GetValue(null, "input_load_state_btn"));
    }

    [Fact]
    public void RetroArch_ReApply_IsUnchanged()
    {
        Write("retroarch.cfg",
            "input_exit_emulator = \"escape\"",
            "input_rewind = \"r\"",
            "input_hold_fast_forward = \"l\"",
            "input_save_state = \"f2\"",
            "input_load_state = \"f4\"",
            "rewind_enable = \"true\"");
        var configurator = new RetroArchHotkeyConfigurator(_root, BackupRoot);

        configurator.Apply(HotkeyProfile.Default);
        var second = configurator.Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Unchanged, second.Status);
    }

    [Fact]
    public void RetroArch_MissingFile_IsConfigurationNotFound()
    {
        var result = new RetroArchHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.ConfigurationNotFound, result.Status);
    }

    // ---- Azahar --------------------------------------------------------------------------------

    [Fact]
    public void Azahar_Apply_LatestNames_BindsKeys_ClearsConflicts_PinsDefaults()
    {
        Write(Path.Combine("config", "qt-config.ini"),
            "[UI]",
            @"Shortcuts\Main%20Window\Quick%20Save\KeySeq\default=true",
            @"Shortcuts\Main%20Window\Quick%20Load\KeySeq\default=true",
            @"Shortcuts\Main%20Window\Toggle%20Turbo%20Mode\KeySeq\default=true",
            @"Shortcuts\Main%20Window\Stop%20Emulation\KeySeq\default=true",
            @"Shortcuts\Main%20Window\Stop%20Emulation\KeySeq=F5",
            @"Shortcuts\Main%20Window\Load%20Amiibo\KeySeq\default=true",
            @"Shortcuts\Main%20Window\Load%20Amiibo\KeySeq=F2",
            @"Shortcuts\Main%20Window\Rotate%20Screens%20Upright\KeySeq\default=true",
            @"Shortcuts\Main%20Window\Rotate%20Screens%20Upright\KeySeq=F8",
            @"Shortcuts\Main%20Window\Continue\Pause%20Emulation\KeySeq\default=true",
            @"Shortcuts\Main%20Window\Continue\Pause%20Emulation\KeySeq=F4");

        var result = new AzaharHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Changed, result.Status);
        var document = Read(Path.Combine("config", "qt-config.ini"));
        // The latest action names win, and each is bound to its scheme key.
        Assert.Equal("F2", document.GetValue("UI", @"Shortcuts\Main%20Window\Quick%20Save\KeySeq"));
        Assert.Equal("F4", document.GetValue("UI", @"Shortcuts\Main%20Window\Quick%20Load\KeySeq"));
        Assert.Equal("F8", document.GetValue("UI", @"Shortcuts\Main%20Window\Stop%20Emulation\KeySeq"));
        Assert.Equal("L", document.GetValue("UI", @"Shortcuts\Main%20Window\Toggle%20Turbo%20Mode\KeySeq"));
        // Writes pin the default flag off so Azahar keeps them.
        Assert.Equal("false", document.GetValue("UI", @"Shortcuts\Main%20Window\Quick%20Save\KeySeq\default"));
        // Shortcuts that held our keys are cleared (empty KeySeq), including the F8 one the naive spec missed.
        Assert.Equal("", document.GetValue("UI", @"Shortcuts\Main%20Window\Load%20Amiibo\KeySeq"));
        Assert.Equal("", document.GetValue("UI", @"Shortcuts\Main%20Window\Rotate%20Screens%20Upright\KeySeq"));
        Assert.Equal("", document.GetValue("UI", @"Shortcuts\Main%20Window\Continue\Pause%20Emulation\KeySeq"));

        Assert.Equal(HotkeyBindingStatus.Unsupported, result.Bindings.Single(b => b.Action == HotkeyAction.Rewind).Status);
        Assert.Equal(HotkeyBindingStatus.Bound, result.Bindings.Single(b => b.Action == HotkeyAction.SaveState).Status);
    }

    [Fact]
    public void Azahar_Apply_OlderNames_FallsBackToTheAvailableShortcuts()
    {
        Write(Path.Combine("config", "qt-config.ini"),
            "[UI]",
            @"Shortcuts\Main%20Window\Save%20to%20Oldest%20Slot\KeySeq\default=true",
            @"Shortcuts\Main%20Window\Save%20to%20Oldest%20Slot\KeySeq=Ctrl+C",
            @"Shortcuts\Main%20Window\Load%20from%20Newest%20Slot\KeySeq\default=true",
            @"Shortcuts\Main%20Window\Load%20from%20Newest%20Slot\KeySeq=Ctrl+V",
            @"Shortcuts\Main%20Window\Toggle%20Per-Application%20Speed\KeySeq\default=true",
            @"Shortcuts\Main%20Window\Toggle%20Per-Application%20Speed\KeySeq=Ctrl+Z",
            @"Shortcuts\Main%20Window\Stop%20Emulation\KeySeq\default=true",
            @"Shortcuts\Main%20Window\Stop%20Emulation\KeySeq=F5");

        var result = new AzaharHotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Changed, result.Status);
        var document = Read(Path.Combine("config", "qt-config.ini"));
        Assert.Equal("F2", document.GetValue("UI", @"Shortcuts\Main%20Window\Save%20to%20Oldest%20Slot\KeySeq"));
        Assert.Equal("F4", document.GetValue("UI", @"Shortcuts\Main%20Window\Load%20from%20Newest%20Slot\KeySeq"));
        Assert.Equal("L", document.GetValue("UI", @"Shortcuts\Main%20Window\Toggle%20Per-Application%20Speed\KeySeq"));
        Assert.Equal("F8", document.GetValue("UI", @"Shortcuts\Main%20Window\Stop%20Emulation\KeySeq"));
    }

    // ---- RPCS3 ---------------------------------------------------------------------------------

    [Fact]
    public void Rpcs3_Apply_AddsStopShortcut_AndReportsOthersUnsupported()
    {
        Write(Path.Combine("GuiConfigs", "CurrentSettings.ini"), "[main_window]", "geometry=abc");

        var result = new Rpcs3HotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.Changed, result.Status);
        Assert.Equal("F8", Read(Path.Combine("GuiConfigs", "CurrentSettings.ini")).GetValue("Shortcuts", "game_window_stop"));

        Assert.Equal(HotkeyBindingStatus.Bound, result.Bindings.Single(b => b.Action == HotkeyAction.CloseGame).Status);
        Assert.All(
            result.Bindings.Where(b => b.Action != HotkeyAction.CloseGame),
            b => Assert.Equal(HotkeyBindingStatus.Unsupported, b.Status));
    }

    [Fact]
    public void Rpcs3_MissingFile_IsConfigurationNotFound()
    {
        var result = new Rpcs3HotkeyConfigurator(_root, BackupRoot).Apply(HotkeyProfile.Default);

        Assert.Equal(HotkeyApplyStatus.ConfigurationNotFound, result.Status);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private void Write(string relative, params string[] lines)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join('\n', lines) + "\n");
    }

    private EmulatorConfigDocument Read(string relative) =>
        new(File.ReadAllText(Path.Combine(_root, relative)));

    private bool BackupExists() =>
        Directory.Exists(BackupRoot) && Directory.EnumerateFiles(BackupRoot, "*.bak", SearchOption.AllDirectories).Any();
}
