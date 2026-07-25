using System.Diagnostics;
using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Emulators.Pcsx2;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

/// <summary>
/// Opt-in end-to-end sync against a real, already-configured rclone remote. Enabled only when
/// EMUSHELF_TEST_LIVE_SYNC=1; it uses the real app data directory, rclone config, and PCSX2 saves.
/// </summary>
public sealed class LiveCloudSyncTests
{
    [Fact]
    public async Task Sync_AgainstConfiguredRemote_Completes()
    {
        if (Environment.GetEnvironmentVariable("EMUSHELF_TEST_LIVE_SYNC") != "1")
            return;

        var baseDir = Environment.GetEnvironmentVariable("EMUSHELF_TEST_LIVE_BASE")!;
        var pcsx2Dir = Environment.GetEnvironmentVariable("EMUSHELF_TEST_PCSX2_DIR")!;
        var remote = Environment.GetEnvironmentVariable("EMUSHELF_TEST_REMOTE")!;
        var cloudFolder = Environment.GetEnvironmentVariable("EMUSHELF_TEST_CLOUDFOLDER")!;
        var logPath = Environment.GetEnvironmentVariable("EMUSHELF_TEST_LOG")!;

        var appPaths = new AppPaths(baseDir);
        appPaths.EnsureDirectoriesExist();
        var provider = new Pcsx2SaveLocationProvider(pcsx2Dir);
        var memcards = await provider.GetMemoryCardsDirectoryAsync();
        var endpoint = new FileSystemLocalSaveEndpoint(memcards, appPaths);
        var transport = new RcloneCloudSyncTransport(appPaths, remote, cloudFolder);
        var manifests = new JsonSaveSyncManifestStore(appPaths);
        var service = new SaveSyncService(endpoint, transport, manifests);

        var units = await provider.GetSaveUnitsAsync();
        var progress = new LoggingProgress(logPath);
        progress.Write($"START units={units.Count} memcards={memcards}");
        var stopwatch = Stopwatch.StartNew();

        var report = await service.SyncAsync(provider, progress);

        stopwatch.Stop();
        progress.Write(
            $"DONE elapsed={stopwatch.Elapsed.TotalSeconds:0.0}s uploaded={report.Uploaded} " +
            $"downloaded={report.Downloaded} unchanged={report.Unchanged} conflicts={report.Conflicts}");
        Assert.NotEmpty(report.Results);
    }

    private sealed class LoggingProgress(string path) : IProgress<SaveSyncProgress>
    {
        private readonly object _gate = new();

        public void Report(SaveSyncProgress value) =>
            Write($"[{value.Completed}/{value.Total}] {value.Action} {value.CurrentUnit}");

        public void Write(string line)
        {
            lock (_gate)
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}");
        }
    }
}
