using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.SaveSync;
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
    public async Task LocalBackend_ScopedDownloadSessionStillServesAnUnannouncedUnit()
    {
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "scoped-remote");
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var cloudFolder = Path.GetFullPath(remoteRoot).Replace('\\', '/');
        var seeding = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        foreach (var unitId in new[] { "pcsx2/Mcd001.ps2", "rpcs3/savedata/BCES00006", "retroarch/nds/Contra 4.srm" })
        {
            await seeding.UploadAsync(
                unitId,
                new MemoryStream(Encoding.UTF8.GetBytes(unitId)),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(unitId))),
                new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
        }

        await seeding.FlushAsync();

        // The session is scoped to the announced unit; the un-announced one must still arrive,
        // through a single-payload fetch, rather than failing the pass.
        var transport = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        await transport.ListAsync();
        transport.ExpectDownloads(["pcsx2/Mcd001.ps2"]);

        Assert.Equal("pcsx2/Mcd001.ps2", await ReadAllAsync(transport, "pcsx2/Mcd001.ps2"));
        Assert.Equal("rpcs3/savedata/BCES00006", await ReadAllAsync(transport, "rpcs3/savedata/BCES00006"));
        await Assert.ThrowsAsync<CloudPayloadMissingException>(
            () => transport.DownloadAsync("pcsx2/Missing.ps2"));
        await transport.FlushAsync();
    }

    [Fact]
    public async Task LocalBackend_AnIndexEntryWithNoPayloadIsReportedAndThenPrunedFromTheIndex()
    {
        // Regression: a flush that uploaded index.json alongside the payloads could commit an entry
        // whose payload never arrived. The owning machine then saw "unchanged" forever while every
        // other machine failed downloading it, so the entry has to be removable.
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "stale-index-remote");
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var cloudFolder = Path.GetFullPath(remoteRoot).Replace('\\', '/');
        var seeding = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        foreach (var unitId in new[] { "ppsspp/ULES00841", "ppsspp/ULUS10277" })
        {
            await seeding.UploadAsync(
                unitId,
                new MemoryStream(Encoding.UTF8.GetBytes(unitId)),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(unitId))),
                new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
        }

        await seeding.FlushAsync();
        // Reproduce the damage the old flush ordering could leave behind.
        File.Delete(Path.Combine(remoteRoot, "ppsspp", "ULES00841.payload"));

        var transport = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        Assert.Equal(2, (await transport.ListAsync()).Count);
        var missing = await Assert.ThrowsAsync<CloudPayloadMissingException>(
            () => transport.DownloadAsync("ppsspp/ULES00841"));
        Assert.Equal("ppsspp/ULES00841", missing.UnitId);
        await transport.FlushAsync();

        // The healthy unit survives; the entry with no payload is gone, so the machine that still
        // has that save will upload it instead of believing the remote already has it.
        var repaired = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        var remaining = await repaired.ListAsync();
        Assert.Equal(["ppsspp/ULUS10277"], remaining.Select(snapshot => snapshot.UnitId));
    }

    [Fact]
    public async Task LocalBackend_VerificationFindsBrokenEntriesTheOwningMachineWouldNeverDownload()
    {
        // The machine that uploaded a save never downloads it, so it cannot discover a failed
        // upload by failing a download. One listing of the remote is how it finds out.
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "verify-remote");
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var cloudFolder = Path.GetFullPath(remoteRoot).Replace('\\', '/');
        var seeding = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        var units = new[] { "rpcs3/savedata/BCES00006", "rpcs3/trophy/NPWR00706_00", "pcsx2/Mcd001.ps2" };
        foreach (var unitId in units)
        {
            await seeding.UploadAsync(
                unitId,
                new MemoryStream(Encoding.UTF8.GetBytes(unitId)),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(unitId))),
                new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
        }

        await seeding.FlushAsync();
        File.Delete(Path.Combine(remoteRoot, "rpcs3", "savedata", "BCES00006.payload"));
        File.Delete(Path.Combine(remoteRoot, "rpcs3", "trophy", "NPWR00706_00.payload"));

        var transport = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        await transport.ListAsync();
        var missing = await transport.FindMissingPayloadsAsync();

        Assert.Equal(["rpcs3/savedata/BCES00006", "rpcs3/trophy/NPWR00706_00"], missing);

        await transport.FlushAsync();
        var repaired = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);

        Assert.Equal(
            ["pcsx2/Mcd001.ps2"],
            (await repaired.ListAsync()).Select(snapshot => snapshot.UnitId));
        Assert.Empty(await repaired.FindMissingPayloadsAsync());
    }

    [Fact]
    public async Task LocalBackend_FolderIdShortcutIsNotAppliedToANonDriveRemote()
    {
        // The id form drops the folder from the remote path, which only works because
        // --drive-root-folder-id re-roots a Drive remote there. Any other backend must keep the path.
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "non-drive-remote");
        Directory.CreateDirectory(remoteRoot);
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var transport = new RcloneCloudSyncTransport(
            AppPaths,
            "testlocal",
            Path.GetFullPath(remoteRoot).Replace('\\', '/'),
            rclonePath);

        Assert.Null(await transport.ResolveCloudFolderIdAsync());
    }

    private static async Task<string> ReadAllAsync(RcloneCloudSyncTransport transport, string unitId)
    {
        await using var stream = await transport.DownloadAsync(unitId);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
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
