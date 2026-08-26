using EmuShelf.Core.Launching.Android;
using EmuShelf.Integrations.Emulators.Android;

namespace EmuShelf.Infrastructure.Tests.Launching.Android;

public class AndroidIntentFactoryTests
{
    private const string MgsUri =
        "content://com.android.externalstorage.documents/tree/AE6A-1092%3Aroms%2Fpsx/document/" +
        "AE6A-1092%3Aroms%2Fpsx%2FMetal%20Gear%20Solid.m3u";

    [Fact]
    public void DuckStation_TargetsEmulationActivity_WithBootPathAndOneShot()
    {
        // The corrected 0b handoff: EmulationActivity (not MainActivity), bootPath extra, isOneShot.
        var intent = AndroidIntentFactory.Build(AndroidEmulatorLaunchProfiles.DuckStation, MgsUri);

        Assert.Equal("com.github.stenzek.duckstation/com.github.stenzek.duckstation.EmulationActivity", intent.Component);
        Assert.Null(intent.Action);
        Assert.Null(intent.DataUri);
        Assert.Equal(MgsUri, intent.StringExtras["bootPath"]);
        Assert.True(intent.BoolExtras[AndroidIntentFactory.OneShotExtra]);
    }

    [Fact]
    public void Armsx2_UsesViewWithTheUriAsData_NoExtras()
    {
        var intent = AndroidIntentFactory.Build(AndroidEmulatorLaunchProfiles.Armsx2, MgsUri);

        Assert.Equal("com.armsx2/com.armsx2.Main", intent.Component);
        Assert.Equal(AndroidIntentActions.View, intent.Action);
        Assert.Equal(MgsUri, intent.DataUri);
        Assert.Empty(intent.StringExtras);
    }

    [Fact]
    public void Dolphin_UsesMainWithAutoStartFileExtra()
    {
        var intent = AndroidIntentFactory.Build(AndroidEmulatorLaunchProfiles.Dolphin, MgsUri);

        Assert.Equal("org.dolphinemu.dolphinemu/org.dolphinemu.dolphinemu.ui.main.MainActivity", intent.Component);
        Assert.Equal(AndroidIntentActions.Main, intent.Action);
        Assert.Equal(MgsUri, intent.StringExtras["AutoStartFile"]);
        Assert.Null(intent.DataUri);
    }

    [Fact]
    public void WatermelonDs_UsesItsCustomActionAndUriExtra()
    {
        var intent = AndroidIntentFactory.Build(AndroidEmulatorLaunchProfiles.WatermelonDs, MgsUri);

        Assert.Equal("me.magnum.melondualds.LAUNCH_ROM", intent.Action);
        Assert.Equal(MgsUri, intent.StringExtras["uri"]);
        Assert.False(intent.BoolExtras.ContainsKey(AndroidIntentFactory.OneShotExtra));
    }

    [Fact]
    public void RetroArch_CarriesRomPathAndCorePath()
    {
        const string romPath = "/storage/AE6A-1092/roms/psx/game.m3u";
        const string corePath = "/data/data/com.retroarch.aarch64/cores/swanstation_libretro_android.so";

        var intent = AndroidIntentFactory.Build(AndroidEmulatorLaunchProfiles.RetroArch, romPath, corePath);

        Assert.Equal("com.retroarch.aarch64/com.retroarch.browser.retroactivity.RetroActivityFuture", intent.Component);
        Assert.Equal(AndroidIntentActions.View, intent.Action);
        Assert.Equal(romPath, intent.StringExtras[AndroidIntentFactory.RetroArchRomExtra]);
        Assert.Equal(corePath, intent.StringExtras[AndroidIntentFactory.RetroArchCoreExtra]);
    }

    [Fact]
    public void RetroArch_CarriesConfigEnvironmentExtras()
    {
        // Without these RetroActivityFuture ignores the user's retroarch.cfg (hotkeys, gamepad, settings).
        // Confirmed on the Thor: the config only loads when CONFIGFILE points at the app's external files.
        const string romPath = "/storage/AE6A-1092/roms/gba/game.gba";
        const string corePath = "/data/data/com.retroarch.aarch64/cores/mgba_libretro_android.so";

        var intent = AndroidIntentFactory.Build(AndroidEmulatorLaunchProfiles.RetroArch, romPath, corePath);

        const string external = "/storage/emulated/0/Android/data/com.retroarch.aarch64/files";
        Assert.Equal($"{external}/retroarch.cfg", intent.StringExtras[AndroidIntentFactory.RetroArchConfigExtra]);
        Assert.Equal("/data/user/0/com.retroarch.aarch64", intent.StringExtras[AndroidIntentFactory.RetroArchDataDirExtra]);
        Assert.Equal("/storage/emulated/0", intent.StringExtras[AndroidIntentFactory.RetroArchSdcardExtra]);
        Assert.Equal(external, intent.StringExtras[AndroidIntentFactory.RetroArchExternalExtra]);
    }

    [Fact]
    public void RetroArch_WithoutCorePath_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AndroidIntentFactory.Build(AndroidEmulatorLaunchProfiles.RetroArch, "/roms/game.m3u"));
    }

    [Fact]
    public void Build_WithEmptyRomReference_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            AndroidIntentFactory.Build(AndroidEmulatorLaunchProfiles.Ppsspp, string.Empty));
    }
}
