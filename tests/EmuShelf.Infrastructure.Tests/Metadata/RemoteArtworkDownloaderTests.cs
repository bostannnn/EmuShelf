using System.Net;
using System.Net.Http.Headers;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Metadata;
using EmuShelf.Infrastructure.Metadata;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class RemoteArtworkDownloaderTests : TempAppDirectoryTestBase
{
    private static readonly byte[] PngSignature =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // ISO Base Media (MP4): a 4-byte box size, then the "ftyp" box type, then a major brand.
    private static readonly byte[] Mp4Signature =
        [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D];

    [Fact]
    public async Task DownloadFirstAsync_FallsBackAfterMissingCandidate()
    {
        AppPaths.EnsureDirectoriesExist();
        var requested = new List<Uri>();
        using var httpClient = new HttpClient(new DelegateHandler(request =>
        {
            requested.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath.Contains("missing", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(PngSignature),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        }));
        var downloader = new RemoteArtworkDownloader(AppPaths, httpClient);
        var candidates = new[]
        {
            new ArtworkCandidate(
                "primary",
                new Uri("https://example.test/missing.jpg"),
                ".jpg"),
            new ArtworkCandidate(
                "fallback",
                new Uri("https://example.test/available.png"),
                ".png"),
        };

        var downloaded = await downloader.DownloadFirstAsync(candidates);

        Assert.NotNull(downloaded);
        Assert.Equal("fallback", downloaded.Candidate.ProviderId);
        Assert.Equal(2, requested.Count);
        Assert.Equal(PngSignature, await File.ReadAllBytesAsync(downloaded.TemporaryPath));
        File.Delete(downloaded.TemporaryPath);
    }

    [Fact]
    public async Task DownloadFirstAsync_FallsBackAfterServerError()
    {
        AppPaths.EnsureDirectoriesExist();
        var logger = new RecordingLogger();
        using var httpClient = new HttpClient(new DelegateHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("primary", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(PngSignature),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        }));
        var downloader = new RemoteArtworkDownloader(AppPaths, httpClient, logger);

        var downloaded = await downloader.DownloadFirstAsync(
        [
            new ArtworkCandidate(
                "primary",
                new Uri("https://example.test/primary.jpg"),
                ".jpg"),
            new ArtworkCandidate(
                "fallback",
                new Uri("https://example.test/fallback.png"),
                ".png"),
        ]);

        Assert.NotNull(downloaded);
        Assert.Equal("fallback", downloaded.Candidate.ProviderId);
        Assert.Contains(logger.Warnings, message => message.Contains("HTTP 503", StringComparison.Ordinal));
        File.Delete(downloaded.TemporaryPath);
    }

    [Fact]
    public async Task DownloadFirstAsync_FallsBackAfterNonImageResponse()
    {
        AppPaths.EnsureDirectoriesExist();
        var logger = new RecordingLogger();
        using var httpClient = new HttpClient(new DelegateHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("not-image", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("not an image"),
                };
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(PngSignature),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        }));
        var downloader = new RemoteArtworkDownloader(AppPaths, httpClient, logger);

        var downloaded = await downloader.DownloadFirstAsync(
        [
            new ArtworkCandidate(
                "primary",
                new Uri("https://example.test/not-image"),
                ".jpg"),
            new ArtworkCandidate(
                "fallback",
                new Uri("https://example.test/fallback.png"),
                ".png"),
        ]);

        Assert.NotNull(downloaded);
        Assert.Equal("fallback", downloaded.Candidate.ProviderId);
        Assert.Contains(logger.Warnings, message => message.Contains("non-image", StringComparison.Ordinal));
        File.Delete(downloaded.TemporaryPath);
    }

    [Fact]
    public async Task DownloadFirstAsync_FallsBackAfterInvalidImagePayload()
    {
        AppPaths.EnsureDirectoriesExist();
        var logger = new RecordingLogger();
        using var httpClient = new HttpClient(new DelegateHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(
                    request.RequestUri!.AbsolutePath.Contains("invalid", StringComparison.Ordinal)
                        ? "not really png"u8.ToArray()
                        : PngSignature),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return response;
        }));
        var downloader = new RemoteArtworkDownloader(AppPaths, httpClient, logger);

        var downloaded = await downloader.DownloadFirstAsync(
        [
            new ArtworkCandidate(
                "primary",
                new Uri("https://example.test/invalid.png"),
                ".png"),
            new ArtworkCandidate(
                "fallback",
                new Uri("https://example.test/fallback.png"),
                ".png"),
        ]);

        Assert.NotNull(downloaded);
        Assert.Equal("fallback", downloaded.Candidate.ProviderId);
        Assert.Contains(
            logger.Warnings,
            message => message.Contains("supported image", StringComparison.Ordinal));
        File.Delete(downloaded.TemporaryPath);
    }

    [Fact]
    public async Task DownloadFirstAsync_CopiesLocalArtworkWithoutAnHttpRequest()
    {
        AppPaths.EnsureDirectoriesExist();
        var source = Path.Combine(BaseDirectory, "cover.png");
        await File.WriteAllBytesAsync(source, PngSignature);
        using var httpClient = new HttpClient(new DelegateHandler(_ =>
            throw new Xunit.Sdk.XunitException("HTTP must not be used for local artwork.")));
        var downloader = new RemoteArtworkDownloader(AppPaths, httpClient);

        var downloaded = await downloader.DownloadFirstAsync(
        [
            new ArtworkCandidate("local", new Uri(source), ".png"),
        ]);

        Assert.NotNull(downloaded);
        Assert.Equal("local", downloaded.Candidate.ProviderId);
        Assert.Equal(PngSignature, await File.ReadAllBytesAsync(downloaded.TemporaryPath));
        File.Delete(downloaded.TemporaryPath);
    }

    [Fact]
    public async Task DownloadFirstAsync_BlocksRedirectToPrivateAddress()
    {
        AppPaths.EnsureDirectoriesExist();
        var requested = new List<Uri>();
        using var httpClient = new HttpClient(new DelegateHandler(request =>
        {
            requested.Add(request.RequestUri!);
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://127.0.0.1/private-cover.png");
            return response;
        }));
        var policy = new RecordingUriPolicy(uri => uri.Host == "images.example");
        var downloader = new RemoteArtworkDownloader(AppPaths, httpClient, uriPolicy: policy);

        var downloaded = await downloader.DownloadFirstAsync(
        [
            new ArtworkCandidate(
                "web-search",
                new Uri("https://images.example/cover.png"),
                ".png"),
        ]);

        Assert.Null(downloaded);
        Assert.Single(requested);
        Assert.Equal(
            ["images.example", "127.0.0.1"],
            policy.CheckedUris.Select(uri => uri.Host).ToArray());
    }

    [Fact]
    public async Task DownloadFirstAsync_DownloadsMp4_ForVideoCandidate()
    {
        AppPaths.EnsureDirectoriesExist();
        using var httpClient = new HttpClient(new DelegateHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Mp4Signature),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            return response;
        }));
        var downloader = new RemoteArtworkDownloader(AppPaths, httpClient);

        var downloaded = await downloader.DownloadFirstAsync(
        [
            new ArtworkCandidate(
                "screenscraper",
                new Uri("https://example.test/clip.mp4"),
                ".mp4",
                RemoteMediaKind.Video),
        ]);

        Assert.NotNull(downloaded);
        Assert.Equal(Mp4Signature, await File.ReadAllBytesAsync(downloaded.TemporaryPath));
        File.Delete(downloaded.TemporaryPath);
    }

    [Fact]
    public async Task DownloadFirstAsync_RejectsImageContentType_ForVideoCandidate()
    {
        AppPaths.EnsureDirectoriesExist();
        var logger = new RecordingLogger();
        using var httpClient = new HttpClient(new DelegateHandler(request =>
        {
            var isNormalized = request.RequestUri!.AbsolutePath.Contains("normalized", StringComparison.Ordinal);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(isNormalized ? Mp4Signature : PngSignature),
            };
            response.Content.Headers.ContentType =
                new MediaTypeHeaderValue(isNormalized ? "video/mp4" : "image/png");
            return response;
        }));
        var downloader = new RemoteArtworkDownloader(AppPaths, httpClient, logger);

        var downloaded = await downloader.DownloadFirstAsync(
        [
            new ArtworkCandidate(
                "raw",
                new Uri("https://example.test/raw.mp4"),
                ".mp4",
                RemoteMediaKind.Video),
            new ArtworkCandidate(
                "normalized",
                new Uri("https://example.test/normalized.mp4"),
                ".mp4",
                RemoteMediaKind.Video),
        ]);

        Assert.NotNull(downloaded);
        Assert.Equal("normalized", downloaded.Candidate.ProviderId);
        Assert.Contains(logger.Warnings, message => message.Contains("non-video", StringComparison.Ordinal));
        File.Delete(downloaded.TemporaryPath);
    }

    [Fact]
    public async Task DownloadFirstAsync_RejectsPayloadWithoutMp4Signature_ForVideoCandidate()
    {
        AppPaths.EnsureDirectoriesExist();
        var logger = new RecordingLogger();
        using var httpClient = new HttpClient(new DelegateHandler(request =>
        {
            var isValid = request.RequestUri!.AbsolutePath.Contains("valid", StringComparison.Ordinal);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(isValid ? Mp4Signature : "not really mp4"u8.ToArray()),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            return response;
        }));
        var downloader = new RemoteArtworkDownloader(AppPaths, httpClient, logger);

        var downloaded = await downloader.DownloadFirstAsync(
        [
            new ArtworkCandidate(
                "bogus",
                new Uri("https://example.test/bogus.mp4"),
                ".mp4",
                RemoteMediaKind.Video),
            new ArtworkCandidate(
                "valid",
                new Uri("https://example.test/valid.mp4"),
                ".mp4",
                RemoteMediaKind.Video),
        ]);

        Assert.NotNull(downloaded);
        Assert.Equal("valid", downloaded.Candidate.ProviderId);
        Assert.Contains(logger.Warnings, message => message.Contains("supported video", StringComparison.Ordinal));
        File.Delete(downloaded.TemporaryPath);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> response) :
        HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Warnings { get; } = [];

        public void Information(string message) { }
        public void Warning(string message, Exception? exception = null) => Warnings.Add(message);
        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class RecordingUriPolicy(Func<Uri, bool> isAllowed) : IRemoteArtworkUriPolicy
    {
        public List<Uri> CheckedUris { get; } = [];

        public Task<bool> IsAllowedAsync(
            Uri uri,
            CancellationToken cancellationToken = default)
        {
            CheckedUris.Add(uri);
            return Task.FromResult(isAllowed(uri));
        }
    }
}
