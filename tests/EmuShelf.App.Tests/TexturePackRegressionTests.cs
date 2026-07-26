using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators;

namespace EmuShelf.App.Tests;

/// <summary>
/// Covers the integration seams the original unit tests missed: identifier availability, override
/// persistence, and the cached-load status. Each test here corresponds to a defect that shipped.
/// </summary>
public sealed class TexturePackRegressionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("emushelf-texture-regression").FullName;

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
    public async Task AGameCubePack_MatchesWithoutTheOptInMetadataPassHavingRunFirst()
    {
        // The defect: GameCube, Wii, PS1, and PS2 write no identifiers at import, so every pack for
        // them sat at "identification pending" unless the user had run network metadata enrichment.
        var textures = CreateDolphinPack("GALE01");
        var game = new Game { Id = 7, SystemId = "gamecube", Path = "/games/melee.iso", Title = "Melee" };
        var metadata = new StubMetadataStore();
        var coordinator = Create(
            new TexturePackSettings().WithOverride("gamecube", textures),
            metadata,
            [game],
            // A local disc-header read; no network and no consent involved.
            extractor: _ => [new GameIdentifier(GameIdentifierKind.DiscId, "GALE01", "DiscHeader")]);

        var result = await coordinator.RefreshAsync(TestContext.Current.CancellationToken);

        var match = Assert.Single(result.Map.GetMatches(7));
        Assert.Equal("GALE01", match.PackKey);
        Assert.Equal(1, result.Map.MatchedCount);
        Assert.True(metadata.WroteIdentifiersFor(7));
    }

    [Fact]
    public async Task TheBackfill_NeverReExtractsEvidenceThatIsAlreadyStored()
    {
        var textures = CreateDolphinPack("GALE01");
        var game = new Game { Id = 7, SystemId = "gamecube", Path = "/games/melee.iso", Title = "Melee" };
        var metadata = new StubMetadataStore();
        metadata.Seed(7, new GameIdentifier(GameIdentifierKind.DiscId, "GALE01", "DiscHeader"));
        var extractions = 0;
        var coordinator = Create(
            new TexturePackSettings().WithOverride("gamecube", textures),
            metadata,
            [game],
            extractor: _ =>
            {
                extractions++;
                return [];
            });

        var result = await coordinator.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Single(result.Map.GetMatches(7));
        Assert.Equal(0, extractions);
    }

    [Fact]
    public async Task TheBackfill_DoesNotReadDiscsForAPlatformWithNoUsablePack()
    {
        // An empty texture folder can never produce a match, so reading every disc for it would be
        // pure cost. Only platforms holding a usable pack are worth the evidence read.
        var empty = Path.Combine(_root, "empty-textures");
        Directory.CreateDirectory(empty);
        var game = new Game { Id = 7, SystemId = "gamecube", Path = "/games/melee.iso", Title = "Melee" };
        var extractions = 0;
        var coordinator = Create(
            new TexturePackSettings().WithOverride("gamecube", empty),
            new StubMetadataStore(),
            [game],
            extractor: _ =>
            {
                extractions++;
                return [];
            });

        await coordinator.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, extractions);
    }

    [Fact]
    public async Task AStartupCachedLoad_NeverReadsDiscsForEvidence()
    {
        // Startup must stay cheap: the backfill belongs to the explicit rescan only.
        var textures = CreateDolphinPack("GALE01");
        var game = new Game { Id = 7, SystemId = "gamecube", Path = "/games/melee.iso", Title = "Melee" };
        var extractions = 0;
        var coordinator = Create(
            new TexturePackSettings().WithOverride("gamecube", textures),
            new StubMetadataStore(),
            [game],
            extractor: _ =>
            {
                extractions++;
                return [];
            });

        await coordinator.LoadCachedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, extractions);
    }

    [Fact]
    public void AnOverride_IsPersistedSoItSurvivesARestart()
    {
        // The defect: the override lived only in memory, so a chosen folder silently reverted to
        // auto-detection on the next launch.
        var settings = new SettingsRecorder();
        var coordinator = new TexturePackCoordinator(
            new FakePaths(),
            new StubMetadataStore(),
            new AppSettings(),
            NullAppLogger.Instance,
            new MemoryStore(),
            settingsService: settings);

        coordinator.UpdateOverride("gamecube", @"D:\Dolphin\Load\Textures");

        Assert.Equal(@"D:\Dolphin\Load\Textures", settings.Saved?.TexturePacks.GetOverride("gamecube"));
    }

    [Fact]
    public void ClearingAnOverride_IsAlsoPersisted()
    {
        var settings = new SettingsRecorder();
        var coordinator = new TexturePackCoordinator(
            new FakePaths(),
            new StubMetadataStore(),
            new AppSettings
            {
                TexturePacks = new TexturePackSettings().WithOverride("gamecube", @"D:\old"),
            },
            NullAppLogger.Instance,
            new MemoryStore(),
            settingsService: settings);

        coordinator.UpdateOverride("gamecube", null);

        Assert.Null(settings.Saved?.TexturePacks.GetOverride("gamecube"));
    }

    [Fact]
    public async Task ACachedInventory_ReportsItsOwnRootStatusRatherThanNotScannedYet()
    {
        // The defect: a cached load reported Unknown/stale, so Settings said "Not scanned yet"
        // while showing a perfectly good cached inventory.
        var textures = CreateDolphinPack("GALE01");
        var store = new MemoryStore();
        var settings = new TexturePackSettings().WithOverride("gamecube", textures);

        await Create(settings, new StubMetadataStore(), [], store: store)
            .RefreshAsync(TestContext.Current.CancellationToken);

        var reloaded = Create(settings, new StubMetadataStore(), [], store: store);
        var result = await reloaded.LoadCachedAsync(TestContext.Current.CancellationToken);

        var platform = result.Platforms.Single(p => p.SystemId == "gamecube");
        Assert.Equal(TexturePackRootStatus.Ready, platform.RootStatus);
        Assert.False(platform.IsStale);
        Assert.True(reloaded.HasScanned);
    }

    [Fact]
    public void DolphinDiscovery_PrefersAPopulatedUserFolderOverAnEmptyOneBesideTheExecutable()
    {
        // The real case this came from: a frontend-managed layout keeps the Dolphin binary under
        // <root>/Emulators/dolphin-emu (with its own empty User folder) while the actual packs live
        // in <root>/saves/dolphin/User. Picking the empty one found zero packs forever.
        var install = Path.Combine(_root, "Emulators", "dolphin-emu");
        Directory.CreateDirectory(Path.Combine(install, "User", "Load", "Textures"));
        var managed = Path.Combine(_root, "saves", "dolphin", "User");
        Directory.CreateDirectory(Path.Combine(managed, "Load", "Textures", "GALE01"));

        var chosen = EmulatorUserDirectories.FindDolphin(install, isFlatpak: false);

        Assert.Equal(managed, chosen);
    }

    [Fact]
    public void DolphinDiscovery_KeepsTheFolderBesideTheExecutableWhenItIsTheOneWithPacks()
    {
        // The ordinary portable install must not be dragged away by the new candidate.
        var install = Path.Combine(_root, "Emulators", "dolphin-emu");
        Directory.CreateDirectory(Path.Combine(install, "User", "Load", "Textures", "GALE01"));
        Directory.CreateDirectory(Path.Combine(_root, "saves", "dolphin", "User", "Load", "Textures"));

        var chosen = EmulatorUserDirectories.FindDolphin(install, isFlatpak: false);

        Assert.Equal(Path.Combine(install, "User"), chosen);
    }

    [Fact]
    public void DolphinDiscovery_FallsBackToTheFirstExistingFolderWhenNoneHoldsPacks()
    {
        var install = Path.Combine(_root, "Emulators", "dolphin-emu");
        Directory.CreateDirectory(Path.Combine(install, "User"));

        var chosen = EmulatorUserDirectories.FindDolphin(install, isFlatpak: false);

        Assert.Equal(Path.Combine(install, "User"), chosen);
    }

    [Fact]
    public void TheUnsupportedTooltip_DoesNotBlameConfiguration()
    {
        // A console EmuShelf will never track packs for must not imply the user can fix it by
        // configuring an emulator.
        Assert.DoesNotContain("configured", TexturePackDisplay.Unsupported.Tooltip, StringComparison.OrdinalIgnoreCase);
    }

    private string CreateDolphinPack(string gameId)
    {
        var directory = Path.Combine(_root, "textures", gameId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "tex1_64x64_abcdef0123456789_1.png"), "x");
        return Path.Combine(_root, "textures");
    }

    private TexturePackCoordinator Create(
        TexturePackSettings settings,
        StubMetadataStore metadata,
        Game[] games,
        Func<Game, IReadOnlyList<GameIdentifier>>? extractor = null,
        ITexturePackInventoryStore? store = null) =>
        new(
            new FakePaths(),
            metadata,
            new AppSettings { TexturePacks = settings },
            NullAppLogger.Instance,
            store ?? new MemoryStore(),
            gamesForSystem: systemId => games.Where(game => game.SystemId == systemId).ToArray(),
            metadataProfiles:
            [
                new MetadataSystemProfile(
                    "gamecube",
                    GameIdentifierKind.DiscId,
                    new Uri("https://example.invalid/catalog"),
                    new StubExtractor(extractor ?? (_ => [])),
                    []),
            ]);

    private sealed class StubExtractor(Func<Game, IReadOnlyList<GameIdentifier>> extract)
        : IGameIdentifierExtractor
    {
        public IReadOnlyList<GameIdentifier> Extract(Game game) => extract(game);
    }

    private sealed class SettingsRecorder : ISettingsService
    {
        public AppSettings? Saved { get; private set; }
        public AppSettings Load() => Saved ?? new AppSettings();
        public void Save(AppSettings settings) => Saved = settings;
    }

    private sealed class MemoryStore : ITexturePackInventoryStore
    {
        private readonly Dictionary<string, TexturePackInventorySnapshot> _snapshots = new(StringComparer.Ordinal);

        public Task<TexturePackInventorySnapshot?> LoadAsync(
            string installationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_snapshots.GetValueOrDefault(installationId));

        public Task SaveAsync(
            TexturePackInventorySnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            _snapshots[snapshot.InstallationId] = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class StubMetadataStore : IGameMetadataStore
    {
        private readonly Dictionary<long, IReadOnlyList<GameIdentifier>> _identifiers = new();
        private readonly HashSet<long> _written = [];

        public void Seed(long gameId, params GameIdentifier[] identifiers) =>
            _identifiers[gameId] = identifiers;

        public bool WroteIdentifiersFor(long gameId) => _written.Contains(gameId);

        public IReadOnlyDictionary<long, IReadOnlyList<GameIdentifier>> GetAllIdentifiers() =>
            new Dictionary<long, IReadOnlyList<GameIdentifier>>(_identifiers);

        public IReadOnlyList<GameIdentifier> GetIdentifiers(long gameId) =>
            _identifiers.GetValueOrDefault(gameId, []);

        public void ReplaceIdentifiers(long gameId, IReadOnlyList<GameIdentifier> identifiers)
        {
            _identifiers[gameId] = identifiers;
            _written.Add(gameId);
        }

        public Game? GetGame(long gameId) => null;
        public IReadOnlyList<Game> GetGamesMissingMetadata(string? systemId = null) => [];
        public bool TryApplyCatalogTitle(long gameId, string canonicalTitle, string filenameTitle) => false;
        public bool TryApplyDownloadedCover(long gameId, string coverPath, string providerId, string sourceUri) => false;
        public void RecordAttempt(GameMetadataAttempt attempt) { }
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
        public void EnsureDirectoriesExist() { }
    }
}
