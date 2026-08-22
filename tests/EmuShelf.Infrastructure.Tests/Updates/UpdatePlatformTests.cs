using EmuShelf.Core.Updates;

namespace EmuShelf.Infrastructure.Tests.Updates;

public class UpdatePlatformTests
{
    [Theory]
    [InlineData("EmuShelf-win-x64.zip", "EmuShelf-win-x64.sha256")]
    [InlineData("EmuShelf-linux-x64.AppImage", "EmuShelf-linux-x64.sha256")]
    [InlineData("EmuShelf-macos-arm64.zip", "EmuShelf-macos-arm64.sha256")]
    [InlineData("EmuShelf-android-arm64.apk", "EmuShelf-android-arm64.sha256")]
    public void ChecksumAssetNameFor_ReplacesExtensionWithSha256(string payload, string expected)
    {
        Assert.Equal(expected, UpdatePlatform.ChecksumAssetNameFor(payload));
    }

    [Fact]
    public void CurrentAssetName_MatchesTheRunningPlatformArtifact()
    {
        var name = UpdatePlatform.CurrentAssetName();

        if (OperatingSystem.IsWindows())
            Assert.Equal("EmuShelf-win-x64.zip", name);
        else if (OperatingSystem.IsLinux())
            Assert.Equal("EmuShelf-linux-x64.AppImage", name);
        else if (OperatingSystem.IsMacOS())
            // Apple Silicon runners/dev machines have an artifact; Intel Macs have none.
            Assert.True(name is "EmuShelf-macos-arm64.zip" or null);
        else if (OperatingSystem.IsAndroid())
            // arm64 handhelds (the target) ship an APK; any other Android ABI has none.
            Assert.True(name is "EmuShelf-android-arm64.apk" or null);
        else
            Assert.Null(name);
    }
}
