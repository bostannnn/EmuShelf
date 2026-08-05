using System.Net;
using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Updates;
using EmuShelf.Infrastructure.Updates;

namespace EmuShelf.Infrastructure.Tests.Updates;

public class GitHubUpdateServiceTests : TempAppDirectoryTestBase
{
    private static readonly byte[] PayloadBytes = Encoding.UTF8.GetBytes("dummy portable EmuShelf payload");
    private static string PayloadHash => Convert.ToHexStringLower(SHA256.HashData(PayloadBytes));

    public GitHubUpdateServiceTests() => AppPaths.EnsureDirectoriesExist();

    [Fact]
    public async Task CheckAsync_NewerRelease_ReturnsUpdateWithPlatformAsset()
    {
        var asset = UpdatePlatform.CurrentAssetName();
        if (asset is null)
            return; // No published artifact for this platform (e.g. Intel macOS); nothing to assert.

        var service = CreateService(current: new SemanticVersion(1, 0, 0), releaseTag: "v1.2.0", correctChecksum: true);

        var result = await service.CheckAsync();

        var available = Assert.IsType<UpdateCheckResult.UpdateAvailable>(result);
        Assert.Equal(new SemanticVersion(1, 2, 0), available.Version);
        Assert.Equal(asset, available.Payload.Name);
        Assert.Equal(UpdatePlatform.ChecksumAssetNameFor(asset), available.Checksum.Name);
    }

    [Fact]
    public async Task CheckAsync_SameVersion_ReturnsUpToDate()
    {
        var service = CreateService(current: new SemanticVersion(1, 2, 0), releaseTag: "v1.2.0", correctChecksum: true);

        var result = await service.CheckAsync();

        Assert.IsType<UpdateCheckResult.UpToDate>(result);
    }

    [Fact]
    public async Task DownloadAndStageAsync_VerifiedChecksum_StagesFile()
    {
        if (UpdatePlatform.CurrentAssetName() is null)
            return;

        var service = CreateService(current: new SemanticVersion(1, 0, 0), releaseTag: "v1.2.0", correctChecksum: true);
        var update = Assert.IsType<UpdateCheckResult.UpdateAvailable>(await service.CheckAsync());

        var staged = await service.DownloadAndStageAsync(update);

        Assert.True(File.Exists(staged.PayloadPath));
        Assert.Equal(PayloadBytes, await File.ReadAllBytesAsync(staged.PayloadPath));
        Assert.Equal(new SemanticVersion(1, 2, 0), staged.Version);
    }

    [Fact]
    public async Task DownloadAndStageAsync_TamperedChecksum_ThrowsAndDeletes()
    {
        if (UpdatePlatform.CurrentAssetName() is null)
            return;

        var service = CreateService(current: new SemanticVersion(1, 0, 0), releaseTag: "v1.2.0", correctChecksum: false);
        var update = Assert.IsType<UpdateCheckResult.UpdateAvailable>(await service.CheckAsync());

        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAndStageAsync(update));

        var stagingDirectory = Path.Combine(AppPaths.CacheDirectory, "updates", update.Version.ToString());
        Assert.False(File.Exists(Path.Combine(stagingDirectory, update.Payload.Name)));
    }

    private GitHubUpdateService CreateService(SemanticVersion current, string releaseTag, bool correctChecksum)
    {
        var checksumLine = correctChecksum
            ? $"{PayloadHash}  payload"
            : $"{new string('0', 64)}  payload";
        var handler = new StubHandler(ReleaseJson(releaseTag), checksumLine, PayloadBytes);
        var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EmuShelf/test");
        return new GitHubUpdateService(http, current, AppPaths, NullAppLogger.Instance);
    }

    // Assets for every platform so the test asserts whichever one the running OS/arch expects.
    private static string ReleaseJson(string tag) => $$"""
    {
      "tag_name": "{{tag}}",
      "body": "Notes.",
      "published_at": "2026-08-05T12:00:00Z",
      "assets": [
        { "name": "EmuShelf-win-x64.zip", "browser_download_url": "https://example.test/win.zip", "size": 20 },
        { "name": "EmuShelf-win-x64.sha256", "browser_download_url": "https://example.test/win.sha256", "size": 70 },
        { "name": "EmuShelf-linux-x64.AppImage", "browser_download_url": "https://example.test/linux.AppImage", "size": 20 },
        { "name": "EmuShelf-linux-x64.sha256", "browser_download_url": "https://example.test/linux.sha256", "size": 70 },
        { "name": "EmuShelf-macos-arm64.zip", "browser_download_url": "https://example.test/mac.zip", "size": 20 },
        { "name": "EmuShelf-macos-arm64.sha256", "browser_download_url": "https://example.test/mac.sha256", "size": 70 }
      ]
    }
    """;

    private sealed class StubHandler(string releaseJson, string checksumLine, byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            HttpResponseMessage response;
            if (url.Contains("releases/latest", StringComparison.Ordinal))
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(releaseJson) };
            else if (url.EndsWith(".sha256", StringComparison.Ordinal))
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(checksumLine) };
            else
                response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            return Task.FromResult(response);
        }
    }
}
