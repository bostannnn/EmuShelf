using EmuShelf.App.Services;
using EmuShelf.App.Startup;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Storage;
using EmuShelf.Core.Storage.Android;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.MelonDs;
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
    public void NintendoDs_HasOneRowServedByRetroArchOrEitherMelonDsChannel()
    {
        // One DS row, three (system, emulator) profiles behind it. RetroArch stays the default so an
        // install that never picked an emulator is unaffected; both melonDS channels are selectable
        // and each builds its own provider carrying its own id (their save states never mix).
        Assert.Single(SaveProviderRegistry.All, descriptor => descriptor.SystemId == "nds");
        Assert.Equal(
            ["retroarch", "melonds", "melonds-nightly"],
            SaveProviderRegistry.Profiles
                .Where(descriptor => descriptor.SystemId == "nds")
                .Select(descriptor => descriptor.EmulatorId));
        Assert.Equal("retroarch", SaveProviderRegistry.Resolve("nds", null)!.EmulatorId);

        foreach (var emulatorId in new[] { "melonds", "melonds-nightly" })
        {
            var profile = SaveProviderRegistry.Resolve("nds", emulatorId);
            Assert.NotNull(profile);
            Assert.Equal(emulatorId, profile!.EmulatorId);
            Assert.True(profile.SupportsSaveStates);
            var provider = profile.CreateProvider(new SaveProviderContext(
                DirectoryOverride: null,
                EmulatorDirectory: "/emu/melonDS",
                IsFlatpak: false,
                Paths: new StubPaths(),
                ActiveEmulatorId: emulatorId));
            var melonDs = Assert.IsType<MelonDsSaveLocationProvider>(provider);
            Assert.Equal(emulatorId, melonDs.EmulatorId);
            // Battery saves key by system (shared with RetroArch); save states stay per-channel.
            Assert.Equal("nds/", melonDs.UnitIdPrefix);
            Assert.Equal($"{emulatorId}/nds/", melonDs.StateNamespacePrefix);
        }
    }

    [Theory]
    [InlineData("melonds")]
    [InlineData("melonds-nightly")]
    public void AndroidMelonDs_ResolvesItsOwnProfileSoARestoreLandsOnTheExtensionItReads(string emulatorId)
    {
        // Android melonDS has no package-derived save root (its config is app-private), so its
        // installation carries only the emulator id. That id is load-bearing: without it the active
        // emulator reads as "none", the registry falls back to the system default (RetroArch for DS),
        // and a restored save lands on the .srm a libretro core reads — which standalone melonDS
        // ignores, so the game boots as new.
        var installation = AppBootstrapper.ResolveAndroidEmulator(
            "nds",
            new EmulatorConfiguration("nds", ExecutablePath: null, LaunchArguments: null)
            {
                EmulatorId = emulatorId,
            });

        Assert.NotNull(installation);
        Assert.Equal(emulatorId, installation!.EmulatorId);
        Assert.Null(installation.Directory);
        Assert.Equal(emulatorId, SaveProviderRegistry.Resolve("nds", installation.EmulatorId)!.EmulatorId);
    }

    [Fact]
    public void AndroidWatermelonDs_StillFallsBackToTheRetroArchProfile()
    {
        // WatermelonDS is a RetroArch-shaped standalone (it writes <game>.srm into a flat folder), so it
        // keeps resolving through the RetroArch provider's exact-folder override — unchanged behaviour.
        Assert.Null(AppBootstrapper.ResolveAndroidEmulator(
            "nds",
            new EmulatorConfiguration("nds", ExecutablePath: null, LaunchArguments: null)
            {
                EmulatorId = "watermelonds",
            }));
    }

    [Fact]
    public async Task AndroidMelonDsProvider_SyncsTheChosenFolderAndSitsOutWithoutOne()
    {
        // The Android branch of CreateMelonDsProvider — the counterpart to the resolution above, and
        // until now reachable from neither the suite (OperatingSystem.IsAndroid() is false on the test
        // host) nor production (the resolution it depends on returned null). Unix-only for the same
        // reason as the Dolphin case below: Path.GetFullPath rebases a POSIX path on Windows.
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX Android path; Path.GetFullPath rebases it on Windows.");
        const string chosenFolder = "/storage/emulated/0/User/Watermelon-DS";

        // No override → nothing to sync. melonDS records its save path in app-private storage EmuShelf
        // cannot read, so the platform sits out rather than guessing at a path.
        Assert.Null(SaveProviderRegistry.CreateMelonDsProvider(
            "melonds",
            new SaveProviderContext(
                DirectoryOverride: null,
                EmulatorDirectory: null,
                IsFlatpak: false,
                Paths: new StubPaths()),
            isAndroid: true));

        var provider = SaveProviderRegistry.CreateMelonDsProvider(
            "melonds-nightly",
            new SaveProviderContext(
                DirectoryOverride: chosenFolder,
                EmulatorDirectory: null,
                IsFlatpak: false,
                Paths: new StubPaths()),
            isAndroid: true);

        var melonDs = Assert.IsType<MelonDsSaveLocationProvider>(provider);
        Assert.Equal("melonds-nightly", melonDs.EmulatorId);
        Assert.Equal(
            chosenFolder,
            await melonDs.GetSaveDataDirectoryAsync(TestContext.Current.CancellationToken));
        // The battery key stays cross-emulator so the same save meets WatermelonDS's and a libretro
        // core's copy; only the save-state namespace is per channel.
        Assert.Equal("nds/", melonDs.UnitIdPrefix);
        Assert.Equal("melonds-nightly/nds/", melonDs.StateNamespacePrefix);
    }

    [Fact]
    public void NintendoDsRow_ReadsTheSameWhicheverEmulatorIsActive()
    {
        // The row's static text comes from the first profile for the system, so with three emulators
        // behind one row it must not name any of them (the live detection line is per-emulator).
        var profiles = SaveProviderRegistry.Profiles
            .Where(descriptor => descriptor.SystemId == "nds")
            .ToArray();

        Assert.Single(profiles.Select(descriptor => descriptor.SaveShapeDescription).Distinct());
        Assert.Single(profiles.Select(descriptor => descriptor.OverridePlaceholder).Distinct());
        Assert.All(profiles, descriptor =>
        {
            Assert.DoesNotContain("RetroArch", descriptor.SaveShapeDescription);
            Assert.DoesNotContain("RetroArch", descriptor.OverridePlaceholder);
            Assert.DoesNotContain("melonDS", descriptor.SaveShapeDescription);
        });
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
        var installation = AppBootstrapper.ResolveAndroidEmulator(systemId, configuration: null);

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
        // PSP (PPSSPP) is not a RetroArch system and has no fixed package-derived save root, so it
        // stays null even with a configuration present — the user must pick its Memory Stick folder.
        Assert.Null(AppBootstrapper.ResolveAndroidEmulator(
            "psp",
            new EmulatorConfiguration("psp", ExecutablePath: null, LaunchArguments: null)
            {
                EmulatorId = "ppsspp",
            }));
    }

    [Theory]
    [InlineData("gba", "/data/data/com.retroarch.aarch64/cores/mgba_libretro_android.so")]
    [InlineData("snes", "/data/data/com.retroarch.aarch64/cores/snes9x_libretro_android.so")]
    public void AndroidRetroArchSystem_AutoLocatesThePackageFilesRootWithItsConfiguredCore(
        string systemId,
        string corePath)
    {
        // RetroArch's config is readable in its own Android/data files dir, so a RetroArch system
        // auto-locates there (no user override) and carries the DB-configured core so the provider can
        // name the per-core save folder.
        var installation = AppBootstrapper.ResolveAndroidEmulator(
            systemId,
            new EmulatorConfiguration(systemId, ExecutablePath: null, LaunchArguments: "-L \"{CorePath}\" \"{GamePath}\"")
            {
                EmulatorId = "retroarch",
                CorePath = corePath,
            });

        Assert.NotNull(installation);
        Assert.Equal("retroarch", installation!.EmulatorId);
        Assert.False(installation.IsFlatpak);
        Assert.Equal(corePath, installation.CorePath);
        Assert.Equal(
            AndroidExternalStorageUri.ExternalAppFilesDirectory(
                AndroidEmulatorLaunchProfiles.RetroArch.PackageName),
            installation.Directory);
    }

    [Fact]
    public void AndroidPlayStation_HasNoDuckStationProvider_SyncsOnlyViaBeetlePsx()
    {
        // DuckStation configured → no installation. Its Android memory cards are owner-only (0600) and
        // unreadable by EmuShelf, so there is no Android DuckStation save provider and PS1-via-DuckStation
        // does not sync on Android (DECISIONS 2026-08-20 / -08-22, docs/android-save-sync-model.md).
        Assert.Null(AppBootstrapper.ResolveAndroidEmulator(
            "playstation",
            new EmulatorConfiguration("playstation", ExecutablePath: null, LaunchArguments: null)
            {
                EmulatorId = "duckstation",
            }));

        // Configured for a RetroArch PS1 core (Beetle PSX) → routes to the RetroArch package root with
        // the core carried, so PS1 saves sync through the readable RetroArch provider.
        const string beetle = "/data/data/com.retroarch.aarch64/cores/mednafen_psx_hw_libretro_android.so";
        var beetlePsx = AppBootstrapper.ResolveAndroidEmulator(
            "playstation",
            new EmulatorConfiguration("playstation", ExecutablePath: null, LaunchArguments: null)
            {
                EmulatorId = "retroarch",
                CorePath = beetle,
            });
        Assert.Equal("retroarch", beetlePsx!.EmulatorId);
        Assert.Equal(beetle, beetlePsx.CorePath);
        Assert.Equal(
            AndroidExternalStorageUri.ExternalAppFilesDirectory(
                AndroidEmulatorLaunchProfiles.RetroArch.PackageName),
            beetlePsx.Directory);
    }

    [Fact]
    public void AndroidRetroArchSystem_WithNoConfiguredEmulator_StaysNull()
    {
        // A RetroArch-served system whose emulator is not configured (or is a non-RetroArch emulator)
        // has no auto-located save root — the provider must not be handed the RetroArch package dir.
        Assert.Null(AppBootstrapper.ResolveAndroidEmulator("gba", configuration: null));
        Assert.Null(AppBootstrapper.ResolveAndroidEmulator(
            "nds",
            new EmulatorConfiguration("nds", ExecutablePath: null, LaunchArguments: null)
            {
                EmulatorId = "watermelonds",
            }));
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
