using System.Buffers.Binary;
using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Shell;
using EmuShelf.Core.Systems;
using EmuShelf.Infrastructure.Importing;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Launching;
using EmuShelf.Infrastructure.Metadata;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// Drives MainViewModel through the real library/scanner/rules services (only the
/// dialogs are faked) on a headless Avalonia UI thread, covering the add-folder,
/// search, and availability flows that can't be clicked in an automated run.
/// </summary>
public class MainViewModelTests : IDisposable
{
    private const string NintendoLogoHex =
        "24FFAE51699AA2213D84820A84E409AD11248B98C0817F21A352BE199309CE2010464A4AF82731EC58C7E83382E3CEBF85F4DF94CE4B09C194568AC01372A7FC9F844D73A3CA9A615897A327FC039876231DC7610304AE56BF38840040A70EFDFF52FE036F9530F197FBC08560D68025A963BE03014E38E2F9A234FFBB3E0344780090CB88113A9465C07C6387F03CAFD625E48B380AAC7221D4F807";
    private readonly string _baseDirectory =
        Path.Combine(Path.GetTempPath(), "EmuShelfAppTests", Guid.NewGuid().ToString("N"));
    private readonly GameLibrary _library;
    private readonly SqliteGameMetadataStore _metadataStore;
    private readonly LibraryDatabase _database;
    private readonly FakeDialogService _dialogs = new();
    private static readonly GameSystem Ps1 = KnownSystems.All.Single(s => s.Id == "playstation");
    private static readonly GameSystem Psp = KnownSystems.All.Single(s => s.Id == "psp");
    private static readonly GameSystem Ps3 = KnownSystems.All.Single(s => s.Id == "playstation3");
    private static readonly GameSystem GameCube = KnownSystems.All.Single(s => s.Id == "gamecube");
    private static readonly GameSystem MegaDrive = KnownSystems.All.Single(s => s.Id == "megadrive");
    private static readonly GameSystem NintendoDs = KnownSystems.All.Single(s => s.Id == "nds");
    private static readonly GameSystem GameBoyAdvance = KnownSystems.All.Single(s => s.Id == "gba");

    public MainViewModelTests()
    {
        var appPaths = new AppPaths(_baseDirectory);
        appPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(appPaths);
        _database.Initialize();
        var pathResolver = new RelativePathResolver(appPaths);
        _library = new GameLibrary(_database, pathResolver);
        _metadataStore = new SqliteGameMetadataStore(_database, pathResolver);
    }

    private MainViewModel CreateViewModel(
        IGameImportRules? importRules = null,
        IEmulatorLaunchService? launchService = null,
        IGameCoverService? covers = null,
        IAppThemeService? themes = null,
        IGameMetadataService? metadata = null,
        IMetadataPreferencesService? metadataPreferences = null,
        IRetroAchievementsIdentificationService? retroAchievements = null,
        IRetroAchievementsReadStore? retroAchievementsRead = null,
        IRetroAchievementsAccountService? retroAccount = null,
        IRetroAchievementsMatchingService? retroMatching = null,
        IRetroAchievementsProgressService? retroProgress = null,
        IRetroAchievementsDetailsService? retroDetails = null,
        IRetroAchievementsRefreshService? retroRefresh = null,
        IGameMetadataStore? metadataStore = null,
        IEmulatorConfigurationStore? emulatorConfigurations = null,
        IInterfaceModeService? interfaceModeService = null,
        IGameSaveSyncService? gameSaveSync = null,
        IApplicationLifetimeService? applicationLifetime = null,
        IScreenScraperPreviewService? screenScraperPreview = null,
        IGameScrapeApplicationService? scrapeApply = null,
        IScreenScraperBatchService? scrapeBatch = null,
        IScreenScraperAccountService? screenScraperAccount = null,
        ISettingsService? settingsService = null,
        TexturePackCoordinator? texturePacks = null,
        IFileRevealService? fileReveal = null)
    {
        importRules ??= new FileImportRules();
        return new(
            _library,
            new FolderScanner(importRules),
            importRules,
            new FileAvailabilityChecker(),
            _dialogs,
            KnownSystems.All,
            launchService,
            texturePacks: texturePacks,
            emulatorConfigurations: emulatorConfigurations,
            covers: covers,
            themeService: themes,
            metadataService: metadata,
            metadataPreferences: metadataPreferences,
            retroAchievements: retroAchievements,
            retroAchievementsRead: retroAchievementsRead,
            retroAccount: retroAccount,
            retroMatching: retroMatching,
            retroProgress: retroProgress,
            retroDetails: retroDetails,
            retroRefresh: retroRefresh,
            metadataStore: metadataStore,
            interfaceModeService: interfaceModeService,
            gameSaveSync: gameSaveSync,
            applicationLifetime: applicationLifetime,
            screenScraperAccount: screenScraperAccount,
            screenScraperPreview: screenScraperPreview,
            scrapeApply: scrapeApply,
            scrapeBatch: scrapeBatch,
            settingsService: settingsService,
            fileReveal: fileReveal);
    }

    private string MakeRomsFolder()
    {
        var folder = Path.Combine(_baseDirectory, "roms");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Alpha.cue"), "x");
        File.WriteAllText(Path.Combine(folder, "Beta.chd"), "x");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "x"); // not a game
        return folder;
    }

    private async Task AssertCartridgeFolderFlowAsync(
        GameSystem system,
        string fileName,
        byte[] bytes,
        string expectedTitle,
        string expectedGameCode,
        GameTitleOrigin expectedTitleOrigin = GameTitleOrigin.Embedded)
    {
        var folder = Path.Combine(_baseDirectory, system.Id);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllBytes(path, bytes);
        _dialogs.FolderToReturn = folder;
        _dialogs.SystemToReturn = system;
        var vm = CreateViewModel(metadataStore: _metadataStore);

        await vm.AddFolderCommand.ExecuteAsync(null);

        var game = Assert.Single(_library.GetGames(system.Id));
        Assert.Equal(expectedTitle, game.Title);
        Assert.Equal(expectedTitleOrigin, game.TitleOrigin);
        Assert.True(game.IsAvailable);
        var identifiers = _metadataStore.GetIdentifiers(game.Id);
        var gameCode = Assert.Single(identifiers, identifier => identifier.Kind == GameIdentifierKind.TitleId);
        Assert.Equal(expectedGameCode, gameCode.Value);
        var sha1 = Assert.Single(identifiers, identifier => identifier.Kind == GameIdentifierKind.Sha1);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(bytes)), sha1.Value);
        Assert.True(sha1.IsPrimary);

        File.Delete(path);
        await vm.RefreshAvailabilityAsync();
        Assert.False(Assert.Single(_library.GetGames(system.Id)).IsAvailable);

        File.WriteAllBytes(path, bytes);
        await vm.RescanSystemCommand.ExecuteAsync(null);
        Assert.True(Assert.Single(_library.GetGames(system.Id)).IsAvailable);
        Assert.Equal("Rescan complete — no new games", vm.StatusText);
    }

    private static byte[] CreateNintendoDsRom(string title, string gameCode)
    {
        var bytes = new byte[0x10000];
        Convert.FromHexString(NintendoLogoHex).CopyTo(bytes, 0xC0);
        System.Text.Encoding.ASCII.GetBytes(title).CopyTo(bytes, 0);
        System.Text.Encoding.ASCII.GetBytes(gameCode).CopyTo(bytes, 0x0C);
        "01"u8.CopyTo(bytes.AsSpan(0x10));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x20, 4), 0x4000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x2C, 4), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x30, 4), 0x5000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x3C, 4), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x84, 4), 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x15C, 2), 0xCF56);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x15E, 2), CalculateNintendoCrc16(bytes.AsSpan(0, 0x15E)));
        return bytes;
    }

    private static byte[] CreateGameBoyAdvanceRom(string title, string gameCode)
    {
        var bytes = new byte[0x1000];
        bytes[3] = 0xEA;
        Convert.FromHexString(NintendoLogoHex).CopyTo(bytes, 0x04);
        System.Text.Encoding.ASCII.GetBytes(title).CopyTo(bytes, 0xA0);
        System.Text.Encoding.ASCII.GetBytes(gameCode).CopyTo(bytes, 0xAC);
        "01"u8.CopyTo(bytes.AsSpan(0xB0));
        bytes[0xB2] = 0x96;
        byte checksum = unchecked((byte)-0x19);
        foreach (var value in bytes.AsSpan(0xA0, 0x1D))
            checksum -= value;
        bytes[0xBD] = checksum;
        return bytes;
    }

    private static ushort CalculateNintendoCrc16(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    [AvaloniaFact]
    public async Task AddFolder_ScansAndPopulatesGamesForChosenSystem()
    {
        _dialogs.FolderToReturn = MakeRomsFolder();
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(Ps1.Id, vm.SelectedSystem?.Id);
        Assert.Equal(["Alpha", "Beta"], vm.Games.Select(g => g.Title).OrderBy(t => t));
        Assert.True(vm.HasGames);
        Assert.Single(_library.GetLibraryFolders("playstation")); // remembered for rescan
    }

    [AvaloniaFact]
    public async Task AddEmptyFolder_DoesNotLeaveAHiddenPlatformSelected()
    {
        var emptyFolder = Path.Combine(_baseDirectory, "empty-roms");
        Directory.CreateDirectory(emptyFolder);
        _dialogs.FolderToReturn = emptyFolder;
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(LibraryScope.AllGames, vm.CurrentLibraryScope);
        Assert.Null(vm.SelectedSystem);
        Assert.Empty(vm.NavigationSystems);
    }

    [AvaloniaFact]
    public async Task AddFolder_PlayStation3IsReservedForTheExplicitRpcs3LibrarySync()
    {
        _dialogs.FolderToReturn = MakeRomsFolder();
        _dialogs.SystemToReturn = Ps3;
        var vm = CreateViewModel();

        await vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Empty(_library.GetGames(Ps3.Id));
        Assert.Empty(_library.GetLibraryFolders(Ps3.Id));
        Assert.Contains("imported only from RPCS3", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task MegaDriveFolderImport_RescanAndAvailabilityUseTheStrictRomPathAndPersistSha1()
    {
        var folder = Path.Combine(_baseDirectory, "Mega Drive ROMs");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "Ristar (USA).md");
        var bytes = new byte[0x4000];
        "SEGA"u8.CopyTo(bytes.AsSpan(0x100));
        File.WriteAllBytes(path, bytes);
        File.WriteAllText(Path.Combine(folder, "Archive.zip"), "not a ROM");
        _dialogs.FolderToReturn = folder;
        _dialogs.SystemToReturn = MegaDrive;
        var vm = CreateViewModel(metadataStore: _metadataStore);

        await vm.AddFolderCommand.ExecuteAsync(null);

        var game = Assert.Single(_library.GetGames(MegaDrive.Id));
        Assert.Equal("Ristar (USA)", game.Title);
        Assert.Equal(GameTitleOrigin.Filename, game.TitleOrigin);
        Assert.True(game.IsAvailable);
        var identifier = Assert.Single(_metadataStore.GetIdentifiers(game.Id));
        Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
        Assert.Equal("471EE01E97220D35105CC5E9FB2F03765623CD05", identifier.Value);

        File.Delete(path);
        await vm.RefreshAvailabilityAsync();
        Assert.False(Assert.Single(_library.GetGames(MegaDrive.Id)).IsAvailable);

        File.WriteAllBytes(path, bytes);
        await vm.RescanSystemCommand.ExecuteAsync(null);
        Assert.True(Assert.Single(_library.GetGames(MegaDrive.Id)).IsAvailable);
        Assert.Equal("Rescan complete — no new games", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task AddGames_HeaderlessMegaDriveFileIsSkippedAfterConfirmation()
    {
        var path = Path.Combine(_baseDirectory, "roms", "Not a Mega Drive ROM.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[0x4000]);
        _dialogs.FilesToReturn = [path];
        _dialogs.SystemToReturn = MegaDrive;
        var vm = CreateViewModel();

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Empty(_library.GetGames(MegaDrive.Id));
        Assert.Contains("not recognized as Mega Drive / Genesis", vm.StatusText);
    }

    [AvaloniaFact]
    public Task NintendoDsFolderImport_RescanAndAvailabilityPersistHeaderAndExactEvidence() =>
        AssertCartridgeFolderFlowAsync(
            NintendoDs,
            "Example DS.nds",
            CreateNintendoDsRom("Example DS", "ABCE"),
            "Example DS",
            "ABCE",
            GameTitleOrigin.Filename);

    [AvaloniaFact]
    public Task GameBoyAdvanceFolderImport_RescanAndAvailabilityPersistHeaderAndExactEvidence() =>
        AssertCartridgeFolderFlowAsync(
            GameBoyAdvance,
            "Example GBA.gba",
            CreateGameBoyAdvanceRom("Example GBA", "ABCE"),
            "Example GBA",
            "ABCE",
            GameTitleOrigin.Filename);

    [AvaloniaFact]
    public async Task AddGames_PspEmbeddedEvidenceSetsTheTitleAndPersistsExactIdentifier()
    {
        var path = Path.Combine(_baseDirectory, "roms", "ULUS10002.iso");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        _dialogs.FilesToReturn = [path];
        var psp = KnownSystems.All.Single(system => system.Id == "psp");
        _dialogs.SystemToReturn = psp;
        var rules = new EmbeddedEvidenceImportRules(psp, "Lumines", "ULUS10002");
        var vm = CreateViewModel(rules, metadataStore: _metadataStore);

        await vm.AddGamesCommand.ExecuteAsync(null);

        var game = Assert.Single(_library.GetGames("psp"));
        Assert.Equal("Lumines", game.Title);
        Assert.Equal(GameTitleOrigin.Embedded, game.TitleOrigin);
        var identifier = Assert.Single(_metadataStore.GetIdentifiers(game.Id));
        Assert.Equal(GameIdentifierKind.Serial, identifier.Kind);
        Assert.Equal("ULUS10002", identifier.Value);
        Assert.Equal("PSP PARAM.SFO", identifier.Source);
    }

    [AvaloniaFact]
    public async Task AddGames_RetriesPspEvidenceForAnExistingEntryAfterMetadataWriteFailure()
    {
        var path = Path.Combine(_baseDirectory, "roms", "ULUS10002.iso");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        _dialogs.FilesToReturn = [path];
        var psp = KnownSystems.All.Single(system => system.Id == "psp");
        _dialogs.SystemToReturn = psp;
        var rules = new EmbeddedEvidenceImportRules(psp, "Lumines", "ULUS10002");
        var metadataStore = new FailingOnceMetadataStore(_metadataStore);
        var vm = CreateViewModel(rules, metadataStore: metadataStore);

        await vm.AddGamesCommand.ExecuteAsync(null);

        var game = Assert.Single(_library.GetGames("psp"));
        Assert.Empty(_metadataStore.GetIdentifiers(game.Id));
        Assert.Contains("Import failed", vm.StatusText);

        await vm.AddGamesCommand.ExecuteAsync(null);

        var identifier = Assert.Single(_metadataStore.GetIdentifiers(game.Id));
        Assert.Equal("ULUS10002", identifier.Value);
        Assert.Equal(2, metadataStore.ReplaceIdentifiersCallCount);
    }

    [AvaloniaFact]
    public async Task AddGames_InvalidPspImageIsSkippedEvenAfterPspConfirmation()
    {
        var path = Path.Combine(_baseDirectory, "roms", "not-a-psp.iso");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not an ISO9660 image");
        _dialogs.FilesToReturn = [path];
        _dialogs.SystemToReturn = KnownSystems.All.Single(system => system.Id == "psp");
        var vm = CreateViewModel();

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Empty(_library.GetGames("psp"));
        Assert.Contains("not recognized as PSP", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task SettingsSyncRpcs3Library_ImportsOnlyRecordedEntriesAndRetainsSourceMissingState()
    {
        var configuration = Path.Combine(_baseDirectory, "rpcs3", "config");
        var game = Path.Combine(_baseDirectory, "rpcs3", "games", "Example Game");
        Directory.CreateDirectory(configuration);
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(configuration, "games.yml"), $"BLES12345: '{game}'\n");
        _dialogs.Rpcs3ConfigurationDirectoryToReturn = configuration;
        var viewModel = CreateViewModel();

        await viewModel.OpenSettingsCommand.ExecuteAsync(null);
        await _dialogs.MaintenanceActions!.SyncRpcs3Library!();

        var imported = Assert.Single(_library.GetGames("playstation3"));
        Assert.Equal("BLES12345", imported.ExternalSourceEntryId);
        Assert.Equal("rpcs3-library", imported.ExternalSourceId);
        Assert.Equal("Example Game", imported.Title);
        Assert.Equal(GameTitleOrigin.Filename, imported.TitleOrigin);
        Assert.True(imported.IsAvailable);
        Assert.Equal("RPCS3 library sync complete — 1 added", viewModel.StatusText);

        File.WriteAllText(Path.Combine(configuration, "games.yml"), string.Empty);
        await _dialogs.MaintenanceActions.SyncRpcs3Library();
        await viewModel.RefreshAvailabilityAsync();

        var sourceMissing = Assert.Single(_library.GetGames("playstation3"));
        Assert.False(sourceMissing.IsAvailable);
        var sourceMissingView = Assert.Single(viewModel.Games);
        Assert.Equal("Source missing", sourceMissingView.AvailabilityText);
        Assert.Equal("SOURCE MISSING", sourceMissingView.UnavailableBadgeText);
        Assert.Contains("external emulator library", sourceMissingView.UnavailableLaunchStatus);
    }

    [AvaloniaFact]
    public async Task SettingsSyncRpcs3Library_UsesTheConfiguredExecutablesConfigGameListWithoutPrompting()
    {
        var installation = Path.Combine(_baseDirectory, "rpcs3");
        var configuration = Path.Combine(installation, "config");
        var game = Path.Combine(_baseDirectory, "rpcs3", "games", "Example Game");
        Directory.CreateDirectory(configuration);
        Directory.CreateDirectory(game);
        var executable = Path.Combine(installation, "rpcs3.exe");
        File.WriteAllText(executable, string.Empty);
        File.WriteAllText(Path.Combine(configuration, "games.yml"), $"BLES12345: '{game}'\n");
        var configurations = new SqliteEmulatorConfigurationStore(
            _database,
            new RelativePathResolver(new AppPaths(_baseDirectory)));
        configurations.Save(new EmulatorConfiguration("playstation3", executable, "--no-gui \"{GamePath}\""));
        var viewModel = CreateViewModel(emulatorConfigurations: configurations);

        await viewModel.OpenSettingsCommand.ExecuteAsync(null);
        await _dialogs.MaintenanceActions!.SyncRpcs3Library!();

        Assert.Equal("RPCS3 library sync complete — 1 added", viewModel.StatusText);
        Assert.Equal("Example Game", Assert.Single(_library.GetGames("playstation3")).Title);
    }

    [AvaloniaFact]
    public async Task SettingsSyncRpcs3Library_StartsMetadataEnrichmentForNewlyAddedGames()
    {
        // PlayStation 3 games enter the library only through the RPCS3 sync — every file/folder
        // import path reserves PS3 for it — so the sync must hand newly added games to the same
        // opt-in enrichment path, or PS3 games would never receive a title or cover.
        var configuration = Path.Combine(_baseDirectory, "rpcs3", "config");
        var game = Path.Combine(_baseDirectory, "rpcs3", "games", "Example Game");
        Directory.CreateDirectory(configuration);
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(configuration, "games.yml"), $"BLES12345: '{game}'\n");
        _dialogs.Rpcs3ConfigurationDirectoryToReturn = configuration;
        _dialogs.MetadataConsentToReturn = MetadataConsentChoice.Always;
        var metadata = new RecordingMetadataService();
        var viewModel = CreateViewModel(
            metadata: metadata,
            metadataPreferences: new RecordingMetadataPreferences());

        await viewModel.OpenSettingsCommand.ExecuteAsync(null);
        await _dialogs.MaintenanceActions!.SyncRpcs3Library!();
        await metadata.Called.WaitAsync(TimeSpan.FromSeconds(2));

        var added = Assert.Single(_library.GetGames("playstation3"));
        Assert.Equal(added.Id, Assert.Single(metadata.GameIds));

        // A re-sync adds nothing, so it must not re-enrich the whole library on every sync.
        await _dialogs.MaintenanceActions.SyncRpcs3Library();
        Assert.Equal(1, metadata.CallCount);
    }

    [AvaloniaFact]
    public async Task SettingsSyncRpcs3Library_ShowsFileMissingWhenRpcs3StillListsThePath()
    {
        var configuration = Path.Combine(_baseDirectory, "rpcs3", "config");
        var game = Path.Combine(_baseDirectory, "rpcs3", "games", "Example Game");
        Directory.CreateDirectory(configuration);
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(configuration, "games.yml"), $"BLES12345: '{game}'\n");
        _dialogs.Rpcs3ConfigurationDirectoryToReturn = configuration;
        var viewModel = CreateViewModel();

        await viewModel.OpenSettingsCommand.ExecuteAsync(null);
        await _dialogs.MaintenanceActions!.SyncRpcs3Library!();
        Directory.Delete(game);
        await _dialogs.MaintenanceActions.SyncRpcs3Library();

        var unavailable = Assert.Single(_library.GetGames("playstation3"));
        Assert.True(unavailable.IsPresentInExternalSource);
        Assert.False(unavailable.IsAvailable);
        var unavailableView = Assert.Single(viewModel.Games);
        Assert.Equal("Unavailable", unavailableView.AvailabilityText);
        Assert.Equal("FILE MISSING", unavailableView.UnavailableBadgeText);
        Assert.Contains("recorded by its external emulator library", unavailableView.UnavailableLaunchStatus);
    }

    [AvaloniaFact]
    public async Task SettingsSyncRpcs3Library_ReportsAPathConflictWithoutChangingTheLibrary()
    {
        var configuration = Path.Combine(_baseDirectory, "rpcs3", "config");
        var path = Path.Combine(_baseDirectory, "games", "Shared game");
        Directory.CreateDirectory(configuration);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(configuration, "games.yml"), $"BLES12345: '{path}'\n");
        _library.AddGames([
            new Game
            {
                SystemId = Ps1.Id,
                Path = path,
                Title = "Manual game",
                DateAdded = DateTimeOffset.UtcNow,
            },
        ]);
        _dialogs.Rpcs3ConfigurationDirectoryToReturn = configuration;
        var viewModel = CreateViewModel();

        await viewModel.OpenSettingsCommand.ExecuteAsync(null);
        await _dialogs.MaintenanceActions!.SyncRpcs3Library!();

        Assert.Contains("already owned by a different EmuShelf game", viewModel.StatusText);
        Assert.Single(_library.GetGames(Ps1.Id));
        Assert.Empty(_library.GetGames(Ps3.Id));
    }

    [AvaloniaFact]
    public async Task SettingsSyncRpcs3Library_RejectsUnsupportedInputWithoutImporting()
    {
        var configuration = Path.Combine(_baseDirectory, "rpcs3", "config");
        Directory.CreateDirectory(configuration);
        File.WriteAllText(
            Path.Combine(configuration, "games.yml"),
            "BLES12345:\n  path: /games/example\n");
        _dialogs.Rpcs3ConfigurationDirectoryToReturn = configuration;
        var viewModel = CreateViewModel();

        await viewModel.OpenSettingsCommand.ExecuteAsync(null);
        await _dialogs.MaintenanceActions!.SyncRpcs3Library!();

        Assert.Empty(_library.GetGames("playstation3"));
        Assert.Contains("No games were imported", viewModel.StatusText);
    }

    [AvaloniaFact]
    public async Task AddFolder_ThenSearch_FiltersGames()
    {
        _dialogs.FolderToReturn = MakeRomsFolder();
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();
        await vm.AddFolderCommand.ExecuteAsync(null);

        vm.SearchText = "alph";
        vm.ApplyFilter(); // apply immediately instead of waiting for the debounce timer

        Assert.Equal(["Alpha"], vm.Games.Select(g => g.Title));
    }

    [AvaloniaFact]
    public async Task RefreshAvailability_MarksMissingFileUnavailable()
    {
        var folder = MakeRomsFolder();
        _dialogs.FolderToReturn = folder;
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();
        await vm.AddFolderCommand.ExecuteAsync(null);

        // Delete one backing file, then run the availability pass.
        File.Delete(Path.Combine(folder, "Alpha.cue"));
        await vm.RefreshAvailabilityAsync();

        var alpha = vm.Games.Single(g => g.Title == "Alpha");
        var beta = vm.Games.Single(g => g.Title == "Beta");
        Assert.False(alpha.IsAvailable);
        Assert.True(beta.IsAvailable);
    }

    [AvaloniaFact]
    public async Task ShowGameInFolder_RevealsTheSelectedLaunchSource()
    {
        var folder = MakeRomsFolder();
        _dialogs.FolderToReturn = folder;
        _dialogs.SystemToReturn = Ps1;
        var reveal = new FakeFileRevealService();
        var vm = CreateViewModel(fileReveal: reveal);
        await vm.AddFolderCommand.ExecuteAsync(null);

        var alpha = vm.Games.Single(g => g.Title == "Alpha");
        await alpha.ShowInFolderCommand.ExecuteAsync(alpha);

        Assert.Equal(1, reveal.RevealCount);
        Assert.Equal(alpha.LaunchModel.Path, reveal.LastRevealedPath);
        Assert.EndsWith("Alpha.cue", reveal.LastRevealedPath);
    }

    [AvaloniaFact]
    public async Task ShowGameInFolder_ReportsAFriendlyStatusWhenTheRevealFails()
    {
        var folder = MakeRomsFolder();
        _dialogs.FolderToReturn = folder;
        _dialogs.SystemToReturn = Ps1;
        var reveal = new FakeFileRevealService { Failure = new DirectoryNotFoundException("gone") };
        var vm = CreateViewModel(fileReveal: reveal);
        await vm.AddFolderCommand.ExecuteAsync(null);

        var alpha = vm.Games.Single(g => g.Title == "Alpha");
        await alpha.ShowInFolderCommand.ExecuteAsync(alpha);

        Assert.Contains("Could not open the folder for Alpha", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task AddGames_Files_ImportsUnderConfirmedSystem()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Equal(["Alpha"], vm.Games.Select(g => g.Title));
    }

    [AvaloniaFact]
    public async Task AddGames_PlayStation3IsReservedForTheExplicitRpcs3LibrarySync()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps3;
        var vm = CreateViewModel();

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Empty(_library.GetGames(Ps3.Id));
        Assert.Contains("imported only from RPCS3", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task FirstImport_OffersOptInAndAlwaysChoiceStartsMetadata()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        _dialogs.MetadataConsentToReturn = MetadataConsentChoice.Always;
        var metadata = new RecordingMetadataService();
        var preferences = new RecordingMetadataPreferences();
        var vm = CreateViewModel(
            metadata: metadata,
            metadataPreferences: preferences);

        await vm.AddGamesCommand.ExecuteAsync(null);
        await metadata.Called.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, _dialogs.MetadataConsentPrompts);
        Assert.Equal(MetadataConsentChoice.Always, preferences.RecordedChoice);
        Assert.True(preferences.AutomaticallyFetchAfterImport);
        Assert.Single(metadata.GameIds);
    }

    [AvaloniaFact]
    public async Task Import_RunsRetroAchievementsIdentification_EvenWhenMetadataDeclined()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        _dialogs.MetadataConsentToReturn = MetadataConsentChoice.NotNow;
        var metadata = new RecordingMetadataService();
        var achievements = new RecordingRetroAchievementsIdentificationService();
        var vm = CreateViewModel(
            metadata: metadata,
            metadataPreferences: new RecordingMetadataPreferences(),
            retroAchievements: achievements,
            retroAccount: new RecordingRetroAchievementsAccountService(isConnected: true));

        await vm.AddGamesCommand.ExecuteAsync(null);
        await achievements.Called.WaitAsync(TimeSpan.FromSeconds(2));

        // Local hashing runs on the imported game regardless of the network-metadata choice.
        Assert.Single(achievements.GameIds);
        Assert.False(metadata.Called.IsCompleted);
    }

    [AvaloniaFact]
    public async Task SettingsRescan_QueuesOnlyNewGamesForRetroAchievementsWhenConnected()
    {
        var folder = MakeRomsFolder();
        _dialogs.FolderToReturn = folder;
        _dialogs.SystemToReturn = Ps1;
        var identification = new RecordingRetroAchievementsIdentificationService();
        var account = new RecordingRetroAchievementsAccountService(isConnected: false);
        var matching = new RecordingRetroAchievementsMatchingService();
        var vm = CreateViewModel(
            retroAchievements: identification,
            retroAccount: account,
            retroMatching: matching,
            retroProgress: new RecordingRetroAchievementsProgressService());

        await vm.AddFolderCommand.ExecuteAsync(null);
        await account.ConnectAsync("Player", "key", TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(folder, "Gamma.chd"), "x");
        await vm.OpenSettingsCommand.ExecuteAsync(null);

        await _dialogs.MaintenanceActions!.RescanAll(new Progress<string>());
        await identification.Called.WaitAsync(TimeSpan.FromSeconds(2));

        var gammaId = _library.GetGames().Single(game => game.Title == "Gamma").Id;
        Assert.Equal([gammaId], identification.GameIds);
        Assert.Equal([false], matching.ForceRefreshCatalogues);
    }

    [AvaloniaFact]
    public async Task SettingsRetroAchievementsRefresh_ForcesCatalogueRematchWithoutRehashingKnownGames()
    {
        var path = Path.Combine(_baseDirectory, "Existing.cue");
        File.WriteAllText(path, "FILE \"Existing.bin\" BINARY");
        _library.AddGames([
            new Game
            {
                SystemId = Ps1.Id,
                Path = path,
                Title = "Existing game",
                IsAvailable = true,
                DateAdded = DateTimeOffset.UtcNow,
            },
        ]);
        var gameId = Assert.Single(_library.GetGames()).Id;
        var identification = new RecordingRetroAchievementsIdentificationService();
        var matching = new RecordingRetroAchievementsMatchingService();
        var vm = CreateViewModel(
            retroAchievements: identification,
            retroAccount: new RecordingRetroAchievementsAccountService(isConnected: true),
            retroMatching: matching,
            retroProgress: new RecordingRetroAchievementsProgressService());

        await vm.OpenSettingsCommand.ExecuteAsync(null);
        var sync = await _dialogs.RetroAchievementsContext!.RefreshMatchesAsync!(
            null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(sync);
        Assert.Equal([gameId], identification.GameIds);
        Assert.Equal([true], matching.ForceRefreshCatalogues);
        Assert.Equal(1, sync.Progress?.UpdatedGames);
    }

    [AvaloniaFact]
    public async Task Connect_BackfillsExistingLibraryBeforeMatchingAndRefreshingProgress()
    {
        var path = Path.Combine(_baseDirectory, "Existing.cue");
        File.WriteAllText(path, "FILE \"Existing.bin\" BINARY");
        _library.AddGames([
            new Game
            {
                SystemId = Ps1.Id,
                Path = path,
                Title = "Existing game",
                IsAvailable = true,
                DateAdded = DateTimeOffset.UtcNow,
            },
        ]);
        var gameId = Assert.Single(_library.GetGames()).Id;
        var identification = new RecordingRetroAchievementsIdentificationService();
        var account = new RecordingRetroAchievementsAccountService(isConnected: false);
        var matching = new RecordingRetroAchievementsMatchingService();
        var progress = new RecordingRetroAchievementsProgressService();
        var reported = new RecordingProgress<RetroAchievementsLibrarySyncProgress>();
        var vm = CreateViewModel(
            retroAchievements: identification,
            retroAccount: account,
            retroMatching: matching,
            retroProgress: progress);

        var outcome = await vm.ConnectRetroAchievementsAsync(
            "Player",
            "SECRET",
            reported,
            TestContext.Current.CancellationToken);

        Assert.Equal(RetroAchievementsConnectionResult.Connected, outcome.Result);
        Assert.Equal([gameId], identification.GameIds);
        Assert.Equal(1, matching.Calls);
        Assert.Equal(1, progress.Calls);
        Assert.NotNull(outcome.Sync);
        Assert.Contains(reported.Values, value =>
            value.Phase == RetroAchievementsLibrarySyncPhase.Identifying &&
            value.CurrentGameTitle == "Existing game");
        Assert.Contains(reported.Values, value =>
            value.Phase == RetroAchievementsLibrarySyncPhase.Matching);
        Assert.Contains(reported.Values, value =>
            value.Phase == RetroAchievementsLibrarySyncPhase.RefreshingProgress);
    }

    [AvaloniaFact]
    public async Task Disconnect_WaitsForBackgroundImportSyncBeforeClearingAccountProgress()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        var identification = new RecordingRetroAchievementsIdentificationService();
        var account = new RecordingRetroAchievementsAccountService(isConnected: true);
        var progress = new BlockingRetroAchievementsProgressService();
        var details = new RecordingRetroAchievementsDetailsService();
        var vm = CreateViewModel(
            retroAchievements: identification,
            retroAccount: account,
            retroMatching: new RecordingRetroAchievementsMatchingService(),
            retroProgress: progress,
            retroDetails: details);

        await vm.AddGamesCommand.ExecuteAsync(null);
        await progress.Started.WaitAsync(TimeSpan.FromSeconds(2));
        var disconnect = vm.DisconnectRetroAchievementsAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.False(disconnect.IsCompleted);
            Assert.False(progress.Cleared);
        }
        finally
        {
            progress.CompleteRefresh();
        }

        await disconnect;

        Assert.True(progress.Cleared);
        Assert.True(details.Cleared);
        Assert.False(account.IsConnected);
    }

    [AvaloniaFact]
    public async Task ConfirmedAchievementLink_OpensDetailsThroughGameCommand()
    {
        var path = Path.Combine(_baseDirectory, "Achievements.cue");
        File.WriteAllText(path, "FILE \"Achievements.bin\" BINARY");
        _library.AddGames([
            new Game
            {
                SystemId = Ps1.Id,
                Path = path,
                Title = "Achievements game",
                DateAdded = DateTimeOffset.UtcNow,
            },
        ]);
        var gameId = Assert.Single(_library.GetGames()).Id;
        var readStore = new StaticRetroAchievementsReadStore(gameId, 1234);
        var vm = CreateViewModel(retroAchievementsRead: readStore);

        await vm.ReloadGamesAsync();
        var game = Assert.Single(vm.Games);
        await game.OpenAchievementsCommand.ExecuteAsync(game);

        Assert.True(game.CanOpenAchievementDetails);
        Assert.Equal(("Achievements game", 1234), _dialogs.AchievementDetailsRequest);
    }

    [AvaloniaFact]
    public async Task OpenTextureFolder_CreatesTheSerialFolderAndRevealsIt()
    {
        var path = Path.Combine(_baseDirectory, "MetalGear.cue");
        File.WriteAllText(path, "FILE \"MetalGear.bin\" BINARY");
        _library.AddGames([new Game { SystemId = Ps1.Id, Path = path, Title = "Metal Gear Solid", DateAdded = DateTimeOffset.UtcNow }]);
        var gameId = Assert.Single(_library.GetGames()).Id;
        _metadataStore.ReplaceIdentifiers(gameId,
            [new GameIdentifier(GameIdentifierKind.Serial, "SLUS-00594", "test", IsPrimary: true)]);

        var textures = Path.Combine(_baseDirectory, "duckstation-textures");
        Directory.CreateDirectory(textures);
        var coordinator = new TexturePackCoordinator(
            new AppPaths(_baseDirectory),
            _metadataStore,
            new AppSettings { TexturePacks = new TexturePackSettings().WithOverride("playstation", textures) },
            NullAppLogger.Instance);
        var reveal = new FakeFileRevealService();
        var vm = CreateViewModel(metadataStore: _metadataStore, texturePacks: coordinator, fileReveal: reveal);

        await vm.ReloadGamesAsync();
        var game = Assert.Single(vm.Games);
        Assert.True(game.CanOpenTextureFolder);

        await game.OpenTextureFolderCommand.ExecuteAsync(game);

        var expected = Path.Combine(textures, "SLUS-00594");
        Assert.True(Directory.Exists(expected));
        Assert.Equal(expected, reveal.LastOpenedDirectory);
    }

    [AvaloniaFact]
    public async Task OpenTextureFolder_IsHiddenForSystemsWithoutTexturePackSupport()
    {
        var path = Path.Combine(_baseDirectory, "Sonic.md");
        File.WriteAllText(path, "x");
        _library.AddGames([new Game { SystemId = MegaDrive.Id, Path = path, Title = "Sonic", DateAdded = DateTimeOffset.UtcNow }]);
        var vm = CreateViewModel(metadataStore: _metadataStore);

        await vm.ReloadGamesAsync();
        var game = Assert.Single(vm.Games);

        Assert.False(game.CanOpenTextureFolder);
    }

    [AvaloniaFact]
    public async Task FocusedGameWidget_UpdatesWhenAchievementDetailsRefresh()
    {
        // #6: unlocking an achievement is reflected by a details refresh (from the achievements
        // overlay or the post-exit pass). The focused-game dock widget must pick that up, not only a
        // later full reload — previously the widget kept showing the pre-unlock count ("0/9").
        var path = Path.Combine(_baseDirectory, "WidgetAchievements.cue");
        File.WriteAllText(path, "FILE \"WidgetAchievements.bin\" BINARY");
        _library.AddGames([new Game { SystemId = Ps1.Id, Path = path, Title = "Widget game", DateAdded = DateTimeOffset.UtcNow }]);
        var gameId = Assert.Single(_library.GetGames()).Id;
        const int raGameId = 9001;
        var readStore = new MutableRetroAchievementsReadStore(gameId, raGameId);
        var details = new RecordingRetroAchievementsDetailsService();
        var vm = CreateViewModel(
            retroAchievementsRead: readStore,
            retroAccount: new RecordingRetroAchievementsAccountService(isConnected: true),
            retroDetails: details);
        vm.IsGamepadMode = true;
        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);
        // The set is confirmed but no progress has loaded, so the widget shows the em dash.
        Assert.Equal("—/—", vm.FocusedGame!.GamepadAchievementCountText);

        // The unlock lands in the store, then a details refresh announces it.
        readStore.SetProgress(raGameId, awarded: 1, total: 9);
        details.Publish(new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(raGameId, "Widget game", 9, 1, 0, []),
            DateTimeOffset.UtcNow));

        Assert.Equal("1/9", vm.FocusedGame.GamepadAchievementCountText);
        Assert.True(vm.FocusedGame.ShowAchievementMark);
    }

    [AvaloniaFact]
    public async Task GamepadAchievements_StayInTheMainOverlayAndNeverRequestDesktopDialog()
    {
        var path = Path.Combine(_baseDirectory, "GamepadAchievements.cue");
        File.WriteAllText(path, "FILE \"GamepadAchievements.bin\" BINARY");
        _library.AddGames([new Game { SystemId = Ps1.Id, Path = path, Title = "Gamepad achievements", DateAdded = DateTimeOffset.UtcNow }]);
        var gameId = Assert.Single(_library.GetGames()).Id;
        var vm = CreateViewModel(
            retroAchievementsRead: new StaticRetroAchievementsReadStore(gameId, 4321),
            retroAccount: new RecordingRetroAchievementsAccountService(isConnected: true),
            retroDetails: new RecordingRetroAchievementsDetailsService());
        vm.IsGamepadMode = true;
        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);

        await vm.OpenFocusedAchievementsCommand.ExecuteAsync(null);

        Assert.Equal(GamepadOverlayKind.Achievements, vm.GamepadOverlay);
        Assert.Null(_dialogs.AchievementDetailsRequest);
        vm.CloseGamepadOverlayCommand.Execute(null);
        Assert.Equal(GamepadOverlayKind.None, vm.GamepadOverlay);
        Assert.Same(vm.FocusedGame, Assert.Single(vm.Games));
    }

    [AvaloniaFact]
    public async Task GamepadAchievements_RefreshReplacementPreservesFocusedAchievementId()
    {
        var path = Path.Combine(_baseDirectory, "GamepadAchievementRefresh.cue");
        File.WriteAllText(path, "FILE \"GamepadAchievementRefresh.bin\" BINARY");
        _library.AddGames([new Game { SystemId = Ps1.Id, Path = path, Title = "Refreshing achievements", DateAdded = DateTimeOffset.UtcNow }]);
        var gameId = Assert.Single(_library.GetGames()).Id;
        var cached = new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(
                4321, "Refreshing achievements", 2, 0, 0,
                [
                    new RetroAchievementsAchievement(1, "First row", "", 5, "", 1, null, null),
                    new RetroAchievementsAchievement(2, "Focused row", "", 10, "", 2, null, null),
                ]),
            DateTimeOffset.UtcNow);
        var details = new RecordingRetroAchievementsDetailsService(cached);
        var vm = CreateViewModel(
            retroAchievementsRead: new StaticRetroAchievementsReadStore(gameId, 4321),
            retroAccount: new RecordingRetroAchievementsAccountService(isConnected: true),
            retroDetails: details);
        vm.IsGamepadMode = true;
        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);
        await vm.OpenFocusedAchievementsCommand.ExecuteAsync(null);
        vm.FocusedGamepadAchievement = vm.GamepadAchievementDetails!.Achievements[1];
        Assert.Equal(2, vm.FocusedGamepadAchievement.AchievementId);

        details.Publish(new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(
                4321, "Refreshing achievements", 2, 1, 1,
                [
                    new RetroAchievementsAchievement(3, "New first row", "", 5, "", 1, null, null),
                    new RetroAchievementsAchievement(2, "Refreshed focused row", "", 10, "", 2, DateTimeOffset.UtcNow, null),
                ]),
            DateTimeOffset.UtcNow.AddMinutes(1)));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        Assert.Equal(2, vm.FocusedGamepadAchievement?.AchievementId);
        Assert.Contains(vm.FocusedGamepadAchievement!, vm.GamepadAchievementDetails!.Achievements);
    }

    [AvaloniaFact]
    public async Task GamepadAchievements_ControllerCyclesFiltersSortAndGridFocus()
    {
        var path = Path.Combine(_baseDirectory, "GamepadAchievementNavigation.cue");
        File.WriteAllText(path, "FILE \"GamepadAchievementNavigation.bin\" BINARY");
        _library.AddGames([new Game
        {
            SystemId = Ps1.Id,
            Path = path,
            Title = "Achievement navigation",
            DateAdded = DateTimeOffset.UtcNow,
        }]);
        var gameId = Assert.Single(_library.GetGames()).Id;
        var earnedAt = DateTimeOffset.UtcNow;
        var cached = new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(
                4321,
                "Achievement navigation",
                4,
                2,
                2,
                [
                    new RetroAchievementsAchievement(1, "First", "", 5, "", 1, earnedAt, null),
                    new RetroAchievementsAchievement(2, "Second", "", 25, "", 2, null, null),
                    new RetroAchievementsAchievement(3, "Third", "", 10, "", 3, earnedAt, earnedAt),
                    new RetroAchievementsAchievement(4, "Fourth", "", 10, "", 4, null, null),
                ]),
            earnedAt);
        var vm = CreateViewModel(
            retroAchievementsRead: new StaticRetroAchievementsReadStore(gameId, 4321),
            retroAccount: new RecordingRetroAchievementsAccountService(isConnected: true),
            retroDetails: new RecordingRetroAchievementsDetailsService(cached));
        vm.IsGamepadMode = true;
        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);
        await vm.OpenFocusedAchievementsCommand.ExecuteAsync(null);
        // Column count is derived arithmetically from the grid width (100px tiles, 12px gutters); a
        // 250px viewport yields 2 columns. Headless tests raise no SizeChanged, so set it directly.
        vm.GamepadAchievementViewportWidth = 250;
        Assert.Equal(2, vm.GamepadAchievementColumnCount);

        Assert.Equal(1, vm.FocusedGamepadAchievement?.AchievementId);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateRight));
        Assert.Equal(2, vm.FocusedGamepadAchievement?.AchievementId);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateRight));
        Assert.Equal(2, vm.FocusedGamepadAchievement?.AchievementId);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateDown));
        Assert.Equal(4, vm.FocusedGamepadAchievement?.AchievementId);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateRight));
        Assert.Equal(4, vm.FocusedGamepadAchievement?.AchievementId);

        vm.GamepadAchievementViewportWidth = 400; // -> 3 columns
        Assert.Equal(3, vm.GamepadAchievementColumnCount);
        vm.FocusedGamepadAchievement = vm.GamepadAchievementDetails!.VisibleAchievements[1];
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateDown));
        Assert.Equal(2, vm.FocusedGamepadAchievement?.AchievementId);
        vm.FocusedGamepadAchievement = vm.GamepadAchievementDetails.VisibleAchievements[3];
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateLeft));
        Assert.Equal(4, vm.FocusedGamepadAchievement?.AchievementId);
        vm.GamepadAchievementViewportWidth = 250; // -> 2 columns

        Assert.True(vm.DispatchGamepadAction(GamepadAction.NextPlatform));
        Assert.Equal(AchievementDisplayFilter.Locked, vm.GamepadAchievementDetails!.SelectedFilter);
        Assert.Equal([2, 4], vm.GamepadAchievementDetails.VisibleAchievements.Select(row => row.AchievementId));
        Assert.Equal(4, vm.FocusedGamepadAchievement?.AchievementId);

        Assert.True(vm.DispatchGamepadAction(GamepadAction.NextPlatform));
        Assert.Equal(AchievementDisplayFilter.Unlocked, vm.GamepadAchievementDetails.SelectedFilter);
        Assert.Equal([1, 3], vm.GamepadAchievementDetails.VisibleAchievements.Select(row => row.AchievementId));
        Assert.Equal(1, vm.FocusedGamepadAchievement?.AchievementId);
        var revisionBeforeSort = vm.GamepadAchievementLayoutRevision;

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Actions));
        Assert.Equal(AchievementDisplaySort.Points, vm.GamepadAchievementDetails.SelectedSort);
        Assert.Equal([3, 1], vm.GamepadAchievementDetails.VisibleAchievements.Select(row => row.AchievementId));
        Assert.Equal(3, vm.FocusedGamepadAchievement?.AchievementId);
        Assert.Equal(0, vm.GamepadAchievementDetails.VisibleAchievements.IndexOf(vm.FocusedGamepadAchievement!));
        Assert.Equal(revisionBeforeSort + 1, vm.GamepadAchievementLayoutRevision);

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Actions));
        Assert.Equal(AchievementDisplaySort.UnlockedFirst, vm.GamepadAchievementDetails.SelectedSort);
        Assert.Equal(1, vm.FocusedGamepadAchievement?.AchievementId);
        Assert.Equal(0, vm.GamepadAchievementDetails.VisibleAchievements.IndexOf(vm.FocusedGamepadAchievement!));
        Assert.Equal(revisionBeforeSort + 2, vm.GamepadAchievementLayoutRevision);
    }

    [AvaloniaFact]
    public async Task GamepadAchievements_RowProjectionSlicesEveryAchievementAndReflowsOnWidth()
    {
        var path = Path.Combine(_baseDirectory, "GamepadAchievementRows.cue");
        File.WriteAllText(path, "FILE \"GamepadAchievementRows.bin\" BINARY");
        _library.AddGames([new Game
        {
            SystemId = Ps1.Id,
            Path = path,
            Title = "Achievement rows",
            DateAdded = DateTimeOffset.UtcNow,
        }]);
        var gameId = Assert.Single(_library.GetGames()).Id;
        var achievements = Enumerable.Range(1, 23)
            .Select(index => new RetroAchievementsAchievement(
                index, $"Achievement {index}", "", 5, "", index,
                index <= 4 ? DateTimeOffset.UtcNow : null, null))
            .ToArray();
        var cached = new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(4321, "Achievement rows", 23, 4, 0, achievements),
            DateTimeOffset.UtcNow);
        var vm = CreateViewModel(
            retroAchievementsRead: new StaticRetroAchievementsReadStore(gameId, 4321),
            retroAccount: new RecordingRetroAchievementsAccountService(isConnected: true),
            retroDetails: new RecordingRetroAchievementsDetailsService(cached));
        vm.IsGamepadMode = true;
        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);
        await vm.OpenFocusedAchievementsCommand.ExecuteAsync(null);

        // A 620px grid holds 5 fixed 100px tiles with 12px gutters.
        vm.GamepadAchievementViewportWidth = 620;
        Assert.Equal(5, vm.GamepadAchievementColumnCount);
        AssertRowsCover(vm, columns: 5);

        // Narrowing to 4 columns must re-slice, still covering every achievement in order.
        vm.GamepadAchievementViewportWidth = 500;
        Assert.Equal(4, vm.GamepadAchievementColumnCount);
        AssertRowsCover(vm, columns: 4);

        // Filtering replaces the visible set; the rows must follow and never strand a tile.
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NextPlatform));
        Assert.Equal(AchievementDisplayFilter.Locked, vm.GamepadAchievementDetails!.SelectedFilter);
        Assert.Equal(19, vm.GamepadAchievementDetails.VisibleAchievements.Count);
        AssertRowsCover(vm, columns: 4);

        return;

        static void AssertRowsCover(MainViewModel vm, int columns)
        {
            var visible = vm.GamepadAchievementDetails!.VisibleAchievements;
            var rows = vm.GamepadAchievementRows;
            Assert.Equal((visible.Count + columns - 1) / columns, rows.Count);
            Assert.All(rows.Take(rows.Count - 1), row => Assert.Equal(columns, row.Count));
            Assert.InRange(rows[^1].Count, 1, columns);
            // Flattening the rows top-to-bottom, left-to-right reproduces the visible list exactly —
            // so no achievement is dropped or duplicated by the projection.
            Assert.Equal(visible, rows.SelectMany(row => row).ToArray());
        }
    }

    [AvaloniaFact]
    public async Task GamepadActions_UseModalNavigationAndCoverHandsOffToDesktopInsteadOfPicker()
    {
        var path = Path.Combine(_baseDirectory, "GamepadActions.cue");
        File.WriteAllText(path, "FILE \"GamepadActions.bin\" BINARY");
        _library.AddGames([new Game { SystemId = Ps1.Id, Path = path, Title = "Gamepad actions", DateAdded = DateTimeOffset.UtcNow }]);
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);

        vm.OpenFocusedGameActionsCommand.Execute(null);
        Assert.Equal(GamepadOverlayKind.Actions, vm.GamepadOverlay);
        Assert.True(vm.GamepadOverlayOptions[0].IsFocused);
        vm.MoveGamepadOverlayDownCommand.Execute(null);
        Assert.Equal(1, vm.GamepadOverlaySelectionIndex);

        await vm.SetFocusedCoverCommand.ExecuteAsync(null);
        Assert.Equal(GamepadOverlayKind.CoverDesktopHandoff, vm.GamepadOverlay);
        Assert.Null(_dialogs.LastCoverGameTitle);

        vm.EditFocusedTitleCommand.Execute(null);
        vm.FocusedGame!.DraftTitle = "Unsaved controller title";
        vm.CloseGamepadOverlayCommand.Execute(null);
        Assert.False(vm.FocusedGame.IsEditingTitle);
        Assert.Equal("Gamepad actions", vm.FocusedGame.DraftTitle);

        // The d-pad stays inside the cover grid: Up on the only (top-row) tile keeps focus there
        // instead of climbing into the platform rail. Platforms are switched with LB/RB.
        var focusedBeforeUp = vm.FocusedGame;
        vm.MoveGamepadFocusUpCommand.Execute(null);
        Assert.Same(focusedBeforeUp, vm.FocusedGame);
    }

    [AvaloniaFact]
    public async Task GamepadScrape_IsOfferedInActions_AndOpensControllerNativeOverlay()
    {
        var (vm, _) = await SetUpGamepadScraperAsync("GamepadScrape", ScraperFixtures.ReadyPreview());

        vm.OpenFocusedGameActionsCommand.Execute(null);
        Assert.Contains(vm.GamepadOverlayOptions, option => option.Label == "Scrape with ScreenScraper");

        await vm.ScrapeFocusedGameCommand.ExecuteAsync(null);

        // Controller-native: the scraper opens inside Gamepad mode and never hands off to Desktop.
        Assert.Equal(GamepadOverlayKind.Scraper, vm.GamepadOverlay);
        Assert.True(vm.IsGamepadScraperOpen);
        Assert.NotNull(vm.GamepadScraperDetails);
        Assert.Equal(GameScraperState.Ready, vm.GamepadScraperDetails!.Scraper.State);
        Assert.Null(_dialogs.LastScraperGameId);

        // B, while still reviewing, steps back to the game's Actions menu (the overlay is disposed).
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(GamepadOverlayKind.Actions, vm.GamepadOverlay);
        Assert.Null(vm.GamepadScraperDetails);
    }

    [AvaloniaFact]
    public async Task GamepadScraper_ControllerTogglesAndApplies_ThenReturnsToLibrary()
    {
        var (vm, apply) = await SetUpGamepadScraperAsync("GamepadScrapeApply", ScraperFixtures.ReadyPreview());
        vm.OpenFocusedGameActionsCommand.Execute(null);
        await vm.ScrapeFocusedGameCommand.ExecuteAsync(null);
        var details = vm.GamepadScraperDetails!;
        Assert.Equal(GamepadScraperTargetKind.Apply, details.FocusedKind);

        // Ready opens on Apply; Up walks Apply → Refresh → Media → BoxArt → Field, and A toggles it.
        while (details.FocusedKind != GamepadScraperTargetKind.Field)
            Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateUp));
        Assert.True(details.Scraper.Fields[0].IsSelected);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Confirm));
        Assert.False(details.Scraper.Fields[0].IsSelected);

        // Down walks Field → BoxArt → Media → Refresh → Apply; A applies.
        for (var i = 0; i < 4; i++)
            Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateDown));
        Assert.Equal(GamepadScraperTargetKind.Apply, details.FocusedKind);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Confirm));

        Assert.NotNull(apply.Request);
        Assert.Equal(GameScraperState.Applied, details.Scraper.State);

        // From the terminal Applied state, B returns all the way to the library and refreshes it.
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(GamepadOverlayKind.None, vm.GamepadOverlay);
        Assert.Null(vm.GamepadScraperDetails);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.Single(vm.Games);
    }

    private async Task<(MainViewModel Vm, StubGameScrapeApplicationService Apply)> SetUpGamepadScraperAsync(
        string name,
        params ScreenScraperPreviewResult[] previews)
    {
        var path = Path.Combine(_baseDirectory, $"{name}.cue");
        File.WriteAllText(path, $"FILE \"{name}.bin\" BINARY");
        _library.AddGames([new Game
        {
            SystemId = Ps1.Id,
            Path = path,
            Title = "Gamepad scrape",
            DateAdded = DateTimeOffset.UtcNow,
        }]);
        var apply = new StubGameScrapeApplicationService();
        var vm = CreateViewModel(
            screenScraperPreview: new StubScreenScraperPreviewService(previews),
            scrapeApply: apply,
            screenScraperAccount: new StubScreenScraperAccountService(),
            settingsService: new StubSettingsService());
        vm.IsGamepadMode = true;
        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);
        return (vm, apply);
    }

    [AvaloniaFact]
    public async Task PlatformShoulderButtons_FromOffListScope_SnapToAllGames()
    {
        var path = Path.Combine(_baseDirectory, "OffList.cue");
        File.WriteAllText(path, "FILE \"OffList.bin\" BINARY");
        _library.AddGames([new Game { SystemId = Ps1.Id, Path = path, Title = "Off list", DateAdded = DateTimeOffset.UtcNow }]);
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        vm.SelectedSystem = Ps1;
        await vm.ReloadGamesAsync();

        // Recently Added is not a stop on the LB/RB cycle; from there the next press returns to
        // All Games rather than dead-ending. (Couch reaches this scope only via a cross-mode restore now,
        // but the off-list snap behaviour is unchanged.)
        await vm.ShowRecentlyAddedCommand.ExecuteAsync(null);
        Assert.Equal(LibraryScope.RecentlyAdded, vm.CurrentLibraryScope);

        await vm.NextPlatformCommand.ExecuteAsync(null);
        Assert.True(vm.IsAllGamesSelected);
    }

    [AvaloniaFact]
    public async Task GamepadDiscPicker_SelectsThenLaunchesTheRememberedDisc()
    {
        var disc1 = Path.Combine(_baseDirectory, "Remembered Game (Disc 1).cue");
        var disc2 = Path.Combine(_baseDirectory, "Remembered Game (Disc 2).cue");
        File.WriteAllText(disc1, "FILE \"Disc 1.bin\" BINARY");
        File.WriteAllText(disc2, "FILE \"Disc 2.bin\" BINARY");
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = disc1, Title = "Remembered Game (Disc 1)", DateAdded = DateTimeOffset.UtcNow },
            new Game { SystemId = Ps1.Id, Path = disc2, Title = "Remembered Game (Disc 2)", DateAdded = DateTimeOffset.UtcNow },
        ]);
        var launcher = new RecordingLaunchService(new GameLaunchResult(true, "Finished"));
        var vm = CreateViewModel(launchService: launcher);
        vm.IsGamepadMode = true;
        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);

        Assert.Equal("Remembered Game", vm.FocusedGame.Title);
        Assert.True(vm.FocusedGame.IsMultiDisc);
        Assert.Equal("2 discs", vm.FocusedGame.DiscCountText);
        Assert.Equal("Disc 1 of 2", vm.FocusedGame.DiscBadgeText);

        vm.OpenFocusedGameActionsCommand.Execute(null);
        Assert.Contains(vm.GamepadOverlayOptions, option => option.Label == "Select disc");
        vm.OpenFocusedDiscSelectionCommand.Execute(null);
        Assert.Equal(GamepadOverlayKind.DiscSelection, vm.GamepadOverlay);
        Assert.Equal(["Disc 1 (current)", "Disc 2"],
            vm.GamepadOverlayOptions.Select(option => option.Label));
        Assert.Equal(0, vm.GamepadOverlaySelectionIndex);

        await ((IAsyncRelayCommand)vm.GamepadOverlayOptions[1].Command).ExecuteAsync(null);

        Assert.Null(launcher.Game);
        Assert.Equal(2, vm.FocusedGame!.SelectedDiscNumber);
        Assert.Equal("Disc 2 selected", vm.FocusedGame.SelectedDiscText);
        Assert.Equal("Disc 2 of 2", vm.FocusedGame.DiscBadgeText);
        Assert.Single(_library.GetDiscSelections().Values, id => id == vm.FocusedGame.LaunchModel.Id);

        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);
        Assert.Equal(2, vm.FocusedGame.SelectedDiscNumber);
        await vm.LaunchFocusedGameCommand.ExecuteAsync(null);
        Assert.Equal(disc2, launcher.Game?.Path);

        var desktopDisc1 = vm.FocusedGame.DiscOptions[0];
        Assert.Equal("Disc 1", desktopDisc1.Label);
        await desktopDisc1.SelectDiscCommand.ExecuteAsync(null);
        Assert.Equal(disc2, launcher.Game?.Path);
        Assert.Equal(1, vm.FocusedGame.SelectedDiscNumber);
        Assert.Equal("Disc 1 of 2", vm.FocusedGame.DiscBadgeText);
        Assert.Single(_library.GetDiscSelections().Values, id => id == desktopDisc1.Disc.Game.Id);
        Assert.Equal("Disc 1 selected for Remembered Game", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task GamepadDiscPicker_ChangesTheDefaultWithoutLaunching()
    {
        var disc1 = Path.Combine(_baseDirectory, "Failure Game (Disc 1).cue");
        var disc2 = Path.Combine(_baseDirectory, "Failure Game (Disc 2).cue");
        File.WriteAllText(disc1, "x");
        File.WriteAllText(disc2, "x");
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = disc1, Title = "Failure Game (Disc 1)", DateAdded = DateTimeOffset.UtcNow },
            new Game { SystemId = Ps1.Id, Path = disc2, Title = "Failure Game (Disc 2)", DateAdded = DateTimeOffset.UtcNow },
        ]);
        var launcher = new RecordingLaunchService(new GameLaunchResult(false, "Failed"));
        var vm = CreateViewModel(launchService: launcher);
        vm.IsGamepadMode = true;
        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);

        vm.OpenFocusedDiscSelectionCommand.Execute(null);
        await ((IAsyncRelayCommand)vm.GamepadOverlayOptions[1].Command).ExecuteAsync(null);

        Assert.Equal(2, vm.FocusedGame!.SelectedDiscNumber);
        Assert.Single(_library.GetDiscSelections().Values, id => id == vm.FocusedGame.LaunchModel.Id);
        Assert.Null(launcher.Game);
    }

    [AvaloniaFact]
    public async Task ShoulderButtons_CycleAllGamesAndSystemsWithWrapSkippingRecentlyAdded()
    {
        // Option A: LB/RB cycle one ordered list — All Games, then each system — and wrap at both
        // ends. Recently Added is not a stop (it lives in the Collections overlay), so shoulder
        // input steps straight from All Games to the first system.
        var ps1Path = Path.Combine(_baseDirectory, "RailPs1.cue");
        var gameCubePath = Path.Combine(_baseDirectory, "RailGameCube.iso");
        File.WriteAllText(ps1Path, "FILE \"RailPs1.bin\" BINARY");
        File.WriteAllText(gameCubePath, "gamecube");
        _library.AddGames([
            new Game { SystemId = Ps1.Id, Path = ps1Path, Title = "Rail PS1", DateAdded = DateTimeOffset.UtcNow },
            new Game { SystemId = GameCube.Id, Path = gameCubePath, Title = "Rail GameCube", DateAdded = DateTimeOffset.UtcNow },
        ]);
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        await vm.ShowAllGamesCommand.ExecuteAsync(null); // also populates NavigationSystems + GamepadPlatforms
        Assert.True(vm.IsAllGamesSelected);
        Assert.Equal(2, vm.NavigationSystems.Count);

        // RB steps All Games -> first system; Recently Added is never a stop, and the rail marks
        // exactly one active tab.
        await vm.NextPlatformCommand.ExecuteAsync(null);
        Assert.Equal(vm.NavigationSystems[0].Id, vm.SelectedSystem?.Id);
        Assert.False(vm.IsRecentlyAddedSelected);
        Assert.Single(vm.GamepadPlatforms, platform => platform.IsActive);

        // RB again to the last system, then RB wraps back to All Games (no platform tab active).
        await vm.NextPlatformCommand.ExecuteAsync(null);
        Assert.Equal(vm.NavigationSystems[1].Id, vm.SelectedSystem?.Id);
        await vm.NextPlatformCommand.ExecuteAsync(null);
        Assert.True(vm.IsAllGamesSelected);
        Assert.DoesNotContain(vm.GamepadPlatforms, platform => platform.IsActive);

        // LB from All Games wraps to the last system.
        await vm.PreviousPlatformCommand.ExecuteAsync(null);
        Assert.Equal(vm.NavigationSystems[^1].Id, vm.SelectedSystem?.Id);
    }

    [AvaloniaFact]
    public void GroupLeaderSystemIds_MarkTheFirstVisibleSystemOfEachManufacturer()
    {
        // Two Nintendo systems and one Sega system populated; every other platform stays empty and,
        // under the default hide-empty behaviour, absent from the navigation list.
        _library.AddGames([
            new Game { SystemId = GameBoyAdvance.Id, Path = Path.Combine(_baseDirectory, "advance.gba"), Title = "GBA", DateAdded = DateTimeOffset.UtcNow },
            new Game { SystemId = GameCube.Id, Path = Path.Combine(_baseDirectory, "cube.rvz"), Title = "GC", DateAdded = DateTimeOffset.UtcNow },
            new Game { SystemId = MegaDrive.Id, Path = Path.Combine(_baseDirectory, "genesis.md"), Title = "MD", DateAdded = DateTimeOffset.UtcNow },
        ]);

        var vm = CreateViewModel();

        // Game Boy Advance precedes GameCube in the catalogue, so it leads Nintendo; Mega Drive leads
        // Sega. GameCube is Nintendo but not the first shown, so it is not a group leader (no header).
        Assert.Equal(
            new[] { GameBoyAdvance.Id, MegaDrive.Id }.Order(),
            vm.GroupLeaderSystemIds.Order());
        Assert.DoesNotContain(GameCube.Id, vm.GroupLeaderSystemIds);
    }

    [AvaloniaFact]
    public async Task GamepadCovers_UsePlatformAspectRatioOnASharedShelf()
    {
        // Mixed-platform view: each tile keeps its own platform's cover height, while every tile
        // shares one shelf height so rows stay aligned.
        // PS1 art is square (1.0) and GameCube is portrait (0.708), so the two tiles must differ.
        var ps1Path = Path.Combine(_baseDirectory, "AspectPs1.cue");
        File.WriteAllText(ps1Path, "FILE \"AspectPs1.bin\" BINARY");
        var cubePath = Path.Combine(_baseDirectory, "AspectCube.iso");
        File.WriteAllText(cubePath, "x");
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = ps1Path, Title = "Aspect PS1", DateAdded = DateTimeOffset.UtcNow },
            new Game { SystemId = GameCube.Id, Path = cubePath, Title = "Aspect GC", DateAdded = DateTimeOffset.UtcNow },
        ]);
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        await vm.ShowAllGamesCommand.ExecuteAsync(null);
        vm.GamepadViewportWidth = 1280;

        var ps1 = vm.Games.Single(game => game.Title == "Aspect PS1");
        var cube = vm.Games.Single(game => game.Title == "Aspect GC");

        Assert.NotEqual(ps1.CoverAspectRatio, cube.CoverAspectRatio);
        Assert.NotEqual(ps1.CoverHeight, cube.CoverHeight);
        Assert.Equal(Math.Round(ps1.CoverWidth / ps1.CoverAspectRatio), ps1.CoverHeight);
        Assert.Equal(Math.Round(cube.CoverWidth / cube.CoverAspectRatio), cube.CoverHeight);
        // One shared shelf, tall enough for the tallest cover, keeps the grid rows aligned.
        Assert.Equal(ps1.ShelfCoverHeight, cube.ShelfCoverHeight);
        Assert.Equal(Math.Max(ps1.CoverHeight, cube.CoverHeight), ps1.ShelfCoverHeight);
    }

    [AvaloniaFact]
    public async Task GamepadCovers_KeepCanonicalFrameWhenOffRatioArtworkLoads()
    {
        // Regression for the "covers only take half their space" report: a cover fills its
        // platform's canonical frame (UniformToFill) instead of adopting its own bitmap ratio, so a
        // single tall/off-ratio scan can never balloon the shared shelf and shrink every other cover.
        var firstPath = Path.Combine(_baseDirectory, "ShelfCubeA.iso");
        File.WriteAllText(firstPath, "x");
        var secondPath = Path.Combine(_baseDirectory, "ShelfCubeB.iso");
        File.WriteAllText(secondPath, "y");
        _library.AddGames(
        [
            new Game { SystemId = GameCube.Id, Path = firstPath, Title = "Shelf GC A", DateAdded = DateTimeOffset.UtcNow },
            new Game { SystemId = GameCube.Id, Path = secondPath, Title = "Shelf GC B", DateAdded = DateTimeOffset.UtcNow },
        ]);
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        await vm.ShowAllGamesCommand.ExecuteAsync(null);
        vm.GamepadViewportWidth = 1280;

        var first = vm.Games.Single(game => game.Title == "Shelf GC A");
        var second = vm.Games.Single(game => game.Title == "Shelf GC B");
        var ratioBefore = first.CoverAspectRatio;
        var shelfBefore = first.ShelfCoverHeight;
        Assert.Equal(first.CoverHeight, first.ShelfCoverHeight);

        // A very tall, narrow bitmap: under the reverted per-cover-ratio behavior this stretched the
        // shared shelf to ~7x the cover width and rendered every other tile at a fraction of it.
        first.CoverImage = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(120, 900));

        Assert.Equal(ratioBefore, first.CoverAspectRatio, precision: 5);
        Assert.Equal(shelfBefore, first.ShelfCoverHeight);
        Assert.Equal(shelfBefore, second.ShelfCoverHeight);
        Assert.Equal(first.CoverHeight, first.ShelfCoverHeight);
    }

    [AvaloniaFact]
    public async Task GamepadColumnCount_SurvivesAnAsyncCoverLoad()
    {
        // Regression for "the selector can't move right / gets stuck": a cover finishing loading used
        // to re-run the whole cover layout (via per-cover ratio adoption) and could reset the column
        // count mid-navigation, which clamped Right partway across a row. The count is now derived
        // purely by width arithmetic (matching UniformGridLayout), so it must be stable across an
        // async cover load — the incoming bitmap changes one tile's art, never the grid's stride.
        var firstPath = Path.Combine(_baseDirectory, "ColsCubeA.iso");
        File.WriteAllText(firstPath, "x");
        var secondPath = Path.Combine(_baseDirectory, "ColsCubeB.iso");
        File.WriteAllText(secondPath, "y");
        _library.AddGames(
        [
            new Game { SystemId = GameCube.Id, Path = firstPath, Title = "Cols GC A", DateAdded = DateTimeOffset.UtcNow },
            new Game { SystemId = GameCube.Id, Path = secondPath, Title = "Cols GC B", DateAdded = DateTimeOffset.UtcNow },
        ]);
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        await vm.ShowAllGamesCommand.ExecuteAsync(null);
        vm.GamepadViewportWidth = 1280;

        var columnsBefore = vm.GamepadColumnCount;
        Assert.True(columnsBefore > 1); // the arithmetic produced a real multi-column layout

        vm.Games[0].CoverImage = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(120, 900));

        Assert.Equal(columnsBefore, vm.GamepadColumnCount);
    }

    [AvaloniaFact]
    public async Task GamepadCovers_FilteredShelfUsesOnlyVisibleCoverRatios()
    {
        var ps1Path = Path.Combine(_baseDirectory, "FilteredAspectPs1.cue");
        File.WriteAllText(ps1Path, "FILE \"FilteredAspectPs1.bin\" BINARY");
        var cubePath = Path.Combine(_baseDirectory, "FilteredAspectCube.iso");
        File.WriteAllText(cubePath, "x");
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = ps1Path, Title = "Visible square cover", DateAdded = DateTimeOffset.UtcNow },
            new Game { SystemId = GameCube.Id, Path = cubePath, Title = "Hidden portrait cover", DateAdded = DateTimeOffset.UtcNow },
        ]);
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        await vm.ShowAllGamesCommand.ExecuteAsync(null);
        vm.GamepadViewportWidth = 1280;
        var fullShelfHeight = vm.Games.Max(game => game.ShelfCoverHeight);

        vm.SearchText = "Visible square";
        vm.ApplyFilter();

        var visible = Assert.Single(vm.Games);
        Assert.Equal(visible.CoverHeight, visible.ShelfCoverHeight);
        Assert.True(visible.ShelfCoverHeight < fullShelfHeight);

        vm.SearchText = string.Empty;
        vm.ApplyFilter();
        Assert.Equal(fullShelfHeight, vm.Games[0].ShelfCoverHeight);
        Assert.Equal(fullShelfHeight, vm.Games[1].ShelfCoverHeight);
    }

    [AvaloniaFact]
    public void DispatchGamepadAction_OutsideGamepadMode_IsIgnored()
    {
        var vm = CreateViewModel();

        Assert.False(vm.DispatchGamepadAction(GamepadAction.Search));
        Assert.False(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(GamepadOverlayKind.None, vm.GamepadOverlay);
    }

    [AvaloniaFact]
    public void DispatchGamepadAction_RoutesActionsIdenticallyToTheKeyboardPath()
    {
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;

        // X opens search (a text overlay); the same routing native input and Steam Input share.
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Search));
        Assert.Equal(GamepadOverlayKind.Search, vm.GamepadOverlay);
        Assert.True(vm.GamepadOverlayOwnsTextInput);

        // While a text overlay owns input, B dismisses it and directional input is inert.
        Assert.False(vm.DispatchGamepadAction(GamepadAction.NavigateDown));
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(GamepadOverlayKind.None, vm.GamepadOverlay);
    }

    [AvaloniaFact]
    public void DispatchGamepadAction_MenuOwnsDesktopHandoffAndCancelNeverLeavesTheShelf()
    {
        var mode = new RecordingInterfaceModeService(InterfaceMode.Gamepad);
        var vm = CreateViewModel(interfaceModeService: mode);

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(InterfaceMode.Gamepad, mode.Current);

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Menu));
        Assert.Equal(GamepadOverlayKind.SystemMenu, vm.GamepadOverlay);
        Assert.Equal(
            ["Search", "Settings", "Switch to Desktop mode", "Quit EmuShelf"],
            vm.GamepadOverlayOptions.Select(option => option.Label));

        vm.RequestDesktopModeFromGamepadCommand.Execute(null);
        Assert.Equal(GamepadOverlayKind.DesktopModeConfirmation, vm.GamepadOverlay);
        // Standard two-button confirmation: Cancel plus the short affirmative verb, landing on the action.
        Assert.Equal(["Cancel", "Switch"], vm.GamepadOverlayOptions.Select(option => option.Label));
        Assert.Equal("Switch", vm.GamepadOverlayOptions[vm.GamepadOverlaySelectionIndex].Label);

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(GamepadOverlayKind.SystemMenu, vm.GamepadOverlay);
        Assert.Equal(InterfaceMode.Gamepad, mode.Current);
    }

    [AvaloniaFact]
    public async Task GamepadDesktopModeConfirm_BacksOutToTheOverlayThatOpenedIt()
    {
        var path = Path.Combine(_baseDirectory, "DesktopConfirm.cue");
        File.WriteAllText(path, "FILE \"DesktopConfirm.bin\" BINARY");
        _library.AddGames([new Game { SystemId = Ps1.Id, Path = path, Title = "Desktop confirm", DateAdded = DateTimeOffset.UtcNow }]);
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);

        // Reached through the "Set cover" hand-off, B must return to that hand-off — not the System
        // Menu — because the confirmation is shared between the two entry points.
        vm.OpenFocusedGameActionsCommand.Execute(null);
        await vm.SetFocusedCoverCommand.ExecuteAsync(null);
        Assert.Equal(GamepadOverlayKind.CoverDesktopHandoff, vm.GamepadOverlay);

        vm.RequestDesktopModeFromGamepadCommand.Execute(null);
        Assert.Equal(GamepadOverlayKind.DesktopModeConfirmation, vm.GamepadOverlay);

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(GamepadOverlayKind.CoverDesktopHandoff, vm.GamepadOverlay);

        // And the hand-off's own back still steps up to the game's Actions menu.
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(GamepadOverlayKind.Actions, vm.GamepadOverlay);
    }

    [AvaloniaFact]
    public async Task GamepadMenu_SettingsStaysInWindowAndQuitRequiresConfirmation()
    {
        var mode = new RecordingInterfaceModeService(InterfaceMode.Gamepad);
        var lifetime = new RecordingApplicationLifetimeService();
        var vm = CreateViewModel(interfaceModeService: mode, applicationLifetime: lifetime);

        await vm.RequestSettingsFromGamepadCommand.ExecuteAsync(null);
        Assert.Equal(GamepadOverlayKind.Settings, vm.GamepadOverlay);
        Assert.Empty(vm.GamepadOverlayOptions);
        Assert.NotNull(vm.GamepadSettings);
        Assert.Equal(InterfaceMode.Gamepad, mode.Current);
        Assert.Equal(0, _dialogs.SettingsShown);

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(GamepadOverlayKind.SystemMenu, vm.GamepadOverlay);
        Assert.Equal("Settings", vm.GamepadOverlayOptions[vm.GamepadOverlaySelectionIndex].Label);

        mode = new RecordingInterfaceModeService(InterfaceMode.Gamepad);
        vm = CreateViewModel(interfaceModeService: mode, applicationLifetime: lifetime);
        vm.RequestQuitFromGamepadCommand.Execute(null);
        Assert.Equal(GamepadOverlayKind.QuitConfirmation, vm.GamepadOverlay);
        Assert.Equal(0, lifetime.ShutdownRequests);

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(GamepadOverlayKind.SystemMenu, vm.GamepadOverlay);
        Assert.Equal(0, lifetime.ShutdownRequests);

        vm.RequestQuitFromGamepadCommand.Execute(null);
        vm.ConfirmQuitGamepadCommand.Execute(null);
        Assert.Equal(1, lifetime.ShutdownRequests);
    }

    [AvaloniaFact]
    public void GamepadGridNavigationStopsAtVisualRowEdgesAndMissingFinalRowCells()
    {
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        vm.Games.ReplaceAll(Enumerable.Range(0, 6).Select(index => new GameViewModel(
            new Game
            {
                Id = index + 1,
                SystemId = Ps1.Id,
                Path = $"/Games/Game {index + 1}.cue",
                Title = $"Game {index + 1}",
                DateAdded = DateTimeOffset.UtcNow,
            },
            Ps1.Name,
            Ps1.ShortName,
            Ps1.AccentColor,
            coverAspectRatio: Ps1.CoverAspectRatio)));
        vm.GamepadViewportWidth = 1000;
        Assert.Equal(4, vm.GamepadColumnCount);

        vm.FocusedGame = vm.Games[3];
        vm.MoveGamepadFocusRightCommand.Execute(null);
        Assert.Same(vm.Games[3], vm.FocusedGame);

        vm.FocusedGame = vm.Games[4];
        vm.MoveGamepadFocusLeftCommand.Execute(null);
        Assert.Same(vm.Games[4], vm.FocusedGame);

        vm.FocusedGame = vm.Games[1];
        vm.MoveGamepadFocusDownCommand.Execute(null);
        Assert.Same(vm.Games[5], vm.FocusedGame);

        vm.FocusedGame = vm.Games[2];
        vm.MoveGamepadFocusDownCommand.Execute(null);
        Assert.Same(vm.Games[2], vm.FocusedGame);

        // Up on the top row clamps and never escapes into the platform rail.
        vm.FocusedGame = vm.Games[1];
        vm.MoveGamepadFocusUpCommand.Execute(null);
        Assert.Same(vm.Games[1], vm.FocusedGame);
    }

    [AvaloniaFact]
    public void GamepadSpotlightView_TogglesLayout_StepsOneGame_AndPersists()
    {
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        vm.Games.ReplaceAll(Enumerable.Range(0, 6).Select(index => new GameViewModel(
            new Game
            {
                Id = index + 1,
                SystemId = Ps1.Id,
                Path = $"/Games/Game {index + 1}.cue",
                Title = $"Game {index + 1}",
                DateAdded = DateTimeOffset.UtcNow,
            },
            Ps1.Name,
            Ps1.ShortName,
            Ps1.AccentColor,
            coverAspectRatio: Ps1.CoverAspectRatio)));
        vm.HasGames = true;
        vm.GamepadViewportWidth = 1000;
        Assert.Equal(4, vm.GamepadColumnCount);

        // The cover grid is the default couch layout.
        Assert.False(vm.IsGamepadSpotlightView);
        Assert.True(vm.ShowGamepadGrid);
        Assert.False(vm.ShowGamepadSpotlight);

        // Toggle to the spotlight list + hero.
        vm.ToggleGamepadViewCommand.Execute(null);
        Assert.True(vm.IsGamepadSpotlightView);
        Assert.False(vm.ShowGamepadGrid);
        Assert.True(vm.ShowGamepadSpotlight);
        Assert.True(vm.BuildLibraryViewState().GamepadSpotlightView);

        // In the single-column spotlight, Down/Up step exactly one game and Left/Right are inert —
        // unlike the cover grid, where Down spans a whole GamepadColumnCount-wide row.
        vm.FocusedGame = vm.Games[1];
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateDown));
        Assert.Same(vm.Games[2], vm.FocusedGame);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateUp));
        Assert.Same(vm.Games[1], vm.FocusedGame);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateRight));
        Assert.Same(vm.Games[1], vm.FocusedGame);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateLeft));
        Assert.Same(vm.Games[1], vm.FocusedGame);

        // Toggling back restores the grid layout and the persisted flag.
        vm.ToggleGamepadViewCommand.Execute(null);
        Assert.False(vm.IsGamepadSpotlightView);
        Assert.True(vm.ShowGamepadGrid);
        Assert.False(vm.ShowGamepadSpotlight);
        Assert.False(vm.BuildLibraryViewState().GamepadSpotlightView);
    }

    [AvaloniaFact]
    public void GamepadSystemMenu_ViewModeRow_SwitchesLayoutWithLeftRight_AndDropsIntoOptions()
    {
        var mode = new RecordingInterfaceModeService(InterfaceMode.Gamepad);
        var vm = CreateViewModel(interfaceModeService: mode);
        vm.HasGames = true;

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Menu));
        Assert.Equal(GamepadOverlayKind.SystemMenu, vm.GamepadOverlay);

        // The menu opens on the option list, not the view-mode row; Grid is the active tile by default.
        Assert.False(vm.IsGamepadViewModeRowFocused);
        Assert.True(vm.IsGridViewModeSelected);
        Assert.False(vm.IsListViewModeSelected);
        Assert.True(vm.GamepadOverlayOptions[0].IsFocused);

        // Up from the top option lands on the sort row; a second Up reaches the view-mode row above it.
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateUp));
        Assert.True(vm.IsGamepadSortRowFocused);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateUp));
        Assert.True(vm.IsGamepadViewModeRowFocused);
        Assert.DoesNotContain(vm.GamepadOverlayOptions, option => option.IsFocused);

        // Right selects the spotlight list and applies it live; A on the row is inert (stays in the menu).
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateRight));
        Assert.True(vm.IsGamepadSpotlightView);
        Assert.True(vm.IsListViewModeSelected);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Confirm));
        Assert.Equal(GamepadOverlayKind.SystemMenu, vm.GamepadOverlay);

        // Left selects the cover grid again.
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateLeft));
        Assert.False(vm.IsGamepadSpotlightView);
        Assert.True(vm.IsGridViewModeSelected);

        // Down walks View mode → Sort → option list (top entry).
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateDown));
        Assert.True(vm.IsGamepadSortRowFocused);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateDown));
        Assert.False(vm.IsGamepadViewModeRowFocused);
        Assert.False(vm.IsGamepadSortRowFocused);
        Assert.Equal(0, vm.GamepadOverlaySelectionIndex);
        Assert.True(vm.GamepadOverlayOptions[0].IsFocused);
    }

    [AvaloniaFact]
    public void GamepadSystemMenu_SortRow_ChangesSortLiveWithLeftRight_AndIsInertOnConfirm()
    {
        var mode = new RecordingInterfaceModeService(InterfaceMode.Gamepad);
        var vm = CreateViewModel(interfaceModeService: mode);
        vm.HasGames = true;

        Assert.True(vm.DispatchGamepadAction(GamepadAction.Menu));

        // Up from the top option lands on the sort row (the nearer of the two selector rows).
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateUp));
        Assert.True(vm.IsGamepadSortRowFocused);
        Assert.DoesNotContain(vm.GamepadOverlayOptions, option => option.IsFocused);

        // Right steps to the next sort option and applies it live.
        var initial = vm.SortColumn;
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateRight));
        Assert.NotEqual(initial, vm.SortColumn);

        // A on the sort row reverses the current sort's direction (unlike the inert view-mode row) and
        // stays in the menu; a second press toggles it back.
        var descBefore = vm.SortDescending;
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Confirm));
        Assert.Equal(GamepadOverlayKind.SystemMenu, vm.GamepadOverlay);
        Assert.NotEqual(descBefore, vm.SortDescending);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Confirm));
        Assert.Equal(descBefore, vm.SortDescending);

        // Each option carries its own direction: recency and rating are descending, title is A–Z.
        vm.SelectGamepadSortCommand.Execute(LibrarySortColumn.LastPlayed);
        Assert.True(vm.IsGamepadSortRecentlyPlayedSelected);
        Assert.True(vm.SortDescending);
        vm.SelectGamepadSortCommand.Execute(LibrarySortColumn.Title);
        Assert.True(vm.IsGamepadSortTitleSelected);
        Assert.False(vm.SortDescending);
    }

    [AvaloniaFact]
    public async Task EnteringGamepadMode_CoercesADesktopOnlySortToRecentlyPlayed()
    {
        var mode = new RecordingInterfaceModeService(InterfaceMode.Desktop);
        var vm = CreateViewModel(interfaceModeService: mode);
        vm.SortColumn = LibrarySortColumn.Console; // a column the couch Sort row does not offer
        vm.SortDescending = false;

        await mode.SetModeAsync(InterfaceMode.Gamepad);

        Assert.True(vm.IsGamepadMode);
        Assert.Equal(LibrarySortColumn.LastPlayed, vm.SortColumn);
        Assert.True(vm.SortDescending);
        Assert.True(vm.IsGamepadSortRecentlyPlayedSelected);
    }

    [AvaloniaFact]
    public void GamepadSpotlight_LeftRightArmTheHeroActions_ResettingToPlay()
    {
        GameViewModel Make(long id, bool hasAchievements)
        {
            var vm = new GameViewModel(
                new Game
                {
                    Id = id,
                    SystemId = Ps1.Id,
                    Path = $"/Games/Game {id}.cue",
                    Title = $"Game {id}",
                    DateAdded = DateTimeOffset.UtcNow,
                },
                Ps1.Name, Ps1.ShortName, Ps1.AccentColor, coverAspectRatio: Ps1.CoverAspectRatio);
            if (hasAchievements)
                vm.ApplyAchievementsDisplay(new RetroAchievementsDisplay(true, "3/30", "3 of 30 unlocked."));
            return vm;
        }

        var withAchievements = Make(1, hasAchievements: true);
        var withoutAchievements = Make(2, hasAchievements: false);

        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        vm.Games.ReplaceAll([withAchievements, withoutAchievements]);
        vm.HasGames = true;
        vm.GamepadViewportWidth = 1000;
        vm.ToggleGamepadViewCommand.Execute(null); // into spotlight

        // Play is armed by default.
        vm.FocusedGame = withAchievements;
        Assert.True(vm.IsSpotlightPlayFocused);
        Assert.False(vm.IsSpotlightAchievementsFocused);

        // Left arms Achievements (the game has a set); Right re-arms Play.
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateLeft));
        Assert.True(vm.IsSpotlightAchievementsFocused);
        Assert.False(vm.IsSpotlightPlayFocused);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateRight));
        Assert.False(vm.IsSpotlightAchievementsFocused);
        Assert.True(vm.IsSpotlightPlayFocused);

        // Changing the focused game re-arms Play.
        vm.DispatchGamepadAction(GamepadAction.NavigateLeft);
        Assert.True(vm.IsSpotlightAchievementsFocused);
        vm.FocusedGame = withoutAchievements;
        Assert.True(vm.IsSpotlightPlayFocused);

        // A game with no set never arms Achievements.
        Assert.True(vm.DispatchGamepadAction(GamepadAction.NavigateLeft));
        Assert.False(vm.IsSpotlightAchievementsFocused);
        Assert.True(vm.IsSpotlightPlayFocused);
    }

    [Fact]
    public void ComposeSpotlightInfo_ProjectsPresentFieldsToChips_WithYearAndPlayers()
    {
        static GameMetadataValue Value(GameMetadataField field, string value) =>
            new(1, field, value, null, GameMetadataValueOrigin.Provider, "ss", null, null, DateTimeOffset.UtcNow);

        // The developer and publisher match, so the publisher chip collapses into the developer's.
        var facts = MainViewModel.ComposeSpotlightInfo(
        [
            Value(GameMetadataField.Genre, "Beat 'em up"),
            Value(GameMetadataField.ReleaseDate, "1994-03-01"),
            Value(GameMetadataField.Players, "1-2"),
            Value(GameMetadataField.Developer, "Konami"),
            Value(GameMetadataField.Publisher, "Konami"),
        ]);
        Assert.Equal(["Beat 'em up", "1994", "1-2 players", "Konami"], facts);

        // A single player reads in the singular, and a distinct publisher keeps its own chip.
        Assert.Equal(
            ["1 player", "Capcom", "Sony"],
            MainViewModel.ComposeSpotlightInfo(
            [
                Value(GameMetadataField.Players, "1"),
                Value(GameMetadataField.Developer, "Capcom"),
                Value(GameMetadataField.Publisher, "Sony"),
            ]));

        // Missing fields are skipped; empty metadata yields no chips (only the filename caption shows).
        Assert.Equal(["Sports"], MainViewModel.ComposeSpotlightInfo([Value(GameMetadataField.Genre, "Sports")]));
        Assert.Empty(MainViewModel.ComposeSpotlightInfo([]));
    }

    [AvaloniaFact]
    public void SpotlightHero_WithoutADetailsStore_StillResolvesToTheTitleFallback()
    {
        var game = new GameViewModel(
            new Game
            {
                Id = 1,
                SystemId = Ps1.Id,
                Path = "/Games/Game 1.cue",
                Title = "Game 1",
                DateAdded = DateTimeOffset.UtcNow,
            },
            Ps1.Name, Ps1.ShortName, Ps1.AccentColor, coverAspectRatio: Ps1.CoverAspectRatio);

        // CreateViewModel wires no IGameDetailsStore, mirroring a degraded/headless config.
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        vm.Games.ReplaceAll([game]);
        vm.HasGames = true;
        vm.GamepadViewportWidth = 1000;
        vm.ToggleGamepadViewCommand.Execute(null); // into spotlight
        vm.FocusedGame = game;

        // With no store there is no art to resolve, so the hero resolves to "no logo" and shows the
        // title in the logo's place rather than leaving an empty slot with no name.
        Assert.True(game.AreSpotlightDetailsLoaded);
        Assert.True(game.ShowSpotlightTitleFallback);
    }

    /// <summary>
    /// Regression: Right/Left/Down step by GamepadColumnCount. The count is derived arithmetically
    /// from the gamepad viewport (matching UniformGridLayout), and navigation must honor it — Right
    /// steps one tile within a row, Down steps a whole row, and Left clamps at the row's first column
    /// rather than wrapping into the previous row ("can't move left" was a corrupted count reading as
    /// a divisor of the index, so index%columns was always 0).
    /// </summary>
    [AvaloniaFact]
    public void ArithmeticColumnCountDrivesGridNavigation()
    {
        var vm = CreateViewModel();
        vm.IsGamepadMode = true;
        vm.Games.ReplaceAll(Enumerable.Range(0, 8).Select(index => new GameViewModel(
            new Game
            {
                Id = index + 1,
                SystemId = Ps1.Id,
                Path = $"/Games/Game {index + 1}.cue",
                Title = $"Game {index + 1}",
                DateAdded = DateTimeOffset.UtcNow,
            },
            Ps1.Name,
            Ps1.ShortName,
            Ps1.AccentColor,
            coverAspectRatio: Ps1.CoverAspectRatio)));

        // A viewport that fits exactly four columns under UniformGridLayout's arithmetic; setting it
        // recomputes GamepadColumnCount the same way the layout will pack the tiles.
        vm.GamepadViewportWidth = 1100;
        Assert.Equal(4, vm.GamepadColumnCount);

        // Right steps within the row; Down steps a whole row (index + columns).
        vm.FocusedGame = vm.Games[1];
        vm.MoveGamepadFocusRightCommand.Execute(null);
        Assert.Same(vm.Games[2], vm.FocusedGame);
        vm.MoveGamepadFocusDownCommand.Execute(null);
        Assert.Same(vm.Games[6], vm.FocusedGame);

        // Left steps back within the row, and clamps at the row's first column (no wrap upward).
        vm.FocusedGame = vm.Games[5];
        vm.MoveGamepadFocusLeftCommand.Execute(null);
        Assert.Same(vm.Games[4], vm.FocusedGame);
        vm.MoveGamepadFocusLeftCommand.Execute(null);
        Assert.Same(vm.Games[4], vm.FocusedGame);
    }

    [AvaloniaFact]
    public async Task GamepadLaunchSession_ConsumesCancelUntilTheFrontendHasReturned()
    {
        var path = Path.Combine(_baseDirectory, "GamepadReturn.cue");
        File.WriteAllText(path, "FILE \"GamepadReturn.bin\" BINARY");
        _library.AddGames([new Game
        {
            SystemId = Ps1.Id,
            Path = path,
            Title = "Gamepad return",
            DateAdded = DateTimeOffset.UtcNow,
        }]);
        var mode = new RecordingInterfaceModeService(InterfaceMode.Gamepad);
        var launcher = new BlockingLaunchService();
        var vm = CreateViewModel(launchService: launcher, interfaceModeService: mode);
        await vm.ReloadGamesAsync();
        vm.FocusedGame = Assert.Single(vm.Games);

        var launch = vm.LaunchFocusedGameCommand.ExecuteAsync(null);
        await launcher.Started;

        Assert.True(vm.IsGamepadInputSuspended);
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(InterfaceMode.Gamepad, mode.Current);

        launcher.Complete();
        await launch;

        // The short post-return guard also consumes a late Steam-Input Escape/B event.
        Assert.True(vm.DispatchGamepadAction(GamepadAction.Cancel));
        Assert.Equal(InterfaceMode.Gamepad, mode.Current);
    }

    [AvaloniaFact]
    public async Task AddGames_M3uHidesSelectedReferencedDiscs()
    {
        var folder = MakeRomsFolder();
        var playlist = Path.Combine(folder, "Collection.m3u");
        File.WriteAllText(playlist, "Alpha.cue\nBeta.chd\n");
        _dialogs.FilesToReturn =
        [
            playlist,
            Path.Combine(folder, "Alpha.cue"),
            Path.Combine(folder, "Beta.chd"),
        ];
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Equal(["Collection"], vm.Games.Select(g => g.Title));
    }

    [AvaloniaFact]
    public async Task AddGames_AnalysisAndEntrySelectionRunOffUiThreadOnce()
    {
        var folder = MakeRomsFolder();
        var rules = new RecordingImportRules(Ps1);
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel(rules);
        var uiThreadId = Environment.CurrentManagedThreadId;

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Equal(1, rules.AnalysisCalls);
        Assert.NotEqual(uiThreadId, rules.AnalysisThreadId);
        Assert.NotEqual(uiThreadId, rules.SelectionThreadId);
    }

    [AvaloniaFact]
    public async Task AddGames_UnrecognizedNintendoHeader_UsesConfirmedSystem()
    {
        var folder = MakeRomsFolder();
        var path = Path.Combine(folder, "Unusual.rvz");
        File.WriteAllText(path, "unrecognized container");
        _dialogs.FilesToReturn = [path];
        _dialogs.SystemToReturn = GameCube;
        var vm = CreateViewModel();

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Equal(["Unusual"], vm.Games.Select(game => game.Title));
        Assert.Contains("used confirmed GameCube system", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task AddGames_DefiniteNintendoMismatch_IsSkippedWithFeedback()
    {
        var folder = MakeRomsFolder();
        var path = Path.Combine(folder, "Wii Game.iso");
        var header = new byte[0x20];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x18, 4), 0x5D1C9EA3u);
        File.WriteAllBytes(path, header);
        _dialogs.FilesToReturn = [path];
        _dialogs.SystemToReturn = GameCube;
        var vm = CreateViewModel();

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Empty(vm.Games);
        Assert.Equal("Added 0 games — skipped 1 file not recognized as GameCube", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task AddGames_UnsupportedFile_IsSkippedWithFeedback()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "notes.txt")];
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Empty(vm.Games);
        Assert.Equal("Added 0 games — skipped 1 unsupported file", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task AddGames_ExplicitRawBin_IsAllowed()
    {
        var folder = MakeRomsFolder();
        var path = Path.Combine(folder, "Raw Track.bin");
        File.WriteAllText(path, "x");
        _dialogs.FilesToReturn = [path];
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Equal(["Raw Track"], vm.Games.Select(game => game.Title));
    }

    [AvaloniaFact]
    public async Task AddGames_ProvenMegaDriveBinCannotBeImportedAsPlayStation()
    {
        var folder = MakeRomsFolder();
        var path = Path.Combine(folder, "Ristar.bin");
        var bytes = new byte[0x4000];
        "SEGA"u8.CopyTo(bytes.AsSpan(0x100));
        File.WriteAllBytes(path, bytes);
        _dialogs.FilesToReturn = [path];
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();

        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Empty(_library.GetGames(Ps1.Id));
        Assert.Contains("not recognized as PlayStation", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Rescan_PlaylistAddedLater_SuppressesPersistedDiscsWithoutDeletingFiles()
    {
        var folder = Path.Combine(_baseDirectory, "multi-disc");
        Directory.CreateDirectory(folder);
        var disc1 = Path.Combine(folder, "Disc 1.chd");
        var disc2 = Path.Combine(folder, "Disc 2.chd");
        File.WriteAllText(disc1, "x");
        File.WriteAllText(disc2, "x");
        _dialogs.FolderToReturn = folder;
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();
        await vm.AddFolderCommand.ExecuteAsync(null);
        Assert.Equal(["Disc 1", "Disc 2"], vm.Games.Select(game => game.Title));

        var playlist = Path.Combine(folder, "Collection.m3u");
        File.WriteAllText(playlist, "Disc 1.chd\nDisc 2.chd\n");
        await vm.RescanSystemCommand.ExecuteAsync(null);

        Assert.Equal(["Collection"], vm.Games.Select(game => game.Title));
        Assert.True(File.Exists(disc1));
        Assert.True(File.Exists(disc2));
    }

    [AvaloniaFact]
    public async Task AddGames_CueAddedLater_SuppressesPersistedBinWithoutDeletingFile()
    {
        var folder = MakeRomsFolder();
        var bin = Path.Combine(folder, "Raw Track.bin");
        File.WriteAllText(bin, "x");
        _dialogs.FilesToReturn = [bin];
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();
        await vm.AddGamesCommand.ExecuteAsync(null);
        Assert.Equal(["Raw Track"], vm.Games.Select(game => game.Title));

        var cue = Path.Combine(folder, "Game.cue");
        File.WriteAllText(cue, "FILE \"Raw Track.bin\" BINARY\n");
        _dialogs.FilesToReturn = [cue];
        await vm.AddGamesCommand.ExecuteAsync(null);

        Assert.Equal(["Game"], vm.Games.Select(game => game.Title));
        Assert.True(File.Exists(bin));
    }

    [AvaloniaFact]
    public async Task LaunchGame_CommandUsesLaunchServiceAndShowsCompletionStatus()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        var launcher = new RecordingLaunchService(
            new GameLaunchResult(true, "Alpha finished"));
        var vm = CreateViewModel(launchService: launcher);
        await vm.AddGamesCommand.ExecuteAsync(null);

        await vm.LaunchGameCommand.ExecuteAsync(vm.Games.Single());

        Assert.Equal("Alpha", launcher.Game?.Title);
        Assert.Equal("Alpha finished", vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    [AvaloniaFact]
    public async Task LaunchGame_UnavailableSingleDisc_ReportsContextAwareStatusNotDiscWording()
    {
        var folder = MakeRomsFolder();
        var gamePath = Path.Combine(folder, "Alpha.cue");
        _dialogs.FilesToReturn = [gamePath];
        _dialogs.SystemToReturn = Ps1;
        var launcher = new RecordingLaunchService(new GameLaunchResult(true, "Alpha finished"));
        var vm = CreateViewModel(launchService: launcher);
        await vm.AddGamesCommand.ExecuteAsync(null);

        File.Delete(gamePath);
        await vm.RefreshAvailabilityAsync();
        var game = vm.Games.Single();
        Assert.False(game.IsAvailable);

        await vm.LaunchGameCommand.ExecuteAsync(game);

        // The old always-false ternary showed the multi-disc "Disc N of …" wording for every
        // unavailable launch. A single-disc game must instead get its own context-aware status, and
        // the launcher must never be invoked for an unavailable game.
        Assert.Equal(game.UnavailableLaunchStatus, vm.StatusText);
        Assert.DoesNotContain("Disc", vm.StatusText);
        Assert.Equal(StatusSeverity.Error, vm.StatusSeverity);
        Assert.Null(launcher.Game);
    }

    [AvaloniaFact]
    public async Task LaunchGame_AfterTrackedExitSchedulesOneAchievementRefreshForThatGame()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        var refresh = new RecordingRetroAchievementsRefreshService();
        var vm = CreateViewModel(
            launchService: new RecordingLaunchService(
                new GameLaunchResult(true, "Alpha finished", ProcessExited: true)),
            retroRefresh: refresh);
        await vm.AddGamesCommand.ExecuteAsync(null);
        var game = vm.Games.Single();
        game.ApplyAchievementLink(1234);

        await vm.LaunchGameCommand.ExecuteAsync(game);
        await refresh.Called.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1234, refresh.GameId);
        Assert.Equal("Alpha finished", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task LaunchGame_SyncsSelectedSystemBeforeLaunchAndAfterTrackedExit()
    {
        var events = new List<string>();
        var sync = new RecordingGameSaveSyncService(
            events,
            CloudSaveSyncOutcome.Completed(new SaveSyncReport([])),
            CompletedSync(SaveSyncAction.Upload));
        var launcher = new RecordingLaunchService(
            new GameLaunchResult(true, "Lumines finished", ProcessExited: true),
            () => events.Add("launch"));
        var path = Path.Combine(_baseDirectory, "Lumines.iso");
        File.WriteAllText(path, "psp");
        _library.AddGames([new Game { SystemId = Psp.Id, Path = path, Title = "Lumines", IsAvailable = true }]);
        var vm = CreateViewModel(launchService: launcher, gameSaveSync: sync);
        vm.SelectedSystem = Psp;
        await vm.ReloadGamesAsync();

        await vm.LaunchGameCommand.ExecuteAsync(vm.Games.Single());

        Assert.Equal(["sync:psp", "launch", "sync:psp"], events);
        Assert.Contains("save sync after exit: 1 uploaded", vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    [AvaloniaFact]
    public async Task LaunchGame_WaitsForTheCompletePreLaunchSyncWithoutAnApplicationBudget()
    {
        var events = new List<string>();
        var sync = new BlockingGameSaveSyncService(events);
        var launcher = new RecordingLaunchService(
            new GameLaunchResult(true, "Lumines finished"),
            () => events.Add("launch"));
        var path = Path.Combine(_baseDirectory, "Lumines-wait.iso");
        File.WriteAllText(path, "psp");
        _library.AddGames([new Game { SystemId = Psp.Id, Path = path, Title = "Lumines", IsAvailable = true }]);
        var vm = CreateViewModel(launchService: launcher, gameSaveSync: sync);
        vm.SelectedSystem = Psp;
        await vm.ReloadGamesAsync();

        var launch = vm.LaunchGameCommand.ExecuteAsync(vm.Games.Single());
        await sync.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["sync:psp"], events);
        sync.Complete();
        await launch;

        Assert.Equal(["sync:psp", "launch"], events);
    }

    [AvaloniaFact]
    public async Task LaunchGame_FlagsSaveSyncInProgress_ForTheGamepadPanel()
    {
        var events = new List<string>();
        var sync = new BlockingGameSaveSyncService(events);
        var launcher = new RecordingLaunchService(
            new GameLaunchResult(true, "Lumines finished"),
            () => events.Add("launch"));
        var path = Path.Combine(_baseDirectory, "Lumines-flag.iso");
        File.WriteAllText(path, "psp");
        _library.AddGames([new Game { SystemId = Psp.Id, Path = path, Title = "Lumines", IsAvailable = true }]);
        var vm = CreateViewModel(launchService: launcher, gameSaveSync: sync);
        vm.SelectedSystem = Psp;
        await vm.ReloadGamesAsync();

        Assert.False(vm.IsSyncingSavesForLaunch);
        var launch = vm.LaunchGameCommand.ExecuteAsync(vm.Games.Single());
        await sync.Started.Task.WaitAsync(TestContext.Current.CancellationToken);

        // The centered "Syncing saves…" panel is shown from this flag while the pre-launch sync runs.
        Assert.True(vm.IsSyncingSavesForLaunch);

        sync.Complete();
        await launch;

        Assert.False(vm.IsSyncingSavesForLaunch);
    }

    [AvaloniaFact]
    public async Task LaunchGame_PreLaunchSyncFailureWarnsButStillLaunchesAndRetriesAfterExit()
    {
        var events = new List<string>();
        var sync = new RecordingGameSaveSyncService(
            events,
            CloudSaveSyncOutcome.Failed("cloud offline"),
            CompletedSync(SaveSyncAction.None));
        var launcher = new RecordingLaunchService(
            new GameLaunchResult(true, "Lumines finished", ProcessExited: true),
            () => events.Add("launch"));
        var path = Path.Combine(_baseDirectory, "Lumines.iso");
        File.WriteAllText(path, "psp");
        _library.AddGames([new Game { SystemId = Psp.Id, Path = path, Title = "Lumines", IsAvailable = true }]);
        var vm = CreateViewModel(launchService: launcher, gameSaveSync: sync);
        vm.SelectedSystem = Psp;
        await vm.ReloadGamesAsync();

        await vm.LaunchGameCommand.ExecuteAsync(vm.Games.Single());

        Assert.NotNull(launcher.Game);
        Assert.Equal(["sync:psp", "launch", "sync:psp"], events);
        Assert.Contains("pre-launch save sync did not complete", vm.StatusText);
        Assert.Contains("saves currently on disk were used", vm.StatusText);
        Assert.Contains("save sync after exit: 1 already in sync", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task LaunchGame_ReportsAutomaticSyncConflictsFromBothPasses()
    {
        var events = new List<string>();
        var sync = new RecordingGameSaveSyncService(
            events,
            CompletedSync(SaveSyncAction.ConflictRemoteWins),
            CompletedSync(SaveSyncAction.ConflictLocalWins));
        var launcher = new RecordingLaunchService(
            new GameLaunchResult(true, "Lumines finished", ProcessExited: true),
            () => events.Add("launch"));
        var path = Path.Combine(_baseDirectory, "Lumines.iso");
        File.WriteAllText(path, "psp");
        _library.AddGames([new Game { SystemId = Psp.Id, Path = path, Title = "Lumines", IsAvailable = true }]);
        var vm = CreateViewModel(launchService: launcher, gameSaveSync: sync);
        vm.SelectedSystem = Psp;
        await vm.ReloadGamesAsync();

        await vm.LaunchGameCommand.ExecuteAsync(vm.Games.Single());

        Assert.Contains("1 conflict resolved during pre-launch sync (older copy backed up)", vm.StatusText);
        Assert.Contains("save sync after exit: 1 conflict resolved (older copy backed up)", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task LaunchGame_ReportsWhenNoSavesWereFound()
    {
        var events = new List<string>();
        var sync = new RecordingGameSaveSyncService(
            events,
            CloudSaveSyncOutcome.Completed(new SaveSyncReport([])),
            CloudSaveSyncOutcome.Completed(new SaveSyncReport([])));
        var launcher = new RecordingLaunchService(
            new GameLaunchResult(true, "Lumines finished", ProcessExited: true));
        var path = Path.Combine(_baseDirectory, "Lumines.iso");
        File.WriteAllText(path, "psp");
        _library.AddGames([new Game { SystemId = Psp.Id, Path = path, Title = "Lumines", IsAvailable = true }]);
        var vm = CreateViewModel(launchService: launcher, gameSaveSync: sync);
        vm.SelectedSystem = Psp;
        await vm.ReloadGamesAsync();

        await vm.LaunchGameCommand.ExecuteAsync(vm.Games.Single());

        Assert.Contains("no saves were found to sync after exit", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task LaunchGame_DoesNotSyncAfterLaunchWhenNoTrackedProcessExited()
    {
        var events = new List<string>();
        var sync = new RecordingGameSaveSyncService(
            events,
            CloudSaveSyncOutcome.Completed(new SaveSyncReport([])));
        var launcher = new RecordingLaunchService(
            new GameLaunchResult(false, "PPSSPP could not start", ProcessExited: false),
            () => events.Add("launch"));
        var path = Path.Combine(_baseDirectory, "Lumines.iso");
        File.WriteAllText(path, "psp");
        _library.AddGames([new Game { SystemId = Psp.Id, Path = path, Title = "Lumines", IsAvailable = true }]);
        var vm = CreateViewModel(launchService: launcher, gameSaveSync: sync);
        vm.SelectedSystem = Psp;
        await vm.ReloadGamesAsync();

        await vm.LaunchGameCommand.ExecuteAsync(vm.Games.Single());

        Assert.Equal(["sync:psp", "launch"], events);
        Assert.Equal("PPSSPP could not start", vm.StatusText);
    }

    private static CloudSaveSyncOutcome CompletedSync(SaveSyncAction action) =>
        CloudSaveSyncOutcome.Completed(new SaveSyncReport(
            [new SaveUnitSyncResult("ppsspp/ULUS10041DATA00", action, "test")]));

    [AvaloniaFact]
    public async Task LaunchGame_UnavailableGameIsRejectedBeforeLaunchService()
    {
        var folder = MakeRomsFolder();
        var gamePath = Path.Combine(folder, "Alpha.cue");
        _dialogs.FilesToReturn = [gamePath];
        _dialogs.SystemToReturn = Ps1;
        var launcher = new RecordingLaunchService(
            new GameLaunchResult(true, "should not launch"));
        var vm = CreateViewModel(launchService: launcher);
        await vm.AddGamesCommand.ExecuteAsync(null);
        File.Delete(gamePath);
        await vm.RefreshAvailabilityAsync();

        await vm.LaunchGameCommand.ExecuteAsync(vm.Games.Single(game => game.Title == "Alpha"));

        Assert.Null(launcher.Game);
        Assert.Contains("game file could not be found", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task OpenSettings_CommandUsesSettingsDialog()
    {
        var vm = CreateViewModel();

        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(1, _dialogs.SettingsShown);
        Assert.NotNull(_dialogs.MaintenanceActions?.RescanSystem);
        Assert.Same(vm.ThemeChoices, _dialogs.ThemeChoices);
        Assert.Equal(ThemeCatalog.All.Count, _dialogs.ThemeChoices!.Count);
    }

    [AvaloniaFact]
    public async Task SettingsFolderChange_DoesNotGiveDifferentRomTheOldGamesIdentity()
    {
        var oldRoot = Path.Combine(_baseDirectory, "old-roms");
        var newRoot = Path.Combine(_baseDirectory, "new-roms");
        Directory.CreateDirectory(oldRoot);
        Directory.CreateDirectory(newRoot);
        var oldPath = Path.Combine(oldRoot, "Alpha.game");
        var newPath = Path.Combine(newRoot, "Alpha.game");
        File.WriteAllText(oldPath, "ORIGINAL-ID");
        File.WriteAllText(newPath, "DIFFERENT-ID");
        var rules = new FileContentIdentityImportRules(Ps1);
        _dialogs.FolderToReturn = oldRoot;
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel(importRules: rules, metadataStore: _metadataStore);
        await vm.AddFolderCommand.ExecuteAsync(null);
        var original = Assert.Single(_library.GetGames(Ps1.Id));
        await vm.OpenSettingsCommand.ExecuteAsync(null);
        var folder = Assert.Single(_dialogs.MaintenanceActions!.Folders!.Get(Ps1.Id));

        await _dialogs.MaintenanceActions.Folders.Change(Ps1.Id, folder.Id, newRoot);

        var games = _library.GetGames(Ps1.Id);
        Assert.Equal(2, games.Count);
        Assert.Contains(games, game => game.Id == original.Id && game.Path == oldPath);
        Assert.Contains(games, game => game.Id != original.Id && game.Path == newPath);
    }

    [AvaloniaFact]
    public async Task SettingsRescanAll_RefreshesTheCurrentCollectionAfterDiscovery()
    {
        var folder = MakeRomsFolder();
        _dialogs.FolderToReturn = folder;
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();
        await vm.AddFolderCommand.ExecuteAsync(null);
        await vm.ShowAllGamesCommand.ExecuteAsync(null);
        File.WriteAllText(Path.Combine(folder, "Gamma.chd"), "x");
        await vm.OpenSettingsCommand.ExecuteAsync(null);

        await _dialogs.MaintenanceActions!.RescanAll(new Progress<string>());

        Assert.True(vm.IsAllGamesSelected);
        Assert.Equal(["Alpha", "Beta", "Gamma"], vm.Games.Select(game => game.Title));
    }

    [AvaloniaFact]
    public async Task SettingsManualMetadataFetch_RecordsOneTimeOptIn()
    {
        var metadata = new RecordingMetadataService();
        var preferences = new RecordingMetadataPreferences();
        var vm = CreateViewModel(
            metadata: metadata,
            metadataPreferences: preferences);
        await vm.OpenSettingsCommand.ExecuteAsync(null);

        await _dialogs.MaintenanceActions!.FetchAllMetadata!(
            new Progress<MetadataEnrichmentProgress>());

        Assert.Equal(MetadataConsentChoice.FetchOnce, preferences.RecordedChoice);
        Assert.True(preferences.ConsentPromptShown);
        Assert.False(preferences.AutomaticallyFetchAfterImport);
    }

    [AvaloniaFact]
    public async Task OpenSettings_LoadFailureIsShownInStatusArea()
    {
        _dialogs.SettingsException = new IOException("database unavailable");
        var vm = CreateViewModel();

        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.Equal(
            "Could not open emulator settings: database unavailable",
            vm.StatusText);
    }

    [AvaloniaFact]
    public void Constructor_WithSavedNonSystemTheme_MarksThatThemeSelectedWithoutThrowing()
    {
        // Regression: a saved theme other than System fires OnCurrentThemeChanged during construction,
        // which reads ThemeChoices — so the collection must exist before CurrentTheme is assigned.
        var vm = CreateViewModel(themes: new RecordingThemeService(ThemePreference.Dark));

        Assert.Equal(ThemePreference.Dark, vm.CurrentTheme);
        Assert.True(vm.ThemeChoices.Single(choice => choice.Id == ThemePreference.Dark).IsSelected);
        Assert.All(
            vm.ThemeChoices.Where(choice => choice.Id != ThemePreference.Dark),
            choice => Assert.False(choice.IsSelected));
    }

    [AvaloniaFact]
    public async Task SetTheme_AppliesAndUpdatesSelectionState()
    {
        var themes = new RecordingThemeService();
        var vm = CreateViewModel(themes: themes);

        await vm.SetThemeCommand.ExecuteAsync(ThemePreference.Dark);

        Assert.Equal(ThemePreference.Dark, themes.Current);
        Assert.Equal(ThemePreference.Dark, vm.CurrentTheme);
        Assert.True(vm.ThemeChoices.Single(choice => choice.Id == ThemePreference.Dark).IsSelected);
        Assert.False(vm.ThemeChoices.Single(choice => choice.Id == ThemePreference.System).IsSelected);
        Assert.Equal("Appearance set to dark", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task Collections_ShowGamesAcrossSystemsAndRecentlyAddedNewestFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _library.AddGames(
        [
            new Game
            {
                SystemId = Ps1.Id,
                Path = Path.Combine(_baseDirectory, "Older.cue"),
                Title = "Older PlayStation Game",
                DateAdded = now.AddHours(-2),
            },
            new Game
            {
                SystemId = GameCube.Id,
                Path = Path.Combine(_baseDirectory, "Newest.iso"),
                Title = "Newest GameCube Game",
                DateAdded = now,
            },
        ]);
        var vm = CreateViewModel();

        await vm.ShowAllGamesCommand.ExecuteAsync(null);

        Assert.Null(vm.SelectedSystem);
        Assert.True(vm.IsAllGamesSelected);
        Assert.Equal("All Games", vm.LibraryTitle);
        Assert.Equal(
            ["GameCube", "PlayStation"],
            vm.Games.Select(game => game.SystemName).OrderBy(name => name));

        await vm.ShowRecentlyAddedCommand.ExecuteAsync(null);

        Assert.True(vm.IsRecentlyAddedSelected);
        Assert.Equal("Recently Added", vm.LibraryTitle);
        Assert.Equal(
            ["Newest GameCube Game", "Older PlayStation Game"],
            vm.Games.Select(game => game.Title));
    }

    [AvaloniaFact]
    public async Task RecentlyPlayed_ShowsOnlyPlayedGamesMostRecentlyPlayedFirst()
    {
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = Path.Combine(_baseDirectory, "Played First.cue"), Title = "Played First", DateAdded = DateTimeOffset.UtcNow },
            new Game { SystemId = GameCube.Id, Path = Path.Combine(_baseDirectory, "Played Last.iso"), Title = "Played Last", DateAdded = DateTimeOffset.UtcNow },
            new Game { SystemId = Ps1.Id, Path = Path.Combine(_baseDirectory, "Never Played.cue"), Title = "Never Played", DateAdded = DateTimeOffset.UtcNow },
        ]);
        var games = _library.GetGames();
        _library.SetLastPlayed(games.Single(g => g.Title == "Played First").Id, DateTimeOffset.Parse("2026-08-01T00:00:00+00:00"));
        _library.SetLastPlayed(games.Single(g => g.Title == "Played Last").Id, DateTimeOffset.Parse("2026-08-04T00:00:00+00:00"));
        var vm = CreateViewModel();

        await vm.ShowRecentlyPlayedCommand.ExecuteAsync(null);

        Assert.True(vm.IsRecentlyPlayedSelected);
        Assert.Equal("Recently Played", vm.LibraryTitle);
        // Never-played games are excluded; the rest sort most-recently-played first.
        Assert.Equal(["Played Last", "Played First"], vm.Games.Select(game => game.Title));
    }

    [AvaloniaFact]
    public async Task RecentlyPlayed_WithNoPlayedGames_ShowsEmptyState()
    {
        _library.AddGames([new Game { SystemId = Ps1.Id, Path = Path.Combine(_baseDirectory, "Unplayed.cue"), Title = "Unplayed", DateAdded = DateTimeOffset.UtcNow }]);
        var vm = CreateViewModel();

        await vm.ShowRecentlyPlayedCommand.ExecuteAsync(null);

        Assert.True(vm.IsRecentlyPlayedSelected);
        Assert.Empty(vm.Games);
        Assert.Equal("No recently played games", vm.EmptyLibraryTitle);
    }

    [AvaloniaFact]
    public async Task LaunchGame_StampsLastPlayedAndSurfacesInRecentlyPlayed()
    {
        var path = Path.Combine(_baseDirectory, "Ridge Racer.cue");
        File.WriteAllText(path, "ps1");
        _library.AddGames([new Game { SystemId = Ps1.Id, Path = path, Title = "Ridge Racer", IsAvailable = true, DateAdded = DateTimeOffset.UtcNow }]);
        var vm = CreateViewModel(launchService: new RecordingLaunchService(new GameLaunchResult(true, "Ridge Racer finished")));
        await vm.ShowAllGamesCommand.ExecuteAsync(null);

        await vm.LaunchGameCommand.ExecuteAsync(vm.Games.Single());

        Assert.NotNull(_library.GetGames().Single().LastPlayedAt);

        await vm.ShowRecentlyPlayedCommand.ExecuteAsync(null);
        Assert.Equal(["Ridge Racer"], vm.Games.Select(game => game.Title));
    }

    [AvaloniaFact]
    public async Task LaunchGame_WhileViewingRecentlyPlayed_MovesTheGameToTheFront()
    {
        var now = DateTimeOffset.UtcNow;
        var alpha = Path.Combine(_baseDirectory, "Alpha.cue");
        var beta = Path.Combine(_baseDirectory, "Beta.cue");
        File.WriteAllText(alpha, "ps1");
        File.WriteAllText(beta, "ps1");
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = alpha, Title = "Alpha", IsAvailable = true, DateAdded = now },
            new Game { SystemId = Ps1.Id, Path = beta, Title = "Beta", IsAvailable = true, DateAdded = now },
        ]);
        var games = _library.GetGames();
        // Relative to now so the assertion never depends on the test machine's wall clock.
        _library.SetLastPlayed(games.Single(g => g.Title == "Alpha").Id, now.AddDays(-2));
        _library.SetLastPlayed(games.Single(g => g.Title == "Beta").Id, now.AddDays(-1));
        var vm = CreateViewModel(launchService: new RecordingLaunchService(new GameLaunchResult(true, "Alpha finished")));
        await vm.ShowRecentlyPlayedCommand.ExecuteAsync(null);
        Assert.Equal(["Beta", "Alpha"], vm.Games.Select(game => game.Title));

        await vm.LaunchGameCommand.ExecuteAsync(vm.Games.Single(game => game.Title == "Alpha"));

        Assert.Equal(["Alpha", "Beta"], vm.Games.Select(game => game.Title));
    }

    [AvaloniaFact]
    public async Task SortBy_OrdersLibraryByColumnAndTogglesDirection()
    {
        var now = DateTimeOffset.UtcNow;
        _library.AddGames(
        [
            new Game { SystemId = GameCube.Id, Path = Path.Combine(_baseDirectory, "Yoshi.iso"), Title = "Yoshi", DateAdded = now },
            new Game { SystemId = Ps1.Id, Path = Path.Combine(_baseDirectory, "Alpha.chd"), Title = "Alpha", DateAdded = now },
            new Game { SystemId = GameCube.Id, Path = Path.Combine(_baseDirectory, "Mario.cue"), Title = "Mario", DateAdded = now },
        ]);
        var vm = CreateViewModel();
        await vm.ShowAllGamesCommand.ExecuteAsync(null);

        // Default sort: title ascending.
        Assert.Equal(["Alpha", "Mario", "Yoshi"], vm.Games.Select(g => g.Title));

        // Clicking the active column toggles to descending and shows the down glyph.
        vm.SortByCommand.Execute(LibrarySortColumn.Title);
        Assert.True(vm.SortDescending);
        Assert.Equal("▼", vm.TitleSortGlyph);
        Assert.Equal(["Yoshi", "Mario", "Alpha"], vm.Games.Select(g => g.Title));

        // Switching column resets to ascending; ties break by title (GameCube before PlayStation).
        vm.SortByCommand.Execute(LibrarySortColumn.Console);
        Assert.False(vm.SortDescending);
        Assert.Equal(LibrarySortColumn.Console, vm.SortColumn);
        Assert.Equal("▲", vm.ConsoleSortGlyph);
        Assert.Equal(string.Empty, vm.TitleSortGlyph);
        Assert.Equal(["Mario", "Yoshi", "Alpha"], vm.Games.Select(g => g.Title));

        // Format ascending orders by the file extension: CHD, CUE, ISO.
        vm.SortByCommand.Execute(LibrarySortColumn.Format);
        Assert.Equal(["Alpha", "Mario", "Yoshi"], vm.Games.Select(g => g.Title));
    }

    [AvaloniaFact]
    public async Task SaveGameTitle_PersistsTrimsAndRefreshesVisibleGame()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();
        await vm.AddGamesCommand.ExecuteAsync(null);
        var game = vm.Games.Single();
        game.DraftTitle = "  Metal Gear Solid  ";

        await vm.SaveGameTitleCommand.ExecuteAsync(game);

        Assert.Equal("Metal Gear Solid", vm.Games.Single().Title);
        Assert.Equal("Metal Gear Solid", _library.GetGames(Ps1.Id).Single().Title);
        Assert.Equal("Renamed game to Metal Gear Solid", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task SetGameCover_CopiesImageAndPersistsPortableCover()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        var sourcePath = Path.Combine(_baseDirectory, "chosen-cover.png");
        File.WriteAllBytes(sourcePath, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        _dialogs.CoverImageToReturn = sourcePath;
        var vm = CreateViewModel(covers: new GameCoverService(new AppPaths(_baseDirectory)));
        await vm.AddGamesCommand.ExecuteAsync(null);
        var game = vm.Games.Single();

        await vm.SetGameCoverCommand.ExecuteAsync(game);

        var stored = _library.GetGames(Ps1.Id).Single();
        Assert.Equal("Alpha", _dialogs.LastCoverGameTitle);
        Assert.Equal("PlayStation", _dialogs.LastCoverPickerContext!.SystemName);
        Assert.Equal(Ps1.CoverAspectRatio, _dialogs.LastCoverPickerContext.PreferredAspectRatio);
        Assert.NotNull(stored.CoverPath);
        Assert.StartsWith(Path.Combine(_baseDirectory, "Covers"), stored.CoverPath);
        Assert.True(File.Exists(stored.CoverPath));
        Assert.True(File.Exists(sourcePath));
        Assert.True(game.HasCoverImage);
    }

    [AvaloniaFact]
    public async Task SetGameCover_RemovesWebSearchStagingFileAfterImport()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        var stagingPath = WriteTinyPng("web-cover-staging.png");
        _dialogs.PickedGameCoverToReturn = new PickedGameCover(
            stagingPath,
            IsTemporary: true,
            SourceUri: "https://covers.example/alpha.png");
        var vm = CreateViewModel(covers: new GameCoverService(new AppPaths(_baseDirectory)));
        await vm.AddGamesCommand.ExecuteAsync(null);

        await vm.SetGameCoverCommand.ExecuteAsync(vm.Games.Single());

        var stored = _library.GetGames(Ps1.Id).Single();
        Assert.NotNull(stored.CoverPath);
        Assert.True(File.Exists(stored.CoverPath));
        Assert.False(File.Exists(stagingPath));
    }

    [AvaloniaFact]
    public async Task SetGameCover_DatabaseFailurePreservesPreviousCoverAndCleansStage()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        var firstSource = WriteTinyPng("first-cover.png");
        _dialogs.CoverImageToReturn = firstSource;
        var paths = new AppPaths(_baseDirectory);
        var vm = CreateViewModel(covers: new GameCoverService(paths));
        await vm.AddGamesCommand.ExecuteAsync(null);
        var game = vm.Games.Single();
        await vm.SetGameCoverCommand.ExecuteAsync(game);
        var previousCoverPath = _library.GetGames(Ps1.Id).Single().CoverPath!;

        using (var connection = _database.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                CREATE TRIGGER AbortCoverUpdate
                BEFORE UPDATE OF CoverPath ON Games
                BEGIN
                    SELECT RAISE(ABORT, 'intentional cover update failure');
                END;
                """;
            command.ExecuteNonQuery();
        }
        _dialogs.CoverImageToReturn = WriteTinyPng("second-cover.jpg");

        await vm.SetGameCoverCommand.ExecuteAsync(game);

        Assert.Equal(previousCoverPath, _library.GetGames(Ps1.Id).Single().CoverPath);
        Assert.True(File.Exists(previousCoverPath));
        Assert.Single(Directory.EnumerateFiles(paths.CoversDirectory));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(paths.CacheDirectory, "Covers")));
        Assert.Contains("Could not set cover", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task SetGameCover_PreviewFailureKeepsCommittedCoverAndCompletesOldCleanup()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        _dialogs.CoverImageToReturn = WriteTinyPng("picked-cover.png");
        var importedCoverPath = Path.Combine(_baseDirectory, "Covers", "1-version.png");
        var covers = new PreviewFailingCoverService(importedCoverPath);
        var vm = CreateViewModel(covers: covers);
        await vm.AddGamesCommand.ExecuteAsync(null);
        var originalGame = vm.Games.Single();
        var previousCoverPath = WriteTinyPng("previous-cover.png");
        _library.UpdateCoverPath(originalGame.Id, previousCoverPath);
        await vm.ReloadGamesAsync();
        var game = vm.Games.Single();

        await vm.SetGameCoverCommand.ExecuteAsync(game);

        Assert.Equal(importedCoverPath, _library.GetGames(Ps1.Id).Single().CoverPath);
        Assert.Equal(importedCoverPath, game.CoverPath);
        Assert.False(game.HasCoverImage);
        Assert.Contains(previousCoverPath, covers.DeletedCoverPaths);
        Assert.StartsWith("Updated cover for Alpha", vm.StatusText);
        Assert.Contains("preview could not be loaded", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task SetGameCover_ReloadDuringImportUpdatesReplacementTile()
    {
        var folder = MakeRomsFolder();
        _dialogs.FilesToReturn = [Path.Combine(folder, "Alpha.cue")];
        _dialogs.SystemToReturn = Ps1;
        _dialogs.CoverImageToReturn = WriteTinyPng("picked-cover.png");
        var importedCoverPath = Path.Combine(_baseDirectory, "Covers", "1-version.png");
        var thumbnailPath = WriteTinyPng("replacement-thumbnail.png");
        var covers = new DelayedImportCoverService(importedCoverPath, thumbnailPath);
        var vm = CreateViewModel(covers: covers);
        await vm.AddGamesCommand.ExecuteAsync(null);
        var originalGame = vm.Games.Single();

        var setCover = vm.SetGameCoverCommand.ExecuteAsync(originalGame);
        await covers.ImportStarted;
        await vm.ReloadGamesAsync();
        var replacementGame = vm.Games.Single();
        Assert.NotSame(originalGame, replacementGame);
        covers.ReleaseImport();
        await setCover;

        Assert.True(replacementGame.HasCoverImage);
        Assert.Equal(importedCoverPath, replacementGame.CoverPath);
        Assert.Equal(importedCoverPath, _library.GetGames(Ps1.Id).Single().CoverPath);
    }

    [AvaloniaFact]
    public async Task ReloadGames_DefersCoverDecodeUntilRealizedItemRequestsIt()
    {
        var gamePath = Path.Combine(_baseDirectory, "game.cue");
        var coverPath = Path.Combine(_baseDirectory, "cover.png");
        File.WriteAllText(gamePath, "game");
        File.WriteAllBytes(coverPath, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        _library.AddGames([new Game
        {
            SystemId = Ps1.Id,
            Path = gamePath,
            Title = "Game",
            CoverPath = coverPath,
            DateAdded = DateTimeOffset.UtcNow,
        }]);
        var covers = new RecordingCoverService(coverPath);
        var vm = CreateViewModel(covers: covers);
        await vm.ReloadGamesAsync();

        Assert.Equal(0, covers.ThumbnailRequests);
        var game = vm.Games.Single();
        await vm.LoadGameCoverCommand.ExecuteAsync(game);

        Assert.Equal(1, covers.ThumbnailRequests);
        Assert.True(game.HasCoverImage);
    }

    [AvaloniaFact]
    public async Task Launch_DefersNewCoverUiWorkUntilTheFrontendRestores()
    {
        var gamePath = Path.Combine(_baseDirectory, "game.cue");
        var coverPath = WriteTinyPng("game-cover.png");
        File.WriteAllText(gamePath, "game");
        _library.AddGames([new Game
        {
            SystemId = Ps1.Id,
            Path = gamePath,
            Title = "Game",
            CoverPath = coverPath,
            DateAdded = DateTimeOffset.UtcNow,
        }]);
        var covers = new RecordingCoverService(coverPath);
        var launcher = new BlockingLaunchService();
        var vm = CreateViewModel(launchService: launcher, covers: covers);
        await vm.ReloadGamesAsync();
        var game = vm.Games.Single();

        var launch = vm.LaunchGameCommand.ExecuteAsync(game);
        await launcher.Started;
        await vm.LoadGameCoverCommand.ExecuteAsync(game);
        Assert.Equal(0, covers.ThumbnailRequests);

        launcher.Complete();
        await launch;
        await covers.ThumbnailRequested;
        for (var attempt = 0; attempt < 20 && !game.HasCoverImage; attempt++)
            await Task.Delay(10);

        Assert.Equal(1, covers.ThumbnailRequests);
        Assert.True(game.HasCoverImage);
    }

    [AvaloniaFact]
    public async Task Launch_AppliesPendingSearchWhenTheFrontendRestores()
    {
        var gamePath = Path.Combine(_baseDirectory, "game.cue");
        File.WriteAllText(gamePath, "game");
        _library.AddGames([new Game
        {
            SystemId = Ps1.Id,
            Path = gamePath,
            Title = "Visible Game",
            DateAdded = DateTimeOffset.UtcNow,
        }]);
        var launcher = new BlockingLaunchService();
        var vm = CreateViewModel(launchService: launcher);
        await vm.ReloadGamesAsync();
        var game = vm.Games.Single();

        var launch = vm.LaunchGameCommand.ExecuteAsync(game);
        await launcher.Started;
        vm.SearchText = "not present";
        launcher.Complete();
        await launch;

        Assert.Empty(vm.Games);
        Assert.True(vm.IsSearchEmpty);
    }

    [AvaloniaFact]
    public async Task LoadGameCover_StaleFailureDoesNotOverwriteNewerCoverStatus()
    {
        var gamePath = Path.Combine(_baseDirectory, "game.cue");
        var oldCoverPath = WriteTinyPng("old-cover.png");
        var newCoverPath = WriteTinyPng("new-cover.png");
        File.WriteAllText(gamePath, "game");
        _library.AddGames([new Game
        {
            SystemId = Ps1.Id,
            Path = gamePath,
            Title = "Game",
            CoverPath = oldCoverPath,
            DateAdded = DateTimeOffset.UtcNow,
        }]);
        var covers = new DelayedFailingThumbnailService();
        var vm = CreateViewModel(covers: covers);
        await vm.ReloadGamesAsync();
        var game = vm.Games.Single();
        vm.StatusText = "New cover assigned";

        var loadCover = vm.LoadGameCoverCommand.ExecuteAsync(game);
        await covers.LoadStarted;
        game.ApplyCover(newCoverPath, new Avalonia.Media.Imaging.Bitmap(newCoverPath));
        covers.ReleaseFailure();
        await loadCover;

        Assert.Equal("New cover assigned", vm.StatusText);
        Assert.Equal(newCoverPath, game.CoverPath);
    }

    [AvaloniaFact]
    public async Task RemoveGame_RequiresConfirmationAndNeverDeletesGameFile()
    {
        var folder = MakeRomsFolder();
        var gamePath = Path.Combine(folder, "Alpha.cue");
        _dialogs.FilesToReturn = [gamePath];
        _dialogs.SystemToReturn = Ps1;
        var vm = CreateViewModel();
        await vm.AddGamesCommand.ExecuteAsync(null);

        _dialogs.ConfirmRemoveToReturn = false;
        await vm.RemoveGameCommand.ExecuteAsync(vm.Games.Single());
        Assert.Single(_library.GetGames(Ps1.Id));

        _dialogs.ConfirmRemoveToReturn = true;
        await vm.RemoveGameCommand.ExecuteAsync(vm.Games.Single());

        Assert.Equal("Alpha", _dialogs.LastRemoveGameTitle);
        Assert.Empty(_library.GetGames(Ps1.Id));
        Assert.True(File.Exists(gamePath));
        Assert.Contains("game files were not touched", vm.StatusText);
    }

    [AvaloniaFact]
    public async Task RemoveGame_RemovesEveryDiscInAGroupedTitle()
    {
        var disc1 = Path.Combine(_baseDirectory, "Remove Set (Disc 1).cue");
        var disc2 = Path.Combine(_baseDirectory, "Remove Set (Disc 2).cue");
        File.WriteAllText(disc1, "x");
        File.WriteAllText(disc2, "x");
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = disc1, Title = "Remove Set (Disc 1)" },
            new Game { SystemId = Ps1.Id, Path = disc2, Title = "Remove Set (Disc 2)" },
        ]);
        var vm = CreateViewModel();
        await vm.ReloadGamesAsync();
        var titleSet = Assert.Single(vm.Games);

        _dialogs.ConfirmRemoveToReturn = true;
        await vm.RemoveGameCommand.ExecuteAsync(titleSet);

        Assert.Empty(_library.GetGames(Ps1.Id));
        Assert.True(File.Exists(disc1));
        Assert.True(File.Exists(disc2));
    }

    [AvaloniaFact]
    public async Task SelectDisc_WhenPersistenceFails_ReportsErrorInsteadOfClaimingSuccess()
    {
        var disc1 = Path.Combine(_baseDirectory, "Persist Set (Disc 1).cue");
        var disc2 = Path.Combine(_baseDirectory, "Persist Set (Disc 2).cue");
        File.WriteAllText(disc1, "x");
        File.WriteAllText(disc2, "x");
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = disc1, Title = "Persist Set (Disc 1)" },
            new Game { SystemId = Ps1.Id, Path = disc2, Title = "Persist Set (Disc 2)" },
        ]);
        var vm = CreateViewModel();
        await vm.ReloadGamesAsync();
        var titleSet = Assert.Single(vm.Games);
        Assert.True(titleSet.IsMultiDisc);
        var otherDisc = titleSet.DiscOptions.Single(option => !option.IsCurrent);

        // Force the persistence write to throw so the command can only report failure. The old code
        // ignored the return value and always announced success.
        using (var connection = _database.CreateConnection())
        using (var drop = connection.CreateCommand())
        {
            drop.CommandText = "DROP TABLE GameDiscSelections;";
            drop.ExecuteNonQuery();
        }

        await otherDisc.SelectDiscCommand.ExecuteAsync(null);

        Assert.Equal($"Could not select Disc {otherDisc.Disc.Number} for {titleSet.Title}.", vm.StatusText);
        Assert.Equal(StatusSeverity.Error, vm.StatusSeverity);
    }

    [AvaloniaFact]
    public async Task LibrarySelection_UsesOneAnchorAcrossLayoutsAndClearsWhenSearchChanges()
    {
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Alpha.cue", Title = "Alpha" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Beta.cue", Title = "Beta" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Gamma.cue", Title = "Gamma" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Delta.cue", Title = "Delta" },
        ]);
        var vm = CreateViewModel();
        await vm.ReloadGamesAsync();

        vm.SelectGame(vm.Games[1]);
        Assert.Equal(["Beta"], vm.Games.Where(game => game.IsSelected).Select(game => game.Title));
        Assert.Equal("1 game selected", vm.SelectionSummaryText);
        Assert.Equal("Remove from library…", vm.SelectionRemovalText);

        var delta = vm.Games.Single(game => game.Title == "Delta");
        vm.SelectGame(delta, toggle: true);
        Assert.Equal(2, vm.SelectedGameCount);
        Assert.Equal("Remove 2 selected games…", vm.SelectionRemovalText);

        vm.IsGridView = false;
        Assert.Equal(["Beta", "Delta"], vm.Games.Where(game => game.IsSelected).Select(game => game.Title));

        var gamma = vm.Games.Single(game => game.Title == "Gamma");
        vm.SelectGame(gamma, selectRange: true, toggle: true);
        Assert.Equal(["Beta", "Delta", "Gamma"], vm.Games.Where(game => game.IsSelected).Select(game => game.Title));

        var alpha = vm.Games.Single(game => game.Title == "Alpha");
        vm.SelectGame(alpha, selectRange: true);
        Assert.Equal(["Alpha", "Beta", "Delta"], vm.Games.Where(game => game.IsSelected).Select(game => game.Title));

        vm.SearchText = "Alpha";
        vm.ApplyFilter();
        Assert.Equal(0, vm.SelectedGameCount);
        Assert.False(vm.HasSelectedGames);

        vm.SearchText = string.Empty;
        vm.ApplyFilter();

        vm.SelectAllGamesCommand.Execute(null);
        Assert.Equal(4, vm.SelectedGameCount);
        Assert.True(vm.HasSelectedGames);

        await vm.ReloadGamesAsync();
        Assert.Equal(0, vm.SelectedGameCount);
        Assert.False(vm.HasSelectedGames);
    }

    [AvaloniaFact]
    public async Task LibrarySelection_DoesNotSurviveARoundTripThroughACachedScope()
    {
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Alpha.cue", Title = "Alpha" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Beta.cue", Title = "Beta" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Gamma.cue", Title = "Gamma" },
        ]);
        var vm = CreateViewModel();

        // Visit both collection scopes so each is built and cached. Navigation never clears the cache,
        // so returning to either takes the synchronous fast path that the fix guards.
        await vm.ShowAllGamesCommand.ExecuteAsync(null);
        await vm.ShowRecentlyAddedCommand.ExecuteAsync(null);
        await vm.ShowAllGamesCommand.ExecuteAsync(null);
        Assert.True(vm.IsAllGamesSelected);

        vm.SelectGame(vm.Games[0]);
        vm.SelectGame(vm.Games[1], toggle: true);
        Assert.Equal(2, vm.SelectedGameCount);

        // Leave to the cached scope and come back. The cache hit reuses the very same view models, so
        // without the fix their IsSelected flags would ride back in and the count would jump to 2.
        await vm.ShowRecentlyAddedCommand.ExecuteAsync(null);
        await vm.ShowAllGamesCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.SelectedGameCount);
        Assert.False(vm.HasSelectedGames);
        Assert.DoesNotContain(vm.Games, game => game.IsSelected);
    }

    [AvaloniaFact]
    public async Task MarqueeSelection_ReplacesByDefaultAndUnionsWithModifier()
    {
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Alpha.cue", Title = "Alpha" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Beta.cue", Title = "Beta" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Gamma.cue", Title = "Gamma" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Delta.cue", Title = "Delta" },
        ]);
        var vm = CreateViewModel();
        await vm.ReloadGamesAsync();

        var alpha = vm.Games.Single(game => game.Title == "Alpha");
        var beta = vm.Games.Single(game => game.Title == "Beta");
        var gamma = vm.Games.Single(game => game.Title == "Gamma");
        var delta = vm.Games.Single(game => game.Title == "Delta");

        // A prior selection is dropped the moment a non-additive rubber-band begins.
        vm.SelectGame(gamma);
        vm.BeginMarqueeSelection(additive: false);
        Assert.False(vm.HasSelectedGames);

        vm.UpdateMarqueeSelection(vm.Games, [alpha, beta]);
        vm.EndMarqueeSelection();
        Assert.Equal(["Alpha", "Beta"], vm.Games.Where(game => game.IsSelected).Select(game => game.Title));

        // Ctrl/Cmd keeps the pre-drag selection as a base and unions the new box into it.
        vm.BeginMarqueeSelection(additive: true);
        vm.UpdateMarqueeSelection(vm.Games, [delta]);
        vm.EndMarqueeSelection();
        Assert.Equal(["Alpha", "Beta", "Delta"], vm.Games.Where(game => game.IsSelected).Select(game => game.Title));

        // The rubber-band leaves an anchor, so a following Shift-click extends from it.
        vm.SelectGame(gamma, selectRange: true);
        Assert.Equal(4, vm.SelectedGameCount);
    }

    [AvaloniaFact]
    public async Task MarqueeSelection_ShrinksAmongRealizedTilesButKeepsOffscreenClaims()
    {
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Alpha.cue", Title = "Alpha" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Beta.cue", Title = "Beta" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Gamma.cue", Title = "Gamma" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Delta.cue", Title = "Delta" },
        ]);
        var vm = CreateViewModel();
        await vm.ReloadGamesAsync();

        var alpha = vm.Games.Single(game => game.Title == "Alpha");
        var beta = vm.Games.Single(game => game.Title == "Beta");
        var delta = vm.Games.Single(game => game.Title == "Delta");

        vm.BeginMarqueeSelection(additive: false);

        // The box grows over three tiles while all three are on screen.
        vm.UpdateMarqueeSelection([alpha, beta, delta], [alpha, beta, delta]);
        Assert.Equal(3, vm.SelectedGameCount);

        // It then shrinks off Beta. Delta has scrolled out of view (no longer realized/enumerated),
        // so its claim survives; only the still-realized Beta is dropped.
        vm.UpdateMarqueeSelection([alpha, beta], [alpha]);
        Assert.Equal(["Alpha", "Delta"], vm.Games.Where(game => game.IsSelected).Select(game => game.Title));

        vm.EndMarqueeSelection();
    }

    [AvaloniaFact]
    public async Task RemoveOneSelectedGame_UsesTheNamedSingleGameConfirmation()
    {
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Alpha.cue", Title = "Alpha" },
            new Game { SystemId = Ps1.Id, Path = "C:\\Games\\Beta.cue", Title = "Beta" },
        ]);
        var vm = CreateViewModel();
        await vm.ReloadGamesAsync();
        vm.SelectGame(vm.Games.Single(game => game.Title == "Beta"));

        _dialogs.ConfirmRemoveToReturn = true;
        await vm.RemoveSelectedGamesCommand.ExecuteAsync(null);

        Assert.Equal("Beta", _dialogs.LastRemoveGameTitle);
        Assert.Null(_dialogs.LastRemoveGameCount);
        Assert.Equal(["Alpha"], _library.GetGames(Ps1.Id).Select(game => game.Title));
    }

    [AvaloniaFact]
    public async Task RemoveSelectedGames_ConfirmsOnceAndKeepsGameFilesAndCoversUntouched()
    {
        var folder = MakeRomsFolder();
        var availablePath = Path.Combine(folder, "Available.cue");
        var missingPath = Path.Combine(folder, "Missing.cue");
        File.WriteAllText(availablePath, "source");
        _library.AddGames(
        [
            new Game { SystemId = Ps1.Id, Path = availablePath, Title = "Available", IsAvailable = true },
            new Game { SystemId = Ps1.Id, Path = missingPath, Title = "Missing", IsAvailable = false },
        ]);
        var vm = CreateViewModel();
        await vm.ReloadGamesAsync();
        vm.SelectAllGamesCommand.Execute(null);

        _dialogs.ConfirmRemoveGamesToReturn = true;
        await vm.RemoveSelectedGamesCommand.ExecuteAsync(null);

        Assert.Equal(2, _dialogs.LastRemoveGameCount);
        Assert.Empty(_library.GetGames(Ps1.Id));
        Assert.True(File.Exists(availablePath));
        Assert.False(File.Exists(missingPath));
        Assert.Equal(0, vm.SelectedGameCount);
        Assert.Contains("game files and covers were not touched", vm.StatusText);
    }

    private sealed class RecordingImportRules(GameSystem system) : IGameImportRules
    {
        public int AnalysisCalls { get; private set; }
        public int AnalysisThreadId { get; private set; }
        public int SelectionThreadId { get; private set; }

        public GameFileAnalysis AnalyzeFile(string path)
        {
            AnalysisCalls++;
            AnalysisThreadId = Environment.CurrentManagedThreadId;
            return new(
                path,
                [system],
                new Dictionary<string, GameFileMatch>
                {
                    [system.Id] = GameFileMatch.Compatible,
                });
        }

        public bool IsFolderCandidate(string path, GameSystem candidateSystem) => false;

        public GameEntrySelection SelectGameEntries(
            IReadOnlyList<string> candidates,
            GameSystem candidateSystem)
        {
            SelectionThreadId = Environment.CurrentManagedThreadId;
            return new(candidates, []);
        }
    }

    private sealed class EmbeddedEvidenceImportRules(
        GameSystem system,
        string title,
        string discId) : IGameImportRules
    {
        public GameFileAnalysis AnalyzeFile(string path) => new(
            path,
            [system],
            new Dictionary<string, GameFileMatch> { [system.Id] = GameFileMatch.Compatible });

        public bool IsFolderCandidate(string path, GameSystem candidateSystem) =>
            candidateSystem.Id == system.Id;

        public GameEntrySelection SelectGameEntries(
            IReadOnlyList<string> candidates,
            GameSystem candidateSystem) => new(candidates, []);

        public GameImportMetadata ReadImportMetadata(string path, GameSystem candidateSystem) =>
            candidateSystem.Id == system.Id
                ? new GameImportMetadata(
                    title,
                    [new GameIdentifier(GameIdentifierKind.Serial, discId, "PSP PARAM.SFO", true)])
                : GameImportMetadata.Empty;
    }

    private sealed class FileContentIdentityImportRules(GameSystem system) : IGameImportRules
    {
        public GameFileAnalysis AnalyzeFile(string path) => new(
            path,
            [system],
            new Dictionary<string, GameFileMatch> { [system.Id] = GameFileMatch.Compatible });

        public bool IsFolderCandidate(string path, GameSystem candidateSystem) =>
            candidateSystem.Id == system.Id;

        public GameEntrySelection SelectGameEntries(
            IReadOnlyList<string> candidates,
            GameSystem candidateSystem) => new(candidates, []);

        public GameImportMetadata ReadImportMetadata(string path, GameSystem candidateSystem) =>
            candidateSystem.Id == system.Id
                ? new GameImportMetadata(
                    null,
                    [new GameIdentifier(
                        GameIdentifierKind.DiscId,
                        File.ReadAllText(path),
                        "test",
                        true)])
                : GameImportMetadata.Empty;
    }

    private sealed class FailingOnceMetadataStore(IGameMetadataStore inner) : IGameMetadataStore
    {
        private bool _shouldFail = true;

        public int ReplaceIdentifiersCallCount { get; private set; }

        public Game? GetGame(long gameId) => inner.GetGame(gameId);

        public IReadOnlyList<Game> GetGamesMissingMetadata(string? systemId = null) =>
            inner.GetGamesMissingMetadata(systemId);

        public IReadOnlyList<GameIdentifier> GetIdentifiers(long gameId) => inner.GetIdentifiers(gameId);

        public IReadOnlyDictionary<long, IReadOnlyList<GameIdentifier>> GetAllIdentifiers() =>
            inner.GetAllIdentifiers();

        public void ReplaceIdentifiers(long gameId, IReadOnlyList<GameIdentifier> identifiers)
        {
            ReplaceIdentifiersCallCount++;
            if (_shouldFail)
            {
                _shouldFail = false;
                throw new IOException("Transient metadata-store failure.");
            }

            inner.ReplaceIdentifiers(gameId, identifiers);
        }

        public bool TryApplyCatalogTitle(long gameId, string canonicalTitle, string filenameTitle) =>
            inner.TryApplyCatalogTitle(gameId, canonicalTitle, filenameTitle);

        public bool TryApplyDownloadedCover(
            long gameId,
            string coverPath,
            string providerId,
            string sourceUri) =>
            inner.TryApplyDownloadedCover(gameId, coverPath, providerId, sourceUri);

        public void RecordAttempt(GameMetadataAttempt attempt) => inner.RecordAttempt(attempt);
    }

    private sealed class RecordingLaunchService(
        GameLaunchResult result,
        Action? onLaunch = null) : IEmulatorLaunchService
    {
        public Game? Game { get; private set; }

        public Task<GameLaunchResult> LaunchAsync(
            Game game,
            string? displayName = null,
            Func<CancellationToken, Task>? beforeStart = null,
            CancellationToken cancellationToken = default)
        {
            Game = game;
            return LaunchCoreAsync(beforeStart, cancellationToken);
        }

        private async Task<GameLaunchResult> LaunchCoreAsync(
            Func<CancellationToken, Task>? beforeStart,
            CancellationToken cancellationToken)
        {
            if (beforeStart is not null)
                await beforeStart(cancellationToken);
            onLaunch?.Invoke();
            return result;
        }
    }

    private sealed class RecordingGameSaveSyncService(
        List<string> events,
        params CloudSaveSyncOutcome[] outcomes) : IGameSaveSyncService
    {
        private int _nextOutcome;

        public bool CanSyncSystem(string systemId) => systemId == "psp";

        public Task<CloudSaveSyncOutcome> SyncSystemAsync(
            string systemId,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<string>? launchStateKeys = null)
        {
            events.Add($"sync:{systemId}");
            var index = Math.Min(_nextOutcome++, outcomes.Length - 1);
            return Task.FromResult(outcomes[index]);
        }
    }

    private sealed class BlockingGameSaveSyncService(List<string> events) : IGameSaveSyncService
    {
        private readonly TaskCompletionSource _complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CanSyncSystem(string systemId) => systemId == "psp";

        public async Task<CloudSaveSyncOutcome> SyncSystemAsync(
            string systemId,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<string>? launchStateKeys = null)
        {
            events.Add($"sync:{systemId}");
            Started.TrySetResult();
            await _complete.Task.WaitAsync(cancellationToken);
            return CloudSaveSyncOutcome.Completed(new SaveSyncReport([]));
        }

        public void Complete() => _complete.TrySetResult();
    }

    private sealed class BlockingLaunchService : IEmulatorLaunchService
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _complete =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public async Task<GameLaunchResult> LaunchAsync(
            Game game,
            string? displayName = null,
            Func<CancellationToken, Task>? beforeStart = null,
            CancellationToken cancellationToken = default)
        {
            if (beforeStart is not null)
                await beforeStart(cancellationToken);
            _started.TrySetResult();
            await _complete.Task.WaitAsync(cancellationToken);
            return new GameLaunchResult(true, $"{displayName ?? game.Title} finished");
        }

        public void Complete() => _complete.TrySetResult();
    }

    private sealed class RecordingInterfaceModeService(InterfaceMode initial) : IInterfaceModeService
    {
        public InterfaceMode Current { get; private set; } = initial;
        public bool IsCommandLineOverride => false;
        public event EventHandler<InterfaceMode>? ModeChanged;

        public Task SetModeAsync(InterfaceMode mode, CancellationToken cancellationToken = default)
        {
            Current = mode;
            ModeChanged?.Invoke(this, mode);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingApplicationLifetimeService : IApplicationLifetimeService
    {
        public int ShutdownRequests { get; private set; }

        public void Shutdown() => ShutdownRequests++;
    }

    private sealed class RecordingRetroAchievementsRefreshService : IRetroAchievementsRefreshService
    {
        private readonly TaskCompletionSource _called =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Called => _called.Task;
        public int? GameId { get; private set; }

        public Task<RetroAchievementsProgressRefreshSummary?> RefreshSummaryAtStartupIfStaleAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RetroAchievementsProgressRefreshSummary?>(null);

        public Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>?> RefreshAfterTrackedExitAsync(
            int retroAchievementsGameId,
            CancellationToken cancellationToken = default)
        {
            GameId = retroAchievementsGameId;
            _called.TrySetResult();
            return Task.FromResult<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>?>(
                RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>.Success(
                    new RetroAchievementsDetailsSnapshot(
                        new RetroAchievementsGameDetails(retroAchievementsGameId, "Game", 0, 0, 0, []),
                        DateTimeOffset.UtcNow)));
        }
    }

    private sealed class RecordingThemeService(
        ThemePreference initial = ThemePreference.System) : IAppThemeService
    {
        public ThemePreference Current { get; private set; } = initial;

        public bool AmbientFromArtwork { get; private set; }

        public event EventHandler? AmbientFromArtworkChanged;

        public Task SetThemeAsync(
            ThemePreference preference,
            CancellationToken cancellationToken = default)
        {
            Current = preference;
            return Task.CompletedTask;
        }

        public Task SetAmbientFromArtworkAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            AmbientFromArtwork = enabled;
            AmbientFromArtworkChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public void ApplyArtworkPalette(ArtworkPalette palette) { }

        public void ClearArtworkPalette() { }
    }

    private sealed class RecordingMetadataService : IGameMetadataService
    {
        private readonly TaskCompletionSource _called =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Called => _called.Task;
        public IReadOnlyList<long> GameIds { get; private set; } = [];
        public int CallCount { get; private set; }

        public Task<MetadataEnrichmentSummary> EnrichAsync(
            IEnumerable<long> gameIds,
            IProgress<MetadataEnrichmentProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            GameIds = gameIds.ToArray();
            CallCount++;
            _called.TrySetResult();
            return Task.FromResult(new MetadataEnrichmentSummary(
                GameIds.Count,
                0,
                0,
                GameIds.Count,
                0));
        }

        public Task<MetadataEnrichmentSummary> EnrichMissingAsync(
            string? systemId = null,
            IProgress<MetadataEnrichmentProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MetadataEnrichmentSummary(0, 0, 0, 0, 0));
    }

    private sealed class RecordingRetroAchievementsIdentificationService
        : IRetroAchievementsIdentificationService
    {
        private readonly TaskCompletionSource _called =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Called => _called.Task;
        public IReadOnlyList<long> GameIds { get; private set; } = [];

        public Task<RetroAchievementsIdentificationSummary> IdentifyAsync(
            IEnumerable<long> gameIds,
            CancellationToken cancellationToken = default,
            IProgress<RetroAchievementsLibrarySyncProgress>? progress = null)
        {
            GameIds = gameIds.ToArray();
            _called.TrySetResult();
            progress?.Report(new RetroAchievementsLibrarySyncProgress(
                RetroAchievementsLibrarySyncPhase.Identifying,
                0,
                GameIds.Count,
                "Existing game"));
            progress?.Report(new RetroAchievementsLibrarySyncProgress(
                RetroAchievementsLibrarySyncPhase.Identifying,
                GameIds.Count,
                GameIds.Count));
            return Task.FromResult(new RetroAchievementsIdentificationSummary(
                GameIds.Count, 0, GameIds.Count, 0, 0));
        }
    }

    private sealed class RecordingRetroAchievementsAccountService(bool isConnected)
        : IRetroAchievementsAccountService
    {
        public RetroAchievementsAccount? Account { get; } = new("Player", "ULID-9");
        public bool IsConnected { get; private set; } = isConnected;
        public RetroAchievementsCredentials? CurrentCredentials => IsConnected
            ? new RetroAchievementsCredentials("Player", "SECRET", "ULID-9")
            : null;

        public Task<RetroAchievementsConnectionResult> ConnectAsync(
            string username,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.FromResult(RetroAchievementsConnectionResult.Connected);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }
    }

    private sealed class StaticRetroAchievementsReadStore(long localGameId, int retroAchievementsGameId)
        : IRetroAchievementsReadStore
    {
        public IReadOnlyDictionary<long, RetroAchievementsGameLink> GetAllLinks() =>
            new Dictionary<long, RetroAchievementsGameLink>
            {
                [localGameId] = new RetroAchievementsGameLink(
                    localGameId,
                    RetroAchievementsIdentificationStatus.Hashed,
                    "hash",
                    "algorithm",
                    "fingerprint",
                    retroAchievementsGameId,
                    true,
                    DateTimeOffset.UtcNow,
                    null),
            };

        public IReadOnlyDictionary<int, RetroAchievementsProgressSnapshot> GetAllProgress() =>
            new Dictionary<int, RetroAchievementsProgressSnapshot>();
    }

    private sealed class MutableRetroAchievementsReadStore(long localGameId, int retroAchievementsGameId)
        : IRetroAchievementsReadStore
    {
        private readonly Dictionary<int, RetroAchievementsProgressSnapshot> _progress = new();

        public void SetProgress(int raGameId, int awarded, int total) =>
            _progress[raGameId] = new RetroAchievementsProgressSnapshot(
                new RetroAchievementsGameProgress(raGameId, total, awarded, 0), DateTimeOffset.UtcNow);

        public IReadOnlyDictionary<long, RetroAchievementsGameLink> GetAllLinks() =>
            new Dictionary<long, RetroAchievementsGameLink>
            {
                [localGameId] = new RetroAchievementsGameLink(
                    localGameId,
                    RetroAchievementsIdentificationStatus.Hashed,
                    "hash",
                    "algorithm",
                    "fingerprint",
                    retroAchievementsGameId,
                    true,
                    DateTimeOffset.UtcNow,
                    null),
            };

        public IReadOnlyDictionary<int, RetroAchievementsProgressSnapshot> GetAllProgress() => _progress;
    }

    private sealed class RecordingRetroAchievementsMatchingService : IRetroAchievementsMatchingService
    {
        public int Calls { get; private set; }
        public List<bool> ForceRefreshCatalogues { get; } = [];

        public Task<RetroAchievementsMatchSummary> MatchAsync(
            RetroAchievementsCredentials? credentials,
            bool forceRefreshCatalogues,
            CancellationToken cancellationToken = default,
            IProgress<RetroAchievementsLibrarySyncProgress>? progress = null)
        {
            Calls++;
            ForceRefreshCatalogues.Add(forceRefreshCatalogues);
            progress?.Report(new RetroAchievementsLibrarySyncProgress(
                RetroAchievementsLibrarySyncPhase.Matching,
                0,
                1,
                "Existing game"));
            progress?.Report(new RetroAchievementsLibrarySyncProgress(
                RetroAchievementsLibrarySyncPhase.Matching,
                1,
                1));
            return Task.FromResult(new RetroAchievementsMatchSummary(1, 1, 0, 0, 0));
        }
    }

    private sealed class RecordingRetroAchievementsProgressService : IRetroAchievementsProgressService
    {
        public int Calls { get; private set; }

        public Task<RetroAchievementsProgressRefreshSummary> RefreshAllAsync(
            RetroAchievementsCredentials credentials,
            CancellationToken cancellationToken = default,
            IProgress<RetroAchievementsLibrarySyncProgress>? progress = null)
        {
            Calls++;
            progress?.Report(new RetroAchievementsLibrarySyncProgress(
                RetroAchievementsLibrarySyncPhase.RefreshingProgress,
                1,
                1));
            return Task.FromResult(new RetroAchievementsProgressRefreshSummary(
                1,
                1,
                RetroAchievementsRequestStatus.Success));
        }

        public void Clear() { }
    }

    private sealed class BlockingRetroAchievementsProgressService : IRetroAchievementsProgressService
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _complete =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;
        public bool Cleared { get; private set; }

        public async Task<RetroAchievementsProgressRefreshSummary> RefreshAllAsync(
            RetroAchievementsCredentials credentials,
            CancellationToken cancellationToken = default,
            IProgress<RetroAchievementsLibrarySyncProgress>? progress = null)
        {
            _started.TrySetResult();
            await _complete.Task.WaitAsync(cancellationToken);
            return new RetroAchievementsProgressRefreshSummary(
                1,
                1,
                RetroAchievementsRequestStatus.Success);
        }

        public void Clear() => Cleared = true;

        public void CompleteRefresh() => _complete.TrySetResult();
    }

    private sealed class RecordingRetroAchievementsDetailsService(
        RetroAchievementsDetailsSnapshot? cached = null) : IRetroAchievementsDetailsService
    {
        public bool Cleared { get; private set; }
        public event Action<RetroAchievementsDetailsSnapshot>? DetailsRefreshed;

        public RetroAchievementsDetailsSnapshot? GetCached(int retroAchievementsGameId) => cached;

        public Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>> RefreshAsync(
            RetroAchievementsCredentials credentials,
            int retroAchievementsGameId,
            CancellationToken cancellationToken = default,
            bool manual = false) =>
            Task.FromResult(RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>.Failure(
                RetroAchievementsRequestStatus.Offline));

        public void Clear() => Cleared = true;

        public void Publish(RetroAchievementsDetailsSnapshot snapshot) => DetailsRefreshed?.Invoke(snapshot);
    }

    private sealed class RecordingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }

    private sealed class RecordingMetadataPreferences : IMetadataPreferencesService
    {
        public bool AutomaticallyFetchAfterImport { get; private set; }
        public bool ConsentPromptShown { get; private set; }
        public MetadataConsentChoice? RecordedChoice { get; private set; }

        public Task SaveAutomaticFetchAsync(
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            AutomaticallyFetchAfterImport = enabled;
            ConsentPromptShown = true;
            return Task.CompletedTask;
        }

        public Task RecordConsentAsync(
            MetadataConsentChoice choice,
            CancellationToken cancellationToken = default)
        {
            RecordedChoice = choice;
            AutomaticallyFetchAfterImport = choice == MetadataConsentChoice.Always;
            ConsentPromptShown = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCoverService(string thumbnailPath) : IGameCoverService
    {
        private readonly TaskCompletionSource _thumbnailRequested =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ThumbnailRequests { get; private set; }
        public Task ThumbnailRequested => _thumbnailRequested.Task;

        public Task<ImportedGameCover> ImportAsync(
            long gameId,
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ImportedGameCover>(new NotSupportedException());

        public Task<string?> GetThumbnailAsync(
            long gameId,
            string coverPath,
            CancellationToken cancellationToken = default)
        {
            ThumbnailRequests++;
            _thumbnailRequested.TrySetResult();
            return Task.FromResult<string?>(thumbnailPath);
        }

        public Task DeleteOwnedCoverAsync(
            long gameId,
            string coverPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DelayedImportCoverService(
        string importedCoverPath,
        string thumbnailPath) : IGameCoverService
    {
        private readonly TaskCompletionSource _importStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseImport =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ImportStarted => _importStarted.Task;

        public async Task<ImportedGameCover> ImportAsync(
            long gameId,
            string sourcePath,
            CancellationToken cancellationToken = default)
        {
            _importStarted.TrySetResult();
            await _releaseImport.Task.WaitAsync(cancellationToken);
            return new ImportedGameCover(importedCoverPath, thumbnailPath);
        }

        public Task<string?> GetThumbnailAsync(
            long gameId,
            string coverPath,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(thumbnailPath);

        public Task DeleteOwnedCoverAsync(
            long gameId,
            string coverPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ReleaseImport() => _releaseImport.TrySetResult();
    }

    private sealed class DelayedFailingThumbnailService : IGameCoverService
    {
        private readonly TaskCompletionSource _loadStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFailure =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task LoadStarted => _loadStarted.Task;

        public Task<ImportedGameCover> ImportAsync(
            long gameId,
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ImportedGameCover>(new NotSupportedException());

        public async Task<string?> GetThumbnailAsync(
            long gameId,
            string coverPath,
            CancellationToken cancellationToken = default)
        {
            _loadStarted.TrySetResult();
            await _releaseFailure.Task.WaitAsync(cancellationToken);
            throw new IOException("old cover disappeared");
        }

        public Task DeleteOwnedCoverAsync(
            long gameId,
            string coverPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ReleaseFailure() => _releaseFailure.TrySetResult();
    }

    private sealed class PreviewFailingCoverService(string importedCoverPath) : IGameCoverService
    {
        public List<string> DeletedCoverPaths { get; } = [];

        public Task<ImportedGameCover> ImportAsync(
            long gameId,
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ImportedGameCover(importedCoverPath, "missing-thumbnail.png"));

        public Task<string?> GetThumbnailAsync(
            long gameId,
            string coverPath,
            CancellationToken cancellationToken = default) =>
            Task.FromException<string?>(new IOException("thumbnail cache disappeared"));

        public Task DeleteOwnedCoverAsync(
            long gameId,
            string coverPath,
            CancellationToken cancellationToken = default)
        {
            DeletedCoverPaths.Add(coverPath);
            return Task.CompletedTask;
        }
    }

    private string WriteTinyPng(string fileName)
    {
        var path = Path.Combine(_baseDirectory, fileName);
        File.WriteAllBytes(path, Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        return path;
    }

    public void Dispose()
    {
        if (!Directory.Exists(_baseDirectory))
            return;

        // MainViewModel starts a background library load in its constructor (SelectedSystem ->
        // ReloadGamesAsync reads library.db on a Task.Run thread), so a just-finished fast test
        // can still have that read holding the file open. On Windows an open handle blocks
        // Directory.Delete, so retry briefly to let the in-flight read (or a transient AV/indexer
        // scan) release the file before giving up.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(_baseDirectory, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 40)
            {
                Thread.Sleep(50);
            }
        }
    }
}
