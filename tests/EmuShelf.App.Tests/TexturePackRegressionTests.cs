using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Dolphin;

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
    public async Task DolphinTextures_FollowTheConfiguredLoadPathWhenItPointsOutsideTheUserFolder()
    {
        // The real case: a frontend redirects [General] LoadPath away from <User>/Load, so the
        // packs are nowhere near the user directory. Reading the key is what survives the move.
        var user = Path.Combine(_root, "Emulators", "dolphin-emu", "User");
        var moved = Path.Combine(_root, "somewhere", "else", "Load");
        Directory.CreateDirectory(Path.Combine(moved, "Textures", "GALE01"));
        // Written the way Dolphin actually writes it: mixed separators and a trailing slash.
        WriteDolphinIni(user, "LoadPath = " + moved.Replace('\\', '/') + "/");

        var resolution = await new DolphinTextureRootResolver("i", user).ResolveAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(moved, "Textures"), resolution.RootDirectory);
    }

    [Fact]
    public async Task DolphinTextures_FallBackToTheDefaultLoadFolderWhenTheKeyIsAbsent()
    {
        var user = Path.Combine(_root, "User");
        WriteDolphinIni(user, "UseDiscordPresence = False");

        var resolution = await new DolphinTextureRootResolver("i", user).ResolveAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(user, "Load", "Textures"), resolution.RootDirectory);
    }

    [Fact]
    public async Task DolphinTextures_UseTheDefaultWhenNoConfigurationExistsAtAll()
    {
        var user = Path.Combine(_root, "FreshUser");
        Directory.CreateDirectory(user);

        var resolution = await new DolphinTextureRootResolver("i", user).ResolveAsync(
            TestContext.Current.CancellationToken);

        Assert.True(resolution.IsResolved);
        Assert.Equal(Path.Combine(user, "Load", "Textures"), resolution.RootDirectory);
    }

    [Fact]
    public async Task DolphinTextures_ResolveARelativeLoadPathAgainstTheUserFolder()
    {
        var user = Path.Combine(_root, "RelUser");
        WriteDolphinIni(user, @"LoadPath = .\CustomLoad\");

        var resolution = await new DolphinTextureRootResolver("i", user).ResolveAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(user, "CustomLoad", "Textures"), resolution.RootDirectory);
    }

    [Fact]
    public async Task DolphinTextures_ReadLoadPathFromASeparateConfigDirectory_OnTheLinuxXdgSplit()
    {
        // On native Linux and Flatpak, Dolphin.ini lives in a separate XDG config tree, not
        // <data>/Config. The resolver reads the config directory it is handed; a stale Dolphin.ini in
        // the data tree's Config/ — where the old code wrongly looked — must be ignored.
        var dataUser = Path.Combine(_root, "data", "dolphin-emu");
        var configDirectory = Path.Combine(_root, "config", "dolphin-emu");
        var moved = Path.Combine(_root, "packs", "Load");
        Directory.CreateDirectory(Path.Combine(moved, "Textures", "GALE01"));
        Directory.CreateDirectory(configDirectory);
        File.WriteAllLines(
            Path.Combine(configDirectory, "Dolphin.ini"),
            ["[General]", "LoadPath = " + moved.Replace('\\', '/') + "/"]);
        Directory.CreateDirectory(Path.Combine(dataUser, "Config"));
        File.WriteAllLines(
            Path.Combine(dataUser, "Config", "Dolphin.ini"),
            ["[General]", "LoadPath = " + Path.Combine(_root, "WRONG").Replace('\\', '/') + "/"]);

        var resolution = await new DolphinTextureRootResolver(
            "i", dataUser, overrideDirectory: null, configDirectory: configDirectory)
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(moved, "Textures"), resolution.RootDirectory);
    }

    [Fact]
    public async Task DolphinTextures_ResolveARelativeLoadPathAgainstTheDataDirectory_NotTheConfigDirectory()
    {
        // Dolphin resolves a relative LoadPath against the user (data) directory, so a split config
        // tree must not change where the relative value lands.
        var dataUser = Path.Combine(_root, "data", "dolphin-emu");
        var configDirectory = Path.Combine(_root, "config", "dolphin-emu");
        Directory.CreateDirectory(configDirectory);
        File.WriteAllLines(
            Path.Combine(configDirectory, "Dolphin.ini"),
            ["[General]", @"LoadPath = .\CustomLoad\"]);

        var resolution = await new DolphinTextureRootResolver(
            "i", dataUser, overrideDirectory: null, configDirectory: configDirectory)
            .ResolveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(dataUser, "CustomLoad", "Textures"), resolution.RootDirectory);
    }

    [Fact]
    public void DolphinUserDirectory_HonoursThePortableMarkerBesideTheExecutable()
    {
        // Dolphin's own rule: portable.txt makes the adjacent User folder authoritative, ahead of
        // any platform default. Deliberately not asserting the negative case — what wins without
        // the marker depends on whether this machine happens to have a Documents\Dolphin Emulator.
        var install = Path.Combine(_root, "Emulators", "dolphin-emu");
        Directory.CreateDirectory(Path.Combine(install, "User"));
        File.WriteAllText(Path.Combine(install, "portable.txt"), string.Empty);

        Assert.Equal(
            Path.Combine(install, "User"),
            EmulatorUserDirectories.FindDolphin(install, isFlatpak: false));
    }

    [Fact]
    public void FindDolphinConfigDirectory_Flatpak_ResolvesTheXdgConfigTree_NotDataConfig()
    {
        // The Steam Deck regression: Dolphin's Flatpak keeps config under the sandbox's XDG_CONFIG_HOME
        // (config/dolphin-emu), a *different* tree from data/dolphin-emu — so appending "Config" to the
        // data user directory pointed at nothing. The Flatpak branch resolves against the home directory
        // (SpecialFolder.UserProfile). On Unix that follows $HOME, so the redirect below makes this
        // deterministic; on Windows SpecialFolder.UserProfile reads the real profile from the OS and
        // ignores %USERPROFILE%, so the redirect can't take — and Flatpak is a Linux-only path anyway.
        Assert.SkipWhen(
            OperatingSystem.IsWindows(),
            "SpecialFolder.UserProfile ignores the %USERPROFILE% override on Windows; Flatpak is Linux-only.");

        var home = Directory.CreateTempSubdirectory("emushelf-dolphin-flatpak").FullName;
        var config = Path.Combine(home, ".var", "app", "org.DolphinEmu.dolphin-emu", "config", "dolphin-emu");
        var dataConfig = Path.Combine(home, ".var", "app", "org.DolphinEmu.dolphin-emu", "data", "dolphin-emu", "Config");
        Directory.CreateDirectory(config);
        Directory.CreateDirectory(dataConfig); // the wrong tree the old code appended "Config" to; must be ignored.

        var previousHome = Environment.GetEnvironmentVariable("HOME");
        var previousUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        Environment.SetEnvironmentVariable("HOME", home);
        Environment.SetEnvironmentVariable("USERPROFILE", home);
        try
        {
            var resolved = EmulatorUserDirectories.FindDolphinConfigDirectory(installationDirectory: null, isFlatpak: true);

            Assert.Equal(Path.GetFullPath(config), resolved);
            Assert.NotEqual(Path.GetFullPath(dataConfig), resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HOME", previousHome);
            Environment.SetEnvironmentVariable("USERPROFILE", previousUserProfile);
            try { Directory.Delete(home, true); } catch (IOException) { }
        }
    }

    private static void WriteDolphinIni(string userDirectory, string generalLine)
    {
        var config = Path.Combine(userDirectory, "Config");
        Directory.CreateDirectory(config);
        File.WriteAllLines(
            Path.Combine(config, "Dolphin.ini"),
            ["[General]", generalLine]);
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
        public bool TryApplyDownloadedCover(long gameId, string coverPath, string providerId, string sourceUri, bool overwriteUserCover = false) => false;
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
