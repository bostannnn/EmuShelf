using EmuShelf.Core.Launching.Android;
using EmuShelf.Integrations.Emulators.Android;

namespace EmuShelf.Infrastructure.Tests.Launching.Android;

public class AndroidLaunchResolverTests
{
    [Fact]
    public void Resolve_Ps1MultiDisc_WithGrantRoot_ReproducesTheOnDeviceVerifiedUri()
    {
        // The exact URI that booted Metal Gear Solid on the Thor: tree scoped to the emulator's grant
        // folder (roms/psx), the nested multi-disc .m3u as the document beneath it.
        var result = AndroidLaunchResolver.Resolve(
            "playstation",
            "/storage/AE6A-1092/roms/psx/Metal Gear Solid (USA) (Rev 1)/Metal Gear Solid (USA) (Rev 1).m3u",
            preferredEmulatorId: "android.duckstation",
            emulatorGrantRoot: "/storage/AE6A-1092/roms/psx");

        Assert.True(result.Success);
        Assert.Equal("android.duckstation", result.Profile!.Id);
        Assert.Equal(
            "content://com.android.externalstorage.documents/tree/AE6A-1092%3Aroms%2Fpsx/document/" +
            "AE6A-1092%3Aroms%2Fpsx%2FMetal%20Gear%20Solid%20(USA)%20(Rev%201)%2F" +
            "Metal%20Gear%20Solid%20(USA)%20(Rev%201).m3u",
            result.Intent!.StringExtras["bootPath"]);
    }

    [Fact]
    public void Resolve_WithoutGrantRoot_FallsBackToTheGameParentFolderTree()
    {
        // Best-effort default (not yet on-device verified for a nested game): tree = the game's parent.
        var result = AndroidLaunchResolver.Resolve(
            "playstation",
            "/storage/AE6A-1092/roms/psx/Koudelka (USA)/Koudelka (USA).m3u",
            preferredEmulatorId: "android.duckstation");

        Assert.True(result.Success);
        Assert.Contains(
            "tree/AE6A-1092%3Aroms%2Fpsx%2FKoudelka%20(USA)/document/",
            result.Intent!.StringExtras["bootPath"]);
    }

    [Fact]
    public void Resolve_GrantRootThatIsNotAnAncestor_IsIgnored()
    {
        // A stale/mismatched grant root must not scope the URI outside the game's own hierarchy.
        var result = AndroidLaunchResolver.Resolve(
            "playstation",
            "/storage/AE6A-1092/roms/psx/game.chd",
            preferredEmulatorId: "android.duckstation",
            emulatorGrantRoot: "/storage/AE6A-1092/roms/ps2");

        Assert.True(result.Success);
        // Falls back to the game parent (roms/psx), not the mismatched ps2 grant root.
        Assert.Contains("tree/AE6A-1092%3Aroms%2Fpsx/document/", result.Intent!.StringExtras["bootPath"]);
    }

    [Fact]
    public void Resolve_DefaultsToMaintainedEmulator_WhenNoPreference()
    {
        // PlayStation: RetroArch (maintained) sorts before DuckStation (frozen), but RetroArch needs a
        // core — so without one the resolver reports that rather than silently succeeding.
        var result = AndroidLaunchResolver.Resolve(
            "playstation",
            "/storage/emulated/0/roms/psx/game.chd");

        Assert.False(result.Success);
        Assert.Contains("core", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_StaleRetroArchWithoutCore_FallsBackToTheStandaloneDefault()
    {
        // A legacy DS row still says "retroarch" but has no core, so it cannot launch. DS's maintained-
        // first default is WatermelonDS (a standalone needing no core) — the same row the settings picker
        // migrates such a selection to — so the resolver launches that instead of prompting for a core.
        var result = AndroidLaunchResolver.Resolve(
            "nds",
            "/storage/emulated/0/roms/nds/game.nds",
            preferredEmulatorId: AndroidEmulatorLaunchProfiles.RetroArch.Id,
            retroArchCorePath: null);

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal(AndroidEmulatorLaunchProfiles.WatermelonDs.Id, result.Profile!.Id);
    }

    [Fact]
    public void Resolve_DeliberateRetroArchCore_OnStandaloneDefaultSystem_LaunchesRetroArch()
    {
        // The fallback must hinge on the *empty* core path, not merely on RetroArch being preferred: a
        // real DS RetroArch-core pick carries a core path, so it launches RetroArch (melonDS DS) and is
        // never hijacked to WatermelonDS just because a standalone is the maintained default.
        var corePath = AndroidRetroArchCoreCatalog.BySystem["nds"][0].Path;
        var result = AndroidLaunchResolver.Resolve(
            "nds",
            "/storage/emulated/0/roms/nds/game.nds",
            preferredEmulatorId: AndroidEmulatorLaunchProfiles.RetroArch.Id,
            retroArchCorePath: corePath);

        Assert.True(result.Success, result.FailureReason);
        Assert.Equal(AndroidEmulatorLaunchProfiles.RetroArch.Id, result.Profile!.Id);
        Assert.Equal(corePath, result.Intent!.StringExtras["LIBRETRO"]);
    }

    [Fact]
    public void Resolve_StaleRetroArchWithoutCore_StillPromptsWhenRetroArchIsTheMaintainedDefault()
    {
        // PS1's maintained-first default is RetroArch itself (DuckStation is frozen), so dropping the
        // unusable preference lands back on RetroArch — the core prompt is correct, not a dead-end to
        // the deprioritised standalone.
        var result = AndroidLaunchResolver.Resolve(
            "playstation",
            "/storage/emulated/0/roms/psx/game.chd",
            preferredEmulatorId: AndroidEmulatorLaunchProfiles.RetroArch.Id,
            retroArchCorePath: null);

        Assert.False(result.Success);
        Assert.Contains("core", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RetroArch_UsesPlainPathAndCore()
    {
        var result = AndroidLaunchResolver.Resolve(
            "snes",
            "/storage/emulated/0/roms/snes/game.sfc",
            retroArchCorePath: "/data/data/com.retroarch.aarch64/cores/snes9x_libretro_android.so");

        Assert.True(result.Success);
        Assert.Equal("android.retroarch", result.Profile!.Id);
        Assert.Equal("/storage/emulated/0/roms/snes/game.sfc", result.Intent!.StringExtras["ROM"]);
    }

    [Fact]
    public void Resolve_UnsupportedSystem_Fails()
    {
        var result = AndroidLaunchResolver.Resolve("playstation3", "/storage/emulated/0/roms/ps3/game.iso");

        Assert.False(result.Success);
    }

    [Fact]
    public void Resolve_AppPrivatePath_FailsWithSharedStorageReason()
    {
        var result = AndroidLaunchResolver.Resolve(
            "psp",
            "/data/data/com.emushelf.app/files/roms/game.chd");

        Assert.False(result.Success);
        Assert.Contains("shared storage", result.FailureReason, StringComparison.OrdinalIgnoreCase);
    }
}
