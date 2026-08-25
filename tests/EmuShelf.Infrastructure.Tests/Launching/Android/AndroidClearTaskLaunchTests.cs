using EmuShelf.Core.Launching.Android;
using EmuShelf.Integrations.Emulators.Android;

namespace EmuShelf.Infrastructure.Tests.Launching.Android;

public class AndroidClearTaskLaunchTests
{
    private const string SafUri =
        "content://com.android.externalstorage.documents/tree/primary%3Aroms%2F3ds/document/primary%3Aroms%2F3ds%2Fgame.cci";

    [Fact]
    public void Azahar_RequestsAFreshTask_SoARelaunchReloadsTheRom()
    {
        var intent = AndroidIntentFactory.Build(AndroidEmulatorLaunchProfiles.Azahar, SafUri);

        Assert.True(intent.ClearTask);
    }

    [Fact]
    public void OnlyAzahar_ClearsTheTask_TheSixWorkingEmulatorsAreUntouched()
    {
        var clearing = AndroidEmulatorLaunchProfiles.All
            .Where(profile => profile.ClearTaskOnLaunch)
            .Select(profile => profile.Id)
            .ToList();

        Assert.Equal(["android.azahar"], clearing);
    }
}
