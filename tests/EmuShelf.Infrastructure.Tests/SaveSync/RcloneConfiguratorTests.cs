using EmuShelf.Infrastructure.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class RcloneConfiguratorTests
{
    [Fact]
    public void DescribeFailure_ReturnsNull_OnSuccess()
    {
        Assert.Null(RcloneConfigurator.DescribeFailure(0, "anything on stderr"));
    }

    [Theory]
    [InlineData("listen tcp 127.0.0.1:53682: bind: address already in use")]
    [InlineData("Error: config failed to refresh token: failed to start auth webserver: " +
        "listen tcp 127.0.0.1:53682: bind: ADDRESS ALREADY IN USE")]
    public void DescribeFailure_RecognizesThePortConflict(string stderr)
    {
        // A leftover sign-in holding rclone's loopback port must be a distinct, explainable failure
        // rather than a generic error, so the UI can tell the user how to clear it.
        Assert.IsType<RcloneSignInServerBusyException>(RcloneConfigurator.DescribeFailure(2, stderr));
    }

    [Fact]
    public void DescribeFailure_WrapsOtherErrors_WithTheExitCodeAndStderr()
    {
        var failure = RcloneConfigurator.DescribeFailure(1, "  couldn't reach the remote  ");

        var io = Assert.IsType<IOException>(failure);
        Assert.Contains("code 1", io.Message);
        Assert.Contains("couldn't reach the remote", io.Message);
        // The stderr is trimmed into the message, not left with its surrounding whitespace.
        Assert.DoesNotContain("  couldn't", io.Message);
    }

    [Fact]
    public void DescribeFailure_ToleratesNullStderr()
    {
        var io = Assert.IsType<IOException>(RcloneConfigurator.DescribeFailure(1, null!));
        Assert.Contains("code 1", io.Message);
    }

    [Fact]
    public void PathsEqual_MatchesIdenticalPaths()
    {
        var path = Absolute("EmuShelf", "rclone");
        Assert.True(RcloneConfigurator.PathsEqual(path, path));
    }

    [Fact]
    public void PathsEqual_NormalizesTraversalBeforeComparing()
    {
        Assert.True(RcloneConfigurator.PathsEqual(Absolute("EmuShelf", "sub", "..", "rclone"), Absolute("EmuShelf", "rclone")));
    }

    [Fact]
    public void PathsEqual_RejectsDifferentPaths()
    {
        Assert.False(RcloneConfigurator.PathsEqual(Absolute("a", "rclone"), Absolute("b", "rclone")));
    }

    [Theory]
    [InlineData(null, "/x/rclone")]
    [InlineData("/x/rclone", null)]
    [InlineData("", "/x/rclone")]
    public void PathsEqual_RejectsNullOrEmpty(string? a, string? b)
    {
        Assert.False(RcloneConfigurator.PathsEqual(a, b));
    }

    [Fact]
    public void PathsEqual_IsCaseSensitiveOnlyOnLinux()
    {
        // Linux file systems are case-sensitive, so casing must matter there; Windows/macOS default to
        // case-insensitive, so the same two paths must match. This is exactly what lets an AppImage
        // orphan be recognised by its stable config path rather than its rotating mount path.
        var lower = Absolute("emushelf", "rclone");
        var upper = Absolute("EMUSHELF", "rclone");
        Assert.Equal(!OperatingSystem.IsLinux(), RcloneConfigurator.PathsEqual(lower, upper));
    }

    [Fact]
    public void CommandLineReferencesConfig_MatchesOurConfigArgument()
    {
        var config = Absolute("Data", "Settings", "rclone.conf");
        // /proc/<pid>/cmdline is the NUL-separated argv. The binary path is a stale AppImage mount, but
        // the --config argument is our portable rclone.conf, stable across launches — that is the match.
        var cmdline = NulJoin("/tmp/.mount_EmuShAAAA/usr/bin/rclone", "--config", config, "config", "create", "emushelf-gdrive", "drive");
        Assert.True(RcloneConfigurator.CommandLineReferencesConfig(cmdline, config));
    }

    [Fact]
    public void CommandLineReferencesConfig_IgnoresAnUnrelatedRcloneWithADifferentConfig()
    {
        var ours = Absolute("Data", "Settings", "rclone.conf");
        var theirs = Absolute("home", "me", ".config", "rclone", "rclone.conf");
        var cmdline = NulJoin("rclone", "--config", theirs, "copy", "drive:", "backup:");
        Assert.False(RcloneConfigurator.CommandLineReferencesConfig(cmdline, ours));
    }

    [Fact]
    public void CommandLineReferencesConfig_ToleratesEmptyCmdline()
    {
        Assert.False(RcloneConfigurator.CommandLineReferencesConfig("", Absolute("Data", "Settings", "rclone.conf")));
    }

    private static string Absolute(params string[] segments) =>
        Path.GetFullPath(Path.Combine(
            OperatingSystem.IsWindows() ? @"C:\" : "/",
            Path.Combine(segments)));

    private static string NulJoin(params string[] arguments) => string.Join('\0', arguments) + '\0';
}
