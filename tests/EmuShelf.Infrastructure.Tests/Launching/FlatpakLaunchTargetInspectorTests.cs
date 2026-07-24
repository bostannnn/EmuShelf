using EmuShelf.Core.Launching;
using EmuShelf.Infrastructure.Launching;

namespace EmuShelf.Infrastructure.Tests.Launching;

public sealed class FlatpakLaunchTargetInspectorTests
{
    [Fact]
    public void Inspect_InstalledApplication_PassesUsingOnlyAnInstallCheck()
    {
        var calls = new List<IReadOnlyList<string>>();
        var inspector = new FlatpakLaunchTargetInspector(arguments =>
        {
            calls.Add(arguments);
            return new FlatpakLaunchTargetInspector.CommandResult(0, null);
        });

        var result = inspector.Inspect(
            new FlatpakApplicationTarget("net.pcsx2.PCSX2"),
            ["/games/My Game.chd"]);

        Assert.True(result.CanLaunch);
        Assert.Null(result.FailureMessage);
        // A single install probe; the buggy per-file --file-access probe is gone because EmuShelf
        // now grants the sandbox read-only access to the game paths at launch time.
        Assert.Equal([["info", "net.pcsx2.PCSX2"]], calls);
        Assert.DoesNotContain(calls, call => call.Any(argument => argument.StartsWith("--file-access", StringComparison.Ordinal)));
    }

    [Fact]
    public void Inspect_ApplicationNotInstalled_BlocksLaunch()
    {
        var inspector = new FlatpakLaunchTargetInspector(_ =>
            new FlatpakLaunchTargetInspector.CommandResult(1, null));

        var result = inspector.Inspect(
            new FlatpakApplicationTarget("net.pcsx2.PCSX2"), ["/games/disc.chd"]);

        Assert.False(result.CanLaunch);
        Assert.Contains("is not installed", result.FailureMessage);
    }
}
