using EmuShelf.Core.Systems;

namespace EmuShelf.Core.Importing;

/// <summary>
/// Recursively walks a folder for candidate game files. Runs off the UI thread;
/// reports progress and honours cancellation. This is the shared scanner the
/// design doc calls for — per-system format quirks live behind <see cref="IGameImportRules"/>.
/// </summary>
public interface IFolderScanner
{
    /// <summary>
    /// Walks <paramref name="folderPath"/> recursively and returns the entries to
    /// persist plus referenced component paths to suppress for <paramref name="system"/>.
    /// </summary>
    Task<GameEntrySelection> ScanAsync(
        string folderPath,
        GameSystem system,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
