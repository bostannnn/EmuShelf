using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Metadata;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class ScreenScraperFingerprintServiceTests : TempAppDirectoryTestBase
{
    private readonly LibraryDatabase _database;
    private readonly GameLibrary _library;
    private readonly SqliteGameFileFingerprintStore _store;

    public ScreenScraperFingerprintServiceTests()
    {
        AppPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(AppPaths);
        _database.Initialize();
        var resolver = new RelativePathResolver(AppPaths);
        _library = new GameLibrary(_database, resolver);
        _store = new SqliteGameFileFingerprintStore(_database, resolver);
    }

    [Fact]
    public async Task WholeFileFingerprint_ComputesAllHashesOnce_AndPreservesSource()
    {
        var game = AddGame("Known.gba", "123456789"u8.ToArray(), "gba");
        var originalBytes = File.ReadAllBytes(game.Path);
        var originalLastWrite = File.GetLastWriteTimeUtc(game.Path);
        var service = new ScreenScraperFingerprintService(_store);
        Assert.True(KnownScreenScraperFingerprintProfiles.TryGet(game.SystemId, out var profile));

        var computed = await service.GetOrComputeAsync(game, profile!, allowCompute: true);
        var cached = await service.GetOrComputeAsync(game, profile!, allowCompute: false);

        Assert.Equal(ScreenScraperFingerprintStatus.Computed, computed.Status);
        Assert.Equal("CBF43926", computed.Fingerprint!.Crc32);
        Assert.Equal("25F9E794323B453885F5181F1B624D0B", computed.Fingerprint.Md5);
        Assert.Equal("F7C3BC1D808E04732ADF679965CCC34CA7AE3441", computed.Fingerprint.Sha1);
        Assert.Equal(ScreenScraperFingerprintStatus.Cached, cached.Status);
        Assert.Equal(computed.Fingerprint.Sha1, cached.Fingerprint!.Sha1);
        Assert.Equal(originalBytes, File.ReadAllBytes(game.Path));
        Assert.Equal(originalLastWrite, File.GetLastWriteTimeUtc(game.Path));

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT SourcePath FROM GameFileFingerprints WHERE GameId = $gameId;";
        command.Parameters.AddWithValue("$gameId", game.Id);
        Assert.Equal("Games/Known.gba", ((string)command.ExecuteScalar()!).Replace('\\', '/'));
    }

    [Fact]
    public async Task ChangedFile_InvalidatesCache_AndRequiresFreshConsent()
    {
        var game = AddGame("Changed.nds", "first"u8.ToArray(), "nds");
        var service = new ScreenScraperFingerprintService(_store);
        Assert.True(KnownScreenScraperFingerprintProfiles.TryGet(game.SystemId, out var profile));
        var first = await service.GetOrComputeAsync(game, profile!, allowCompute: true);

        File.WriteAllBytes(game.Path, "changed-content"u8.ToArray());
        File.SetLastWriteTimeUtc(game.Path, DateTime.UtcNow.AddMinutes(1));
        var withoutConsent = await service.GetOrComputeAsync(game, profile!, allowCompute: false);
        var refreshed = await service.GetOrComputeAsync(game, profile!, allowCompute: true);

        Assert.Equal(ScreenScraperFingerprintStatus.ConsentRequired, withoutConsent.Status);
        Assert.Equal(ScreenScraperFingerprintStatus.Computed, refreshed.Status);
        Assert.NotEqual(first.Fingerprint!.Sha1, refreshed.Fingerprint!.Sha1);
    }

    [Fact]
    public async Task CompressedContainer_IsRejectedBeforeAnyReadOrCacheWrite()
    {
        var game = AddGame("Compressed.chd", "container"u8.ToArray(), "playstation2");
        var service = new ScreenScraperFingerprintService(_store);
        Assert.True(KnownScreenScraperFingerprintProfiles.TryGet(game.SystemId, out var profile));

        var result = await service.GetOrComputeAsync(game, profile!, allowCompute: true);

        Assert.Equal(ScreenScraperFingerprintStatus.UnsupportedFormat, result.Status);
        Assert.Null(_store.Get(game.Id, ScreenScraperProvider.Id));
    }

    [Fact]
    public async Task Fingerprinting_HonorsCancellationWithoutPersistingPartialEvidence()
    {
        var game = AddGame("Cancelled.gbc", new byte[1024], "gbc");
        var service = new ScreenScraperFingerprintService(_store);
        Assert.True(KnownScreenScraperFingerprintProfiles.TryGet(game.SystemId, out var profile));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetOrComputeAsync(game, profile!, allowCompute: true, cancellation.Token));

        Assert.Null(_store.Get(game.Id, ScreenScraperProvider.Id));
    }

    [Fact]
    public async Task EmptyFile_IsRejectedWithoutCreatingUselessEvidence()
    {
        var game = AddGame("Empty.gba", [], "gba");
        var service = new ScreenScraperFingerprintService(_store);
        Assert.True(KnownScreenScraperFingerprintProfiles.TryGet(game.SystemId, out var profile));

        var result = await service.GetOrComputeAsync(game, profile!, allowCompute: true);

        Assert.Equal(ScreenScraperFingerprintStatus.ReadFailed, result.Status);
        Assert.Null(_store.Get(game.Id, ScreenScraperProvider.Id));
    }

    [Fact]
    public async Task ConcurrentRequestsForOneGame_ComputeAndPersistOnlyOnce()
    {
        var game = AddGame("Concurrent.sfc", new byte[4 * 1024 * 1024], "snes");
        var store = new CountingFingerprintStore();
        var service = new ScreenScraperFingerprintService(store);
        Assert.True(KnownScreenScraperFingerprintProfiles.TryGet(game.SystemId, out var profile));

        var results = await Task.WhenAll(
            service.GetOrComputeAsync(game, profile!, allowCompute: true),
            service.GetOrComputeAsync(game, profile!, allowCompute: true));

        Assert.Equal(1, store.UpsertCount);
        Assert.Contains(results, result => result.Status == ScreenScraperFingerprintStatus.Computed);
        Assert.Contains(results, result => result.Status == ScreenScraperFingerprintStatus.Cached);
    }

    [Fact]
    public async Task Fingerprint_CascadesWhenGameIsRemoved()
    {
        var game = AddGame("Cascade.gba", "cascade"u8.ToArray(), "gba");
        var service = new ScreenScraperFingerprintService(_store);
        Assert.True(KnownScreenScraperFingerprintProfiles.TryGet(game.SystemId, out var profile));
        await service.GetOrComputeAsync(game, profile!, allowCompute: true);

        _library.RemoveGame(game.Id);

        using var connection = _database.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM GameFileFingerprints;";
        Assert.Equal(0L, (long)command.ExecuteScalar()!);
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
        return _library.GetGames().Single(game => game.Path == path);
    }

    private sealed class CountingFingerprintStore : IGameFileFingerprintStore
    {
        private GameFileFingerprint? _fingerprint;
        public int UpsertCount { get; private set; }

        public GameFileFingerprint? Get(long gameId, string providerId) => _fingerprint;

        public void Upsert(GameFileFingerprint fingerprint)
        {
            UpsertCount++;
            _fingerprint = fingerprint;
        }

        public void Remove(long gameId, string providerId) => _fingerprint = null;
    }
}
