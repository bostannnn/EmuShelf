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
    public async Task Preview_UsesSerial_ForDiscSystems_ExtractingOnDemand_WithoutHashing()
    {
        var game = AddGame("Game.chd", "compressed-container"u8.ToArray(), "playstation2");
        _credentials.SaveCredentials(new ScreenScraperUserCredentials("player", "password"));
        _client.Result = SuccessfulGameResult();
        var resolver = new RelativePathResolver(AppPaths);
        var preview = new ScreenScraperPreviewService(
            _games,
            _details,
            _credentials,
            new ScreenScraperFingerprintService(new SqliteGameFileFingerprintStore(_database, resolver)),
            _client,
            KnownScreenScraperProfiles.All,
            new Dictionary<string, IGameIdentifierExtractor>(StringComparer.OrdinalIgnoreCase)
            {
                ["playstation2"] = new StubExtractor([
                    new GameIdentifier(GameIdentifierKind.Serial, "SLUS-12345", "test"),
                ]),
            });

        // allowFingerprinting is false: a CHD cannot be whole-file hashed, yet the serial route matches.
        var result = await preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: false);

        Assert.True(result.IsSuccess);
        Assert.Equal("SLUS-12345", _client.LastRequest!.Serial);
        Assert.Equal(0, _client.LastRequest.RomSize);
        Assert.Equal(GameProviderMatchMethod.Serial, result.Preview!.Match.MatchMethod);
        Assert.Null(result.Preview.FingerprintStatus);
        // The extracted serial is persisted so later runs reuse it.
        Assert.Contains(
            _games.GetIdentifiers(game.Id),
            identifier => identifier.Kind == GameIdentifierKind.Serial && identifier.Value == "SLUS-12345");
    }

    [Fact]
    public async Task Preview_UsesDiscId_ForGameCube_ExtractingOnDemand_WithoutHashing()
    {
        // A .rvz container is never whole-file hashable, yet the GameCube disc game code read from
        // inside it is the serialnum ScreenScraper indexes, so the serial route still matches.
        var game = AddGame("Melee.rvz", "compressed-container"u8.ToArray(), "gamecube");
        _credentials.SaveCredentials(new ScreenScraperUserCredentials("player", "password"));
        _client.Result = SuccessfulGameResult();
        var resolver = new RelativePathResolver(AppPaths);
        var preview = new ScreenScraperPreviewService(
            _games,
            _details,
            _credentials,
            new ScreenScraperFingerprintService(new SqliteGameFileFingerprintStore(_database, resolver)),
            _client,
            KnownScreenScraperProfiles.All,
            new Dictionary<string, IGameIdentifierExtractor>(StringComparer.OrdinalIgnoreCase)
            {
                ["gamecube"] = new StubExtractor([
                    new GameIdentifier(GameIdentifierKind.DiscId, "GALE01", "DiscHeader", IsPrimary: true),
                ]),
            });

        var result = await preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: false);

        Assert.True(result.IsSuccess);
        Assert.Equal("GALE01", _client.LastRequest!.Serial);
        Assert.Equal(0, _client.LastRequest.RomSize);
        Assert.Equal(GameProviderMatchMethod.Serial, result.Preview!.Match.MatchMethod);
        Assert.Null(result.Preview.FingerprintStatus);
        Assert.Contains(
            _games.GetIdentifiers(game.Id),
            identifier => identifier.Kind == GameIdentifierKind.DiscId && identifier.Value == "GALE01");
    }

    [Fact]
    public async Task Preview_UsesSerial_ForPlayStation3_WhichHasNoWholeFileHashRoute()
    {
        // PlayStation 3 has no whole-file hash policy at all, so before the serial route covered it
        // every automatic lookup failed as UnsupportedFormat. The title-id serial now matches instead.
        var game = AddGame("Demons Souls.iso", "ps3-content"u8.ToArray(), "playstation3");
        _credentials.SaveCredentials(new ScreenScraperUserCredentials("player", "password"));
        _client.Result = SuccessfulGameResult();
        var resolver = new RelativePathResolver(AppPaths);
        var preview = new ScreenScraperPreviewService(
            _games,
            _details,
            _credentials,
            new ScreenScraperFingerprintService(new SqliteGameFileFingerprintStore(_database, resolver)),
            _client,
            KnownScreenScraperProfiles.All,
            new Dictionary<string, IGameIdentifierExtractor>(StringComparer.OrdinalIgnoreCase)
            {
                ["playstation3"] = new StubExtractor([
                    new GameIdentifier(GameIdentifierKind.Serial, "BLES00932", "PARAM.SFO"),
                ]),
            });

        var result = await preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: false);

        Assert.True(result.IsSuccess);
        Assert.Equal("BLES00932", _client.LastRequest!.Serial);
        Assert.Equal(GameProviderMatchMethod.Serial, result.Preview!.Match.MatchMethod);
        Assert.Null(result.Preview.FingerprintStatus);
    }

    [Fact]
    public async Task Preview_HashesCleanCartridgeDump_ForNintendo3ds()
    {
        // A clean NCSD cartridge dump is the file No-Intro/ScreenScraper index by whole-file hash,
        // so 3DS now takes the hash route instead of dead-ending on UnsupportedFormat.
        var game = AddGame("Game.3ds", "3ds-cartridge-content"u8.ToArray(), "3ds");
        _credentials.SaveCredentials(new ScreenScraperUserCredentials("player", "password"));
        _client.Result = SuccessfulGameResult();

        var result = await _preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(ScreenScraperFingerprintStatus.Computed, result.Preview!.FingerprintStatus);
        Assert.False(string.IsNullOrEmpty(_client.LastRequest!.Sha1));
        Assert.True(_client.LastRequest.RomSize > 0);
        Assert.Equal(17, _client.LastRequest.SystemId);
        Assert.Equal(GameProviderMatchMethod.Sha1, result.Preview.Match.MatchMethod);
    }

    [Fact]
    public async Task Preview_RejectsInstallable3dsContainer_WithoutCallingProvider()
    {
        // A .cia is an installable package, not the cartridge dump, so its whole-file hash is never in
        // the catalogue. It is rejected before any request, leaving the caller to title-search.
        var game = AddGame("Game.cia", "installable-package"u8.ToArray(), "3ds");
        _credentials.SaveCredentials(new ScreenScraperUserCredentials("player", "password"));

        var result = await _preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: true);

        Assert.Equal(ScreenScraperPreviewStatus.UnsupportedFormat, result.Status);
        Assert.Equal(0, _client.GameRequestCount);
    }

    [Fact]
    public async Task Preview_MatchesArcadeBySetFileName_WithoutHashingOrConsent()
    {
        var game = AddGame("tmnt.zip", "arcade-set-archive"u8.ToArray(), "arcade");
        _credentials.SaveCredentials(new ScreenScraperUserCredentials("player", "password"));
        _client.Result = SuccessfulGameResult();

        // allowFingerprinting is false: an arcade set has no whole-file hash, but the file name
        // (the FBNeo/MAME set id ScreenScraper indexes) matches without reading any bytes.
        var result = await _preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: false);

        Assert.True(result.IsSuccess);
        Assert.Equal("tmnt.zip", _client.LastRequest!.RomName);
        Assert.True(_client.LastRequest.AllowFileNameMatch);
        Assert.Null(_client.LastRequest.Serial);
        Assert.Null(_client.LastRequest.Sha1);
        Assert.Equal(0, _client.LastRequest.RomSize);
        Assert.Equal(75, _client.LastRequest.SystemId);
        Assert.Equal(GameProviderMatchMethod.FileName, result.Preview!.Match.MatchMethod);
        Assert.Equal("tmnt.zip", result.Preview.Match.EvidenceValue);
        Assert.Null(result.Preview.FingerprintStatus);
    }

    [Fact]
    public async Task Preview_ArcadeSetNotFound_SurfacesProviderNotFound_ForTitleSearchFallback()
    {
        var game = AddGame("renamed arcade game.zip", "arcade-set-archive"u8.ToArray(), "arcade");
        _credentials.SaveCredentials(new ScreenScraperUserCredentials("player", "password"));
        _client.Result = new ScreenScraperResult<ScreenScraperGameInfo>(
            ScreenScraperRequestStatus.NotFound, null, null, "No match");

        var result = await _preview.PreviewAsync(
            game.Id,
            new ScreenScraperSettings { Enabled = true },
            allowFingerprinting: false);

        // An unknown/renamed set is a normal provider miss — the UI turns this into a title search.
        Assert.Equal(ScreenScraperPreviewStatus.ProviderFailure, result.Status);
        Assert.Equal(ScreenScraperRequestStatus.NotFound, result.RequestStatus);
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
                RomCrc32: null,
                RomMd5: null,
                RomSha1: null,
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

    private sealed class StubExtractor(IReadOnlyList<GameIdentifier> identifiers) : IGameIdentifierExtractor
    {
        public IReadOnlyList<GameIdentifier> Extract(Game game) => identifiers;
    }

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

        public Task<ScreenScraperResult<IReadOnlyList<ScreenScraperSystem>>> GetSystemsAsync(
            ScreenScraperUserCredentials userCredentials,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>> SearchResult { get; set; } =
            new(ScreenScraperRequestStatus.Success, [], null, null);

        public Task<ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>> SearchGamesAsync(
            ScreenScraperUserCredentials userCredentials,
            int systemId,
            string query,
            CancellationToken cancellationToken = default) => Task.FromResult(SearchResult);
    }
}
