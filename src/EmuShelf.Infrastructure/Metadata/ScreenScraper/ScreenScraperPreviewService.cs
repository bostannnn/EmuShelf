using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;

namespace EmuShelf.Infrastructure.Metadata.ScreenScraper;

public sealed class ScreenScraperPreviewService : IScreenScraperPreviewService
{
    private readonly IGameMetadataStore _games;
    private readonly IGameDetailsStore _details;
    private readonly IScreenScraperCredentialStore _credentials;
    private readonly IScreenScraperFingerprintService _fingerprints;
    private readonly IScreenScraperClient _client;
    private readonly IReadOnlyDictionary<string, ScreenScraperSystemProfile> _profiles;
    private readonly IReadOnlyDictionary<string, IGameIdentifierExtractor> _identifierExtractors;

    public ScreenScraperPreviewService(
        IGameMetadataStore games,
        IGameDetailsStore details,
        IScreenScraperCredentialStore credentials,
        IScreenScraperFingerprintService fingerprints,
        IScreenScraperClient client,
        IReadOnlyList<ScreenScraperSystemProfile> profiles,
        IReadOnlyDictionary<string, IGameIdentifierExtractor>? identifierExtractors = null)
    {
        _games = games;
        _details = details;
        _credentials = credentials;
        _fingerprints = fingerprints;
        _client = client;
        _profiles = profiles.ToDictionary(profile => profile.SystemId, StringComparer.OrdinalIgnoreCase);
        _identifierExtractors = identifierExtractors ??
            new Dictionary<string, IGameIdentifierExtractor>(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<ScreenScraperPreviewResult> PreviewAsync(
        long gameId,
        ScreenScraperSettings settings,
        bool allowFingerprinting,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
            return Failure(ScreenScraperPreviewStatus.ProviderDisabled, "ScreenScraper is disabled.");

        var credentials = _credentials.GetCredentials();
        if (credentials is null)
            return Failure(ScreenScraperPreviewStatus.NotConnected, "Connect a ScreenScraper account first.");

        var game = await Task.Run(() => _games.GetGame(gameId), cancellationToken);
        if (game is null)
            return Failure(ScreenScraperPreviewStatus.LibraryGameMissing, "The library game no longer exists.");
        if (!_profiles.TryGetValue(game.SystemId, out var profile))
            return Failure(ScreenScraperPreviewStatus.UnsupportedSystem, "This platform is not mapped to ScreenScraper.");

        // Three match routes, in order: (1) disc product code (a disc serial, or the GameCube/Wii
        // disc game code), read from inside the container — so a compressed image (CHD/CSO/RVZ/WBFS/…)
        // that cannot be whole-file hashed still matches; (2) arcade romsets, matched by the ROM file
        // name that ScreenScraper indexes as the set identity; (3) everything else falls through to the
        // whole-file hash fingerprint.
        var serial = await ResolveSerialAsync(game, cancellationToken);

        ScreenScraperGameRequest request;
        GameProviderMatchMethod matchMethod;
        string? evidenceValue;
        ScreenScraperFingerprintStatus? fingerprintStatus;

        if (serial is not null)
        {
            request = new ScreenScraperGameRequest(
                profile.ProviderSystemId,
                Path.GetFileName(game.Path),
                RomSize: 0,
                Serial: serial,
                Language: settings.PreferredLanguage);
            matchMethod = GameProviderMatchMethod.Serial;
            evidenceValue = serial;
            fingerprintStatus = null;
        }
        else if (FileNameMatchSystems.Contains(game.SystemId))
        {
            // No file bytes are read: the set name lives in the file name, so this needs no
            // fingerprint consent. An unknown or renamed set returns NotFound and the caller can
            // still fall back to title search.
            var fileName = Path.GetFileName(game.Path);
            request = new ScreenScraperGameRequest(
                profile.ProviderSystemId,
                fileName,
                RomSize: 0,
                Language: settings.PreferredLanguage,
                AllowFileNameMatch: true);
            matchMethod = GameProviderMatchMethod.FileName;
            evidenceValue = fileName;
            fingerprintStatus = null;
        }
        else
        {
            var fingerprint = await _fingerprints.GetOrComputeAsync(
                game,
                profile.FingerprintProfile,
                allowFingerprinting,
                cancellationToken);
            if (!fingerprint.IsSuccess)
                return FromFingerprintFailure(fingerprint);

            var evidence = fingerprint.Fingerprint!;
            request = new ScreenScraperGameRequest(
                profile.ProviderSystemId,
                Path.GetFileName(game.Path),
                evidence.FileSize,
                evidence.Crc32,
                evidence.Md5,
                evidence.Sha1,
                Language: settings.PreferredLanguage);
            matchMethod = GameProviderMatchMethod.Sha1;
            evidenceValue = evidence.Sha1;
            fingerprintStatus = fingerprint.Status;
        }

        var response = await _client.GetGameInfoAsync(credentials, request, cancellationToken);
        if (!response.IsSuccess)
        {
            return new ScreenScraperPreviewResult(
                ScreenScraperPreviewStatus.ProviderFailure,
                null,
                response.Status,
                response.Error);
        }

        return await BuildPreviewAsync(
            game, profile, response.Data!, settings, matchMethod, evidenceValue, fingerprintStatus,
            response.Quota, cancellationToken);
    }

    public async Task<ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>> SearchAsync(
        long gameId,
        string query,
        ScreenScraperSettings settings,
        CancellationToken cancellationToken = default)
    {
        var credentials = _credentials.GetCredentials();
        if (credentials is null)
        {
            return new ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>(
                ScreenScraperRequestStatus.AuthenticationFailed, null, null, "Connect a ScreenScraper account first.");
        }

        var game = await Task.Run(() => _games.GetGame(gameId), cancellationToken);
        if (game is null || !_profiles.TryGetValue(game.SystemId, out var profile))
        {
            return new ScreenScraperResult<IReadOnlyList<ScreenScraperGameMatch>>(
                ScreenScraperRequestStatus.NotFound, null, null, "This platform is not mapped to ScreenScraper.");
        }

        return await _client.SearchGamesAsync(credentials, profile.ProviderSystemId, query, cancellationToken);
    }

    public async Task<ScreenScraperPreviewResult> PreviewByProviderGameIdAsync(
        long gameId,
        string providerGameId,
        ScreenScraperSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled)
            return Failure(ScreenScraperPreviewStatus.ProviderDisabled, "ScreenScraper is disabled.");

        var credentials = _credentials.GetCredentials();
        if (credentials is null)
            return Failure(ScreenScraperPreviewStatus.NotConnected, "Connect a ScreenScraper account first.");

        var game = await Task.Run(() => _games.GetGame(gameId), cancellationToken);
        if (game is null)
            return Failure(ScreenScraperPreviewStatus.LibraryGameMissing, "The library game no longer exists.");
        if (!_profiles.TryGetValue(game.SystemId, out var profile))
            return Failure(ScreenScraperPreviewStatus.UnsupportedSystem, "This platform is not mapped to ScreenScraper.");

        var response = await _client.GetGameInfoAsync(
            credentials,
            new ScreenScraperGameRequest(
                profile.ProviderSystemId,
                Path.GetFileName(game.Path),
                RomSize: 0,
                ProviderGameId: providerGameId,
                Language: settings.PreferredLanguage),
            cancellationToken);
        if (!response.IsSuccess)
        {
            return new ScreenScraperPreviewResult(
                ScreenScraperPreviewStatus.ProviderFailure, null, response.Status, response.Error);
        }

        return await BuildPreviewAsync(
            game, profile, response.Data!, settings, GameProviderMatchMethod.UserSelectedTitleSearch,
            providerGameId, fingerprintStatus: null, response.Quota, cancellationToken);
    }

    private async Task<ScreenScraperPreviewResult> BuildPreviewAsync(
        Game game,
        ScreenScraperSystemProfile profile,
        ScreenScraperGameInfo providerGame,
        ScreenScraperSettings settings,
        GameProviderMatchMethod matchMethod,
        string? evidenceValue,
        ScreenScraperFingerprintStatus? fingerprintStatus,
        ScreenScraperQuota? quota,
        CancellationToken cancellationToken)
    {
        var fetchedAt = DateTimeOffset.UtcNow;
        var match = new GameProviderMatch(
            game.Id,
            ScreenScraperProvider.Id,
            profile.ProviderSystemId.ToString(),
            profile.MappingVersion,
            providerGame.ProviderGameId,
            providerGame.ProviderRomId,
            matchMethod,
            evidenceValue,
            GameMetadataStatus.Matched,
            fetchedAt,
            null);
        var metadata = ScreenScraperMetadataMapper.MapMetadata(
            game.Id, profile.ProviderSystemId, providerGame, settings, fetchedAt);
        var media = ScreenScraperMetadataMapper.SelectMedia(providerGame, settings);
        var existingDetails = await Task.Run(() => _details.GetDetails(game.Id), cancellationToken);
        return new ScreenScraperPreviewResult(
            ScreenScraperPreviewStatus.Success,
            new ScreenScraperGamePreview(
                game.Id, match, metadata, media, existingDetails, quota, fingerprintStatus),
            ScreenScraperRequestStatus.Success,
            null);
    }

    // Serial-based matching is enabled only for disc systems whose extracted code is the disc product
    // code ScreenScraper indexes as serialnum: the PlayStation/PS2/PS3/PSP disc serial, the Dreamcast
    // IP.BIN product number, and the GameCube/Wii disc game code. Cartridge header codes are
    // deliberately excluded so a rom hack is never matched to the original release by a shared code.
    private static readonly IReadOnlySet<string> SerialSystems =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "playstation", "playstation2", "playstation3", "psp", "gamecube", "wii", "dreamcast",
        };

    // File-name matching is enabled only for arcade, where the ROM file name is the FBNeo/MAME set
    // short id ScreenScraper indexes as the canonical game identity (there is no whole-file hash of a
    // repacked set archive). Console systems are excluded: there the file name is an arbitrary label
    // a rom hack can share, which is exactly what the client's name-only guard rejects.
    private static readonly IReadOnlySet<string> FileNameMatchSystems =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "arcade" };

    private async Task<string?> ResolveSerialAsync(Game game, CancellationToken cancellationToken)
    {
        if (!SerialSystems.Contains(game.SystemId))
            return null;

        var stored = await Task.Run(() => _games.GetIdentifiers(game.Id), cancellationToken);
        if (FindSerial(stored) is { } cachedSerial)
            return cachedSerial;

        // No serial on file yet (a freshly imported disc). Extract it on demand — a targeted read
        // of the boot record that works through CHD/CSO/… — and persist it so later runs reuse it.
        if (!_identifierExtractors.TryGetValue(game.SystemId, out var extractor))
            return null;

        var extracted = await Task.Run(() => extractor.Extract(game), cancellationToken);
        if (extracted.Count == 0)
            return null;

        await Task.Run(() => _games.ReplaceIdentifiers(game.Id, extracted), cancellationToken);
        return FindSerial(extracted);
    }

    // A disc product code is persisted as either a Serial (PlayStation/Dreamcast disc serials) or a
    // DiscId (the GameCube/Wii disc game code); both are the value ScreenScraper indexes as serialnum.
    // A Serial wins when a system happens to offer both, so the more specific disc serial is preferred.
    private static string? FindSerial(IReadOnlyList<GameIdentifier> identifiers)
    {
        string? discId = null;
        foreach (var identifier in identifiers)
        {
            if (string.IsNullOrWhiteSpace(identifier.Value))
                continue;
            if (identifier.Kind == GameIdentifierKind.Serial)
                return identifier.Value;
            if (identifier.Kind == GameIdentifierKind.DiscId)
                discId ??= identifier.Value;
        }

        return discId;
    }

    private static ScreenScraperPreviewResult FromFingerprintFailure(
        ScreenScraperFingerprintResult fingerprint) =>
        Failure(
            fingerprint.Status switch
            {
                ScreenScraperFingerprintStatus.ConsentRequired =>
                    ScreenScraperPreviewStatus.FingerprintConsentRequired,
                ScreenScraperFingerprintStatus.UnsupportedFormat =>
                    ScreenScraperPreviewStatus.UnsupportedFormat,
                ScreenScraperFingerprintStatus.SourceMissing =>
                    ScreenScraperPreviewStatus.SourceMissing,
                ScreenScraperFingerprintStatus.SourceChanged =>
                    ScreenScraperPreviewStatus.SourceChanged,
                _ => ScreenScraperPreviewStatus.FingerprintFailed,
            },
            fingerprint.Error);

    private static ScreenScraperPreviewResult Failure(
        ScreenScraperPreviewStatus status,
        string? error) => new(status, null, null, error);
}
