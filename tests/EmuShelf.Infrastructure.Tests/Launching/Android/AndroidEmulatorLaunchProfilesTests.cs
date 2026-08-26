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
    }

    [Fact]
    public void SelectionIds_AreShortStableAndDistinct()
    {
        var ids = AndroidEmulatorLaunchProfiles.All.Select(profile => profile.SelectionId).ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ["duckstation", "armsx2", "dolphin", "ppsspp", "azahar", "watermelonds", "retroarch"],
            ids);
        Assert.All(ids, id => Assert.DoesNotContain('.', id));
    }

    [Fact]
    public void EmulatorChoiceCatalog_FlattensNdsStandaloneAndRetroArchCores()
    {
        var choices = AndroidEmulatorChoiceCatalog.BySystem["nds"];

        Assert.Equal(
            ["WatermelonDS", "RetroArch · melonDS DS", "RetroArch · melonDS", "RetroArch · DeSmuME"],
            choices.Select(choice => choice.DisplayName));
        Assert.Equal("watermelonds", choices[0].EmulatorId);
        Assert.Null(choices[0].CorePath);
        Assert.All(choices.Skip(1), choice =>
        {
            Assert.Equal("retroarch", choice.EmulatorId);
            Assert.False(string.IsNullOrWhiteSpace(choice.CoreId));
            Assert.False(string.IsNullOrWhiteSpace(choice.CorePath));
        });

        var melonDs = choices[2];
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
