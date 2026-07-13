using System.Buffers.Binary;
using Avalonia.Headless.XUnit;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Library;
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
    private readonly FakeDialogService _dialogs = new();
    private static readonly GameSystem Ps1 = KnownSystems.All.Single(s => s.Id == "playstation");
    private static readonly GameSystem GameCube = KnownSystems.All.Single(s => s.Id == "gamecube");

    public MainViewModelTests()
    {
        var appPaths = new AppPaths(_baseDirectory);
        appPaths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(appPaths);
        database.Initialize();
        _library = new GameLibrary(database, new RelativePathResolver(appPaths));
    }

    private MainViewModel CreateViewModel(IGameImportRules? importRules = null)
    {
        importRules ??= new FileImportRules();
        return new(
            _library,
            new FolderScanner(importRules),
            importRules,
            new FileAvailabilityChecker(),
            _dialogs,
            KnownSystems.All);
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

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
            Directory.Delete(_baseDirectory, recursive: true);
    }
}
