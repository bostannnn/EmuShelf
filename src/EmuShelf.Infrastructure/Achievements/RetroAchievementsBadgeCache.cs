using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>
/// On-demand cache for the public PNG badges referenced by RetroAchievements game detail. The
/// cache is bounded by both item count and bytes, atomically writes only PNG responses, and
/// coalesces duplicate requests so opening/reopening a popup cannot start parallel downloads for
/// the same badge.
/// </summary>
public sealed class RetroAchievementsBadgeCache : IRetroAchievementsBadgeCache
{
    public const string DefaultMediaBaseAddress = "https://media.retroachievements.org/Badge/";
    private const int DefaultMaximumEntries = 750;
    private const long DefaultMaximumBytes = 96 * 1024 * 1024;
    private const int MaximumBadgeBytes = 1024 * 1024;
    private static readonly Regex BadgeNamePattern = new(
        "^[A-Za-z0-9_-]{1,100}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    private readonly string _directory;
    private readonly HttpClient _httpClient;
    private readonly IAppLogger _logger;
    private readonly Uri _mediaBaseAddress;
    private readonly int _maximumEntries;
    private readonly long _maximumBytes;
    private readonly SemaphoreSlim _downloadLimit = new(4, 4);
    private readonly SemaphoreSlim _trimLock = new(1, 1);
    private readonly ConcurrentDictionary<string, Task<string?>> _inFlight = new(StringComparer.Ordinal);

    public RetroAchievementsBadgeCache(
        IAppPaths paths,
        HttpClient httpClient,
        IAppLogger? logger = null,
        string mediaBaseAddress = DefaultMediaBaseAddress,
        int maximumEntries = DefaultMaximumEntries,
        long maximumBytes = DefaultMaximumBytes)
    {
        if (maximumEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        _directory = Path.Combine(paths.CacheDirectory, "RetroAchievements", "Badges");
        _httpClient = httpClient;
        _logger = logger ?? NullAppLogger.Instance;
        _mediaBaseAddress = new Uri(mediaBaseAddress, UriKind.Absolute);
        _maximumEntries = maximumEntries;
        _maximumBytes = maximumBytes;
    }

    public async Task<string?> GetBadgePathAsync(
        string badgeName,
        CancellationToken cancellationToken = default)
    {
        if (!BadgeNamePattern.IsMatch(badgeName))
            return null;

        var path = GetPath(badgeName);
        if (TryUseCached(path))
            return path;

        var request = _inFlight.GetOrAdd(badgeName, DownloadAsync);
        try
        {
            return await request.WaitAsync(cancellationToken);
        }
        finally
        {
            if (request.IsCompleted)
                _inFlight.TryRemove(new KeyValuePair<string, Task<string?>>(badgeName, request));
        }
    }

    private async Task<string?> DownloadAsync(string badgeName)
    {
        await _downloadLimit.WaitAsync();
        try
        {
            var path = GetPath(badgeName);
            if (TryUseCached(path))
                return path;

            using var response = await _httpClient.GetAsync(
                new Uri(_mediaBaseAddress, badgeName + ".png"),
                HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                _logger.Information(
                    $"RetroAchievements badge {badgeName} was unavailable ({(int)response.StatusCode}).");
                return null;
            }

            var bytes = await ReadPngAsync(response.Content);
            if (bytes is null)
            {
                _logger.Warning($"RetroAchievements badge {badgeName} returned an invalid image.");
                return null;
            }
            if (bytes.Length > _maximumBytes)
            {
                _logger.Warning($"RetroAchievements badge {badgeName} exceeds the local cache limit.");
                return null;
            }

            Directory.CreateDirectory(_directory);
            await _trimLock.WaitAsync();
            try
            {
                TrimForIncoming(bytes.Length);
                AtomicFile.WriteAllBytes(path, bytes);
            }
            finally
            {
                _trimLock.Release();
            }

            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            _logger.Information($"RetroAchievements badge {badgeName} could not be downloaded ({ex.GetType().Name}).");
            return null;
        }
        finally
        {
            _downloadLimit.Release();
        }
    }

    private static async Task<byte[]?> ReadPngAsync(HttpContent content)
    {
        if (content.Headers.ContentLength is { } length &&
            (length <= PngSignature.Length || length > MaximumBadgeBytes))
        {
            return null;
        }

        await using var source = await content.ReadAsStreamAsync();
        await using var destination = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(buffer);
            if (read == 0)
                break;
            if (destination.Length + read > MaximumBadgeBytes)
                return null;
            await destination.WriteAsync(buffer.AsMemory(0, read));
        }

        var bytes = destination.ToArray();
        return bytes.Length > PngSignature.Length && bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature)
            ? bytes
            : null;
    }

    private void TrimForIncoming(long incomingBytes)
    {
        if (!Directory.Exists(_directory))
            return;

        var entries = new DirectoryInfo(_directory)
            .EnumerateFiles("*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.LastAccessTimeUtc)
            .ToList();
        var totalBytes = entries.Sum(file => file.Length);
        while (entries.Count >= _maximumEntries || totalBytes + incomingBytes > _maximumBytes)
        {
            if (entries.Count == 0)
                return;

            var oldest = entries[0];
            entries.RemoveAt(0);
            totalBytes -= oldest.Length;
            try
            {
                oldest.Delete();
            }
            catch (IOException)
            {
                // A failed cache trim only affects future cache capacity; it must never affect
                // popup rendering or a newly downloaded valid badge.
            }
        }
    }

    private string GetPath(string badgeName) => Path.Combine(_directory, badgeName + ".png");

    private static bool TryUseCached(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length <= PngSignature.Length)
                return false;
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
