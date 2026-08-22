using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Emulators.RetroArch;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class RetroArchSaveSyncTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task PortableWindowsConfigResolvesTheApplicationDirectoryPrefix()
    {
        // ":" is RetroArch's own prefix for its application directory, which is what a portable
        // Windows install actually writes into retroarch.cfg.
        var installation = Path.Combine(BaseDirectory, "RetroArch");
        WriteConfig(installation, savefileDirectory: ":\\saves");
        var saves = Path.Combine(installation, "saves");
        Directory.CreateDirectory(saves);
        await File.WriteAllTextAsync(Path.Combine(saves, "Metroid Fusion (USA).srm"), "gba save");
        await File.WriteAllTextAsync(Path.Combine(saves, "Super Mario All-Stars (USA).srm"), "snes save");
        await File.WriteAllTextAsync(Path.Combine(saves, "Contra 4 (USA).state"), "save state");
        await File.WriteAllTextAsync(Path.Combine(saves, "notes.txt"), "not a save");

        var provider = CreateProvider(
            "gba",
            "mgba_libretro.dll",
            installation,
            gameFileNames: ["Metroid Fusion (USA)", "Super Mario All-Stars (USA)"]);
        var info = await provider.GetSaveInfoAsync();

        Assert.Equal(saves, info.SaveDirectory);
        Assert.Equal("mGBA", info.Core.Name);
        Assert.False(info.SortedByCore);
        Assert.Equal(
            [
                new SaveUnit("gba/Metroid Fusion (USA).srm", "Metroid Fusion (USA).srm", SaveUnitKind.File),
                new SaveUnit(
                    "gba/Super Mario All-Stars (USA).srm",
                    "Super Mario All-Stars (USA).srm",
                    SaveUnitKind.File),
            ],
            await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task ASharedSaveFolderClaimsOnlyTheSavesOfThisSystemsOwnGames()
    {
        // With per-core sorting off — RetroArch's default — every core writes .srm into the same
        // folder, so each system's row must claim only the saves named after its own library.
        var installation = Path.Combine(BaseDirectory, "RetroArch-shared");
        WriteConfig(installation, savefileDirectory: ":\\saves");
        var saves = Path.Combine(installation, "saves");
        Directory.CreateDirectory(saves);
        await File.WriteAllTextAsync(Path.Combine(saves, "Metroid Fusion (USA).srm"), "gba");
        await File.WriteAllTextAsync(Path.Combine(saves, "Contra 4 (USA).srm"), "ds");
        await File.WriteAllTextAsync(Path.Combine(saves, "Super Mario All-Stars (USA).srm"), "snes");

        var gba = CreateProvider(
            "gba", "mgba_libretro.dll", installation, gameFileNames: ["Metroid Fusion (USA)"]);
        var nds = CreateProvider(
            "nds", "melondsds_libretro.dll", installation, gameFileNames: ["Contra 4 (USA)"]);

        Assert.False((await gba.GetSaveInfoAsync()).IsExclusive);
        Assert.Equal(
            ["gba/Metroid Fusion (USA).srm"],
            (await gba.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
        Assert.Equal(
            ["nds/Contra 4 (USA).srm"],
            (await nds.GetSaveUnitsAsync()).Select(unit => unit.UnitId));

        // The same rule guards a remote-only unit: a save for a game this machine's library does
        // not have is never written into the shared folder under this system's name.
        Assert.NotNull(gba.ResolveUnit("gba/Metroid Fusion (USA).srm"));
        Assert.Null(gba.ResolveUnit("gba/Contra 4 (USA).srm"));
        Assert.Null(CreateProvider("gba", "mgba_libretro.dll", installation).ResolveUnit(
            "gba/Metroid Fusion (USA).srm"));
    }

    [Fact]
    public async Task AChosenFolderIsTreatedAsThisSystemsOwnWithoutLibraryMatching()
    {
        var installation = Path.Combine(BaseDirectory, "RetroArch-chosen-exclusive");
        WriteConfig(installation, savefileDirectory: ":\\saves");
        var chosen = Path.Combine(BaseDirectory, "snes-only-saves");
        Directory.CreateDirectory(chosen);
        await File.WriteAllTextAsync(Path.Combine(chosen, "Not In Library (USA).srm"), "snes");

        var provider = CreateProvider("snes", "snes9x_libretro.dll", installation, directoryOverride: chosen);
        var info = await provider.GetSaveInfoAsync();

        Assert.True(info.IsExclusive);
        Assert.Single(await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task SortingByCorePlacesSavesInTheCoresOwnFolder()
    {
        var installation = Path.Combine(BaseDirectory, "RetroArch-sorted");
        WriteConfig(installation, savefileDirectory: ":\\saves", extra: ["sort_savefiles_enable = \"true\""]);
        var sorted = Path.Combine(installation, "saves", "melonDS DS");
        Directory.CreateDirectory(sorted);
        await File.WriteAllTextAsync(Path.Combine(sorted, "Pokemon - Black Version (USA).sav"), "ds save");
        // The core's own hint file lives in the same folder and is not save data.
        await File.WriteAllTextAsync(Path.Combine(sorted, "Place NDS saves here"), string.Empty);
        Directory.CreateDirectory(Path.Combine(installation, "saves"));
        await File.WriteAllTextAsync(
            Path.Combine(installation, "saves", "Unsorted (USA).srm"),
            "another core's save");

        var provider = CreateProvider("nds", "melondsds_libretro.dll", installation);
        var info = await provider.GetSaveInfoAsync();

        Assert.Equal(sorted, info.SaveDirectory);
        Assert.True(info.SortedByCore);
        Assert.Equal(
            ["nds/Pokemon - Black Version (USA).sav"],
            (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
    }

    [Fact]
    public async Task CorelessOverrideSyncsAnExactFolder_ForWatermelonDsShapedSaves()
    {
        // WatermelonDS has no libretro core but writes <game>.srm into a flat folder. Pointed at that
        // folder as an override, the RetroArch provider must sync it under nds/ keys without requiring
        // a core (see docs/android-save-sync-model.md). Before this it threw "No libretro core."
        var folder = Path.Combine(BaseDirectory, "Watermelon-DS");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "Trauma Center - Under the Knife (USA).srm"), "ds save");
        await File.WriteAllTextAsync(Path.Combine(folder, "The World Ends With You (USA).srm"), "ds save");

        var provider = new RetroArchSaveLocationProvider(
            "nds",
            corePath: null,
            installationDirectory: BaseDirectory,
            directoryOverride: folder,
            homeDirectory: Path.Combine(BaseDirectory, "unused-home"),
            isWindows: false,
            isMacOS: false);
        var info = await provider.GetSaveInfoAsync();

        Assert.Equal(folder, info.SaveDirectory);
        Assert.False(info.SortedByCore);
        Assert.True(info.IsExclusive);
        Assert.Equal(
            [
                "nds/The World Ends With You (USA).srm",
                "nds/Trauma Center - Under the Knife (USA).srm",
            ],
            (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
        Assert.NotNull(provider.ResolveUnit("nds/Trauma Center - Under the Knife (USA).srm"));
    }

    [Fact]
    public async Task AndroidCoreFileNameStillNamesTheSortedByCoreSaveFolder()
    {
        // RetroArch's Android cores are "<core>_libretro_android.so". The "_android" build tag must be
        // dropped when naming the core, or the sorted-by-core folder cannot be resolved and every
        // RetroArch system on Android silently syncs nothing (measured on the Thor). See
        // docs/android-save-sync-model.md.
        var installation = Path.Combine(BaseDirectory, "RetroArch-android");
        WriteConfig(installation, savefileDirectory: ":\\saves", extra: ["sort_savefiles_enable = \"true\""]);
        var sorted = Path.Combine(installation, "saves", "mGBA");
        Directory.CreateDirectory(sorted);
        await File.WriteAllTextAsync(Path.Combine(sorted, "Metroid Fusion (USA).srm"), "gba save");

        var provider = CreateProvider(
            "gba",
            "/data/data/com.retroarch.aarch64/cores/mgba_libretro_android.so",
            installation);
        var info = await provider.GetSaveInfoAsync();

        Assert.Equal("mGBA", info.Core.Name);
        Assert.Equal(sorted, info.SaveDirectory);
        Assert.True(info.SortedByCore);
        Assert.Equal(
            ["gba/Metroid Fusion (USA).srm"],
            (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
    }

    [Fact]
    public async Task ASharedCoreFolderClaimsOnlyThisSystemsLibrarySavesEvenWhenSortedByCore()
    {
        // mGBA serves both Game Boy Advance and Game Boy Color, so with "sort saves by core" on both
        // systems resolve to the same saves/mGBA folder. Flagged as a shared core, each claims only
        // its own library — otherwise each would claim the whole folder and upload every save twice.
        var installation = Path.Combine(BaseDirectory, "RetroArch-shared-core");
        WriteConfig(installation, savefileDirectory: ":\\saves", extra: ["sort_savefiles_enable = \"true\""]);
        var sorted = Path.Combine(installation, "saves", "mGBA");
        Directory.CreateDirectory(sorted);
        await File.WriteAllTextAsync(Path.Combine(sorted, "Metroid Fusion (USA).srm"), "gba save");
        await File.WriteAllTextAsync(Path.Combine(sorted, "Wario Land 3 (World) (En,Ja).srm"), "gbc save");

        var gba = SharedCoreProvider("gba", installation, ["Metroid Fusion (USA)"]);
        var gbc = SharedCoreProvider("gbc", installation, ["Wario Land 3 (World) (En,Ja)"]);

        Assert.Equal(
            ["gba/Metroid Fusion (USA).srm"],
            (await gba.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
        Assert.Equal(
            ["gbc/Wario Land 3 (World) (En,Ja).srm"],
            (await gbc.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
        // Not exclusive, so a cross-system download of the other system's save is refused too.
        Assert.NotNull(gba.ResolveUnit("gba/Metroid Fusion (USA).srm"));
        Assert.Null(gba.ResolveUnit("gba/Wario Land 3 (World) (En,Ja).srm"));
    }

    [Fact]
    public void StateOwnershipFiltersByLibraryOnlyForASharedCore()
    {
        var installation = Path.Combine(BaseDirectory, "RetroArch-shared-states");

        var shared = SharedCoreProvider("gba", installation, ["Metroid Fusion (USA)"]);
        Assert.True(shared.StateBelongsToThisSystem("/x/Metroid Fusion (USA).state"));
        Assert.True(shared.StateBelongsToThisSystem("/x/Metroid Fusion (USA).state3"));
        Assert.True(shared.StateBelongsToThisSystem("/x/Metroid Fusion (USA).state.auto"));
        Assert.False(shared.StateBelongsToThisSystem("/x/Wario Land 3 (World) (En,Ja).state"));

        // An unshared core owns its whole state folder, unchanged.
        var unshared = new RetroArchSaveLocationProvider(
            "gba", "mgba_libretro.dll", installation,
            homeDirectory: Path.Combine(installation, "unused-home"), isWindows: true, isMacOS: false,
            gameFileNames: () => ["Metroid Fusion (USA)"], coreSharedAcrossSystems: false);
        Assert.True(unshared.StateBelongsToThisSystem("/x/Wario Land 3 (World) (En,Ja).state"));
    }

    [Fact]
    public async Task ACoreOverrideRelocatesOnlyThatCoresSaves()
    {
        var installation = Path.Combine(BaseDirectory, "RetroArch-override");
        WriteConfig(
            installation,
            savefileDirectory: ":\\saves",
            extra: ["rgui_config_directory = \":\\config\""]);
        var overrideDirectory = Path.Combine(installation, "config", "Genesis Plus GX");
        Directory.CreateDirectory(overrideDirectory);
        var relocated = Path.Combine(BaseDirectory, "md-saves");
        await File.WriteAllLinesAsync(
            Path.Combine(overrideDirectory, "Genesis Plus GX.cfg"),
            [$"savefile_directory = \"{relocated.Replace('\\', '/')}\""]);
        Directory.CreateDirectory(relocated);
        await File.WriteAllTextAsync(Path.Combine(relocated, "Sonic (USA).srm"), "md save");

        Assert.Equal(
            relocated,
            await CreateProvider("megadrive", "genesis_plus_gx_libretro.dll", installation).GetSaveDirectoryAsync());
        Assert.Equal(
            Path.Combine(installation, "saves"),
            await CreateProvider("snes", "snes9x_libretro.dll", installation).GetSaveDirectoryAsync());
        Assert.False(
            (await CreateProvider("megadrive", "genesis_plus_gx_libretro.dll", installation).GetSaveInfoAsync())
            .HasUnreadPerGameOverride);

        // A per-game override beside it moves one game's saves somewhere EmuShelf cannot enumerate,
        // so the resolved folder stays the same and the situation is reported rather than hidden.
        await File.WriteAllLinesAsync(
            Path.Combine(overrideDirectory, "Sonic (USA).cfg"),
            ["savefile_directory = \":\\sonic-saves\""]);

        var info = await CreateProvider("megadrive", "genesis_plus_gx_libretro.dll", installation)
            .GetSaveInfoAsync();

        Assert.Equal(relocated, info.SaveDirectory);
        Assert.True(info.HasUnreadPerGameOverride);
    }

    [Fact]
    public async Task LinuxMacAndFlatpakReadTheirOwnConfigurationLocations()
    {
        var home = Path.Combine(BaseDirectory, "ra-home");
        var xdg = Path.Combine(BaseDirectory, "ra-xdg");
        var installation = Path.Combine(BaseDirectory, "ra-install");
        var linuxSaves = Path.Combine(BaseDirectory, "linux-saves");
        var macSaves = Path.Combine(BaseDirectory, "mac-saves");
        var flatpakSaves = Path.Combine(BaseDirectory, "flatpak-saves");
        WriteConfigAt(Path.Combine(xdg, "retroarch"), linuxSaves);
        WriteConfigAt(Path.Combine(home, "Library", "Application Support", "RetroArch", "config"), macSaves);
        WriteConfigAt(
            Path.Combine(home, ".var", "app", "org.libretro.RetroArch", "config", "retroarch"), flatpakSaves);

        Assert.Equal(linuxSaves, await new RetroArchSaveLocationProvider(
            "snes", "snes9x_libretro.so", installation, homeDirectory: home, xdgConfigHome: xdg,
            isWindows: false, isMacOS: false).GetSaveDirectoryAsync());
        Assert.Equal(macSaves, await new RetroArchSaveLocationProvider(
            "snes", "snes9x_libretro.dylib", installation, homeDirectory: home,
            isWindows: false, isMacOS: true).GetSaveDirectoryAsync());
        Assert.Equal(flatpakSaves, await new RetroArchSaveLocationProvider(
            "snes", "snes9x_libretro.so", installation, homeDirectory: home,
            isWindows: false, isMacOS: false, isFlatpak: true).GetSaveDirectoryAsync());
    }

    [Fact]
    public async Task AnyCoreSyncsWhileTheSaveNameIdentifiesTheGame()
    {
        // Changing core changes the save extension (.srm → .dsv here) but not the name RetroArch
        // derives it from, so a core swap must not silently stop syncing.
        var installation = Path.Combine(BaseDirectory, "RetroArch-any-core");
        WriteConfig(installation, savefileDirectory: ":\\saves");
        var saves = Path.Combine(installation, "saves");
        Directory.CreateDirectory(saves);
        await File.WriteAllTextAsync(Path.Combine(saves, "Contra 4 (USA).dsv"), "desmume save");
        await File.WriteAllTextAsync(Path.Combine(saves, "Contra 4 (USA).srm"), "melonds save");
        await File.WriteAllTextAsync(Path.Combine(saves, "Contra 4 (USA).state"), "save state");
        await File.WriteAllTextAsync(Path.Combine(saves, "Contra 4 (USA).state3"), "save state");
        await File.WriteAllTextAsync(Path.Combine(saves, "Contra 4 (USA).state.auto"), "save state");
        await File.WriteAllTextAsync(Path.Combine(saves, "Contra 4 (USA).png"), "screenshot");

        var provider = CreateProvider(
            "nds", "desmume_libretro.dll", installation, gameFileNames: ["Contra 4 (USA)"]);

        Assert.Equal(
            ["nds/Contra 4 (USA).dsv", "nds/Contra 4 (USA).srm"],
            (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
    }

    [Fact]
    public async Task TheApplicationDirectoryPrefixNeverResolvesIntoAnUnrelatedInstallationFolder()
    {
        // A Linux/Flatpak configuration lives away from the executable, and EmuShelf's "installation
        // directory" for a Flatpak is its own portable folder. A ":" there must anchor on the
        // configuration that used it, not on a directory RetroArch knows nothing about.
        var home = Path.Combine(BaseDirectory, "prefix-home");
        var unrelated = Path.Combine(BaseDirectory, "emushelf-portable-base");
        Directory.CreateDirectory(unrelated);
        var configDirectory = Path.Combine(
            home, ".var", "app", "org.libretro.RetroArch", "config", "retroarch");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllLinesAsync(
            Path.Combine(configDirectory, "retroarch.cfg"),
            ["savefile_directory = \":\\saves\""]);

        var provider = new RetroArchSaveLocationProvider(
            "snes", "snes9x_libretro.so", unrelated, homeDirectory: home,
            isWindows: false, isMacOS: false, isFlatpak: true);

        Assert.Equal(Path.Combine(configDirectory, "saves"), await provider.GetSaveDirectoryAsync());
    }

    [Fact]
    public async Task TheSameGameSavedUnderTwoExtensionsIsReportedRatherThanPickedBetween()
    {
        // Real case: one machine's melonDS DS writes .sav, another's writes .srm, so syncing brings
        // both into the same folder. Both are kept — neither is EmuShelf's to discard — but the
        // emulator loads only one, so the ambiguity has to be visible.
        var installation = Path.Combine(BaseDirectory, "RetroArch-ambiguous");
        WriteConfig(installation, savefileDirectory: ":\\saves", extra: ["sort_savefiles_enable = \"true\""]);
        var sorted = Path.Combine(installation, "saves", "melonDS DS");
        Directory.CreateDirectory(sorted);
        await File.WriteAllTextAsync(Path.Combine(sorted, "Contra 4 (USA).sav"), "windows save");
        await File.WriteAllTextAsync(Path.Combine(sorted, "Contra 4 (USA).srm"), "deck save");
        await File.WriteAllTextAsync(Path.Combine(sorted, "Tetris DS (USA).srm"), "only one");

        var provider = CreateProvider("nds", "melondsds_libretro.dll", installation);

        Assert.Equal(3, (await provider.GetSaveUnitsAsync()).Count);
        Assert.Equal(["Contra 4 (USA)"], await provider.GetAmbiguousSaveNamesAsync());
    }

    [Fact]
    public async Task NoConfiguredCoreFailsClosed()
    {
        var installation = Path.Combine(BaseDirectory, "RetroArch-no-core");
        WriteConfig(installation, savefileDirectory: ":\\saves");

        var exception = await Assert.ThrowsAsync<RetroArchConfigurationFormatException>(
            () => CreateProvider("gba", corePath: null, installation).GetSaveUnitsAsync());

        Assert.IsAssignableFrom<SaveProviderConfigurationException>(exception);
    }

    [Fact]
    public async Task SortingUsesTheCoresOwnInfoEntryAndFailsClosedWithoutOne()
    {
        var installation = Path.Combine(BaseDirectory, "RetroArch-info");
        WriteConfig(installation, savefileDirectory: ":\\saves", extra: ["sort_savefiles_enable = \"true\""]);
        Directory.CreateDirectory(Path.Combine(installation, "info"));
        await File.WriteAllLinesAsync(
            Path.Combine(installation, "info", "picodrive_libretro.info"),
            ["display_name = \"Sega - MD (PicoDrive)\"", "corename = \"PicoDrive\"", "categories = \"Emulator\""]);

        Assert.Equal(
            Path.Combine(installation, "saves", "PicoDrive"),
            await CreateProvider("megadrive", "picodrive_libretro.dll", installation).GetSaveDirectoryAsync());

        var exception = await Assert.ThrowsAsync<RetroArchConfigurationFormatException>(
            () => CreateProvider("megadrive", "unknown_core_libretro.dll", installation).GetSaveDirectoryAsync());

        Assert.Contains("unknown_core_libretro.dll", exception.Message);
    }

    [Fact]
    public async Task SortedSaveStatesReadTheCoreNameFromAFlatpakInfoEntry()
    {
        // The Steam Deck report: a Flatpak RetroArch keeps its info files under the user profile,
        // not beside EmuShelf's portable folder. When save states sort by core the folder is named
        // after the core, so that info entry must be read — a core absent from the built-in fallback
        // (Mesen-S here) otherwise loses its state folder and Settings reports it as unavailable.
        var home = Path.Combine(BaseDirectory, "deck-home");
        var installation = Path.Combine(BaseDirectory, "emushelf-portable-base");
        Directory.CreateDirectory(installation);
        var configDirectory = Path.Combine(
            home, ".var", "app", "org.libretro.RetroArch", "config", "retroarch");
        var states = Path.Combine(BaseDirectory, "deck-states");
        Directory.CreateDirectory(configDirectory);
        await File.WriteAllLinesAsync(
            Path.Combine(configDirectory, "retroarch.cfg"),
            [
                $"savefile_directory = \"{Path.Combine(BaseDirectory, "deck-saves").Replace('\\', '/')}\"",
                $"savestate_directory = \"{states.Replace('\\', '/')}\"",
                "sort_savestates_enable = \"true\"",
            ]);
        var infoDirectory = Path.Combine(configDirectory, "info");
        Directory.CreateDirectory(infoDirectory);
        await File.WriteAllLinesAsync(
            Path.Combine(infoDirectory, "mesen-s_libretro.info"),
            ["display_name = \"Nintendo - SNES (Mesen-S)\"", "corename = \"Mesen-S\""]);

        var provider = new RetroArchSaveLocationProvider(
            "snes", "mesen-s_libretro.so", installation, homeDirectory: home,
            isWindows: false, isMacOS: false, isFlatpak: true);

        Assert.Equal(Path.Combine(states, "Mesen-S"), (await provider.GetContentDirectoriesAsync()).SaveStates);
    }

    [Fact]
    public async Task BsnesSortsSavesAndSaveStatesIntoItsOwnFolderThroughTheBuiltInFallback()
    {
        // bsnes is a common SNES core that was missing from the fallback name table, so with no
        // info file to read, its sorted save and save-state folders could not be named — base
        // saves failed closed and save states came back unavailable. Both now resolve like any
        // other core.
        var installation = Path.Combine(BaseDirectory, "RetroArch-bsnes");
        WriteConfig(
            installation,
            savefileDirectory: ":\\saves",
            extra: ["sort_savefiles_enable = \"true\"", "sort_savestates_enable = \"true\""]);

        var provider = CreateProvider("snes", "bsnes_libretro.dll", installation);

        Assert.Equal(Path.Combine(installation, "saves", "bsnes"), await provider.GetSaveDirectoryAsync());
        Assert.Equal(
            Path.Combine(installation, "states", "bsnes"),
            (await provider.GetContentDirectoriesAsync()).SaveStates);
    }

    [Theory]
    [InlineData("cloud_sync_enable = \"true\"", "cloud sync")]
    [InlineData("savefiles_in_content_dir = \"true\"", "next to the game files")]
    [InlineData("sort_savefiles_by_content_enable = \"true\"", "content directory")]
    public async Task LayoutsEmuShelfCannotResolveExactlyFailClosed(string line, string expected)
    {
        var installation = Path.Combine(BaseDirectory, "RetroArch-" + Math.Abs(line.GetHashCode()));
        WriteConfig(installation, savefileDirectory: ":\\saves", extra: [line]);

        var exception = await Assert.ThrowsAsync<RetroArchConfigurationFormatException>(
            () => CreateProvider("snes", "snes9x_libretro.dll", installation).GetSaveUnitsAsync());

        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public async Task AnUnsetSaveDirectoryFailsClosedRatherThanGuessingTheContentFolder()
    {
        var installation = Path.Combine(BaseDirectory, "RetroArch-default");
        WriteConfig(installation, savefileDirectory: "default");

        await Assert.ThrowsAsync<RetroArchConfigurationFormatException>(
            () => CreateProvider("snes", "snes9x_libretro.dll", installation).GetSaveDirectoryAsync());
    }

    [Fact]
    public async Task AMissingConfigurationFailsClosedAndAnOverrideIsUsedDirectly()
    {
        var installation = Path.Combine(BaseDirectory, "RetroArch-missing");
        Directory.CreateDirectory(installation);

        await Assert.ThrowsAsync<RetroArchConfigurationFormatException>(
            () => CreateProvider("snes", "snes9x_libretro.dll", installation).GetSaveDirectoryAsync());

        var chosen = Path.Combine(BaseDirectory, "chosen-saves");
        Directory.CreateDirectory(chosen);
        await File.WriteAllTextAsync(Path.Combine(chosen, "Chrono Trigger (USA).srm"), "snes save");

        var provider = CreateProvider("snes", "snes9x_libretro.dll", installation, directoryOverride: chosen);

        Assert.Equal(chosen, await provider.GetSaveDirectoryAsync());
        Assert.Single(await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public void ResolveUnitRejectsTraversalOtherExtensionsAndAnotherSystemsNamespace()
    {
        var installation = Path.Combine(BaseDirectory, "RetroArch-resolve");
        WriteConfig(installation, savefileDirectory: ":\\saves");
        var provider = CreateProvider(
            "snes", "snes9x_libretro.dll", installation, gameFileNames: ["Chrono Trigger (USA)"]);

        var location = provider.ResolveUnit("snes/Chrono Trigger (USA).srm");

        Assert.NotNull(location);
        Assert.Equal(
            Path.Combine(installation, "saves", "Chrono Trigger (USA).srm"),
            location.Path);
        Assert.Equal(SaveUnitKind.File, location.Kind);
        Assert.Null(provider.ResolveUnit("snes/../states/Chrono Trigger (USA).srm"));
        Assert.Null(provider.ResolveUnit("snes/Chrono Trigger (USA).state"));
        Assert.Null(provider.ResolveUnit("gba/Metroid Fusion (USA).srm"));
        Assert.Null(provider.ResolveUnit("retroarch/Chrono Trigger (USA).srm"));
    }

    [Fact]
    public async Task SaveRoundTripsToASecondMachineByFileName()
    {
        var pathsA = new AppPaths(Path.Combine(BaseDirectory, "ra-machine-a"));
        var pathsB = new AppPaths(Path.Combine(BaseDirectory, "ra-machine-b"));
        pathsA.EnsureDirectoriesExist();
        pathsB.EnsureDirectoriesExist();
        var installationA = Path.Combine(pathsA.BaseDirectory, "RetroArch");
        var installationB = Path.Combine(pathsB.BaseDirectory, "RetroArch");
        WriteConfig(installationA, savefileDirectory: ":\\saves");
        WriteConfig(installationB, savefileDirectory: ":\\saves");
        Directory.CreateDirectory(Path.Combine(installationA, "saves"));
        await File.WriteAllTextAsync(
            Path.Combine(installationA, "saves", "Contra 4 (USA).srm"),
            "ds progress");

        var providerA = CreateProvider(
            "nds", "melondsds_libretro.dll", installationA, gameFileNames: ["Contra 4 (USA)"]);
        var providerB = CreateProvider(
            "nds", "melondsds_libretro.dll", installationB, gameFileNames: ["Contra 4 (USA)"]);
        var remote = new InMemoryCloudSyncTransport();
        var serviceA = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerA, pathsA), remote, new JsonSaveSyncManifestStore(pathsA));
        var serviceB = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerB, pathsB), remote, new JsonSaveSyncManifestStore(pathsB));

        Assert.Equal(1, (await serviceA.SyncAsync(providerA)).Uploaded);
        Assert.Equal(1, (await serviceB.SyncAsync(providerB)).Downloaded);
        Assert.Equal(
            "ds progress",
            await File.ReadAllTextAsync(Path.Combine(installationB, "saves", "Contra 4 (USA).srm")));
    }

    private static RetroArchSaveLocationProvider CreateProvider(
        string systemId,
        string? corePath,
        string installation,
        string? directoryOverride = null,
        IReadOnlyCollection<string>? gameFileNames = null) =>
        new(
            systemId,
            corePath,
            installation,
            directoryOverride: directoryOverride,
            homeDirectory: Path.Combine(installation, "unused-home"),
            isWindows: true,
            isMacOS: false,
            gameFileNames: gameFileNames is null ? null : () => gameFileNames);

    private static RetroArchSaveLocationProvider SharedCoreProvider(
        string systemId,
        string installation,
        IReadOnlyCollection<string> gameFileNames) =>
        new(
            systemId,
            "mgba_libretro.dll",
            installation,
            homeDirectory: Path.Combine(installation, "unused-home"),
            isWindows: true,
            isMacOS: false,
            gameFileNames: () => gameFileNames,
            coreSharedAcrossSystems: true);

    private static void WriteConfig(
        string installationDirectory,
        string savefileDirectory,
        IEnumerable<string>? extra = null)
    {
        Directory.CreateDirectory(installationDirectory);
        var lines = new List<string>
        {
            "# RetroArch configuration",
            "cloud_sync_enable = \"false\"",
            "cloud_sync_sync_saves = \"true\"",
            $"savefile_directory = \"{savefileDirectory}\"",
            "savefiles_in_content_dir = \"false\"",
            "savestate_directory = \":\\states\"",
            "sort_savefiles_by_content_enable = \"false\"",
            "sort_savefiles_enable = \"false\"",
        };
        lines.AddRange(extra ?? []);
        File.WriteAllLines(Path.Combine(installationDirectory, "retroarch.cfg"), lines);
    }

    private static void WriteConfigAt(string configDirectory, string savefileDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        Directory.CreateDirectory(savefileDirectory);
        File.WriteAllLines(
            Path.Combine(configDirectory, "retroarch.cfg"),
            [$"savefile_directory = \"{savefileDirectory.Replace('\\', '/')}\""]);
    }
}
