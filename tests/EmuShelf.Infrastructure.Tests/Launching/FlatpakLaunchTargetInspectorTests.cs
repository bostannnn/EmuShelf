using EmuShelf.Core.Launching;
using EmuShelf.Infrastructure.Launching;

namespace EmuShelf.Infrastructure.Tests.Launching;

public sealed class FlatpakLaunchTargetInspectorTests
{
    private const string InstallListArguments = "list --app --columns=application,branch";

    [Fact]
    public void Inspect_InstalledApplication_PassesUsingOnlyAnInstallListing()
    {
        var calls = new List<IReadOnlyList<string>>();
        var inspector = new FlatpakLaunchTargetInspector(arguments =>
        {
            calls.Add(arguments);
            return new FlatpakLaunchTargetInspector.CommandResult(0, "net.pcsx2.PCSX2\tstable");
        });

        var result = inspector.Inspect(
            new FlatpakApplicationTarget("net.pcsx2.PCSX2"),
            ["/games/My Game.chd"]);

        Assert.True(result.CanLaunch);
        Assert.Null(result.FailureMessage);
        // A single branch listing; the buggy per-file --file-access probe is gone because EmuShelf now
        // grants the sandbox read-only access to the game paths at launch time.
        Assert.Equal([["list", "--app", "--columns=application,branch"]], calls);
        Assert.DoesNotContain(calls, call => call.Any(argument => argument.StartsWith("--file-access", StringComparison.Ordinal)));
    }

    [Fact]
    public void Inspect_ApplicationNotInstalled_BlocksLaunch()
    {
        var inspector = new FlatpakLaunchTargetInspector(_ =>
            new FlatpakLaunchTargetInspector.CommandResult(0, "org.libretro.RetroArch\tstable"));

        var result = inspector.Inspect(
            new FlatpakApplicationTarget("net.pcsx2.PCSX2"), ["/games/disc.chd"]);

        Assert.False(result.CanLaunch);
        Assert.Contains("is not installed", result.FailureMessage);
    }

    [Fact]
    public void Inspect_ListingCommandFails_BlocksLaunch()
    {
        var inspector = new FlatpakLaunchTargetInspector(_ =>
            new FlatpakLaunchTargetInspector.CommandResult(1, null));

        var result = inspector.Inspect(
            new FlatpakApplicationTarget("net.pcsx2.PCSX2"), ["/games/disc.chd"]);

        Assert.False(result.CanLaunch);
        Assert.Contains("is not installed", result.FailureMessage);
    }

    [Fact]
    public void Inspect_MultipleBranchesInstalled_UnpinnedTargetStillPasses()
    {
        // The regression this fixes: with both stable and beta installed, `flatpak info <appId>` fails
        // with "Multiple branches available…" and the app looks uninstalled. Listing branches does not.
        var inspector = new FlatpakLaunchTargetInspector(_ =>
            new FlatpakLaunchTargetInspector.CommandResult(0, "net.pcsx2.PCSX2\tstable\nnet.pcsx2.PCSX2\tbeta"));

        var result = inspector.Inspect(
            new FlatpakApplicationTarget("net.pcsx2.PCSX2"), ["/games/disc.chd"]);

        Assert.True(result.CanLaunch);
    }

    [Fact]
    public void Inspect_BranchPinnedTargetInstalled_Passes()
    {
        var inspector = new FlatpakLaunchTargetInspector(_ =>
            new FlatpakLaunchTargetInspector.CommandResult(0, "net.pcsx2.PCSX2\tstable\nnet.pcsx2.PCSX2\tbeta"));

        var result = inspector.Inspect(
            new FlatpakApplicationTarget("net.pcsx2.PCSX2", "beta"), ["/games/disc.chd"]);

        Assert.True(result.CanLaunch);
    }

    [Fact]
    public void Inspect_BranchPinnedTargetMissingThatBranch_BlocksLaunch()
    {
        var inspector = new FlatpakLaunchTargetInspector(_ =>
            new FlatpakLaunchTargetInspector.CommandResult(0, "net.pcsx2.PCSX2\tstable"));

        var result = inspector.Inspect(
            new FlatpakApplicationTarget("net.pcsx2.PCSX2", "beta"), ["/games/disc.chd"]);

        Assert.False(result.CanLaunch);
        Assert.Contains("net.pcsx2.PCSX2//beta", result.FailureMessage);
    }
}
