using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;
using EmuShelf.Core.Updates;

namespace EmuShelf.Infrastructure.Updates;

/// <summary>
/// Checks the EmuShelf GitHub repository's latest release and downloads the portable artifact for the
/// running platform, verifying it against the release's published SHA-256 before returning it. Uses
/// only the public Releases API — no token, and nothing about the user is sent.
/// </summary>
public sealed class GitHubUpdateService : IUpdateService
{
    private const string DefaultRepository = "bostannnn/EmuShelf";

    private readonly HttpClient _http;
    private readonly SemanticVersion _currentVersion;
    private readonly IAppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly string _repository;

    public GitHubUpdateService(
        HttpClient http,
        SemanticVersion currentVersion,
        IAppPaths paths,
        IAppLogger logger,
        string? repository = null)
    {
        _http = http;
        _currentVersion = currentVersion;
        _paths = paths;
        _logger = logger;
        _repository = string.IsNullOrWhiteSpace(repository) ? DefaultRepository : repository;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var assetName = UpdatePlatform.CurrentAssetName();
        if (assetName is null)
            return new UpdateCheckResult.CheckFailed("Automatic updates aren't available for this build.");

        string json;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{_repository}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateCheckResult.CheckFailed("No releases have been published yet.");
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult.CheckFailed($"GitHub returned {(int)response.StatusCode}.");

            json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Update check could not reach GitHub: {ex.Message}");
            return new UpdateCheckResult.CheckFailed("Couldn't reach GitHub. Check your connection.");
        }

        ReleaseInfo? release;
        try
        {
            release = GitHubReleaseParser.ParseRelease(json);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Update check could not parse the GitHub response: {ex.Message}");
            return new UpdateCheckResult.CheckFailed("Couldn't read the latest release information.");
        }

        if (release is null)
            return new UpdateCheckResult.CheckFailed("The latest release has no usable version tag.");

        if (release.Version <= _currentVersion)
            return new UpdateCheckResult.UpToDate(_currentVersion);

        var payload = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase));
        if (payload is null)
            return new UpdateCheckResult.CheckFailed(
                $"EmuShelf {release.Version} is out, but its download for this platform isn't ready yet.");

        var checksumName = UpdatePlatform.ChecksumAssetNameFor(assetName);
        var checksum = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, checksumName, StringComparison.OrdinalIgnoreCase));
        if (checksum is null)
            return new UpdateCheckResult.CheckFailed(
                $"EmuShelf {release.Version} is out, but its checksum file is missing, so it can't be verified.");

        return new UpdateCheckResult.UpdateAvailable(
            release.Version, release.TagName, release.Notes, payload, checksum);
    }

    public async Task<StagedUpdate> DownloadAndStageAsync(
        UpdateCheckResult.UpdateAvailable update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var stagingDirectory = Path.Combine(_paths.CacheDirectory, "updates", update.Version.ToString());
        // A previous, interrupted attempt for the same version must not leave a stale payload behind.
        if (Directory.Exists(stagingDirectory))
            Directory.Delete(stagingDirectory, recursive: true);
        Directory.CreateDirectory(stagingDirectory);

        var payloadPath = Path.Combine(stagingDirectory, update.Payload.Name);
        await DownloadToFileAsync(update.Payload.DownloadUrl, payloadPath, progress, cancellationToken)
            .ConfigureAwait(false);

        var expected = await ReadExpectedChecksumAsync(update.Checksum.DownloadUrl, cancellationToken)
            .ConfigureAwait(false);
        var actual = await ComputeSha256Async(payloadPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(payloadPath);
            _logger.Error(
                $"Update {update.Version} failed verification (expected {expected}, got {actual}).");
            throw new InvalidDataException("The downloaded update failed its checksum check.");
        }

        _logger.Information($"Staged verified update {update.Version} at {payloadPath}.");
        return new StagedUpdate(update.Version, payloadPath);
    }

    private async Task DownloadToFileAsync(
        string url,
        string destination,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

        var buffer = new byte[1 << 16];
        long received = 0;
        int read;
        var lastReported = -1;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            if (progress is null || total is not { } length || length <= 0)
                continue;
            // Report at most once per whole percent so a large download doesn't flood the UI thread.
            var percent = (int)(received * 100 / length);
            if (percent != lastReported)
            {
                lastReported = percent;
                progress.Report(percent / 100.0);
            }
        }
    }

    private async Task<string> ReadExpectedChecksumAsync(string url, CancellationToken cancellationToken)
    {
        var content = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        return GitHubReleaseParser.ParseChecksum(content)
            ?? throw new InvalidDataException("The release's checksum file could not be read.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not delete the unverified update file '{path}': {ex.Message}");
        }
    }
}
