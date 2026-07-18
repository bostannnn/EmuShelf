using System.Buffers.Binary;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Systems;
using EmuShelf.Infrastructure.Importing;
using EmuShelf.Infrastructure.Library;
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
    private readonly string _baseDirectory =
        Path.Combine(Path.GetTempPath(), "EmuShelfAppTests", Guid.NewGuid().ToString("N"));
    private readonly GameLibrary _library;
    private readonly LibraryDatabase _database;
    private readonly FakeDialogService _dialogs = new();
    private static readonly GameSystem Ps1 = KnownSystems.All.Single(s => s.Id == "playstation");
    private static readonly GameSystem GameCube = KnownSystems.All.Single(s => s.Id == "gamecube");

    public MainViewModelTests()
    {
        var appPaths = new AppPaths(_baseDirectory);
        appPaths.EnsureDirectoriesExist();
        _database = new LibraryDatabase(appPaths);
        _database.Initialize();
        _library = new GameLibrary(_database, new RelativePathResolver(appPaths));
    }

    private MainViewModel CreateViewModel(
        IGameImportRules? importRules = null,
        IEmulatorLaunchService? launchService = null,
        IGameCoverService? covers = null,
        IAppThemeService? themes = null,
        IGameMetadataService? metadata = null,
        IMetadataPreferencesService? metadataPreferences = null,
        IRetroAchievementsIdentificationService? retroAchievements = null,
        IRetroAchievementsAccountService? retroAccount = null,
        IRetroAchievementsMatchingService? retroMatching = null,
        IRetroAchievementsProgressService? retroProgress = null)
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
            covers: covers,
            themeService: themes,
            metadataService: metadata,
            metadataPreferences: metadataPreferences,
            retroAchievements: retroAchievements,
            retroAccount: retroAccount,
            retroMatching: retroMatching,
            retroProgress: retroProgress);
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
        var vm = CreateViewModel(
            retroAchievements: identification,
            retroAccount: account,
            retroMatching: new RecordingRetroAchievementsMatchingService(),
            retroProgress: progress);

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
        Assert.False(account.IsConnected);
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

        await _dialogs.MaintenanceActions!.RescanAll();

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

        await _dialogs.MaintenanceActions!.FetchAllMetadata!();

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
    public async Task SetTheme_AppliesAndUpdatesSelectionState()
    {
        var themes = new RecordingThemeService();
        var vm = CreateViewModel(themes: themes);

        await vm.SetThemeCommand.ExecuteAsync(ThemePreference.Dark);

        Assert.Equal(ThemePreference.Dark, themes.Current);
        Assert.True(vm.IsDarkTheme);
        Assert.False(vm.IsSystemTheme);
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
        Assert.NotNull(stored.CoverPath);
        Assert.StartsWith(Path.Combine(_baseDirectory, "Covers"), stored.CoverPath);
        Assert.True(File.Exists(stored.CoverPath));
        Assert.True(File.Exists(sourcePath));
        Assert.True(game.HasCoverImage);
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

    private sealed class RecordingLaunchService(GameLaunchResult result) : IEmulatorLaunchService
    {
        public Game? Game { get; private set; }

        public Task<GameLaunchResult> LaunchAsync(
            Game game,
            CancellationToken cancellationToken = default)
        {
            Game = game;
            return Task.FromResult(result);
        }
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
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _complete.Task.WaitAsync(cancellationToken);
            return new GameLaunchResult(true, $"{game.Title} finished");
        }

        public void Complete() => _complete.TrySetResult();
    }

    private sealed class RecordingThemeService : IAppThemeService
    {
        public ThemePreference Current { get; private set; } = ThemePreference.System;

        public Task SetThemeAsync(
            ThemePreference preference,
            CancellationToken cancellationToken = default)
        {
            Current = preference;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingMetadataService : IGameMetadataService
    {
        private readonly TaskCompletionSource _called =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Called => _called.Task;
        public IReadOnlyList<long> GameIds { get; private set; } = [];

        public Task<MetadataEnrichmentSummary> EnrichAsync(
            IEnumerable<long> gameIds,
            CancellationToken cancellationToken = default)
        {
            GameIds = gameIds.ToArray();
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

    private sealed class RecordingRetroAchievementsMatchingService : IRetroAchievementsMatchingService
    {
        public int Calls { get; private set; }

        public Task<RetroAchievementsMatchSummary> MatchAsync(
            RetroAchievementsCredentials? credentials,
            bool forceRefreshCatalogues,
            CancellationToken cancellationToken = default,
            IProgress<RetroAchievementsLibrarySyncProgress>? progress = null)
        {
            Calls++;
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
        if (Directory.Exists(_baseDirectory))
            Directory.Delete(_baseDirectory, recursive: true);
    }
}
