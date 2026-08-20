using System.Text.Json;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;

namespace EmuShelf.Infrastructure.Launching;

/// <summary>
/// A <see cref="IPendingPlaySessionStore"/> backed by one small JSON file. It lives in app-private durable
/// storage (not the cache, which the OS may reclaim) so a pending session survives the process being
/// killed while an emulator runs. All operations are best-effort and never throw: a corrupt or missing
/// file reads as "no pending session", so a write/read fault can degrade auto-completion but never crash a
/// launch or a startup.
/// </summary>
public sealed class FilePendingPlaySessionStore(string filePath, IAppLogger? logger = null)
    : IPendingPlaySessionStore
{
    private readonly IAppLogger _logger = logger ?? NullAppLogger.Instance;
    private readonly object _gate = new();

    public void Set(PendingPlaySession session)
    {
        lock (_gate)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                // Write-then-rename so a crash mid-write cannot leave a torn file that reads back as
                // corrupt (and silently drops the session). Move within one directory is atomic.
                var temporaryPath = filePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(session));
                File.Move(temporaryPath, filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.Warning($"Could not persist the pending play session: {ex.Message}");
            }
        }
    }

    public PendingPlaySession? Get()
    {
        lock (_gate)
        {
            try
            {
                return File.Exists(filePath)
                    ? JsonSerializer.Deserialize<PendingPlaySession>(File.ReadAllText(filePath))
                    : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _logger.Warning($"Could not read the pending play session: {ex.Message}");
                return null;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warning($"Could not clear the pending play session: {ex.Message}");
            }
        }
    }
}
