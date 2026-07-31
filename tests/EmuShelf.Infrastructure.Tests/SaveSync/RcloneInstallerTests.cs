using EmuShelf.Infrastructure.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class RcloneInstallerTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task InstallAsync_DownloadsRcloneBesideTheApp()
    {
        // Opt-in: set EMUSHELF_TEST_RCLONE_DOWNLOAD=1 to hit the network. CI stays offline.
        if (Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_DOWNLOAD") != "1")
            return;

        AppPaths.EnsureDirectoriesExist();

        var path = await new RcloneInstaller().InstallAsync(AppPaths);

        Assert.Equal(Path.Combine(AppPaths.BaseDirectory, RcloneExecutable.FileName), path);
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 1_000_000); // rclone is one large static binary
    }
}
