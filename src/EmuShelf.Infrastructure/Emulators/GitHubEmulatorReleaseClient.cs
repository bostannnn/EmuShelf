using EmuShelf.Core.Diagnostics;

namespace EmuShelf.Infrastructure.Emulators;

/// <summary>
/// The real <see cref="IEmulatorReleaseClient"/>: talks to the public GitHub Releases API (no token) and
/// streams asset downloads. The download/progress loop mirrors the app self-updater's
/// <c>GitHubUpdateService</c>; it is duplicated rather than shared because that service's streamer is private
/// and the emulator manager must not depend on the semver-gated update path.
/// </summary>
public sealed class GitHubEmulatorReleaseClient : IEmulatorReleaseClient
{
    private readonly HttpClient _http;
    private readonly IAppLogger _logger;

    public GitHubEmulatorReleaseClient(HttpClient http, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _logger = logger;
    }

    public async Task<GitHubEmulatorRelease?> GetLatestReleaseAsync(
        string repository,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return null;

        string json;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repository}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning($"GitHub returned {(int)response.StatusCode} for {repository} releases.");
                return null;
            }

            json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not reach GitHub for {repository} releases: {ex.Message}");
            return null;
        }

        try
        {
            return GitHubEmulatorReleaseParser.Parse(json);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not parse the GitHub response for {repository}: {ex.Message}");
            return null;
        }
    }

    public async Task DownloadAsync(
        string url,
        string destinationPath,
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
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);

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
}
