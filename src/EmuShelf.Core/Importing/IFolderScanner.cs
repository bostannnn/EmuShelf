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
    /// Walks <paramref name="folderPath"/> recursively and returns the absolute paths
    /// that are candidate games for <paramref name="system"/>.
    /// </summary>
    Task<IReadOnlyList<string>> ScanAsync(
        string folderPath,
        GameSystem system,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
