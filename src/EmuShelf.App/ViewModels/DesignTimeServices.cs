using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Library;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.ViewModels;

// No-op service implementations so MainViewModel's parameterless constructor (used by the
// XAML designer's Design.DataContext) works without touching disk or opening dialogs.

internal sealed class EmptyGameLibrary : IGameLibrary
{
    public IReadOnlyList<Game> GetGames(string? systemId = null) => [];
    public int AddGames(IEnumerable<Game> games) => 0;
    public void SetAvailability(long gameId, bool isAvailable) { }
    public IReadOnlyList<LibraryFolder> GetLibraryFolders(string? systemId = null) => [];
    public void AddLibraryFolder(string systemId, string folderPath) { }
}

internal sealed class NullFolderScanner : IFolderScanner
{
    public Task<IReadOnlyList<string>> ScanAsync(
        string folderPath, GameSystem system,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

internal sealed class NoImportRules : IGameImportRules
{
    public IReadOnlyList<GameSystem> SuggestSystems(string path) => [];
    public bool IsCandidate(string path, GameSystem system) => false;
}

internal sealed class AlwaysAvailableChecker : IAvailabilityChecker
{
    public bool IsAvailable(Game game) => true;
}

internal sealed class NullDialogService : IDialogService
{
    public Task<IReadOnlyList<string>> PickGameFilesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
    public Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested) =>
        Task.FromResult<GameSystem?>(null);
}
