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

    public ScreenScraperPreviewService(
        IGameMetadataStore games,
        IGameDetailsStore details,
        IScreenScraperCredentialStore credentials,
        IScreenScraperFingerprintService fingerprints,
        IScreenScraperClient client,
        IReadOnlyList<ScreenScraperSystemProfile> profiles)
    {
        _games = games;
        _details = details;
        _credentials = credentials;
        _fingerprints = fingerprints;
        _client = client;
        _profiles = profiles.ToDictionary(profile => profile.SystemId, StringComparer.OrdinalIgnoreCase);
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

        var fingerprint = await _fingerprints.GetOrComputeAsync(
            game,
            profile.FingerprintProfile,
            allowFingerprinting,
            cancellationToken);
        if (!fingerprint.IsSuccess)
            return FromFingerprintFailure(fingerprint);

        var evidence = fingerprint.Fingerprint!;
        var response = await _client.GetGameInfoAsync(
            credentials,
            new ScreenScraperGameRequest(
                profile.ProviderSystemId,
                Path.GetFileName(game.Path),
                evidence.FileSize,
                evidence.Crc32,
                evidence.Md5,
                evidence.Sha1,
                Language: settings.PreferredLanguage),
            cancellationToken);
        if (!response.IsSuccess)
        {
            return new ScreenScraperPreviewResult(
                ScreenScraperPreviewStatus.ProviderFailure,
                null,
                response.Status,
                response.Error);
        }

        var fetchedAt = DateTimeOffset.UtcNow;
        var providerGame = response.Data!;
        var match = new GameProviderMatch(
            game.Id,
            ScreenScraperProvider.Id,
            profile.ProviderSystemId.ToString(),
            profile.MappingVersion,
            providerGame.ProviderGameId,
            providerGame.ProviderRomId,
            GameProviderMatchMethod.Sha1,
            evidence.Sha1,
            GameMetadataStatus.Matched,
            fetchedAt,
            null);
        var metadata = ScreenScraperMetadataMapper.MapMetadata(
            game.Id,
            profile.ProviderSystemId,
            providerGame,
            settings,
            fetchedAt);
        var media = ScreenScraperMetadataMapper.SelectMedia(providerGame, settings);
        var existingDetails = await Task.Run(() => _details.GetDetails(game.Id), cancellationToken);
        return new ScreenScraperPreviewResult(
            ScreenScraperPreviewStatus.Success,
            new ScreenScraperGamePreview(
                game.Id,
                match,
                metadata,
                media,
                existingDetails,
                response.Quota,
                fingerprint.Status),
            ScreenScraperRequestStatus.Success,
            null);
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
