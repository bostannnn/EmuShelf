using EmuShelf.Core.SaveSync;
using EmuShelf.Integrations.Emulators.MelonDs;
using EmuShelf.Integrations.Emulators.RetroArch;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

/// <summary>
/// Standalone melonDS save sync: melonDS's own <c>SaveFilePath</c> decides the folder, and each save
/// keys by game name so it is the same cloud entry a RetroArch DS core writes as <c>.srm</c>.
/// </summary>
public sealed class MelonDsSaveSyncTests : TempAppDirectoryTestBase
{
    private static readonly string[] Library =
        ["Pokemon Platinum (USA)", "Tetris DS (USA)", "Contra 4 (USA)"];

    [Fact]
    public async Task ConfiguredSaveFolderIsRead_FromTheTomlInstanceTable()
    {
        var configDirectory = CreateDirectory("config");
        var saves = CreateDirectory("melonDS saves");
        WriteToml(configDirectory, saveFilePath: saves, savestatePath: null);
        await WriteFileAsync(saves, "Pokemon Platinum (USA).sav", "ds save");
        await WriteFileAsync(saves, "Tetris DS (USA).sav", "ds save");

        var provider = CreateProvider(configDirectory);
        var info = await provider.GetSaveInfoAsync();

        Assert.Equal(saves, info.SaveDirectory);
        Assert.Equal(Path.Combine(configDirectory, "melonDS.toml"), info.ConfigFilePath);
        Assert.False(info.IsOverridden);
        Assert.Equal(
            ["nds/battery/Pokemon Platinum (USA)", "nds/battery/Tetris DS (USA)"],
            (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
        Assert.Equal(
            Path.Combine(saves, "Pokemon Platinum (USA).sav"),
            provider.ResolveUnit("nds/battery/Pokemon Platinum (USA)")!.Path);
    }

    [Fact]
    public async Task ConfiguredSaveFolderIsRead_FromTheLegacyIni()
    {
        // Pre-1.0 melonDS wrote flat melonDS.ini; newer builds still import it, and a machine that has
        // not been upgraded yet still has only that file.
        var configDirectory = CreateDirectory("legacy-config");
        var saves = CreateDirectory("legacy saves");
        await File.WriteAllLinesAsync(
            Path.Combine(configDirectory, "melonDS.ini"),
            ["WindowWidth=256", $"SaveFilePath={saves}", "SavestatePath="]);
        await WriteFileAsync(saves, "Contra 4 (USA).sav", "ds save");

        var provider = CreateProvider(configDirectory);
        var info = await provider.GetSaveInfoAsync();

        Assert.Equal(saves, info.SaveDirectory);
        Assert.Equal(Path.Combine(configDirectory, "melonDS.ini"), info.ConfigFilePath);
        Assert.Equal(
            ["nds/battery/Contra 4 (USA)"],
            (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
    }

    [Fact]
    public async Task WithNoConfiguredSaveFolder_NothingSyncs_AndTheFolderIsReportedAsUnset()
    {
        // melonDS's default writes each save beside its ROM. EmuShelf does not sync from the user's
        // game folders, so this must report "nothing here" rather than resolve some plausible folder.
        var configDirectory = CreateDirectory("unconfigured");
        WriteToml(configDirectory, saveFilePath: null, savestatePath: null);

        var provider = CreateProvider(configDirectory);
        var info = await provider.GetSaveInfoAsync();

        Assert.Null(info.SaveDirectory);
        Assert.Equal(configDirectory, info.ConfigDirectory);
        Assert.Empty(await provider.GetSaveUnitsAsync());
        Assert.Null(provider.ResolveUnit("nds/battery/Tetris DS (USA)"));
        // Settings still has a folder to show — melonDS's own — rather than an empty line.
        Assert.Equal(configDirectory, await provider.GetSaveDataDirectoryAsync());
    }

    [Fact]
    public async Task AnEmuShelfOverrideWinsOverMelonDsOwnSetting()
    {
        var configDirectory = CreateDirectory("override-config");
        var configured = CreateDirectory("melonDS configured");
        var chosen = CreateDirectory("chosen saves");
        WriteToml(configDirectory, saveFilePath: configured, savestatePath: null);
        await WriteFileAsync(configured, "Tetris DS (USA).sav", "not this one");
        await WriteFileAsync(chosen, "Contra 4 (USA).sav", "this one");

        var provider = CreateProvider(configDirectory, saveDirectoryOverride: chosen);
        var info = await provider.GetSaveInfoAsync();

        Assert.Equal(chosen, info.SaveDirectory);
        Assert.True(info.IsOverridden);
        Assert.Equal(
            ["nds/battery/Contra 4 (USA)"],
            (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
    }

    [Fact]
    public async Task AFolderChosenHereIsClaimedWhole_WithoutLibraryNameMatching()
    {
        // The escape hatch: the user pointed at this folder, so its saves are this platform's even
        // when their names do not match the library's ROM file names (a re-dumped or renamed set).
        // Only the console's own firmware save is still never a game save.
        var configDirectory = CreateDirectory("exclusive-config");
        var chosen = CreateDirectory("chosen exclusive saves");
        WriteToml(configDirectory, saveFilePath: null, savestatePath: null);
        await WriteFileAsync(chosen, "Not In Library (USA).sav", "ds save");
        await WriteFileAsync(chosen, "firmware.sav", "console identity");

        var provider = CreateProvider(configDirectory, saveDirectoryOverride: chosen);

        Assert.Equal(
            ["nds/battery/Not In Library (USA)"],
            (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
        Assert.NotNull(provider.ResolveUnit("nds/battery/Not In Library (USA)"));
        Assert.Null(provider.ResolveUnit("nds/battery/firmware"));
    }

    [Fact]
    public async Task ARelativeSaveFilePathAnchorsOnMelonDsOwnDirectory()
    {
        var configDirectory = CreateDirectory("relative-config");
        var saves = CreateDirectory(Path.Combine("relative-config", "saves"));
        WriteToml(configDirectory, saveFilePath: "saves", savestatePath: null);
        await WriteFileAsync(saves, "Contra 4 (USA).sav", "ds save");

        var provider = CreateProvider(configDirectory);

        Assert.Equal(saves, (await provider.GetSaveInfoAsync()).SaveDirectory);
        Assert.Single(await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task OnlySavesNamedAfterADsGameInTheLibraryAreClaimed()
    {
        // melonDS drops a Slot-2 GBA cartridge's save in the same folder, writes the DSi firmware save
        // as firmware.sav, and the folder may be shared with other emulators entirely.
        var configDirectory = CreateDirectory("shared-config");
        var saves = CreateDirectory("shared saves");
        WriteToml(configDirectory, saveFilePath: saves, savestatePath: null);
        await WriteFileAsync(saves, "Contra 4 (USA).sav", "ds save");
        await WriteFileAsync(saves, "Metroid Fusion (USA).sav", "gba slot-2 save");
        await WriteFileAsync(saves, "firmware.sav", "console identity");
        await WriteFileAsync(saves, "Contra 4 (USA).ml0", "save state");

        var provider = CreateProvider(configDirectory);

        Assert.Equal(
            ["nds/battery/Contra 4 (USA)"],
            (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
        Assert.Null(provider.ResolveUnit("nds/battery/Metroid Fusion (USA)"));
    }

    [Fact]
    public async Task ASaveRoundTripsBetweenMelonDsAndARetroArchDsCore()
    {
        // The whole point of the shared key: the Thor's RetroArch writes Contra 4 (USA).srm and the
        // desktop's melonDS writes Contra 4 (USA).sav, and both are the same raw cartridge dump. One
        // cloud entry, resolved on each machine to the file its own emulator reads.
        var configDirectory = CreateDirectory("interop-config");
        var melonSaves = CreateDirectory("melon saves");
        WriteToml(configDirectory, saveFilePath: melonSaves, savestatePath: null);
        await WriteFileAsync(melonSaves, "Contra 4 (USA).sav", "ds save");

        var retroArchInstallation = CreateDirectory("RetroArch");
        var retroArchSaves = CreateDirectory(Path.Combine("RetroArch", "saves"));
        await File.WriteAllLinesAsync(
            Path.Combine(retroArchInstallation, "retroarch.cfg"),
            ["savefile_directory = \":\\saves\""]);
        await WriteFileAsync(retroArchSaves, "Contra 4 (USA).srm", "ds save");

        var melonDs = CreateProvider(configDirectory);
        var retroArch = new RetroArchSaveLocationProvider(
            "nds",
            "melondsds_libretro.dll",
            retroArchInstallation,
            homeDirectory: Path.Combine(BaseDirectory, "unused-home"),
            isWindows: false,
            isMacOS: false,
            gameFileNames: () => Library);

        var shared = "nds/battery/Contra 4 (USA)";
        Assert.Equal([shared], (await melonDs.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
        Assert.Equal([shared], (await retroArch.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
        Assert.Equal(Path.Combine(melonSaves, "Contra 4 (USA).sav"), melonDs.ResolveUnit(shared)!.Path);
        Assert.Equal(Path.Combine(retroArchSaves, "Contra 4 (USA).srm"), retroArch.ResolveUnit(shared)!.Path);

        // Both providers own the shared key, and neither owns the other's legacy file-name key any
        // more — otherwise one file would sync against itself under two ids.
        Assert.True(melonDs.OwnsUnit(shared));
        Assert.True(retroArch.OwnsUnit(shared));
        Assert.False(melonDs.OwnsUnit("nds/Contra 4 (USA).srm"));
        Assert.False(retroArch.OwnsUnit("nds/Contra 4 (USA).srm"));
        Assert.Null(retroArch.ResolveUnit("nds/Contra 4 (USA).srm"));
    }

    [Fact]
    public async Task AFirstRestoreLandsOnMelonDsOwnExtension_AnExistingSaveKeepsItsName()
    {
        var configDirectory = CreateDirectory("restore-config");
        var saves = CreateDirectory("restore saves");
        WriteToml(configDirectory, saveFilePath: saves, savestatePath: null);
        // This game was previously synced from a RetroArch machine, so it is already a .srm here.
        await WriteFileAsync(saves, "Tetris DS (USA).srm", "ds save");

        var provider = CreateProvider(configDirectory);

        Assert.Equal(
            Path.Combine(saves, "Pokemon Platinum (USA).sav"),
            provider.ResolveUnit("nds/battery/Pokemon Platinum (USA)")!.Path);
        Assert.Equal(
            Path.Combine(saves, "Tetris DS (USA).srm"),
            provider.ResolveUnit("nds/battery/Tetris DS (USA)")!.Path);
    }

    [Fact]
    public async Task WhenBothExtensionsExist_TheOneTheEmulatorTouchedLastIsTheSave()
    {
        // A folder can hold both spellings of one save — a stale .srm left by a RetroArch core beside
        // the .sav melonDS keeps writing, or the reverse. Preferring a fixed extension would sync the
        // copy nobody is playing and overwrite it on the next download, so the newest file wins.
        var configDirectory = CreateDirectory("both-config");
        var saves = CreateDirectory("both saves");
        WriteToml(configDirectory, saveFilePath: saves, savestatePath: null);
        await WriteFileAsync(saves, "Contra 4 (USA).sav", "melonDS is the live one");
        await WriteFileAsync(saves, "Contra 4 (USA).srm", "stale RetroArch copy");
        var stale = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(saves, "Contra 4 (USA).srm"), stale);
        File.SetLastWriteTimeUtc(Path.Combine(saves, "Contra 4 (USA).sav"), stale.AddDays(1));

        var provider = CreateProvider(configDirectory);

        Assert.Equal(
            Path.Combine(saves, "Contra 4 (USA).sav"),
            provider.ResolveUnit("nds/battery/Contra 4 (USA)")!.Path);

        // The same folder, with the RetroArch copy now the live one, resolves the other way.
        File.SetLastWriteTimeUtc(Path.Combine(saves, "Contra 4 (USA).srm"), stale.AddDays(2));
        Assert.Equal(
            Path.Combine(saves, "Contra 4 (USA).srm"),
            CreateProvider(configDirectory).ResolveUnit("nds/battery/Contra 4 (USA)")!.Path);
    }

    [Fact]
    public async Task ALegacyIniPathKeepsCharactersATomlCommentWouldEat()
    {
        // melonDS's legacy INI takes everything after "=" verbatim, so a folder named with a hash must
        // survive. Cutting there would resolve a real folder to a different, non-existent one — and a
        // download would then quietly create it and write saves melonDS never reads.
        var configDirectory = CreateDirectory("hash-config");
        var saves = CreateDirectory("Rock #1 saves");
        await File.WriteAllLinesAsync(
            Path.Combine(configDirectory, "melonDS.ini"),
            [$"SaveFilePath={saves}"]);
        await WriteFileAsync(saves, "Contra 4 (USA).sav", "ds save");

        var provider = CreateProvider(configDirectory);

        Assert.Equal(saves, (await provider.GetSaveInfoAsync()).SaveDirectory);
        Assert.Single(await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task ATomlCommentAfterAnUnquotedValueIsStillTrimmed()
    {
        var configDirectory = CreateDirectory("comment-config");
        var saves = CreateDirectory("commented saves");
        await File.WriteAllLinesAsync(
            Path.Combine(configDirectory, "melonDS.toml"),
            ["[Instance0]", $"SaveFilePath = {saves} # hand-edited"]);
        await WriteFileAsync(saves, "Contra 4 (USA).sav", "ds save");

        Assert.Equal(saves, (await CreateProvider(configDirectory).GetSaveInfoAsync()).SaveDirectory);
    }

    [Fact]
    public async Task TheSaveStateFolderIsReadFromMelonDsOwnSetting()
    {
        var configDirectory = CreateDirectory("state-config");
        var saves = CreateDirectory("state saves");
        var states = CreateDirectory("state states");
        WriteToml(configDirectory, saveFilePath: saves, savestatePath: states);

        var info = await CreateProvider(configDirectory).GetSaveInfoAsync();

        Assert.Equal(states, info.SavestateDirectory);
    }

    [Fact]
    public void SaveStatesStayScopedToTheirOwnChannel()
    {
        // A .ml0 written by the nightly cannot be loaded by the release build, so the two channels
        // never share a save-state namespace — while both share the battery namespace by system.
        var release = CreateProvider(CreateDirectory("channel-release"));
        var nightly = new MelonDsSaveLocationProvider(
            "melonds-nightly", BaseDirectory, homeDirectory: Path.Combine(BaseDirectory, "unused-home"));

        Assert.Equal("nds/", release.UnitIdPrefix);
        Assert.Equal("nds/", nightly.UnitIdPrefix);
        Assert.Equal("melonds/nds/", release.StateNamespacePrefix);
        Assert.Equal("melonds-nightly/nds/", nightly.StateNamespacePrefix);
    }

    [Fact]
    public void TheConfigDirectoryFollowsMelonDsOwnResolutionOrder()
    {
        var home = CreateDirectory("home");
        var installation = CreateDirectory("melonDS install");
        var localAppData = CreateDirectory("localappdata");

        // Nothing exists yet: no config directory is invented.
        Assert.Null(Provider().ResolveConfigDirectory());

        // Qt's per-platform config location, in melonDS's own order.
        var xdg = CreateDirectory(Path.Combine("home", ".config", "melonDS"));
        Assert.Equal(xdg, Provider().ResolveConfigDirectory());
        var preferences = CreateDirectory(Path.Combine("home", "Library", "Preferences", "melonDS"));
        var windows = CreateDirectory(Path.Combine("localappdata", "melonDS"));
        Assert.Equal(xdg, Provider().ResolveConfigDirectory());
        Assert.NotNull(preferences);
        Assert.NotNull(windows);

        // A config file beside the executable is a portable Windows build and outranks all of them.
        File.WriteAllText(Path.Combine(installation, "melonDS.toml"), string.Empty);
        Assert.Equal(installation, Provider().ResolveConfigDirectory());

        // melonDS's own rule wins outright: a "portable" folder beside the executable IS the emu
        // directory, even before it holds a config file.
        var portable = CreateDirectory(Path.Combine("melonDS install", "portable"));
        Assert.Equal(portable, Provider().ResolveConfigDirectory());

        // A Flatpak looks only inside its sandbox.
        var flatpak = CreateDirectory(
            Path.Combine("home", ".var", "app", "net.kuribo64.melonDS", "config", "melonDS"));
        Assert.Equal(flatpak, Provider(isFlatpak: true).ResolveConfigDirectory());

        MelonDsSaveLocationProvider Provider(bool isFlatpak = false) => new(
            "melonds",
            installation,
            homeDirectory: home,
            appDataDirectory: Path.Combine(BaseDirectory, "appdata-absent"),
            localAppDataDirectory: localAppData,
            xdgConfigHome: Path.Combine(home, ".config"),
            isFlatpak: isFlatpak);
    }

    private MelonDsSaveLocationProvider CreateProvider(
        string configDirectory,
        string? saveDirectoryOverride = null) =>
        new(
            "melonds",
            installationDirectory: configDirectory,
            saveDirectoryOverride: saveDirectoryOverride,
            homeDirectory: Path.Combine(BaseDirectory, "unused-home"),
            appDataDirectory: Path.Combine(BaseDirectory, "unused-appdata"),
            localAppDataDirectory: Path.Combine(BaseDirectory, "unused-localappdata"),
            xdgConfigHome: Path.Combine(BaseDirectory, "unused-xdg"),
            gameFileNames: () => Library);

    // A melonDS.toml as melonDS writes it: per-instance settings under [Instance0], quoted strings,
    // and plenty of shapes this reader must skip rather than choke on.
    private static void WriteToml(string configDirectory, string? saveFilePath, string? savestatePath)
    {
        File.WriteAllLines(
            Path.Combine(configDirectory, "melonDS.toml"),
            [
                "# melonDS configuration",
                "LimitFPS = true",
                "Keyboard = [32, 33, 34]",
                "",
                "[Instance0]",
                "DSBatteryLevelOkay = true",
                $"SaveFilePath = \"{Escape(saveFilePath)}\"",
                $"SavestatePath = \"{Escape(savestatePath)}\"",
                "CheatFilePath = \"\"",
                "",
                "[Instance1]",
                // A second window's overrides belong to an instance EmuShelf never launches.
                "SaveFilePath = \"/second/instance/saves\"",
            ]);
    }

    private static string Escape(string? path) => path?.Replace("\\", "\\\\") ?? string.Empty;

    private string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(BaseDirectory, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    private static Task WriteFileAsync(string directory, string fileName, string content) =>
        File.WriteAllTextAsync(Path.Combine(directory, fileName), content);
}
