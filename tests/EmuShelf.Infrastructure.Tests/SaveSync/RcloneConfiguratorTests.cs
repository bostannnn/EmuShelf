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
}
