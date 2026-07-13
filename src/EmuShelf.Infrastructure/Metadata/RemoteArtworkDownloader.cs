using System.Net;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Metadata;

public sealed class RemoteArtworkDownloader : IRemoteArtworkDownloader
{
    private const long MaximumArtworkBytes = 8 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly string _downloadDirectory;
    private readonly IAppLogger _logger;

    public RemoteArtworkDownloader(
        IAppPaths paths,
        HttpClient httpClient,
        IAppLogger? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger ?? NullAppLogger.Instance;
        _downloadDirectory = Path.Combine(paths.CacheDirectory, "Metadata", "Downloads");
        Directory.CreateDirectory(_downloadDirectory);
    }

    public async Task<DownloadedArtwork?> DownloadFirstAsync(
        IReadOnlyList<ArtworkCandidate> candidates,
        CancellationToken cancellationToken = default)
    {
        foreach (var candidate in candidates)
        {
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(
                    candidate.SourceUri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
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

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                LogCandidateFailure(candidate, "provider returned a non-image response");
                continue;
            }
            if (response.Content.Headers.ContentLength is > MaximumArtworkBytes)
            {
                LogCandidateFailure(candidate, "cover exceeded EmuShelf's safety limit");
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
                    await CopyWithLimitAsync(source, destination, cancellationToken);
                }
                if (!HasSupportedImageSignature(path))
                {
                    File.Delete(path);
                    LogCandidateFailure(candidate, "download did not contain a supported image");
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

    private void LogCandidateFailure(
        ArtworkCandidate candidate,
        string reason,
        Exception? exception = null) => _logger.Warning(
        $"Artwork candidate from {candidate.ProviderId} was skipped because the {reason}: " +
        candidate.SourceUri,
        exception);

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

    private static async Task CopyWithLimitAsync(
        Stream source,
        Stream destination,
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
            if (total > MaximumArtworkBytes)
                throw new InvalidDataException("The downloaded cover exceeded EmuShelf's safety limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}
