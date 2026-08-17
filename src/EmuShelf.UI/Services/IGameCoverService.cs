namespace EmuShelf.App.Services;

public sealed record ImportedGameCover(string CoverPath, string ThumbnailPath);

public interface IGameCoverService
{
    /// <summary>Copies a source image into Covers and creates its cached display thumbnail.</summary>
    Task<ImportedGameCover> ImportAsync(
        long gameId,
        string sourcePath,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a current cached thumbnail, creating it off-thread when necessary.</summary>
    Task<string?> GetThumbnailAsync(
        long gameId,
        string coverPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a cover and thumbnail only when the path identifies an EmuShelf-owned
    /// asset for this game. User-selected source files are never eligible.
    /// </summary>
    Task DeleteOwnedCoverAsync(
        long gameId,
        string coverPath,
        CancellationToken cancellationToken = default);
}
