using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Media.Imaging;
using EmuShelf.Core.Storage;

namespace EmuShelf.App.Services;

public sealed class GameCoverService : IGameCoverService
{
    private const int ThumbnailWidth = 300;
    private const int ThumbnailHeight = 400;

    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

    private readonly string _coversDirectory;
    private readonly string _thumbnailDirectory;
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _gameLocks = new();

    public GameCoverService(IAppPaths paths)
    {
        _coversDirectory = Path.GetFullPath(paths.CoversDirectory);
        _thumbnailDirectory = Path.GetFullPath(Path.Combine(paths.CacheDirectory, "Covers"));
        Directory.CreateDirectory(_coversDirectory);
        Directory.CreateDirectory(_thumbnailDirectory);
    }

    public Task<ImportedGameCover> ImportAsync(
        long gameId,
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            var extension = Path.GetExtension(sourcePath);
            if (!SupportedExtensions.Contains(extension))
                throw new InvalidDataException("Choose a PNG, JPEG, WebP, or BMP image.");
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("The selected cover image is unavailable.", sourcePath);

            var gameLock = _gameLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
            await gameLock.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assetName = $"{gameId}-{Guid.NewGuid():N}";
                var coverPath = Path.Combine(
                    _coversDirectory,
                    $"{assetName}{extension.ToLowerInvariant()}");
                var thumbnailPath = GetThumbnailPath(gameId, coverPath);
                var coverTemp = coverPath + $".{Guid.NewGuid():N}.tmp";
                var thumbnailTemp = thumbnailPath + $".{Guid.NewGuid():N}.tmp";
                var coverInstalled = false;
                var thumbnailInstalled = false;

                try
                {
                    File.Copy(sourcePath, coverTemp);
                    CreateThumbnail(coverTemp, thumbnailTemp);

                    File.Move(coverTemp, coverPath);
                    coverInstalled = true;
                    File.Move(thumbnailTemp, thumbnailPath);
                    thumbnailInstalled = true;

                    return new ImportedGameCover(coverPath, thumbnailPath);
                }
                catch
                {
                    if (thumbnailInstalled)
                        File.Delete(thumbnailPath);
                    if (coverInstalled)
                        File.Delete(coverPath);
                    throw;
                }
                finally
                {
                    File.Delete(coverTemp);
                    File.Delete(thumbnailTemp);
                }
            }
            finally
            {
                gameLock.Release();
            }
        }, cancellationToken);

    public Task<string?> GetThumbnailAsync(
        long gameId,
        string coverPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            var gameLock = _gameLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
            await gameLock.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(coverPath))
                    return null;

                var thumbnailPath = GetThumbnailPath(gameId, coverPath);
                if (File.Exists(thumbnailPath) &&
                    File.GetLastWriteTimeUtc(thumbnailPath) >= File.GetLastWriteTimeUtc(coverPath))
                {
                    return thumbnailPath;
                }

                var thumbnailTemp = thumbnailPath + $".{Guid.NewGuid():N}.tmp";
                try
                {
                    CreateThumbnail(coverPath, thumbnailTemp);
                    File.Move(thumbnailTemp, thumbnailPath, overwrite: true);
                    return thumbnailPath;
                }
                finally
                {
                    File.Delete(thumbnailTemp);
                }
            }
            finally
            {
                gameLock.Release();
            }
        }, cancellationToken);

    public Task DeleteOwnedCoverAsync(
        long gameId,
        string coverPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(async () =>
        {
            if (!TryGetOwnedCoverPath(gameId, coverPath, out var ownedCoverPath))
                return;

            var gameLock = _gameLocks.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
            await gameLock.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(ownedCoverPath);
                File.Delete(GetThumbnailPath(gameId, ownedCoverPath));
            }
            finally
            {
                gameLock.Release();
            }
        }, cancellationToken);

    private string GetThumbnailPath(long gameId, string coverPath)
    {
        var id = gameId.ToString(CultureInfo.InvariantCulture);
        var coverName = Path.GetFileNameWithoutExtension(coverPath);
        var cacheName = string.Equals(coverName, id, StringComparison.Ordinal) ||
            coverName.StartsWith($"{id}-", StringComparison.Ordinal)
                ? coverName
                : id;
        return Path.Combine(_thumbnailDirectory, $"{cacheName}.png");
    }

    private bool TryGetOwnedCoverPath(long gameId, string coverPath, out string ownedCoverPath)
    {
        ownedCoverPath = Path.GetFullPath(coverPath);
        var comparison = FilePathComparison.Comparison;
        if (!string.Equals(
                Path.GetDirectoryName(ownedCoverPath),
                _coversDirectory,
                comparison))
        {
            return false;
        }

        var id = gameId.ToString(CultureInfo.InvariantCulture);
        var coverName = Path.GetFileNameWithoutExtension(ownedCoverPath);
        return string.Equals(coverName, id, StringComparison.Ordinal) ||
            coverName.StartsWith($"{id}-", StringComparison.Ordinal);
    }

    private static void CreateThumbnail(string sourcePath, string destinationPath)
    {
        using var thumbnail = SafeImageDecoder.DecodeToFit(
            sourcePath,
            ThumbnailWidth,
            ThumbnailHeight);
        using var destination = File.Create(destinationPath);
        thumbnail.Save(destination, PngBitmapEncoderOptions.Default);
    }

    internal static PixelSize CalculateThumbnailSize(PixelSize sourceSize)
    {
        var scale = Math.Min(
            1d,
            Math.Min(
                ThumbnailWidth / (double)sourceSize.Width,
                ThumbnailHeight / (double)sourceSize.Height));
        return new PixelSize(
            Math.Max(1, (int)Math.Round(sourceSize.Width * scale)),
            Math.Max(1, (int)Math.Round(sourceSize.Height * scale)));
    }
}
