using EmuShelf.Core.Launching.Android;
using EmuShelf.Integrations.Emulators.Android;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Launching.Android;

public class AndroidEmulatorLaunchProfilesTests
{
    [Fact]
    public void EverySupportedSystemHasAtLeastOneLaunchProfile()
    {
        // PS3 is the deliberate exception — aPS3e is not part of v1 (docs/android-port-plan.md).
        var unsupported = KnownSystems.All
            .Select(system => system.Id)
            .Where(id => id != "playstation3")
            .Where(id => AndroidEmulatorLaunchProfiles.ForSystem(id).Count == 0)
            .ToList();

        Assert.Empty(unsupported);
    }

    [Fact]
    public void ForSystem_OrdersMaintainedBuildsBeforeFrozenOnes()
    {
        // PlayStation is served by both frozen DuckStation and maintained RetroArch; the maintained
        // build must sort first so a default pick never lands on an unsupported emulator.
        var playstation = AndroidEmulatorLaunchProfiles.ForSystem("playstation");

        Assert.Equal(AndroidEmulatorMaintenance.Maintained, playstation[0].Maintenance);
        Assert.Contains(playstation, p => p.Id == "android.duckstation");
    }

    [Fact]
    public void ForSystem_PutsTheExplicitSelectionIdBeforeMaintainedFallbacks()
    {
        var playstation = AndroidEmulatorLaunchProfiles.ForSystem("playstation", "duckstation");
        var nds = AndroidEmulatorLaunchProfiles.ForSystem("nds", "retroarch");
        var playstation2 = AndroidEmulatorLaunchProfiles.ForSystem("playstation2", "armsx2");
        var standaloneNds = AndroidEmulatorLaunchProfiles.ForSystem("nds", "watermelonds");

        Assert.Equal(AndroidEmulatorLaunchProfiles.DuckStation.Id, playstation[0].Id);
        Assert.Equal(AndroidEmulatorLaunchProfiles.RetroArch.Id, nds[0].Id);
        Assert.Equal(AndroidEmulatorLaunchProfiles.Armsx2.Id, playstation2[0].Id);
        Assert.Equal(AndroidEmulatorLaunchProfiles.WatermelonDs.Id, standaloneNds[0].Id);

        // The persisted selection id is what the launch path matches on, so each melonDS channel must
        // resolve to its own profile — otherwise picking the nightly would boot the release build.
        Assert.Equal(
            AndroidEmulatorLaunchProfiles.MelonDs.Id,
            AndroidEmulatorLaunchProfiles.ForSystem("nds", "melonds")[0].Id);
        Assert.Equal(
            AndroidEmulatorLaunchProfiles.MelonDsNightly.Id,
            AndroidEmulatorLaunchProfiles.ForSystem("nds", "melonds-nightly")[0].Id);
    }

    [Fact]
    public void SelectionIds_AreShortStableAndDistinct()
    {
        var ids = AndroidEmulatorLaunchProfiles.All.Select(profile => profile.SelectionId).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            [
                "duckstation", "armsx2", "dolphin", "ppsspp", "azahar",
                "watermelonds", "melonds", "melonds-nightly", "retroarch",
            ],
            ids);
        Assert.All(ids, id => Assert.DoesNotContain('.', id));
    }

    [Fact]
    public void EmulatorChoiceCatalog_FlattensNdsStandaloneAndRetroArchCores()
    {
        var choices = AndroidEmulatorChoiceCatalog.BySystem["nds"];

        Assert.Equal(
            [
                "WatermelonDS", "melonDS", "melonDS (nightly)",
                "RetroArch · melonDS DS", "RetroArch · melonDS", "RetroArch · DeSmuME",
            ],
            choices.Select(choice => choice.DisplayName));
        // The standalone builds carry no core; WatermelonDS stays first, so the launch default for a
        // DS system that never chose an emulator is unchanged.
        Assert.Equal(["watermelonds", "melonds", "melonds-nightly"], choices.Take(3).Select(c => c.EmulatorId));
        Assert.All(choices.Take(3), choice => Assert.Null(choice.CorePath));
        Assert.All(choices.Skip(3), choice =>
        {
            Assert.Equal("retroarch", choice.EmulatorId);
            Assert.False(string.IsNullOrWhiteSpace(choice.CoreId));
            Assert.False(string.IsNullOrWhiteSpace(choice.CorePath));
        });

        var melonDs = choices[4];
        Assert.Same(
            melonDs,
            choices.First(choice => choice.Matches("retroarch", melonDs.CorePath)));
    }

    [Fact]
    public void RetroArchCoreCatalog_CoversEveryRetroArchSystemWithAndroidCorePaths()
    {
        foreach (var systemId in AndroidEmulatorLaunchProfiles.RetroArch.SupportedSystemIds)
        {
            var cores = Assert.Contains(systemId, AndroidRetroArchCoreCatalog.BySystem);
            Assert.NotEmpty(cores);
            Assert.All(cores, core =>
            {
                Assert.StartsWith(AndroidRetroArchCoreCatalog.CoreDirectory + "/", core.Path, StringComparison.Ordinal);
                Assert.EndsWith("_libretro_android.so", core.Path, StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    public void AllPackageNames_AreDistinct_ForTheManifestQueriesBlock()
    {
        var packages = AndroidEmulatorLaunchProfiles.AllPackageNames;

        Assert.Equal(packages.Count, packages.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("com.github.stenzek.duckstation", packages);
        Assert.Contains("com.armsx2", packages);
        Assert.Contains("com.retroarch.aarch64", packages);
    }

    [Fact]
    public void EveryProfilePackageIsDeclaredInTheAndroidHeadsQueriesBlock()
    {
        // Package visibility is not optional on API 30+: a profile whose package is missing from
        // <queries> makes PackageManager report the emulator "not installed", so the choice is offered
        // and every launch through it fails. The manifest comment claims this test keeps the two in
        // sync — so actually check it, rather than spot-checking three ids.
        var manifest = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmuShelf.App.Android", "Properties", "AndroidManifest.xml"));

        foreach (var package in AndroidEmulatorLaunchProfiles.AllPackageNames)
            Assert.Contains($"<package android:name=\"{package}\" />", manifest, StringComparison.Ordinal);
    }

    // Walks up from the test binaries to the repository root (the directory holding the solution), so
    // the manifest can be read from source rather than duplicated into a fixture that could drift.
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EmuShelf.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    [Fact]
    public void MelonDsChannels_TargetTheirOwnApplicationIdWithMelonDsOwnLaunchIntent()
    {
        // melonDS's manifest declares the action as "${applicationId}.LAUNCH_ROM" and its
        // EmulatorActivity reads the ROM from the "uri" extra (KEY_URI), so the action follows the
        // suffixed application id while the activity class keeps the base package name. Both channels
        // install side by side — that is the whole reason they are separate profiles — so a launch must
        // name the exact package, or the nightly would boot the release build.
        const string uri = "content://com.android.externalstorage.documents/tree/x/document/y";

        var release = AndroidIntentFactory.Build(AndroidEmulatorLaunchProfiles.MelonDs, uri);
        Assert.Equal("me.magnum.melonds", release.PackageName);
        Assert.Equal("me.magnum.melonds.ui.emulator.EmulatorActivity", release.ActivityName);
        Assert.Equal("me.magnum.melonds.LAUNCH_ROM", release.Action);
        Assert.Equal(uri, Assert.Contains("uri", release.StringExtras));

        var nightly = AndroidIntentFactory.Build(AndroidEmulatorLaunchProfiles.MelonDsNightly, uri);
        Assert.Equal("me.magnum.melonds.nightly", nightly.PackageName);
        Assert.Equal("me.magnum.melonds.ui.emulator.EmulatorActivity", nightly.ActivityName);
        Assert.Equal("me.magnum.melonds.nightly.LAUNCH_ROM", nightly.Action);
        Assert.Equal(uri, Assert.Contains("uri", nightly.StringExtras));

        // Same handoff shape as the WatermelonDS fork, which is the build already proven on device.
        Assert.Equal(
            AndroidEmulatorLaunchProfiles.WatermelonDs.PayloadSlot,
            AndroidEmulatorLaunchProfiles.MelonDs.PayloadSlot);
        Assert.Equal(
            AndroidEmulatorLaunchProfiles.WatermelonDs.PayloadExtraName,
            AndroidEmulatorLaunchProfiles.MelonDs.PayloadExtraName);
    }

    [Fact]
    public void EveryExtraUriProfileNamesItsExtra()
    {
        // A misconfigured ExtraUri profile only fails when a game is launched; assert it up front.
        foreach (var profile in AndroidEmulatorLaunchProfiles.All.Where(p =>
                     p.PayloadSlot == AndroidRomPayloadSlot.ExtraUri))
        {
            Assert.False(string.IsNullOrEmpty(profile.PayloadExtraName), profile.Id);
        }
    }

    [Fact]
    public void EveryProfileBuildsAValidIntent()
    {
        const string uri = "content://com.android.externalstorage.documents/tree/primary%3Aa/document/primary%3Aa%2Fg";
        foreach (var profile in AndroidEmulatorLaunchProfiles.All)
        {
            var core = profile.PayloadSlot == AndroidRomPayloadSlot.RetroArchCore ? "/cores/x.so" : null;
            var intent = AndroidIntentFactory.Build(profile, uri, core);
            Assert.Equal(profile.PackageName, intent.PackageName);
            Assert.False(string.IsNullOrEmpty(intent.ActivityName));
        }
    }
}
