using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Emulators.Rpcs3;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class Rpcs3SaveSyncTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task Windows_ReadsVfsFromTheConfigSubdirectoryBesideTheExecutable()
    {
        var installation = Path.Combine(BaseDirectory, "RPCS3");
        WriteVfs(installation, isWindows: true);
        var saveData = WriteProfile(Path.Combine(installation, "dev_hdd0"), "00000001", "Andoryu");
        WriteSave(saveData, "BCES00001_PROFILE");
        WriteSave(saveData, "BLES00713-DI1-EN-7508082248580");
        Directory.CreateDirectory(Path.Combine(saveData, "PARTIAL_COPY"));

        var provider = CreateProvider(installation, isWindows: true);
        var info = await provider.GetSaveDataInfoAsync();

        Assert.Equal(saveData, info.SaveDataDirectory);
        Assert.Equal("00000001", info.Profile.Id);
        Assert.Equal("Andoryu", info.Profile.Name);
        Assert.Equal(
            [
                new SaveUnit("playstation3/savedata/BCES00001_PROFILE", "BCES00001_PROFILE", SaveUnitKind.Folder),
                new SaveUnit(
                    "playstation3/savedata/BLES00713-DI1-EN-7508082248580",
                    "BLES00713-DI1-EN-7508082248580",
                    SaveUnitKind.Folder),
            ],
            await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task EnumeratesTrophySetsAndVirtualMemoryCardsBesideTheAccountSaves()
    {
        var installation = Path.Combine(BaseDirectory, "RPCS3-trophies");
        WriteVfs(installation, isWindows: true);
        var hdd0 = Path.Combine(installation, "dev_hdd0");
        var saveData = WriteProfile(hdd0, "00000001", "Andoryu");
        WriteSave(saveData, "BCES00006");
        WriteTrophySet(hdd0, "00000001", "NPWR00706_00");
        Directory.CreateDirectory(Path.Combine(hdd0, "home", "00000001", "trophy", "NPWR99999_00"));
        WriteVirtualMemoryCard(hdd0, "SLUS00067_mc1.VM1");
        WriteVirtualMemoryCard(hdd0, "SCES00001_mc1.VM2");
        File.WriteAllText(Path.Combine(hdd0, "savedata", "vmc", "notes.txt"), "not a card");

        var provider = CreateProvider(installation, isWindows: true);
        var info = await provider.GetSaveDataInfoAsync();

        Assert.Equal(Path.Combine(hdd0, "home", "00000001", "trophy"), info.TrophyDirectory);
        Assert.Equal(Path.Combine(hdd0, "savedata", "vmc"), info.VirtualMemoryCardDirectory);
        Assert.Equal(
            [
                "playstation3/savedata/BCES00006",
                "playstation3/trophy/NPWR00706_00",
                "playstation3/vmc/SCES00001_mc1.VM2",
                "playstation3/vmc/SLUS00067_mc1.VM1",
            ],
            (await provider.GetSaveUnitsAsync()).Select(unit => unit.UnitId));
        Assert.Equal(
            SaveUnitKind.File,
            provider.ResolveUnit("playstation3/vmc/SLUS00067_mc1.VM1")!.Kind);
        Assert.Equal(
            Path.Combine(hdd0, "home", "00000001", "trophy", "NPWR00706_00"),
            provider.ResolveUnit("playstation3/trophy/NPWR00706_00")!.Path);
        Assert.Null(provider.ResolveUnit("playstation3/vmc/notes.txt"));
    }

    [Fact]
    public async Task TrophiesFollowTheChosenAccountAndCardsStayConsoleWide()
    {
        var installation = Path.Combine(BaseDirectory, "RPCS3-bound-extras");
        WriteVfs(installation, isWindows: true);
        var hdd0 = Path.Combine(installation, "dev_hdd0");
        WriteProfile(hdd0, "00000001");
        WriteProfile(hdd0, "00000002", "Guest");
        WriteTrophySet(hdd0, "00000002", "NPWR00706_00");
        WriteVirtualMemoryCard(hdd0, "SLUS00067_mc1.VM1");

        var info = await CreateProvider(
                installation,
                isWindows: true,
                directoryOverride: Path.Combine(hdd0, "home", "00000002"))
            .GetSaveDataInfoAsync();

        Assert.Equal(Path.Combine(hdd0, "home", "00000002", "trophy"), info.TrophyDirectory);
        Assert.Equal(Path.Combine(hdd0, "savedata", "vmc"), info.VirtualMemoryCardDirectory);

        // A folder chosen outside RPCS3's own shape keeps the console-wide directory out of scope.
        var detached = Path.Combine(BaseDirectory, "detached", "savedata");
        Directory.CreateDirectory(detached);
        var detachedInfo = await CreateProvider(installation, isWindows: true, directoryOverride: detached)
            .GetSaveDataInfoAsync();

        Assert.Null(detachedInfo.VirtualMemoryCardDirectory);
        Assert.Null(detachedInfo.Profile.Name);
    }

    [Fact]
    public async Task PortableDirectoryBesideTheExecutableWinsOnEveryPlatform()
    {
        var installation = Path.Combine(BaseDirectory, "RPCS3-portable");
        var portable = Path.Combine(installation, "portable");
        WriteVfs(portable, isWindows: false);
        var portableSaves = WriteProfile(Path.Combine(portable, "dev_hdd0"), "00000001");
        WriteSave(portableSaves, "BCES00006");
        var home = Path.Combine(BaseDirectory, "home");
        WriteVfs(Path.Combine(home, ".config", "rpcs3"), isWindows: false);
        WriteProfile(Path.Combine(home, ".config", "rpcs3", "dev_hdd0"), "00000001");

        var provider = new Rpcs3SaveLocationProvider(
            installation,
            homeDirectory: home,
            isWindows: false,
            isMacOS: false);

        Assert.Equal(portableSaves, await provider.GetSaveDataDirectoryAsync());
        Assert.Single(await provider.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task VfsRelocatesTheHardDiskThroughBothTheEmulatorDirectoryAndAnAbsolutePath()
    {
        var installation = Path.Combine(BaseDirectory, "RPCS3-relocated");
        var emulatorDirectory = Path.Combine(BaseDirectory, "rpcs3-data");
        Directory.CreateDirectory(Path.Combine(installation, "config"));
        await File.WriteAllLinesAsync(
            Path.Combine(installation, "config", "vfs.yml"),
            [
                $"$(EmulatorDir): {emulatorDirectory.Replace('\\', '/')}/",
                "/dev_hdd0/: $(EmulatorDir)hdd0/",
                "/dev_hdd1/: $(EmulatorDir)dev_hdd1/",
                "/dev_usb***/:",
                "  /dev_usb000:",
                "    Path: $(EmulatorDir)dev_usb000/",
            ]);
        var relocated = WriteProfile(Path.Combine(emulatorDirectory, "hdd0"), "00000001");
        WriteSave(relocated, "BCUS98229_GOW1");

        Assert.Equal(
            relocated,
            await CreateProvider(installation, isWindows: true).GetSaveDataDirectoryAsync());

        var absoluteHdd = Path.Combine(BaseDirectory, "external-hdd0");
        await File.WriteAllLinesAsync(
            Path.Combine(installation, "config", "vfs.yml"),
            ["$(EmulatorDir): \"\"", $"/dev_hdd0/: {absoluteHdd.Replace('\\', '/')}/"]);
        var external = WriteProfile(absoluteHdd, "00000001");

        Assert.Equal(
            external,
            await CreateProvider(installation, isWindows: true).GetSaveDataDirectoryAsync());
    }

    [Fact]
    public async Task LinuxMacAndFlatpakUseTheirOwnConfigurationRoots()
    {
        var home = Path.Combine(BaseDirectory, "platform-home");
        var xdg = Path.Combine(BaseDirectory, "xdg-config");
        var linux = WriteProfile(Path.Combine(xdg, "rpcs3", "dev_hdd0"), "00000001");
        var mac = WriteProfile(
            Path.Combine(home, "Library", "Application Support", "rpcs3", "dev_hdd0"), "00000001");
        var flatpak = WriteProfile(
            Path.Combine(home, ".var", "app", "net.rpcs3.RPCS3", "config", "rpcs3", "dev_hdd0"), "00000001");
        var installation = Path.Combine(BaseDirectory, "install");

        Assert.Equal(linux, await new Rpcs3SaveLocationProvider(
            installation, homeDirectory: home, xdgConfigHome: xdg, isWindows: false, isMacOS: false)
            .GetSaveDataDirectoryAsync());
        Assert.Equal(mac, await new Rpcs3SaveLocationProvider(
            installation, homeDirectory: home, isWindows: false, isMacOS: true)
            .GetSaveDataDirectoryAsync());
        Assert.Equal(flatpak, await new Rpcs3SaveLocationProvider(
            installation, homeDirectory: home, isWindows: false, isMacOS: false, isFlatpak: true)
            .GetSaveDataDirectoryAsync());
    }

    [Fact]
    public async Task WindowsHonorsRpcs3ConfigDirBeforeTheExecutableDirectory()
    {
        var installation = Path.Combine(BaseDirectory, "RPCS3-env");
        WriteProfile(Path.Combine(installation, "dev_hdd0"), "00000001");
        var configured = Path.Combine(BaseDirectory, "rpcs3-config-dir");
        var expected = WriteProfile(Path.Combine(configured, "dev_hdd0"), "00000001");

        var provider = new Rpcs3SaveLocationProvider(
            installation,
            configDirectoryEnvironmentOverride: configured,
            isWindows: true,
            isMacOS: false);

        Assert.Equal(expected, await provider.GetSaveDataDirectoryAsync());
    }

    [Fact]
    public async Task SeveralAccountsWithSavesFailClosedUntilOneIsChosen()
    {
        var installation = Path.Combine(BaseDirectory, "RPCS3-accounts");
        WriteVfs(installation, isWindows: true);
        var hdd0 = Path.Combine(installation, "dev_hdd0");
        var first = WriteProfile(hdd0, "00000001", "Andoryu");
        var second = WriteProfile(hdd0, "00000002", "Guest");
        WriteSave(first, "BCES00006");
        WriteSave(second, "BLES00144-PROF-");

        var exception = await Assert.ThrowsAsync<Rpcs3ConfigurationFormatException>(
            () => CreateProvider(installation, isWindows: true).GetSaveUnitsAsync());
        Assert.IsAssignableFrom<SaveProviderConfigurationException>(exception);

        // Pointing the override at one account folder resolves the ambiguity without touching RPCS3.
        var bound = CreateProvider(
            installation,
            isWindows: true,
            directoryOverride: Path.Combine(hdd0, "home", "00000002"));
        var info = await bound.GetSaveDataInfoAsync();

        Assert.Equal(second, info.SaveDataDirectory);
        Assert.Equal("00000002", info.Profile.Id);
        Assert.Equal("Guest", info.Profile.Name);
        Assert.Equal(
            [new SaveUnit("playstation3/savedata/BLES00144-PROF-", "BLES00144-PROF-", SaveUnitKind.Folder)],
            await bound.GetSaveUnitsAsync());
    }

    [Fact]
    public async Task TheOnlyAccountHoldingSavesIsBoundWithoutAnOverride()
    {
        var installation = Path.Combine(BaseDirectory, "RPCS3-single-populated");
        WriteVfs(installation, isWindows: true);
        var hdd0 = Path.Combine(installation, "dev_hdd0");
        WriteProfile(hdd0, "00000001");
        var populated = WriteProfile(hdd0, "00000002", "Andoryu");
        WriteSave(populated, "BCES00081-KILLZONE2");

        var info = await CreateProvider(installation, isWindows: true).GetSaveDataInfoAsync();

        Assert.Equal(populated, info.SaveDataDirectory);
        Assert.Equal("00000002", info.Profile.Id);
        Assert.Equal(2, info.AvailableProfiles.Count);
    }

    [Fact]
    public async Task OverrideAcceptsTheHardDiskAndSaveDataFoldersAndRejectsAnUnrelatedFolder()
    {
        var installation = Path.Combine(BaseDirectory, "RPCS3-override");
        WriteVfs(installation, isWindows: true);
        var hdd0 = Path.Combine(installation, "dev_hdd0");
        var saveData = WriteProfile(hdd0, "00000001");
        WriteSave(saveData, "BCES00129");

        // dev_hdd0 also holds its own savedata/vmc directory; the account's savedata must win.
        Assert.Equal(
            saveData,
            await CreateProvider(installation, isWindows: true, directoryOverride: hdd0)
                .GetSaveDataDirectoryAsync());
        Assert.Equal(
            saveData,
            await CreateProvider(installation, isWindows: true, directoryOverride: saveData)
                .GetSaveDataDirectoryAsync());
        Assert.Equal(
            saveData,
            await CreateProvider(
                    installation,
                    isWindows: true,
                    directoryOverride: Path.Combine(hdd0, "home", "00000001"))
                .GetSaveDataDirectoryAsync());
        Assert.Equal(
            [new SaveUnit("playstation3/savedata/BCES00129", "BCES00129", SaveUnitKind.Folder)],
            await CreateProvider(installation, isWindows: true, directoryOverride: hdd0).GetSaveUnitsAsync());

        var unrelated = Path.Combine(BaseDirectory, "not-rpcs3");
        Directory.CreateDirectory(unrelated);

        await Assert.ThrowsAsync<Rpcs3ConfigurationFormatException>(
            () => CreateProvider(installation, isWindows: true, directoryOverride: unrelated)
                .GetSaveDataDirectoryAsync());
    }

    [Fact]
    public async Task UnsupportedVfsPlaceholderFailsClosed()
    {
        var installation = Path.Combine(BaseDirectory, "RPCS3-placeholder");
        Directory.CreateDirectory(Path.Combine(installation, "config"));
        await File.WriteAllLinesAsync(
            Path.Combine(installation, "config", "vfs.yml"),
            ["$(EmulatorDir): \"\"", "/dev_hdd0/: $(FutureMacro)dev_hdd0/"]);

        await Assert.ThrowsAsync<Rpcs3ConfigurationFormatException>(
            () => CreateProvider(installation, isWindows: true).GetSaveUnitsAsync());
    }

    [Fact]
    public void ResolveUnitAllowsARemoteOnlySaveAndRejectsUnsafeOrForeignIds()
    {
        var installation = Path.Combine(BaseDirectory, "RPCS3-resolve");
        WriteVfs(installation, isWindows: true);
        var saveData = WriteProfile(Path.Combine(installation, "dev_hdd0"), "00000001");
        var provider = CreateProvider(installation, isWindows: true);

        var location = provider.ResolveUnit("playstation3/savedata/BCES00484");

        Assert.NotNull(location);
        Assert.Equal(Path.Combine(saveData, "BCES00484"), location.Path);
        Assert.Equal(saveData, location.RootPath);
        Assert.Equal(SaveUnitKind.Folder, location.Kind);
        Assert.Null(provider.ResolveUnit("playstation3/savedata/../trophy"));
        Assert.Null(provider.ResolveUnit("playstation3/savedata/sub/dir"));
        Assert.Null(provider.ResolveUnit("playstation3/exdata/act.dat"));
        Assert.Null(provider.ResolveUnit("playstation3/BCES00484"));
        Assert.Null(provider.ResolveUnit("psp/BCES00484"));

        // Each namespace resolves under its own root, never into a sibling of savedata.
        var trophy = provider.ResolveUnit("playstation3/trophy/NPWR00706_00");
        Assert.Equal(Path.Combine(Path.GetDirectoryName(saveData)!, "trophy"), trophy!.RootPath);
        Assert.Null(provider.ResolveUnit("playstation3/vmc/NPWR00706_00"));
    }

    [Fact]
    public async Task SaveRoundTripsBetweenMachinesWhoseLocalAccountIdsDiffer()
    {
        var pathsA = new AppPaths(Path.Combine(BaseDirectory, "machine-a"));
        var pathsB = new AppPaths(Path.Combine(BaseDirectory, "machine-b"));
        pathsA.EnsureDirectoriesExist();
        pathsB.EnsureDirectoriesExist();
        var installationA = Path.Combine(pathsA.BaseDirectory, "RPCS3");
        var installationB = Path.Combine(pathsB.BaseDirectory, "RPCS3");
        WriteVfs(installationA, isWindows: true);
        WriteVfs(installationB, isWindows: true);
        var savesA = WriteProfile(Path.Combine(installationA, "dev_hdd0"), "00000001", "Andoryu");
        var savesB = WriteProfile(Path.Combine(installationB, "dev_hdd0"), "00000007", "Deck");
        WriteSave(savesA, "BCES00294SIREN-DATA", "in-game progress");

        var providerA = CreateProvider(installationA, isWindows: true);
        var providerB = CreateProvider(installationB, isWindows: true);
        var remote = new InMemoryCloudSyncTransport();
        var serviceA = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerA, pathsA),
            remote,
            new JsonSaveSyncManifestStore(pathsA));
        var serviceB = new SaveSyncService(
            new FileSystemLocalSaveEndpoint(providerB, pathsB),
            remote,
            new JsonSaveSyncManifestStore(pathsB));

        Assert.Equal(1, (await serviceA.SyncAsync(providerA)).Uploaded);
        Assert.Equal(1, (await serviceB.SyncAsync(providerB)).Downloaded);
        Assert.Equal(
            "in-game progress",
            await File.ReadAllTextAsync(
                Path.Combine(savesB, "BCES00294SIREN-DATA", "SAVEDATA.BIN")));
        Assert.True(File.Exists(Path.Combine(savesB, "BCES00294SIREN-DATA", "PARAM.SFO")));
    }

    private static Rpcs3SaveLocationProvider CreateProvider(
        string installation,
        bool isWindows,
        string? directoryOverride = null) =>
        new(
            installation,
            directoryOverride: directoryOverride,
            homeDirectory: Path.Combine(installation, "unused-home"),
            isWindows: isWindows,
            isMacOS: false);

    private static void WriteVfs(string configurationDirectory, bool isWindows)
    {
        var directory = isWindows ? Path.Combine(configurationDirectory, "config") : configurationDirectory;
        Directory.CreateDirectory(directory);
        File.WriteAllLines(
            Path.Combine(directory, "vfs.yml"),
            [
                "$(EmulatorDir): \"\"",
                "/dev_hdd0/: $(EmulatorDir)dev_hdd0/",
                "/dev_hdd1/: $(EmulatorDir)dev_hdd1/",
                "/games/: \"\"",
            ]);
    }

    private static string WriteProfile(string hddDirectory, string profileId, string? userName = null)
    {
        var profile = Path.Combine(hddDirectory, "home", profileId);
        var saveData = Path.Combine(profile, "savedata");
        Directory.CreateDirectory(saveData);

        // A real dev_hdd0 has its own savedata directory next to home/, holding the PS1/PS2
        // Classics virtual memory cards. Every fixture carries it so no resolution step may
        // mistake it for an account's save directory.
        Directory.CreateDirectory(Path.Combine(hddDirectory, "savedata", "vmc"));
        Directory.CreateDirectory(Path.Combine(hddDirectory, "game", "BCES00129", "USRDIR"));
        Directory.CreateDirectory(Path.Combine(profile, "trophy", "NPWR00001_00"));
        if (userName is not null)
            File.WriteAllText(Path.Combine(profile, "localusername"), userName);
        return saveData;
    }

    private static void WriteTrophySet(string hddDirectory, string profileId, string communicationId)
    {
        var directory = Path.Combine(hddDirectory, "home", profileId, "trophy", communicationId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "TROPUSR.DAT"), "unlocked");
        File.WriteAllText(Path.Combine(directory, "TROPCONF.SFM"), "conf");
    }

    private static void WriteVirtualMemoryCard(string hddDirectory, string cardName)
    {
        var directory = Path.Combine(hddDirectory, "savedata", "vmc");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, cardName), "card");
    }

    private static void WriteSave(string saveDataDirectory, string saveName, string contents = "save")
    {
        var directory = Path.Combine(saveDataDirectory, saveName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "PARAM.SFO"), "sfo");
        File.WriteAllText(Path.Combine(directory, "SAVEDATA.BIN"), contents);
    }
}
