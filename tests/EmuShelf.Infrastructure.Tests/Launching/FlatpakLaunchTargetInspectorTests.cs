using EmuShelf.Core.Launching;
using EmuShelf.Infrastructure.Launching;

namespace EmuShelf.Infrastructure.Tests.Launching;

public sealed class FlatpakLaunchTargetInspectorTests
{
    [Fact]
    public void Inspect_ReadAccess_PassesAndUsesExactShellFreeArguments()
    {
        var calls = new List<IReadOnlyList<string>>();
        var inspector = new FlatpakLaunchTargetInspector(arguments =>
        {
            calls.Add(arguments);
            return new FlatpakLaunchTargetInspector.CommandResult(0, "read\n");
        });

        var result = inspector.Inspect(
            new FlatpakApplicationTarget("net.pcsx2.PCSX2"),
            ["/games/My Game.iso"]);

        Assert.True(result.CanLaunch);
        Assert.Equal(["info", "net.pcsx2.PCSX2"], calls[0]);
        Assert.Equal(
            ["info", "--file-access=/games/My Game.iso", "net.pcsx2.PCSX2"],
            calls[1]);
    }

    [Fact]
    public void Inspect_NoAccess_BlocksLaunch()
    {
        var inspector = new FlatpakLaunchTargetInspector(arguments =>
            new FlatpakLaunchTargetInspector.CommandResult(0, arguments.Count == 2 ? "installed" : "none"));

        var result = inspector.Inspect(
            new FlatpakApplicationTarget("org.DolphinEmu.dolphin-emu"), ["/games/disc.iso"]);

        Assert.False(result.CanLaunch);
        Assert.Contains("cannot access", result.FailureMessage);
    }

    [Fact]
    public void Inspect_UnavailableAccessInspection_WarnsAndPermitsUserLaunch()
    {
        var inspector = new FlatpakLaunchTargetInspector(arguments =>
            new FlatpakLaunchTargetInspector.CommandResult(arguments.Count == 2 ? 0 : 1, null));

        var result = inspector.Inspect(
            new FlatpakApplicationTarget("org.ppsspp.PPSSPP"), ["/games/game.iso"]);

        Assert.True(result.CanLaunch);
        Assert.Contains("Could not determine", result.WarningMessage);
    }
}
