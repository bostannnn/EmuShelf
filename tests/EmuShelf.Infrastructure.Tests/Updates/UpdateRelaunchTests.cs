using EmuShelf.Infrastructure.Updates;

namespace EmuShelf.Infrastructure.Tests.Updates;

public class UpdateRelaunchTests
{
    // Stands in for either platform's app target: a Windows .exe or a macOS .app bundle path.
    private const string AppTarget = "/Applications/EmuShelf.app";

    [Fact]
    public void ResolveTarget_WhenSteamLaunchedUs_RelaunchesThroughSteam()
    {
        // Steam sets SteamGameId to the rungameid token for both Steam apps and non-Steam shortcuts;
        // going back through it is what reattaches Steam Input so the controller keeps working.
        var target = UpdateRelaunch.ResolveTarget(AppTarget, "13911887905558691840");

        Assert.Equal("steam://rungameid/13911887905558691840", target);
    }

    [Theory]
    [InlineData(null)]   // launched directly — no Steam variables in the environment
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]    // never a real game id
    [InlineData("not-a-number")]
    public void ResolveTarget_WithoutSteamLaunch_RelaunchesTheAppTarget(string? steamGameId)
    {
        Assert.Equal(AppTarget, UpdateRelaunch.ResolveTarget(AppTarget, steamGameId));
    }
}
