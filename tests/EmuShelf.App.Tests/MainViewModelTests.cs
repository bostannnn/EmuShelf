using Avalonia.Headless.XUnit;
using EmuShelf.App.ViewModels;
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

    public MainViewModelTests()
    {
        var appPaths = new AppPaths(_baseDirectory);
        appPaths.EnsureDirectoriesExist();
        var database = new LibraryDatabase(appPaths);
        database.Initialize();
        _library = new GameLibrary(database, new RelativePathResolver(appPaths));
    }

    private MainViewModel CreateViewModel() => new(
        _library,
        new FolderScanner(new ExtensionImportRules()),
        new ExtensionImportRules(),
        new FileAvailabilityChecker(),
        _dialogs,
        KnownSystems.All);

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

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
            Directory.Delete(_baseDirectory, recursive: true);
    }
}
