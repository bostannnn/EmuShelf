namespace EmuShelf.Infrastructure.Storage;

/// <summary>
/// The production writability probe fed to <c>DataLocationResolver</c>: confirms a directory can actually
/// be created and written, not merely that <c>Directory.Exists</c> reports true. This is what tells a
/// still-valid data folder apart from one on an unmounted SD card or behind a revoked all-files grant,
/// where the path string is unchanged but every write throws.
/// </summary>
public static class DirectoryWritability
{
    public static bool IsWritable(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return false;

        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".emushelf-write-probe.{Guid.NewGuid():N}");
            File.WriteAllText(probePath, string.Empty);
            File.Delete(probePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
