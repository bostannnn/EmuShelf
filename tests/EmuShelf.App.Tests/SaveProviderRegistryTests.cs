using EmuShelf.App.Services;
using EmuShelf.Core.Storage;
using EmuShelf.Integrations.Emulators.DuckStation;
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
        Assert.Single(SaveProviderRegistry.All, descriptor => descriptor.SystemId == "playstation");
        var descriptor = SaveProviderRegistry.Find("playstation");
        Assert.NotNull(descriptor);

        var duckStation = descriptor!.CreateProvider(new SaveProviderContext(
            DirectoryOverride: null,
            EmulatorDirectory: "/emu/duckstation",
            IsFlatpak: false,
            Paths: new StubPaths(),
            ActiveEmulatorId: "duckstation"));
        Assert.IsType<DuckStationSaveLocationProvider>(duckStation);

        var retroArch = descriptor.CreateProvider(new SaveProviderContext(
            DirectoryOverride: null,
            EmulatorDirectory: "/emu/retroarch",
            IsFlatpak: false,
            Paths: new StubPaths(),
            CorePath: "/emu/retroarch/cores/swanstation_libretro.dll",
            ActiveEmulatorId: "retroarch"));
        Assert.IsType<RetroArchSaveLocationProvider>(retroArch);
        Assert.Equal("playstation", ((RetroArchSaveLocationProvider)retroArch!).SystemId);
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
