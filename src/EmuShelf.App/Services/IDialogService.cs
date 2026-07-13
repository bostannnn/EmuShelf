using EmuShelf.Core.Systems;

namespace EmuShelf.App.Services;

/// <summary>
/// UI interactions the view model needs but can't perform itself (file/folder pickers,
/// the system-confirmation prompt). Keeps Avalonia dialog types out of the view model.
/// </summary>
public interface IDialogService
{
    /// <summary>Absolute paths of the picked game files; empty if cancelled.</summary>
    Task<IReadOnlyList<string>> PickGameFilesAsync();

    /// <summary>Absolute path of the picked folder, or null if cancelled.</summary>
    Task<string?> PickFolderAsync();

    /// <summary>
    /// Asks the user to confirm the system for an import, pre-selecting <paramref name="suggested"/>.
    /// Returns null if cancelled.
    /// </summary>
    Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested);
}
