using EmuShelf.App.Services;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.Tests;

/// <summary>Scripts the pickers so view-model flows can be driven without real dialogs.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public IReadOnlyList<string> FilesToReturn { get; set; } = [];
    public string? FolderToReturn { get; set; }
    public GameSystem? SystemToReturn { get; set; }

    public Task<IReadOnlyList<string>> PickGameFilesAsync() => Task.FromResult(FilesToReturn);
    public Task<string?> PickFolderAsync() => Task.FromResult(FolderToReturn);
    public Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested) =>
        Task.FromResult(SystemToReturn);
}
