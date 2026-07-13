using EmuShelf.Core.Launching;
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

    /// <summary>Absolute path of a manually selected emulator executable, or null if cancelled.</summary>
    Task<string?> PickEmulatorExecutableAsync(string emulatorName);

    /// <summary>Absolute path of a manually selected cover image, or null if cancelled.</summary>
    Task<string?> PickCoverImageAsync(string gameTitle);

    /// <summary>Confirms removing a game from the library without touching its files.</summary>
    Task<bool> ConfirmRemoveGameAsync(string gameTitle);

    /// <summary>
    /// Asks the user to confirm the system for an import, pre-selecting <paramref name="suggested"/>.
    /// Returns null if cancelled.
    /// </summary>
    Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested);

    /// <summary>Shows the per-system emulator configuration window.</summary>
    Task ShowEmulatorSettingsAsync(
        IReadOnlyList<GameSystem> systems,
        IReadOnlyList<EmulatorDefinition> emulators,
        IEmulatorConfigurationStore configurations,
        LibraryMaintenanceActions maintenance);
}
