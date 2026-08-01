using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Metadata;
using EmuShelf.Infrastructure.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Metadata;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class ScreenScraperPreviewServiceTests : TempAppDirectoryTestBase
{
    private readonly LibraryDatabase _database;
    private readonly GameLibrary _library;
    private readonly SqliteGameMetadataStore _games;
    private readonly SqliteGameDetailsStore _details;
    private readonly SessionOnlyScreenScraperCredentialStore _credentials = new();
    private readonly StubClient _client = new();
    private readonly ScreenScraperPreviewService _preview;

    public ScreenScraperPreviewServiceTests()
    {
        AppPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(AppPaths);
        _database.Initialize();
        var resolver = new RelativePathResolver(AppPaths);
        _library = new GameLibrary(_database, resolver);
        _games = new SqliteGameMetadataStore(_database, resolver);
        _details = new SqliteGameDetailsStore(_database, resolver);
        var fingerprintStore = new SqliteGameFileFingerprintStore(_database, resolver);
        _preview = new ScreenScraperPreviewService(
            _games,
            _details,
            _credentials,
            new ScreenScraperFingerprintService(fingerprintStore),
            _client,
            KnownScreenScraperProfiles.All);
    }

    [Fact]
    public async Task Preview_ComputesEvidenceAndBuildsCandidatesWithoutApplyingAnything()
    {
        var game = AddGame("Preview.gba", "123456789"u8.ToArray(), "gba");
        _credentials.SaveCredentials(new ScreenScraperUserCredentials("player", "password"));
        _client.Result = SuccessfulGameResult();

        var result = await _preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(ScreenScraperFingerprintStatus.Computed, result.Preview!.FingerprintStatus);
        Assert.Equal("F7C3BC1D808E04732ADF679965CCC34CA7AE3441", _client.LastRequest!.Sha1);
        Assert.Equal(9, _client.LastRequest.RomSize);
        Assert.Equal(12, _client.LastRequest.SystemId);
        Assert.Equal("Provider title", result.Preview.Metadata.Single(
            value => value.Field == GameMetadataField.Title).Value);
        Assert.Equal("box", result.Preview.Media[GameMediaKind.BoxFront].ProviderMediaId);
        Assert.Equal(GameProviderMatchMethod.Sha1, result.Preview.Match.MatchMethod);
        Assert.Equal(ScreenScraperSystemMap.Version, result.Preview.Match.SystemMappingVersion);

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT (SELECT COUNT(*) FROM GameMetadataValues) + " +
            "(SELECT COUNT(*) FROM GameMediaAssets) + " +
            "(SELECT COUNT(*) FROM GameProviderMatches);";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);

        var cached = await _preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: false);
        Assert.Equal(ScreenScraperFingerprintStatus.Cached, cached.Preview!.FingerprintStatus);
    }

    [Fact]
    public async Task Preview_RequiresSeparateFingerprintConsentBeforeCallingProvider()
    {
        var game = AddGame("Consent.nds", "content"u8.ToArray(), "nds");
        _credentials.SaveCredentials(new ScreenScraperUserCredentials("player", "password"));

        var result = await _preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: false);

        Assert.Equal(ScreenScraperPreviewStatus.FingerprintConsentRequired, result.Status);
        Assert.Equal(0, _client.GameRequestCount);
    }

    [Fact]
    public async Task Preview_RejectsUnsupportedContainerWithoutCallingProvider()
    {
        var game = AddGame("Compressed.chd", "content"u8.ToArray(), "playstation2");
        _credentials.SaveCredentials(new ScreenScraperUserCredentials("player", "password"));

        var result = await _preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: true);

        Assert.Equal(ScreenScraperPreviewStatus.UnsupportedFormat, result.Status);
        Assert.Equal(0, _client.GameRequestCount);
    }

    [Fact]
    public async Task Preview_RequiresEnabledProviderAndConnectedAccount()
    {
        var game = AddGame("Account.gbc", "content"u8.ToArray(), "gbc");

        var disabled = await _preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = false },
            allowFingerprinting: true);
        var disconnected = await _preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: true);

        Assert.Equal(ScreenScraperPreviewStatus.ProviderDisabled, disabled.Status);
        Assert.Equal(ScreenScraperPreviewStatus.NotConnected, disconnected.Status);
        Assert.Equal(0, _client.GameRequestCount);
    }

    [Fact]
    public async Task ProviderFailure_IsTypedAndDoesNotEraseExistingDetails()
    {
        var game = AddGame("NoMatch.sfc", "content"u8.ToArray(), "snes");
        _credentials.SaveCredentials(new ScreenScraperUserCredentials("player", "password"));
        _details.TryApplyMetadata(
            new GameMetadataValue(
                game.Id,
                GameMetadataField.Description,
                "Keep me",
                "en",
                GameMetadataValueOrigin.User,
                null,
                null,
                null,
                DateTimeOffset.UtcNow),
            GameMetadataApplyMode.UserEdit);
        _client.Result = new ScreenScraperResult<ScreenScraperGameInfo>(
            ScreenScraperRequestStatus.NotFound,
            null,
            null,
            "No match");

        var result = await _preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: true);

        Assert.Equal(ScreenScraperPreviewStatus.ProviderFailure, result.Status);
        Assert.Equal(ScreenScraperRequestStatus.NotFound, result.RequestStatus);
        Assert.Equal("Keep me", Assert.Single(_details.GetDetails(game.Id).Metadata).Value);
    }

    private Game AddGame(string filename, byte[] contents, string systemId)
    {
        var path = Path.Combine(BaseDirectory, "Games", filename);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, contents);
        _library.AddGames([
            new Game
            {
                SystemId = systemId,
                Path = path,
                Title = Path.GetFileNameWithoutExtension(path),
                TitleOrigin = GameTitleOrigin.Filename,
                DateAdded = DateTimeOffset.UtcNow,
            },
        ]);
        return _library.GetGames().Single(candidate => candidate.Path == path);
    }

    private static ScreenScraperResult<ScreenScraperGameInfo> SuccessfulGameResult() =>
        new(
            ScreenScraperRequestStatus.Success,
            new ScreenScraperGameInfo(
                "provider-game",
                "provider-rom",
                [new ScreenScraperLocalizedText("Provider title", null, "wor")],
                [new ScreenScraperLocalizedText("Description", "en", null)],
                [new ScreenScraperLocalizedText("Action", "en", null)],
                [new ScreenScraperReleaseDate("2001-01-01", "wor")],
                "Developer",
                "Publisher",
                "1-2",
                "18",
                [
                    new ScreenScraperMediaCandidate(
                        "box-2D",
                        new Uri("https://media.example.test/box.png"),
                        ".png",
                        "box",
                        "wor",
                        null,
                        600,
                        900,
                        1000,
                        null,
                        null,
                        null),
                ]),
            new ScreenScraperQuota(2, 1, 1000, 0, 50, null),
            null);

    private sealed class StubClient : IScreenScraperClient
    {
        public ScreenScraperResult<ScreenScraperGameInfo> Result { get; set; } = SuccessfulGameResult();
        public ScreenScraperGameRequest? LastRequest { get; private set; }
        public int GameRequestCount { get; private set; }

        public Task<ScreenScraperResult<ScreenScraperAccountInfo>> GetAccountInfoAsync(
            ScreenScraperUserCredentials userCredentials,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ScreenScraperResult<ScreenScraperGameInfo>> GetGameInfoAsync(
            ScreenScraperUserCredentials userCredentials,
            ScreenScraperGameRequest request,
            CancellationToken cancellationToken = default)
        {
            GameRequestCount++;
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }
}
