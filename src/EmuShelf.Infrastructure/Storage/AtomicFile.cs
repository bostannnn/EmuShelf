namespace EmuShelf.Infrastructure.Storage;

/// <summary>
/// Durable file writes: content is written to a sibling <c>.tmp</c> file and then renamed over
/// the target, so a crash or removed drive mid-write can never truncate the live file into a
/// partial state. Centralizes the pattern used by settings, the credential blob, and caches.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);
        File.Move(tempPath, path, overwrite: true);
    }

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, bytes);
        File.Move(tempPath, path, overwrite: true);
    }

    public static async Task WriteAsync(
        string path,
        Func<Stream, CancellationToken, Task> writeContents,
        CancellationToken cancellationToken = default)
    {
        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
            await writeContents(stream, cancellationToken);
        File.Move(tempPath, path, overwrite: true);
    }
}
