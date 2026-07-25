using System.Text;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>
/// Appends a human-readable history of each sync to <c>Saves/sync-log.txt</c> — what was uploaded
/// and downloaded, and which conflicts were resolved (with a note that the older copy was kept) —
/// so the user can review exactly what happened to their saves.
/// </summary>
public sealed class FileSaveSyncLog
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileSaveSyncLog(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        LogPath = Path.Combine(appPaths.SavesDirectory, "sync-log.txt");
    }

    /// <summary>The absolute path of the activity log file.</summary>
    public string LogPath { get; }

    /// <summary>Whether the log file exists yet (i.e. at least one sync has been recorded).</summary>
    public bool Exists => File.Exists(LogPath);

    /// <summary>Records one completed sync/force operation.</summary>
    public async Task AppendAsync(string operation, SaveSyncReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        var entry = Format(operation, report, DateTimeOffset.Now);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            await File.AppendAllTextAsync(LogPath, entry, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Renders one log entry. Public for testing.</summary>
    public static string Format(string operation, SaveSyncReport report, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(report);

        var builder = new StringBuilder();
        builder.AppendLine($"===== {timestamp:yyyy-MM-dd HH:mm:ss} — {operation} =====");
        AppendList(builder, "Uploaded", report.Results.Where(result => result.Action == SaveSyncAction.Upload));
        AppendList(builder, "Downloaded", report.Results.Where(result => result.Action == SaveSyncAction.Download));

        var conflicts = report.Results
            .Where(result => result.Action is SaveSyncAction.ConflictLocalWins or SaveSyncAction.ConflictRemoteWins)
            .ToList();
        if (conflicts.Count > 0)
        {
            builder.AppendLine($"  Conflicts ({conflicts.Count}) — the newer copy was kept and the older backed up under Saves/conflicts:");
            foreach (var conflict in conflicts)
                builder.AppendLine($"    - {conflict.UnitId}: {conflict.Reason}");
        }

        builder.AppendLine($"  Unchanged: {report.Unchanged}");
        if (report.Uploaded == 0 && report.Downloaded == 0 && conflicts.Count == 0)
            builder.AppendLine("  (everything was already in sync)");
        builder.AppendLine();
        return builder.ToString();
    }

    private static void AppendList(StringBuilder builder, string label, IEnumerable<SaveUnitSyncResult> results)
    {
        var list = results.ToList();
        if (list.Count == 0)
            return;

        builder.AppendLine($"  {label} ({list.Count}):");
        foreach (var result in list)
            builder.AppendLine($"    - {result.UnitId}");
    }
}
