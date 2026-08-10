using System.Net;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Metadata;

public sealed class RemoteArtworkDownloader : IRemoteArtworkDownloader
{
    private const long MaximumArtworkBytes = 8 * 1024 * 1024;
    // Videos are much larger than still artwork; cap them well above image size but still bounded so
    // one runaway file cannot fill a portable drive.
    private const long MaximumVideoBytes = 64 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly string _downloadDirectory;
    private readonly IAppLogger _logger;
    private readonly IRemoteArtworkUriPolicy? _uriPolicy;

    public RemoteArtworkDownloader(
        IAppPaths paths,
        HttpClient httpClient,
        IAppLogger? logger = null,
        IRemoteArtworkUriPolicy? uriPolicy = null)
    {
        _httpClient = httpClient;
        _logger = logger ?? NullAppLogger.Instance;
        _uriPolicy = uriPolicy;
        _downloadDirectory = Path.Combine(paths.CacheDirectory, "Metadata", "Downloads");
        Directory.CreateDirectory(_downloadDirectory);
    }

    public async Task<DownloadedArtwork?> DownloadFirstAsync(
        IReadOnlyList<ArtworkCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        foreach (var candidate in candidates)
        {
            if (candidate.SourceUri.IsFile)
            {
                var localArtwork = await CopyLocalArtworkAsync(candidate, cancellationToken);
                if (localArtwork is not null)
                    return localArtwork;
                continue;
            }

            HttpResponseMessage response;
            try
            {
                response = await GetWithRetryAsync(candidate.SourceUri, cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                LogCandidateFailure(candidate, "request timed out", ex);
                continue;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                LogCandidateFailure(candidate, "request failed", ex);
                continue;
            }
            catch (InvalidDataException ex)
            {
                LogCandidateFailure(candidate, "address was blocked by the web-cover safety policy", ex);
                continue;
            }

            using var _ = response;
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.Information(
                    $"Artwork candidate was not found from {candidate.ProviderId}: " +
                    candidate.SourceUri);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                LogCandidateFailure(
                    candidate,
                    $"provider returned HTTP {(int)response.StatusCode} ({response.StatusCode})");
                continue;
            }

            var expectedPrefix = candidate.MediaKind == RemoteMediaKind.Video ? "video/" : "image/";
            var maxBytes = MaxBytesFor(candidate.MediaKind);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !mediaType.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                LogCandidateFailure(candidate, $"provider returned a non-{TypeNoun(candidate.MediaKind)} response");
                continue;
            }
            if (response.Content.Headers.ContentLength is { } declaredLength && declaredLength > maxBytes)
            {
                LogCandidateFailure(candidate, "media exceeded EmuShelf's safety limit");
                continue;
            }

            var path = Path.Combine(
                _downloadDirectory,
                $"{Guid.NewGuid():N}{candidate.FileExtension}");
            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using (var destination = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous))
                {
                    await CopyWithLimitAsync(source, destination, maxBytes, cancellationToken);
                }
                if (!HasSupportedSignature(path, candidate.MediaKind))
                {
                    File.Delete(path);
                    LogCandidateFailure(candidate, $"download did not contain a supported {TypeNoun(candidate.MediaKind)}");
                    continue;
                }
                return new DownloadedArtwork(candidate, path);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                File.Delete(path);
                LogCandidateFailure(candidate, "download timed out", ex);
            }
            catch (OperationCanceledException)
            {
                File.Delete(path);
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException)
            {
                File.Delete(path);
                LogCandidateFailure(candidate, "download could not be completed", ex);
            }
        }
        return null;
    }

    private async Task<DownloadedArtwork?> CopyLocalArtworkAsync(
        ArtworkCandidate candidate,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_downloadDirectory, $"{Guid.NewGuid():N}{candidate.FileExtension}");
        try
        {
            await using var source = new FileStream(
                candidate.SourceUri.LocalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            await using (var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                await CopyWithLimitAsync(source, destination, MaxBytesFor(candidate.MediaKind), cancellationToken);
            }
            if (!HasSupportedSignature(path, candidate.MediaKind))
            {
                File.Delete(path);
                LogCandidateFailure(candidate, $"local file was not a supported {TypeNoun(candidate.MediaKind)}");
                return null;
            }
            return new DownloadedArtwork(candidate, path);
        }
        catch (OperationCanceledException)
        {
            File.Delete(path);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            File.Delete(path);
            LogCandidateFailure(candidate, "local file could not be copied", ex);
            return null;
        }
    }

    // Cover hosts (GitHub raw, CDNs) rate-limit bulk fetches with HTTP 429/503. A whole
    // library's worth of covers can trip that in a burst, so a throttled response is
    // retried with a short, capped backoff instead of being dropped like a 404.
    private async Task<HttpResponseMessage> GetWithRetryAsync(Uri uri, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var response = await GetFollowingRedirectsAsync(uri, cancellationToken);
            if (attempt >= maxAttempts || !IsThrottled(response.StatusCode))
                return response;

            var status = response.StatusCode;
            var delay = ThrottleDelay(response, attempt);
            response.Dispose();
            _logger.Information(
                $"Artwork host returned HTTP {(int)status}; retrying in {delay.TotalSeconds:0.#}s: {uri}");
            await Task.Delay(delay, cancellationToken);
        }
    }

    private async Task<HttpResponseMessage> GetFollowingRedirectsAsync(
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        const int maximumRedirects = 5;
        var uri = initialUri;
        for (var redirect = 0; ; redirect++)
        {
            if (_uriPolicy is not null && !await _uriPolicy.IsAllowedAsync(uri, cancellationToken))
                throw new InvalidDataException($"Artwork address is not publicly routable: {uri}");

            var response = await _httpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!IsRedirect(response.StatusCode))
                return response;

            if (redirect >= maximumRedirects || response.Headers.Location is null)
            {
                response.Dispose();
                throw new HttpRequestException("The artwork host returned too many redirects.");
            }

            var nextUri = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : Uri.TryCreate(uri, response.Headers.Location, out var resolvedUri)
                    ? resolvedUri
                    : null;
            response.Dispose();
            if (nextUri is null)
                throw new HttpRequestException("The artwork host returned an invalid redirect address.");
            uri = nextUri;
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static bool IsThrottled(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private static TimeSpan ThrottleDelay(HttpResponseMessage response, int attempt)
    {
        // Honor Retry-After when the host sends it, but cap it so one slow host cannot
        // stall an entire library's enrichment; otherwise use exponential backoff.
        var retryAfter = response.Headers.RetryAfter;
        var suggested = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
            ?? TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt - 1));
        if (suggested < TimeSpan.Zero)
            suggested = TimeSpan.Zero;
        var cap = TimeSpan.FromSeconds(5);
        return suggested > cap ? cap : suggested;
    }

    private void LogCandidateFailure(
        ArtworkCandidate candidate,
        string reason,
        Exception? exception = null) => _logger.Warning(
        $"Artwork candidate from {candidate.ProviderId} was skipped because the {reason}: " +
        candidate.SourceUri,
        exception);

    private static long MaxBytesFor(RemoteMediaKind kind) =>
        kind == RemoteMediaKind.Video ? MaximumVideoBytes : MaximumArtworkBytes;

    private static string TypeNoun(RemoteMediaKind kind) =>
        kind == RemoteMediaKind.Video ? "video" : "image";

    private static bool HasSupportedSignature(string path, RemoteMediaKind kind) =>
        kind == RemoteMediaKind.Video ? HasSupportedVideoSignature(path) : HasSupportedImageSignature(path);

    private static bool HasSupportedImageSignature(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header);
        return (read >= 8 && header[..8].SequenceEqual(
                    new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) ||
               (read >= 3 && header[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })) ||
               (read >= 2 && header[..2].SequenceEqual("BM"u8)) ||
               (read >= 12 && header[..4].SequenceEqual("RIFF"u8) &&
                              header[8..12].SequenceEqual("WEBP"u8));
    }

    // ScreenScraper videos are ISO Base Media (MP4), whose first box is the "ftyp" file-type box at
    // offset 4. A size prefix precedes it, so bytes 4..8 carry the "ftyp" marker.
    private static bool HasSupportedVideoSignature(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header);
        return read >= 8 && header[4..8].SequenceEqual("ftyp"u8);
    }

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        var total = 0L;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            total += read;
            if (total > maxBytes)
                throw new InvalidDataException("The downloaded media exceeded EmuShelf's safety limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
