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
/// The real implementations are later milestones, and each is one of the gamepad-mode "desktop
/// escape hatches" the plan enumerates: folder/file pickers over <c>TopLevel.StorageProvider</c> and
/// a URI-aware path model are Milestone D; gamepad-native import, cover search, system pick and the
/// settings surface are A1's remaining feature work / Milestone C's IME.
/// </summary>
public sealed class SingleViewDialogService(IAppLogger logger) : IDialogService
{
    private const string NotYet = "Android dialog not implemented in the A1 skeleton: ";

    public Task<IReadOnlyList<string>> PickGameFilesAsync()
    {
        logger.Information(NotYet + nameof(PickGameFilesAsync));
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<string?> PickFolderAsync()
    {
        logger.Information(NotYet + nameof(PickFolderAsync));
        return Task.FromResult<string?>(null);
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
