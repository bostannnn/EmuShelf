using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Tests;

public class GameScraperViewModelTests
{
    [Fact]
    public async Task Load_Success_BuildsSelectableRows_AndBecomesReady()
    {
        var vm = CreateViewModel(
            new FakePreviewService(SuccessPreview(
                EmptyDetails(),
                [TitleValue("Canonical")],
                new Dictionary<GameMediaKind, ScreenScraperMediaCandidate>
                {
                    [GameMediaKind.BoxFront] = Candidate("box"),
                })),
            new FakeApplyService());

        await vm.LoadAsync();

        Assert.Equal(GameScraperState.Ready, vm.State);
        Assert.True(vm.ShowData);
        var field = Assert.Single(vm.Fields);
        Assert.Equal("Canonical", field.ProposedValue);
        Assert.True(field.CanApply);
        Assert.True(field.IsSelected);
        var media = Assert.Single(vm.Media);
        Assert.True(media.IsSelected);
        Assert.Equal("ScreenScraper requests today: 5 / 20000", vm.QuotaText);
    }

    [Fact]
    public async Task BoxArt_IsSplitIntoItsOwnRow_OutOfTheMediaList_ButStillApplied()
    {
        var vm = CreateViewModel(
            new FakePreviewService(SuccessPreview(
                EmptyDetails(),
                [],
                new Dictionary<GameMediaKind, ScreenScraperMediaCandidate>
                {
                    [GameMediaKind.BoxFront] = Candidate("box"),
                    [GameMediaKind.Screenshot] = Candidate("shot"),
                })),
            new FakeApplyService());

        await vm.LoadAsync();

        Assert.NotNull(vm.BoxArtRow);
        Assert.Equal(GameMediaKind.BoxFront, vm.BoxArtRow!.Kind);
        Assert.Single(vm.OtherMedia);
        Assert.Equal(GameMediaKind.Screenshot, vm.OtherMedia[0].Kind);
        // Media still holds both, so box art is applied alongside the rest.
        Assert.Equal(2, vm.Media.Count);
    }

    [Fact]
    public async Task Apply_SendsSelectedRows_AsFillMissing_AndClosesWithResult()
    {
        var apply = new FakeApplyService
        {
            Result = new GameScrapeApplyResult(
                1, 1, 0, [new GameMediaApplyResult(GameMediaKind.BoxFront, GameMediaApplyOutcome.Imported)], true),
        };
        var vm = CreateViewModel(
            new FakePreviewService(SuccessPreview(
                EmptyDetails(),
                [TitleValue("Canonical")],
                new Dictionary<GameMediaKind, ScreenScraperMediaCandidate>
                {
                    [GameMediaKind.BoxFront] = Candidate("box"),
                })),
            apply);
        await vm.LoadAsync();

        GameScrapeApplyResult? closedWith = null;
        var closed = false;
        vm.CloseRequested += result => { closedWith = result; closed = true; };

        Assert.True(vm.ApplyCommand.CanExecute(null));
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.NotNull(apply.Request);
        Assert.Single(apply.Request!.Metadata);
        Assert.Single(apply.Request.Media);
        Assert.Equal(GameMetadataApplyMode.FillMissing, apply.Request.Mode);
        Assert.Equal(GameScraperState.Applied, vm.State);
        Assert.True(closed);
        Assert.Same(apply.Result, vm.LastApplyResult);
        Assert.Same(vm.LastApplyResult, closedWith);
    }

    [Fact]
    public async Task Load_DoesNotSelectUserOwnedFields()
    {
        var existing = new GameDetails(
            1,
            [new GameMetadataValue(
                1, GameMetadataField.Title, "My Title", null,
                GameMetadataValueOrigin.User, null, null, null, DateTimeOffset.UtcNow)],
            [],
            []);
        var vm = CreateViewModel(
            new FakePreviewService(SuccessPreview(existing, [TitleValue("Canonical")], NoMedia())),
            new FakeApplyService());

        await vm.LoadAsync();

        var field = Assert.Single(vm.Fields);
        Assert.True(field.IsUserOwned);
        Assert.False(field.CanApply);
        Assert.False(field.IsSelected);
        Assert.Equal("My Title", field.CurrentValue);
    }

    [Fact]
    public async Task Load_NotConnected_ShowsTheConnectForm()
    {
        var vm = CreateViewModel(
            new FakePreviewService(Failure(ScreenScraperPreviewStatus.NotConnected)),
            new FakeApplyService());

        await vm.LoadAsync();

        Assert.Equal(GameScraperState.NotConnected, vm.State);
        Assert.True(vm.ShowConnect);
        Assert.False(vm.ShowData);
    }

    [Fact]
    public async Task Connect_Success_EnablesTheProvider_AndReloadsToReady()
    {
        var preview = new FakePreviewService(
            Failure(ScreenScraperPreviewStatus.ProviderDisabled),
            SuccessPreview(EmptyDetails(), [TitleValue("Canonical")], NoMedia()));
        var vm = CreateViewModel(
            preview,
            new FakeApplyService(),
            new FakeAccountService { ConnectResult = ScreenScraperConnectionResult.Connected });
        await vm.LoadAsync();
        Assert.True(vm.ShowConnect);

        vm.Username = "bostan";
        vm.Password = "secret";
        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(GameScraperState.Ready, vm.State);
        Assert.False(vm.ShowConnect);
    }

    [Fact]
    public async Task Connect_Failure_StaysOnConnectFormWithAMessage()
    {
        var vm = CreateViewModel(
            new FakePreviewService(Failure(ScreenScraperPreviewStatus.NotConnected)),
            new FakeApplyService(),
            new FakeAccountService { ConnectResult = ScreenScraperConnectionResult.AuthenticationFailed });
        await vm.LoadAsync();

        vm.Username = "bostan";
        vm.Password = "wrong";
        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(vm.ShowConnect);
        Assert.False(string.IsNullOrEmpty(vm.ConnectStatus));
    }

    [Fact]
    public async Task ConsentRequired_ThenComputeFingerprint_RetriesWithHashingAllowed()
    {
        var preview = new FakePreviewService(
            Failure(ScreenScraperPreviewStatus.FingerprintConsentRequired),
            SuccessPreview(EmptyDetails(), [TitleValue("Canonical")], NoMedia()));
        var vm = CreateViewModel(preview, new FakeApplyService());
        await vm.LoadAsync();

        Assert.Equal(GameScraperState.ConsentRequired, vm.State);
        Assert.True(vm.ComputeFingerprintCommand.CanExecute(null));

        await vm.ComputeFingerprintCommand.ExecuteAsync(null);

        Assert.True(preview.LastAllowFingerprinting);
        Assert.Equal(GameScraperState.Ready, vm.State);
    }

    [Fact]
    public async Task Load_ProviderNotFound_BecomesNoMatch()
    {
        var vm = CreateViewModel(
            new FakePreviewService(Failure(
                ScreenScraperPreviewStatus.ProviderFailure, ScreenScraperRequestStatus.NotFound)),
            new FakeApplyService());

        await vm.LoadAsync();

        Assert.Equal(GameScraperState.NoMatch, vm.State);
        Assert.True(vm.ShowSearch);
    }

    [Fact]
    public async Task Load_UnsupportedFormat_FallsBackToTitleSearch()
    {
        // Arcade sets (and other no-hash formats) can't be whole-file fingerprinted, but the
        // platform is still mapped — so the user should land in the title search, not a dead end.
        var preview = new FakePreviewService(Failure(ScreenScraperPreviewStatus.UnsupportedFormat))
        {
            SearchCandidates =
                [new ScreenScraperGameMatch("42", "Teenage Mutant Ninja Turtles", "Arcade")],
        };
        var vm = CreateViewModel(preview, new FakeApplyService());

        await vm.LoadAsync();

        Assert.Equal(GameScraperState.NoMatch, vm.State);
        Assert.True(vm.ShowSearch);
        Assert.False(vm.ShowMessage);
        Assert.Single(vm.Candidates);
        Assert.True(vm.HasCandidates);
    }

    [Fact]
    public async Task Load_UnsupportedSystem_StaysADeadEnd()
    {
        // A platform ScreenScraper doesn't map at all has no system to search within, so it must
        // remain the read-only "unsupported" message rather than kicking off a title search.
        var preview = new FakePreviewService(Failure(ScreenScraperPreviewStatus.UnsupportedSystem))
        {
            SearchCandidates = [new ScreenScraperGameMatch("1", "Should Not Appear", "Whatever")],
        };
        var vm = CreateViewModel(preview, new FakeApplyService());

        await vm.LoadAsync();

        Assert.Equal(GameScraperState.Unsupported, vm.State);
        Assert.True(vm.ShowMessage);
        Assert.False(vm.ShowSearch);
        Assert.Empty(vm.Candidates);
    }

    [Fact]
    public async Task NoMatch_AutoSearches_AndSelectingACandidateBuildsAPreview()
    {
        var preview = new FakePreviewService(
            Failure(ScreenScraperPreviewStatus.ProviderFailure, ScreenScraperRequestStatus.NotFound))
        {
            SearchCandidates = [new ScreenScraperGameMatch("777", "Some Rom Hack", "Playstation 2")],
            SelectedResult = SuccessPreview(EmptyDetails(), [TitleValue("Some Rom Hack")], NoMedia()),
        };
        var vm = CreateViewModel(preview, new FakeApplyService());

        await vm.LoadAsync();

        Assert.Equal(GameScraperState.NoMatch, vm.State);
        Assert.True(vm.ShowSearch);
        Assert.Single(vm.Candidates);
        Assert.True(vm.HasCandidates);

        await vm.SelectCandidateCommand.ExecuteAsync(vm.Candidates[0]);

        Assert.Equal("777", preview.LastSelectedProviderGameId);
        Assert.Equal(GameScraperState.Ready, vm.State);
    }

    [Fact]
    public async Task AutoSearch_CleansTheFilename_AndShortensUntilItMatches()
    {
        var preview = new FakePreviewService(
            Failure(ScreenScraperPreviewStatus.ProviderFailure, ScreenScraperRequestStatus.NotFound))
        {
            SearchCandidates = [new ScreenScraperGameMatch("1", "Castlevania - Harmony Of Dissonance", "GBA")],
            // Only matches once the query is trimmed down to four words or fewer.
            SearchMatches = query => query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 4,
        };
        var vm = new GameScraperViewModel(
            1,
            "Castlevania Harmony of Dissonance Recolor by JonataGuitar and sorrow (v1.0)",
            preview,
            new FakeApplyService(),
            new FakeAccountService(),
            new ScreenScraperSettings { Enabled = true });

        await vm.LoadAsync();

        Assert.True(vm.HasCandidates);
        // The ladder's ~two-thirds step (3 of 5 words) is the first that matches.
        Assert.Equal("Castlevania Harmony of", vm.SearchQuery);
    }

    [Fact]
    public async Task AutoSearch_ShortensAllTheWayToOneWord_WhenLongerQueriesMiss()
    {
        var preview = new FakePreviewService(
            Failure(ScreenScraperPreviewStatus.ProviderFailure, ScreenScraperRequestStatus.NotFound))
        {
            SearchCandidates = [new ScreenScraperGameMatch("1", "Castlevania - Harmony Of Dissonance", "GBA")],
            // ScreenScraper only matches the single-word query here (its search rewards fewer words).
            SearchMatches = query => query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 1,
        };
        var vm = new GameScraperViewModel(
            1,
            "Castlevania Harmony of Dissonance Recolor (v1.0)",
            preview,
            new FakeApplyService(),
            new FakeAccountService(),
            new ScreenScraperSettings { Enabled = true });

        await vm.LoadAsync();

        Assert.True(vm.HasCandidates);
        Assert.Equal("Castlevania", vm.SearchQuery);
    }

    [Fact]
    public async Task DeselectingEveryRow_DisablesApply()
    {
        var vm = CreateViewModel(
            new FakePreviewService(SuccessPreview(
                EmptyDetails(),
                [TitleValue("Canonical")],
                new Dictionary<GameMediaKind, ScreenScraperMediaCandidate>
                {
                    [GameMediaKind.BoxFront] = Candidate("box"),
                })),
            new FakeApplyService());
        await vm.LoadAsync();

        Assert.True(vm.ApplyCommand.CanExecute(null));
        vm.Fields[0].IsSelected = false;
        vm.Media[0].IsSelected = false;

        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    private static GameScraperViewModel CreateViewModel(
        FakePreviewService preview,
        FakeApplyService apply,
        FakeAccountService? account = null) =>
        new(1, "Game", preview, apply, account ?? new FakeAccountService(),
            new ScreenScraperSettings { Enabled = true });

    private static GameDetails EmptyDetails() => new(1, [], [], []);

    private static IReadOnlyDictionary<GameMediaKind, ScreenScraperMediaCandidate> NoMedia() =>
        new Dictionary<GameMediaKind, ScreenScraperMediaCandidate>();

    private static GameMetadataValue TitleValue(string value) => new(
        1, GameMetadataField.Title, value, null, GameMetadataValueOrigin.Provider,
        ScreenScraperProvider.Id, "100", "https://example.test/game", DateTimeOffset.UtcNow);

    private static ScreenScraperMediaCandidate Candidate(string type) => new(
        type, new Uri($"https://example.test/{type}.png"), ".png", "media-id", "us", "en", 512, 512, 100, null, null, null);

    private static ScreenScraperPreviewResult SuccessPreview(
        GameDetails existing,
        IReadOnlyList<GameMetadataValue> metadata,
        IReadOnlyDictionary<GameMediaKind, ScreenScraperMediaCandidate> media)
    {
        var match = new GameProviderMatch(
            1, ScreenScraperProvider.Id, "58", 1, "100", "200",
            GameProviderMatchMethod.Sha1, "ABC", GameMetadataStatus.Matched, DateTimeOffset.UtcNow, null);
        var preview = new ScreenScraperGamePreview(
            1, match, metadata, media, existing,
            new ScreenScraperQuota(1, 5, 20000, 0, 2000, null),
            ScreenScraperFingerprintStatus.Computed);
        return new ScreenScraperPreviewResult(
            ScreenScraperPreviewStatus.Success, preview, ScreenScraperRequestStatus.Success, null);
    }

    private static ScreenScraperPreviewResult Failure(
        ScreenScraperPreviewStatus status,
        ScreenScraperRequestStatus? requestStatus = null) => new(status, null, requestStatus, null);

    private sealed class FakePreviewService : IScreenScraperPreviewService
    {
        private readonly ScreenScraperPreviewResult[] _results;
        private int _index;

        public FakePreviewService(params ScreenScraperPreviewResult[] results) => _results = results;

        public bool LastAllowFingerprinting { get; private set; }

        public IReadOnlyList<ScreenScraperGameMatch> SearchCandidates { get; set; } = [];

        /// <summary>When set, a query only yields candidates if this predicate returns true.</summary>
        public Func<string, bool>? SearchMatches { get; set; }

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
            CancellationToken cancellationToken = default)
        {
            var yields = SearchMatches?.Invoke(query) ?? true;
            return Task.FromResult(new ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>(
                ScreenScraperRequestStatus.Success,
                yields ? SearchCandidates : [],
                null,
                null));
        }

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

    private sealed class FakeAccountService : IScreenScraperAccountService
    {
        public ScreenScraperConnectionResult ConnectResult { get; set; } = ScreenScraperConnectionResult.Connected;

        public bool IsConnected { get; private set; }

        public ScreenScraperAccountInfo? LastAccountInfo => null;

        public Task<ScreenScraperConnectionSummary> ConnectAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default)
        {
            IsConnected = ConnectResult == ScreenScraperConnectionResult.Connected;
            return Task.FromResult(new ScreenScraperConnectionSummary(ConnectResult));
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeApplyService : IGameScrapeApplicationService
    {
        public GameScrapeApplyRequest? Request { get; private set; }

        public GameScrapeApplyResult Result { get; set; } = new(1, 0, 0, [], false);

        public Task<GameScrapeApplyResult> ApplyAsync(
            GameScrapeApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(Result);
        }
    }
}
