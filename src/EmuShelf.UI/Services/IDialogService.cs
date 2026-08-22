using EmuShelf.App.ViewModels;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.Services;

public sealed record GameCoverPickerContext(
    string GameTitle,
    string SystemName,
    double PreferredAspectRatio);

public sealed record PickedGameCover(
    string SourcePath,
    bool IsTemporary = false,
    string? SourceUri = null);

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
    /// Absolute path where the user wants to write an export archive (a <c>.zip</c>), or null if
    /// cancelled. <paramref name="suggestedFileName"/> pre-fills the save dialog's name field.
    /// </summary>
    Task<string?> PickSaveArchiveAsync(string suggestedFileName);

    /// <summary>Absolute path of a manually selected emulator executable, or null if cancelled.</summary>
    Task<string?> PickEmulatorExecutableAsync(string emulatorName);
    Task<string?> PickLibretroCoreAsync(string systemName);

    /// <summary>Absolute path of the user-selected RPCS3 configuration folder, or null if cancelled.</summary>
    Task<string?> PickRpcs3ConfigurationDirectoryAsync();

    /// <summary>Absolute path of a manually selected cover image, or null if cancelled.</summary>
    Task<string?> PickCoverImageAsync(string gameTitle);

    /// <summary>
    /// Lets the user choose either a local image or an explicit web-search result. Implementations
    /// without the web picker retain the original local-file behavior.
    /// </summary>
    async Task<PickedGameCover?> PickGameCoverAsync(GameCoverPickerContext context)
    {
        var path = await PickCoverImageAsync(context.GameTitle);
        return path is null ? null : new PickedGameCover(path);
    }

    /// <summary>Confirms removing a game from the library without touching its files.</summary>
    Task<bool> ConfirmRemoveGameAsync(string gameTitle);

    /// <summary>Confirms removing several library records without touching their files or covers.</summary>
    Task<bool> ConfirmRemoveGamesAsync(int gameCount);

    /// <summary>
    /// Confirms deleting the library records a rescan found missing on disk, listing their titles so
    /// the user sees exactly what goes. Files and covers are never touched. Defaults to false (keep
    /// them) on platforms that have not built the prompt.
    /// </summary>
    Task<bool> ConfirmRescanRemovalsAsync(IReadOnlyList<string> gameTitles) => Task.FromResult(false);

    /// <summary>Asks once whether newly imported games may use network metadata providers.</summary>
    Task<MetadataConsentChoice> PromptForMetadataConsentAsync(int gameCount);

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
        LibraryMaintenanceActions maintenance,
        IMetadataPreferencesService metadataPreferences,
        RetroAchievementsSettingsContext? retroAchievements = null,
        CloudSaveSyncSettingsContext? cloudSaves = null,
        TexturePackSettingsContext? texturePacks = null,
        ScreenScraperSettingsContext? screenScraper = null,
        IReadOnlyList<ThemeChoiceViewModel>? themeChoices = null,
        bool ambientThemeFromArtwork = false,
        Func<bool, Task>? setAmbientThemeFromArtwork = null,
        AppUpdateCoordinator? updates = null,
        Func<HotkeySettingsContext?>? createHotkeyContext = null);

    /// <summary>Shows cache-first achievement details for one confirmed RetroAchievements game.</summary>
    Task ShowAchievementDetailsAsync(string gameTitle, int retroAchievementsGameId);

    /// <summary>
    /// Opens the ScreenScraper scrape/apply window for one game. Returns true when data was applied,
    /// so the caller can refresh the library. Implementations without the provider return false.
    /// </summary>
    Task<bool> ShowScraperAsync(long gameId, string gameTitle) => Task.FromResult(false);

    /// <summary>
    /// Opens the batch scrape window for a set of games. Returns true when any game was scraped, so
    /// the caller can refresh the library. Implementations without the provider return false.
    /// </summary>
    Task<bool> ShowBatchScraperAsync(IReadOnlyList<long> gameIds, string systemName) => Task.FromResult(false);
}
