using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Core.TexturePacks;

namespace EmuShelf.App.Tests;

public sealed class TexturePackCoordinatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("emushelf-texture-coordinator").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task ACacheOnlyLoadThatFindsNothingCached_StillCountsAsNotScanned()
    {
        // Nothing was examined, so the library must keep saying "not scanned yet" rather than
        // claiming every game definitively has no texture pack.
        var coordinator = Create(new MemoryStore());

        Assert.False(coordinator.HasScanned);
        Assert.Empty(coordinator.Current.Map.Classifications);

        await coordinator.LoadCachedAsync(TestContext.Current.CancellationToken);

        Assert.False(coordinator.HasScanned);
    }

    [Fact]
    public async Task AnExplicitRescan_CountsAsScannedEvenWhenItFindsNoPack()
    {
        var coordinator = Create(new MemoryStore());

        await coordinator.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.True(coordinator.HasScanned);
    }

    [Fact]
    public async Task WithNoEmulatorConfigured_EveryPlatformIsDescribedRatherThanFailing()
    {
        // Deliberately not asserting that no root is found: this test machine may genuinely have an
        // emulator installed in a platform-default location, and discovering it is correct. What
        // must hold either way is that every platform produces a describable row and that a cached
        // load reports nothing scanned yet.
        var coordinator = Create(new MemoryStore());

        var result = await coordinator.LoadCachedAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(result.Platforms);
        Assert.All(result.Platforms, platform =>
        {
            Assert.False(string.IsNullOrWhiteSpace(platform.DisplayName));
            Assert.False(platform.IsOverridden);
            Assert.Equal(TexturePackRootStatus.Unknown, platform.RootStatus);
        });
        // Nothing has been scanned, so no pack can be classified and no game can be marked.
        Assert.Empty(result.Map.Classifications);
    }

    [Fact]
    public async Task ThePlatformRowsCoverEveryRegisteredSystemInPresentationOrder()
    {
        var result = await Create(new MemoryStore()).LoadCachedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            TexturePackProviderRegistry.SystemIds,
            result.Platforms.Select(platform => platform.SystemId).ToArray());
    }

    [Fact]
    public async Task DisabledInSettings_SkipsScanningEntirely()
    {
        var store = new MemoryStore();
        var coordinator = Create(store, new TexturePackSettings { Enabled = false });

        var result = await coordinator.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result.Platforms);
        Assert.Equal(0, store.LoadCount);
    }

    [Fact]
    public async Task DuckStationOverride_IsScannedAndItsUsablePackMatchesAnImportedSerial()
    {
        // A serial folder with a DuckStation-recognized replacement filename is a real pack.
        var textures = Path.Combine(_root, "textures");
        var replacements = Path.Combine(textures, "SLUS-00594", "replacements");
        Directory.CreateDirectory(replacements);
        File.WriteAllText(
            Path.Combine(replacements, "vram-write-0123456789abcdef0123456789abcdef.png"),
            "x");

        var coordinator = Create(
            new MemoryStore(),
            new TexturePackSettings().WithOverride("playstation", textures),
            new StubMetadataStore(new Dictionary<long, IReadOnlyList<GameIdentifier>>
            {
                [7] = [new GameIdentifier(GameIdentifierKind.Serial, "SLUS-00594", "test")],
            }));

        var result = await coordinator.RefreshAsync(TestContext.Current.CancellationToken);

        var match = Assert.Single(result.Map.GetMatches(7));
        Assert.Equal("SLUS-00594", match.PackKey);
        var platform = result.Platforms.Single(p => p.SystemId == "playstation");
        Assert.True(platform.IsOverridden);
        Assert.Equal(TexturePackRootStatus.Ready, platform.RootStatus);
    }

    [Fact]
    public async Task AMissingOverrideFolder_ReportsTheFolderStateWithoutThrowing()
    {
        var coordinator = Create(
            new MemoryStore(),
            new TexturePackSettings().WithOverride("playstation", Path.Combine(_root, "gone")));

        var result = await coordinator.RefreshAsync(TestContext.Current.CancellationToken);

        var platform = result.Platforms.Single(p => p.SystemId == "playstation");
        Assert.Equal(TexturePackRootStatus.Missing, platform.RootStatus);
        Assert.Empty(result.Map.Classifications);
    }

    [Fact]
    public async Task Cancellation_IsObservedAndLeavesThePreviousResultIntact()
    {
        var coordinator = Create(new MemoryStore());
        await coordinator.LoadCachedAsync(TestContext.Current.CancellationToken);
        var before = coordinator.Current;

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.RefreshAsync(cancellation.Token));
        Assert.Same(before, coordinator.Current);
    }

    [Fact]
    public async Task IdentifiersAreReadInOneBulkQueryPerPassRatherThanOncePerGame()
    {
        var metadata = new StubMetadataStore(new Dictionary<long, IReadOnlyList<GameIdentifier>>
        {
            [7] = [new GameIdentifier(GameIdentifierKind.Serial, "SLUS-00594", "test")],
            [8] = [new GameIdentifier(GameIdentifierKind.Serial, "SLUS-00779", "test")],
        });
        var coordinator = Create(new MemoryStore(), new TexturePackSettings(), metadata);

        await coordinator.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, metadata.BulkReads);
        Assert.Equal(0, metadata.PerGameReads);
    }

    private static TexturePackCoordinator Create(
        ITexturePackInventoryStore store,
        TexturePackSettings? settings = null,
        IGameMetadataStore? metadata = null) =>
        new(
            new FakePaths(),
            metadata ?? new StubMetadataStore(new Dictionary<long, IReadOnlyList<GameIdentifier>>()),
            new AppSettings { TexturePacks = settings ?? new TexturePackSettings() },
            NullAppLogger.Instance,
            store);

    private sealed class MemoryStore : ITexturePackInventoryStore
    {
        private readonly Dictionary<string, TexturePackInventorySnapshot> _snapshots = new(StringComparer.Ordinal);

        public int LoadCount { get; private set; }

        public Task<TexturePackInventorySnapshot?> LoadAsync(
            string installationId,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(_snapshots.GetValueOrDefault(installationId));
        }

        public Task SaveAsync(
            TexturePackInventorySnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            _snapshots[snapshot.InstallationId] = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class StubMetadataStore(IReadOnlyDictionary<long, IReadOnlyList<GameIdentifier>> identifiers)
        : IGameMetadataStore
    {
        public int BulkReads { get; private set; }
        public int PerGameReads { get; private set; }

        public IReadOnlyDictionary<long, IReadOnlyList<GameIdentifier>> GetAllIdentifiers()
        {
            BulkReads++;
            return identifiers;
        }

        public IReadOnlyList<GameIdentifier> GetIdentifiers(long gameId)
        {
            PerGameReads++;
            return identifiers.GetValueOrDefault(gameId, []);
        }

        public Game? GetGame(long gameId) => null;

        public IReadOnlyList<Game> GetGamesMissingMetadata(string? systemId = null) => [];

        public void ReplaceIdentifiers(long gameId, IReadOnlyList<GameIdentifier> identifiers)
        {
        }

        public bool TryApplyCatalogTitle(long gameId, string canonicalTitle, string filenameTitle) => false;

        public bool TryApplyDownloadedCover(
            long gameId,
            string coverPath,
            string providerId,
            string sourceUri) => false;

        public void RecordAttempt(GameMetadataAttempt attempt)
        {
        }
    }

    private sealed class FakePaths : IAppPaths
    {
        public string BaseDirectory => "/app";
        public string DataDirectory => "/app/Data";
        public string CoversDirectory => "/app/Covers";
        public string CacheDirectory => "/app/Cache";
        public string LogsDirectory => "/app/Logs";
        public string SettingsDirectory => "/app/Settings";
        public string SavesDirectory => "/app/Saves";
        public string DatabaseFilePath => "/app/Data/library.db";
        public string SettingsFilePath => "/app/Settings/settings.json";

        public void EnsureDirectoriesExist()
        {
        }
    }
}
