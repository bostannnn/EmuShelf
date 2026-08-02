using EmuShelf.App.Services;

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
}
