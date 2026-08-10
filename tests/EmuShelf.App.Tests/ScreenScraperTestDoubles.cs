using EmuShelf.App.Services;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Tests;

/// <summary>
/// Reusable fakes and preview fixtures for the ScreenScraper flows, shared by the single-game
/// scraper tests and the controller-native Gamepad overlay tests. They mirror the private doubles
/// in <c>GameScraperViewModelTests</c> so both surfaces exercise the same shared view model.
/// </summary>
internal static class ScraperFixtures
{
    public static GameDetails EmptyDetails() => new(1, [], [], []);

    public static IReadOnlyDictionary<GameMediaKind, ScreenScraperMediaCandidate> NoMedia() =>
        new Dictionary<GameMediaKind, ScreenScraperMediaCandidate>();

    public static GameMetadataValue TitleValue(string value) => new(
        1, GameMetadataField.Title, value, null, GameMetadataValueOrigin.Provider,
        ScreenScraperProvider.Id, "100", "https://example.test/game", DateTimeOffset.UtcNow);

    public static ScreenScraperMediaCandidate Candidate(string type) => new(
        type, new Uri($"https://example.test/{type}.png"), ".png", "media-id", "us", "en",
        512, 512, 100, null, null, null);

    public static ScreenScraperPreviewResult SuccessPreview(
        GameDetails existing,
        IReadOnlyList<GameMetadataValue> metadata,
        IReadOnlyDictionary<GameMediaKind, ScreenScraperMediaCandidate> media,
        GameMediaKind coverKind = GameMediaKind.BoxFront)
    {
        var match = new GameProviderMatch(
            1, ScreenScraperProvider.Id, "58", 1, "100", "200",
            GameProviderMatchMethod.Sha1, "ABC", GameMetadataStatus.Matched, DateTimeOffset.UtcNow, null);
        var preview = new ScreenScraperGamePreview(
            1, match, metadata, media, existing,
            new ScreenScraperQuota(1, 5, 20000, 0, 2000, null),
            ScreenScraperFingerprintStatus.Computed,
            coverKind);
        return new ScreenScraperPreviewResult(
            ScreenScraperPreviewStatus.Success, preview, ScreenScraperRequestStatus.Success, null);
    }

    /// <summary>A ready-to-review preview: one field plus box art, both selectable by default.</summary>
    public static ScreenScraperPreviewResult ReadyPreview() => SuccessPreview(
        EmptyDetails(),
        [TitleValue("Canonical")],
        new Dictionary<GameMediaKind, ScreenScraperMediaCandidate>
        {
            [GameMediaKind.BoxFront] = Candidate("box"),
            [GameMediaKind.Screenshot] = Candidate("shot"),
        });

    public static ScreenScraperPreviewResult Failure(
        ScreenScraperPreviewStatus status,
        ScreenScraperRequestStatus? requestStatus = null) => new(status, null, requestStatus, null);
}

internal sealed class StubScreenScraperPreviewService : IScreenScraperPreviewService
{
    private readonly ScreenScraperPreviewResult[] _results;
    private int _index;

    public StubScreenScraperPreviewService(params ScreenScraperPreviewResult[] results) => _results = results;

    public bool LastAllowFingerprinting { get; private set; }
    public IReadOnlyList<ScreenScraperGameMatch> SearchCandidates { get; set; } = [];
    public ScreenScraperPreviewResult? SelectedResult { get; set; }
    public string? LastSelectedProviderGameId { get; private set; }

    public Task<ScreenScraperPreviewResult> PreviewAsync(
        long gameId,
        ScreenScraperSettings settings,
        bool allowFingerprinting,
        CancellationToken cancellationToken = default)
    {
        LastAllowFingerprinting = allowFingerprinting;
        var result = _results[Math.Min(_index, _results.Length - 1)];
        _index++;
        return Task.FromResult(result);
    }

    public Task<ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>> SearchAsync(
        long gameId,
        string query,
        ScreenScraperSettings settings,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>(
            ScreenScraperRequestStatus.Success, SearchCandidates, null, null));

    public Task<ScreenScraperPreviewResult> PreviewByProviderGameIdAsync(
        long gameId,
        string providerGameId,
        ScreenScraperSettings settings,
        CancellationToken cancellationToken = default)
    {
        LastSelectedProviderGameId = providerGameId;
        return Task.FromResult(SelectedResult ?? _results[^1]);
    }
}

internal sealed class StubGameScrapeApplicationService : IGameScrapeApplicationService
{
    public GameScrapeApplyRequest? Request { get; private set; }

    public GameScrapeApplyResult Result { get; set; } = new(1, 1, 0, [], false);

    public Task<GameScrapeApplyResult> ApplyAsync(
        GameScrapeApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        Request = request;
        return Task.FromResult(Result);
    }
}

internal sealed class StubScreenScraperAccountService : IScreenScraperAccountService
{
    public ScreenScraperConnectionResult ConnectResult { get; set; } = ScreenScraperConnectionResult.Connected;

    public bool IsConnected { get; private set; }

    public ScreenScraperAccountInfo? LastAccountInfo => null;

    public int ConnectCalls { get; private set; }

    public Task<ScreenScraperConnectionSummary> ConnectAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        ConnectCalls++;
        IsConnected = ConnectResult == ScreenScraperConnectionResult.Connected;
        return Task.FromResult(new ScreenScraperConnectionSummary(ConnectResult));
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }
}

internal sealed class StubSettingsService : ISettingsService
{
    private AppSettings _settings;

    public StubSettingsService(bool screenScraperEnabled = true) =>
        _settings = new AppSettings
        {
            Scraping = new ScrapingSettings
            {
                ScreenScraper = new ScreenScraperSettings { Enabled = screenScraperEnabled },
            },
        };

    public AppSettings Load() => _settings;

    public void Save(AppSettings settings) => _settings = settings;
}
