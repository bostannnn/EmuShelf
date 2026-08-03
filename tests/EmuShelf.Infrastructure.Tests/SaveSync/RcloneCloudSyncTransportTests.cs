using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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

        await transport.UploadAsync(
            "pcsx2/Mcd001/SLUS-20552",
            new MemoryStream(bytes),
            hash,
            modified,
            compatibility: "pcsx2-2-4-x64");
        await transport.FlushAsync();

        var snapshot = Assert.Single(await transport.ListAsync());
        Assert.Equal("pcsx2/Mcd001/SLUS-20552", snapshot.UnitId);
        Assert.Equal(hash, snapshot.ContentHash);
        Assert.Equal(modified, snapshot.ModifiedUtc);
        Assert.Equal("pcsx2-2-4-x64", snapshot.Compatibility);
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
    public async Task LocalBackend_ALargeFlushCommitsTheIndexPerBatchRatherThanOnceAtTheEnd()
    {
        // Regression: the flush uploaded every payload and then wrote index.json once. Because the
        // index carries the content hash that decides what changed, that made a pass all-or-nothing
        // — an interrupted run lost all of its uploads and re-staged the identical set next time, so
        // a large first sync never converged.
        //
        // Committing per batch is what makes an interrupted pass resumable, and it is asserted
        // structurally rather than by cancelling mid-flight: against a local backend every batch
        // finishes in milliseconds, so a cancellation aimed between two batches would land wherever
        // the scheduler happened to be. Each batch costs exactly two rclone copies — its payloads,
        // then the index that commits them — so the invocation count proves the commit granularity,
        // and payload-before-index ordering (covered separately) makes each of those commits safe.
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "batched-remote");
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var cloudFolder = Path.GetFullPath(remoteRoot).Replace('\\', '/');
        var transport = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);

        // Comfortably more than one batch, so there is more than one commit to observe.
        const int unitCount = 150;
        for (var index = 0; index < unitCount; index++)
        {
            var unitId = $"duckstation/states/GAME{index:000}.sav";
            await transport.UploadAsync(
                unitId,
                new MemoryStream(Encoding.UTF8.GetBytes(unitId)),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(unitId))),
                new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero));
        }

        var reports = new List<SaveTransferProgress>();
        await transport.FlushAsync(new InlineProgress<SaveTransferProgress>(reports.Add));

        // Two copies per batch, and more than one batch: the index was committed as the pass went.
        var copies = transport.Timings.Count(timing => timing.StartsWith("rclone copy", StringComparison.Ordinal));
        Assert.True(copies >= 4, $"expected at least two batched commits, saw {copies} rclone copies");
        Assert.Equal(0, copies % 2);

        // Everything arrived, and every index entry's payload is really on the remote.
        var reader = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        Assert.Equal(unitCount, (await reader.ListAsync()).Count);
        Assert.Empty(await reader.FindMissingPayloadsAsync());

        // Progress was reported in saves, not only as a byte percentage, and reached the total.
        Assert.All(reports, report => Assert.Equal(unitCount, report.TotalUnits));
        Assert.Equal(unitCount, reports[^1].CompletedUnits);
        Assert.Equal(100, reports[^1].Percent);
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
    public async Task LocalBackend_AScopedSessionNamingABrokenEntryStillDeliversTheHealthyUnits()
    {
        // The index can promise a payload that is not there, and rclone fails a --files-from
        // session over one absent file. That must not put every other unit's sync behind it —
        // which is the fault the scoping was added alongside.
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "scoped-broken-remote");
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
        File.Delete(Path.Combine(remoteRoot, "ppsspp", "ULES00841.payload"));

        var transport = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        await transport.ListAsync();
        transport.ExpectDownloads(["ppsspp/ULES00841", "ppsspp/ULUS10277"]);

        Assert.Equal("ppsspp/ULUS10277", await ReadAllAsync(transport, "ppsspp/ULUS10277"));
        await Assert.ThrowsAsync<CloudPayloadMissingException>(
            () => transport.DownloadAsync("ppsspp/ULES00841"));
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

    /// <summary>Reports on the calling thread, so a test can act on a report the moment it happens.</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
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

    [Fact]
    public async Task LocalBackend_EmptyIndexIsRejectedInsteadOfReadingAsAnEmptyCloud()
    {
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "empty-index-remote");
        Directory.CreateDirectory(remoteRoot);
        await File.WriteAllTextAsync(Path.Combine(remoteRoot, "index.json"), string.Empty);
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var transport = new RcloneCloudSyncTransport(
            AppPaths,
            "testlocal",
            Path.GetFullPath(remoteRoot).Replace('\\', '/'),
            rclonePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => transport.ListAsync());
    }

    [Fact]
    public async Task MissingRcloneRemoteIsRejectedInsteadOfReadingAsAnEmptyCloud()
    {
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[some-other-remote]\ntype = local\n");
        var transport = new RcloneCloudSyncTransport(
            AppPaths,
            "missingremote",
            "EmuShelf/Saves",
            rclonePath);

        await Assert.ThrowsAsync<IOException>(() => transport.ListAsync());
    }

    [Fact]
    public async Task LocalBackend_VerificationWithoutTheKnownIndexIsRejected()
    {
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "missing-index-during-verification");
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var cloudFolder = Path.GetFullPath(remoteRoot).Replace('\\', '/');
        var seeding = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        await seeding.UploadAsync(
            "pcsx2/Mcd001.ps2",
            new MemoryStream(Encoding.UTF8.GetBytes("card")),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("card"))),
            new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        await seeding.FlushAsync();

        var transport = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        Assert.Single(await transport.ListAsync());
        File.Delete(Path.Combine(remoteRoot, "index.json"));

        await Assert.ThrowsAsync<IOException>(() => transport.ListAsync());
        await Assert.ThrowsAsync<IOException>(() => transport.FindMissingPayloadsAsync());
    }

    [Fact]
    public async Task DownloadOperationalFailureIsNotClassifiedAsAMissingPayload()
    {
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "download-failure-remote");
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        var configurationPath = Path.Combine(AppPaths.SettingsDirectory, "rclone.conf");
        await File.WriteAllTextAsync(configurationPath, "[testlocal]\ntype = local\n");
        var cloudFolder = Path.GetFullPath(remoteRoot).Replace('\\', '/');
        var seeding = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        await seeding.UploadAsync(
            "pcsx2/Mcd001.ps2",
            new MemoryStream(Encoding.UTF8.GetBytes("card")),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("card"))),
            new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        await seeding.FlushAsync();

        var transport = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        Assert.Single(await transport.ListAsync());
        transport.ExpectDownloads(["pcsx2/Mcd001.ps2"]);
        await File.WriteAllTextAsync(configurationPath, "[some-other-remote]\ntype = local\n");

        var failure = await Assert.ThrowsAsync<IOException>(
            () => transport.DownloadAsync("pcsx2/Mcd001.ps2"));
        Assert.IsNotType<CloudPayloadMissingException>(failure);
    }

    [Fact]
    public async Task CallerCancellationStopsTheRcloneProcess()
    {
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "cancelled-upload-remote");
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var transport = new RcloneCloudSyncTransport(
            AppPaths,
            "testlocal",
            Path.GetFullPath(remoteRoot).Replace('\\', '/'),
            rclonePath);
        await transport.UploadAsync(
            "pcsx2/Mcd001.ps2",
            new MemoryStream(new byte[64 * 1024]),
            Convert.ToHexString(SHA256.HashData(new byte[64 * 1024])),
            new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));

        var processName = Path.GetFileNameWithoutExtension(rclonePath);
        var existingProcessIds = Process.GetProcessesByName(processName)
            .Select(process => process.Id)
            .ToHashSet();
        var previousBandwidthLimit = Environment.GetEnvironmentVariable("RCLONE_BWLIMIT");
        try
        {
            Environment.SetEnvironmentVariable("RCLONE_BWLIMIT", "1Ki");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => transport.FlushAsync(cancellationToken: cancellation.Token));

            await Task.Delay(250);
            var survivingProcessIds = Process.GetProcessesByName(processName)
                .Where(process => !existingProcessIds.Contains(process.Id) && !process.HasExited)
                .Select(process => process.Id)
                .ToList();
            Assert.Empty(survivingProcessIds);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RCLONE_BWLIMIT", previousBandwidthLimit);
        }
    }

    [Fact]
    public async Task LocalBackend_NullIndexIsRejectedInsteadOfReadingAsAnEmptyCloud()
    {
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "null-index-remote");
        Directory.CreateDirectory(remoteRoot);
        await File.WriteAllTextAsync(Path.Combine(remoteRoot, "index.json"), "null");
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var transport = new RcloneCloudSyncTransport(
            AppPaths,
            "testlocal",
            Path.GetFullPath(remoteRoot).Replace('\\', '/'),
            rclonePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => transport.ListAsync());
    }

    [Fact]
    public async Task LocalBackend_DuplicateIndexUnitIsRejected()
    {
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "duplicate-index-remote");
        Directory.CreateDirectory(remoteRoot);
        var modified = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        var entries = new[]
        {
            new { UnitId = "pcsx2/Mcd001.ps2", ContentHash = new string('A', 64), ModifiedUtc = modified },
            new { UnitId = "pcsx2/Mcd001.ps2", ContentHash = new string('B', 64), ModifiedUtc = modified },
        };
        await File.WriteAllTextAsync(
            Path.Combine(remoteRoot, "index.json"),
            JsonSerializer.Serialize(entries));
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var transport = new RcloneCloudSyncTransport(
            AppPaths,
            "testlocal",
            Path.GetFullPath(remoteRoot).Replace('\\', '/'),
            rclonePath);

        await Assert.ThrowsAsync<InvalidDataException>(() => transport.ListAsync());
    }

    [Fact]
    public async Task LocalBackend_ExplicitUploadIsNotSkippedWhenSizeAndTimestampMatch()
    {
        var rclonePath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_RCLONE_PATH");
        if (string.IsNullOrWhiteSpace(rclonePath) || !File.Exists(rclonePath))
            return;

        var remoteRoot = Path.Combine(BaseDirectory, "same-time-upload-remote");
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(AppPaths.SettingsDirectory, "rclone.conf"),
            "[testlocal]\ntype = local\n");
        var cloudFolder = Path.GetFullPath(remoteRoot).Replace('\\', '/');
        var modified = new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        var oldBytes = Encoding.UTF8.GetBytes("old!");
        var seeding = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        await seeding.UploadAsync(
            "pcsx2/Mcd001.ps2",
            new MemoryStream(oldBytes),
            Convert.ToHexString(SHA256.HashData(oldBytes)),
            modified);
        await seeding.FlushAsync();

        var newBytes = Encoding.UTF8.GetBytes("new!");
        var newHash = Convert.ToHexString(SHA256.HashData(newBytes));
        var transport = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        Assert.Single(await transport.ListAsync());
        await transport.UploadAsync(
            "pcsx2/Mcd001.ps2",
            new MemoryStream(newBytes),
            newHash,
            modified);
        var stagedPayload = Assert.Single(Directory.EnumerateFiles(
            Path.Combine(AppPaths.SavesDirectory, "transfers"),
            "*.payload",
            SearchOption.AllDirectories));
        var remotePayload = Path.Combine(remoteRoot, "pcsx2", "Mcd001.ps2.payload");
        File.SetLastWriteTimeUtc(remotePayload, File.GetLastWriteTimeUtc(stagedPayload));

        await transport.FlushAsync();

        var verifying = new RcloneCloudSyncTransport(AppPaths, "testlocal", cloudFolder, rclonePath);
        Assert.Equal(newHash, Assert.Single(await verifying.ListAsync()).ContentHash);
        Assert.Equal("new!", await ReadAllAsync(verifying, "pcsx2/Mcd001.ps2"));
    }
}
