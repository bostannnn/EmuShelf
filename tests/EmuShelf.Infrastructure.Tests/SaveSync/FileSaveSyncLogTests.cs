using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class FileSaveSyncLogTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task AppendAsync_CreatesPortableLogWithEveryMeaningfulResult()
    {
        var log = new FileSaveSyncLog(AppPaths);
        var report = new SaveSyncReport(
        [
            new SaveUnitSyncResult("playstation2/Mcd001.ps2", SaveSyncAction.Upload, "Local save was new."),
            new SaveUnitSyncResult("playstation2/Mcd002.ps2", SaveSyncAction.Download, "Cloud save was new."),
            new SaveUnitSyncResult("playstation2/Mcd003.ps2", SaveSyncAction.ConflictLocalWins, "Local copy was newer."),
            new SaveUnitSyncResult("playstation2/Mcd004.ps2", SaveSyncAction.None, "Already current."),
        ]);

        await log.AppendAsync("Sync", report);

        Assert.True(File.Exists(log.LogPath));
        var entry = await File.ReadAllTextAsync(log.LogPath);
        Assert.Contains("— Sync =====", entry);
        Assert.Contains("Uploaded (1):", entry);
        Assert.Contains("playstation2/Mcd001.ps2", entry);
        Assert.Contains("Downloaded (1):", entry);
        Assert.Contains("playstation2/Mcd002.ps2", entry);
        Assert.Contains("Conflicts (1)", entry);
        Assert.Contains("playstation2/Mcd003.ps2: Local copy was newer.", entry);
        Assert.Contains("older backed up under Saves/conflicts", entry);
        Assert.Contains("Unchanged: 1", entry);
    }

    [Fact]
    public void Format_ExplainsWhenNothingNeededSyncing()
    {
        var entry = FileSaveSyncLog.Format(
            "Upload → cloud",
            new SaveSyncReport([new SaveUnitSyncResult("playstation2/Mcd001.ps2", SaveSyncAction.None, "Current.")]),
            new DateTimeOffset(2026, 7, 25, 17, 30, 0, TimeSpan.Zero));

        Assert.Contains("2026-07-25 17:30:00 — Upload → cloud", entry);
        Assert.Contains("Unchanged: 1", entry);
        Assert.Contains("(everything was already in sync)", entry);
        Assert.DoesNotContain("Uploaded (", entry);
        Assert.DoesNotContain("Downloaded (", entry);
        Assert.DoesNotContain("Cloud calls:", entry);
    }

    [Fact]
    public void Format_RecordsHowLongThePassTookAndWhichCloudCallSpentIt()
    {
        var entry = FileSaveSyncLog.Format(
            "Automatic sync (playstation3)",
            new SaveSyncReport([new SaveUnitSyncResult("playstation3/savedata/BCES00006", SaveSyncAction.None, "Current.")]),
            new DateTimeOffset(2026, 7, 26, 17, 30, 0, TimeSpan.Zero),
            TimeSpan.FromSeconds(41.7),
            ["drive list — 12100 ms", "drive upload — 29400 ms"]);

        Assert.Contains("Automatic sync (playstation3) (41.7s)", entry);
        Assert.Contains("Cloud calls:", entry);
        Assert.Contains("drive list — 12100 ms", entry);
        Assert.Contains("drive upload — 29400 ms", entry);
    }
}
