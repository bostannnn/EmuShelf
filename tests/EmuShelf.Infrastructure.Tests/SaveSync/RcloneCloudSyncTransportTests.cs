using System.Security.Cryptography;
using System.Text;
using EmuShelf.Infrastructure.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

/// <summary>
/// Opt-in integration coverage for a real rclone local backend. Set
/// EMUSHELF_TEST_RCLONE_PATH to the rclone executable to enable it; normal CI needs no binary.
/// </summary>
public sealed class RcloneCloudSyncTransportTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task LocalBackend_RoundTripsPayloadAndEmuShelfSidecarHash()
    {
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "rclone-remote");
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var cloudFolder = Path.GetFullPath(remoteRoot).Replace('\\', '/');
        var transport = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        var bytes = Encoding.UTF8.GetBytes("folder-card zip bytes");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("composite folder hash")));
        var modified = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

        await transport.UploadAsync("pcsx2/Mcd001/SLUS-20552", new MemoryStream(bytes), hash, modified);
        await transport.FlushAsync();

        var snapshot = Assert.Single(await transport.ListAsync());
        Assert.Equal("pcsx2/Mcd001/SLUS-20552", snapshot.UnitId);
        Assert.Equal(hash, snapshot.ContentHash);
        Assert.Equal(modified, snapshot.ModifiedUtc);
        await using var downloaded = await transport.DownloadAsync(snapshot.UnitId);
        using var result = new MemoryStream();
        await downloaded.CopyToAsync(result);
        Assert.Equal(bytes, result.ToArray());
    }

    [Fact]
    public async Task LocalBackend_MissingRemoteFolder_ListsNothingInsteadOfThrowing()
    {
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        // Point at a folder that has never been created — the first-sync state.
        var missingFolder = Path.Combine(BaseDirectory, "never-created").Replace('\\', '/');
        var transport = new RcloneCloudSyncTransport(AppPaths, "testlocal", missingFolder, rclonePath);

        Assert.Empty(await transport.ListAsync());
    }
}
