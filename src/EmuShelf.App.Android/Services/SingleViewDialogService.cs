using Avalonia.Controls;
using Avalonia.Platform.Storage;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Walking-skeleton dialog service: every interaction that needs a picker, a modal window or a
/// gamepad-native overlay is not built yet, so each returns the "cancelled / declined / nothing"
/// answer and logs that it was reached. This keeps the shared view model fully constructible and the
/// app launchable without a keyboard, which is the point of A1.
///
/// The remaining stubs are later milestones, and each is one of the gamepad-mode "desktop escape
/// hatches" the plan enumerates: full URI-aware (SAF-backed) sources are Milestone D; cover search and
/// the settings surface are A1's remaining feature work / Milestone C's IME. <see cref="PickFolderAsync"/>
/// is implemented for the A1 gamepad-native import — the system chooser and scan-progress steps are
/// controller-native overlays in the shared view-model, so the only thing the platform must supply is
/// the folder pick itself.
/// </summary>
public sealed class SingleViewDialogService(IAppLogger logger, Func<TopLevel?> topLevel) : IDialogService
{
    private const string NotYet = "Android dialog not implemented in the A1 skeleton: ";

    public Task<IReadOnlyList<string>> PickGameFilesAsync()
    {
        logger.Information(NotYet + nameof(PickGameFilesAsync));
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// <summary>
    /// Opens the Android system folder picker (SAF <c>ACTION_OPEN_DOCUMENT_TREE</c>, surfaced by
    /// Avalonia through <c>TopLevel.StorageProvider</c>) and returns a real filesystem path. With
    /// all-files access granted, the picked tree resolves to a local path the shared
    /// <c>FolderScanner</c> reads unchanged; without it the pick is a content URI with no local path,
    /// which needs the SAF-backed readers of Milestone D — so we log and return null (the shared import
    /// flow drops back to the shelf) rather than handing the scanner a path it cannot open.
    /// </summary>
    public async Task<string?> PickFolderAsync()
    {
        var top = topLevel();
        if (top is null)
        {
            logger.Warning("Folder pick requested before a TopLevel was available.");
            return null;
        }

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a games folder",
            AllowMultiple = false,
        });

        if (folders.Count == 0)
            return null;

        var localPath = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            logger.Information(
                "Folder picker returned a non-local (SAF) URI; SAF-backed scanning is Milestone D.");
            return null;
        }

        return localPath;
    }

    public Task<string?> PickEmulatorExecutableAsync(string emulatorName)
    {
        logger.Information(NotYet + nameof(PickEmulatorExecutableAsync));
        return Task.FromResult<string?>(null);
    }

    public Task<string?> PickLibretroCoreAsync(string systemName)
    {
        logger.Information(NotYet + nameof(PickLibretroCoreAsync));
        return Task.FromResult<string?>(null);
    }

    public Task<string?> PickRpcs3ConfigurationDirectoryAsync()
    {
        logger.Information(NotYet + nameof(PickRpcs3ConfigurationDirectoryAsync));
        return Task.FromResult<string?>(null);
    }

    public Task<string?> PickCoverImageAsync(string gameTitle)
    {
        logger.Information(NotYet + nameof(PickCoverImageAsync));
        return Task.FromResult<string?>(null);
    }

    public Task<bool> ConfirmRemoveGameAsync(string gameTitle)
    {
        logger.Information(NotYet + nameof(ConfirmRemoveGameAsync));
        return Task.FromResult(false);
    }

    public Task<bool> ConfirmRemoveGamesAsync(int gameCount)
    {
        logger.Information(NotYet + nameof(ConfirmRemoveGamesAsync));
        return Task.FromResult(false);
    }

    public Task<MetadataConsentChoice> PromptForMetadataConsentAsync(int gameCount)
    {
        logger.Information(NotYet + nameof(PromptForMetadataConsentAsync));
        return Task.FromResult(MetadataConsentChoice.NotNow);
    }

    public Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested)
    {
        logger.Information(NotYet + nameof(PickSystemAsync));
        return Task.FromResult<GameSystem?>(null);
    }

    public Task ShowEmulatorSettingsAsync(
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
        Func<HotkeySettingsContext?>? createHotkeyContext = null)
    {
        logger.Information(NotYet + nameof(ShowEmulatorSettingsAsync));
        return Task.CompletedTask;
    }

    public Task ShowAchievementDetailsAsync(string gameTitle, int retroAchievementsGameId)
    {
        logger.Information(NotYet + nameof(ShowAchievementDetailsAsync));
        return Task.CompletedTask;
    }
}
