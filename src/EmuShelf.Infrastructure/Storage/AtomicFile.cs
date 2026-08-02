namespace EmuShelf.Infrastructure.Storage;

/// <summary>
/// Durable file writes: content is written to a sibling <c>.tmp</c> file and then renamed over
/// the target, so a crash or removed drive mid-write can never truncate the live file into a
/// partial state. Centralizes the pattern used by settings, the credential blob, and caches.
/// </summary>
public static class AtomicFile
{
    private const int ReplaceAttempts = 8;

    public static void WriteAllText(string path, string contents)
    {
        var tempPath = CreateTempPath(path);
        try
        {
            File.WriteAllText(tempPath, contents);
            ReplaceWithRetry(tempPath, path);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var tempPath = CreateTempPath(path);
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            ReplaceWithRetry(tempPath, path);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    public static async Task WriteAsync(
        string path,
        Func<Stream, CancellationToken, Task> writeContents,
        CancellationToken cancellationToken = default)
    {
        var tempPath = CreateTempPath(path);
        try
        {
            await using (var stream = File.Create(tempPath))
                await writeContents(stream, cancellationToken);
            await ReplaceWithRetryAsync(tempPath, path, cancellationToken);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    private static string CreateTempPath(string path) =>
        $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";

    private static void ReplaceWithRetry(string tempPath, string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (
                (ex is IOException or UnauthorizedAccessException) && attempt < ReplaceAttempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(25 * attempt));
            }
        }
    }

    private static async Task ReplaceWithRetryAsync(
        string tempPath,
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (
                (ex is IOException or UnauthorizedAccessException) && attempt < ReplaceAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), cancellationToken);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
