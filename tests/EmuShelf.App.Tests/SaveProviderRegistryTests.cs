using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.Core.Storage;
using EmuShelf.Core.Storage.Android;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.Android;
using EmuShelf.Integrations.Emulators.RetroArch;

namespace EmuShelf.App.Tests;

public class SaveProviderRegistryTests
{
    [Fact]
    public void Arcade_SyncsBatteryAndStateSavesLikeTheOtherRetroArchPlatforms()
    {
        var arcade = SaveProviderRegistry.Find("arcade");

        Assert.NotNull(arcade);
        Assert.Equal("Arcade", arcade!.DisplayName);
        // Parity with the other RetroArch platforms: battery/NVRAM saves plus guarded save states.
        Assert.True(arcade.SupportsSaveStates);
        Assert.Contains("arcade", SaveProviderRegistry.SystemIds);
    }

    [Fact]
    public void PlayStation_HasOneRowButFollowsTheActiveEmulatorProfile()
    {
        // One PlayStation row (so the Saves section stays one-per-system), whose provider is chosen by
        // the active emulator profile: DuckStation by default, RetroArch when it is made active.
        // One presentation row for PlayStation, but two (system, emulator) profiles behind it.
        Assert.Single(SaveProviderRegistry.All, descriptor => descriptor.SystemId == "playstation");
        Assert.Equal(2, SaveProviderRegistry.Profiles.Count(descriptor => descriptor.SystemId == "playstation"));

        // Resolve picks the profile for the active emulator; each profile builds only its own provider.
        var duckStationProfile = SaveProviderRegistry.Resolve("playstation", "duckstation");
        Assert.NotNull(duckStationProfile);
        Assert.Equal("duckstation", duckStationProfile!.EmulatorId);
        var duckStation = duckStationProfile.CreateProvider(new SaveProviderContext(
            DirectoryOverride: null,
            EmulatorDirectory: "/emu/duckstation",
            IsFlatpak: false,
            Paths: new StubPaths(),
            ActiveEmulatorId: "duckstation"));
        Assert.IsType<DuckStationSaveLocationProvider>(duckStation);

        var retroArchProfile = SaveProviderRegistry.Resolve("playstation", "retroarch");
        Assert.NotNull(retroArchProfile);
        Assert.Equal("retroarch", retroArchProfile!.EmulatorId);
        var retroArch = retroArchProfile.CreateProvider(new SaveProviderContext(
            DirectoryOverride: null,
            EmulatorDirectory: "/emu/retroarch",
            IsFlatpak: false,
            Paths: new StubPaths(),
            CorePath: "/emu/retroarch/cores/swanstation_libretro.dll",
            ActiveEmulatorId: "retroarch"));
        Assert.IsType<RetroArchSaveLocationProvider>(retroArch);
        Assert.Equal("playstation", ((RetroArchSaveLocationProvider)retroArch!).SystemId);

        // With no active emulator the default profile (DuckStation) is used.
        Assert.Equal("duckstation", SaveProviderRegistry.Resolve("playstation", null)!.EmulatorId);
    }

    [Fact]
    public void Dolphin_SyncsSaveStatesFromTheGameCubeRowWhoseLabelNamesWiiToo()
    {
        // Dolphin keeps GameCube and Wii save states in one shared StateSaves folder, wired to sync
        // only from the GameCube provider. The GameCube row therefore owns the toggle for both, and
        // its label says so; the Wii row deliberately has no save-states toggle of its own.
        var gameCube = SaveProviderRegistry.Find("gamecube");
        var wii = SaveProviderRegistry.Find("wii");

        Assert.NotNull(gameCube);
        Assert.NotNull(wii);
        Assert.True(gameCube!.SupportsSaveStates);
        Assert.Equal("Automatically sync save states (GameCube + Wii)", gameCube.SaveStatesLabel);
        Assert.Contains("Wii", gameCube.SaveStatesLabel);

        Assert.False(wii!.SupportsSaveStates);
        Assert.Null(wii.SaveStatesLabel);
    }

    [Theory]
    [InlineData("gamecube")]
    [InlineData("wii")]
    public void AndroidDolphinSystems_ResolveTheSamePackageDerivedFilesRoot(string systemId)
    {
        var installation = AppBootstrapper.ResolveAndroidEmulator(systemId);

        Assert.NotNull(installation);
        Assert.Equal("dolphin", installation!.EmulatorId);
        Assert.False(installation.IsFlatpak);
        Assert.Equal(
            AndroidExternalStorageUri.ExternalAppFilesDirectory(
                AndroidEmulatorLaunchProfiles.Dolphin.PackageName),
            installation.Directory);
    }

    [Fact]
    public void AndroidFolderConfigurableEmulator_StillRequiresAUserOverride()
    {
        Assert.Null(AppBootstrapper.ResolveAndroidEmulator("psp"));
    }

    [Fact]
    public async Task AndroidDolphinProvider_TreatsThePackageFilesRootAsItsUserDirectory()
    {
        // Unix-only: the provider runs the directory through Path.GetFullPath, which on Windows rebases a
        // POSIX "/storage/…" path onto the current drive ("C:\storage\…"), so the exact-path assert can
        // only hold where '/' is the separator. The Android host is always Unix, so this is host-agnostic
        // in production; the test just cannot run on the Windows CI runner. Matches the repo's Unix-only
        // path-test convention (see tests-home-redirect note / TexturePackRegressionTests).
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX Android path; Path.GetFullPath rebases it on Windows.");
        const string filesRoot = "/storage/emulated/0/Android/data/org.dolphinemu.dolphinemu/files";
        var provider = SaveProviderRegistry.CreateDolphinProvider(
            "gamecube",
            new SaveProviderContext(
                DirectoryOverride: null,
                EmulatorDirectory: filesRoot,
                IsFlatpak: false,
                Paths: new StubPaths()),
            isAndroid: true);

        var dolphin = Assert.IsType<EmuShelf.Integrations.Emulators.Dolphin.DolphinSaveLocationProvider>(provider);
        Assert.Equal(
            filesRoot,
            await dolphin.GetUserDirectoryAsync(TestContext.Current.CancellationToken));
    }

    private sealed class StubPaths : IAppPaths
    {
        public string BaseDirectory => "/app";
        public string DataDirectory => "/app/Data";
        public string CoversDirectory => "/app/Covers";
        public string CacheDirectory => "/app/Cache";
        public string LogsDirectory => "/app/Logs";
        public string SettingsDirectory => "/app/Settings";
        public string SavesDirectory => "/app/Saves";
        public string DatabaseFilePath => "/app/Data/library.db";
        public string SettingsFilePath => "/app/Settings/settings.json";

        public void EnsureDirectoriesExist()
        {
        }
    }
}
