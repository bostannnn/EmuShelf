using EmuShelf.App.Services;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.Tests;

/// <summary>Scripts the pickers so view-model flows can be driven without real dialogs.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public IReadOnlyList<string> FilesToReturn { get; set; } = [];
    public string? FolderToReturn { get; set; }
    public GameSystem? SystemToReturn { get; set; }
    public TexturePackSettingsContext? TexturePacks { get; private set; }
    public string? EmulatorExecutableToReturn { get; set; }
    public string? Rpcs3ConfigurationDirectoryToReturn { get; set; }
    public string? CoverImageToReturn { get; set; }
    public PickedGameCover? PickedGameCoverToReturn { get; set; }
    public bool ConfirmRemoveToReturn { get; set; }
    public bool ConfirmRemoveGamesToReturn { get; set; }
    public MetadataConsentChoice MetadataConsentToReturn { get; set; } =
        MetadataConsentChoice.NotNow;
    public int MetadataConsentPrompts { get; private set; }
    public string? LastCoverGameTitle { get; private set; }
    public GameCoverPickerContext? LastCoverPickerContext { get; private set; }
    public string? LastRemoveGameTitle { get; private set; }
    public int? LastRemoveGameCount { get; private set; }
    public Exception? SettingsException { get; set; }
    public int SettingsShown { get; private set; }
    public LibraryMaintenanceActions? MaintenanceActions { get; private set; }
    public (string GameTitle, int RetroAchievementsGameId)? AchievementDetailsRequest { get; private set; }
    public long? LastScraperGameId { get; private set; }
    public string? LastScraperGameTitle { get; private set; }
    public bool ScraperAppliedToReturn { get; set; }

    public Task<bool> ShowScraperAsync(long gameId, string gameTitle)
    {
        LastScraperGameId = gameId;
        LastScraperGameTitle = gameTitle;
        return Task.FromResult(ScraperAppliedToReturn);
    }

    public Task<IReadOnlyList<string>> PickGameFilesAsync() => Task.FromResult(FilesToReturn);
    public Task<string?> PickFolderAsync() => Task.FromResult(FolderToReturn);
    public Task<string?> PickEmulatorExecutableAsync(string emulatorName) =>
        Task.FromResult(EmulatorExecutableToReturn);

    public string? LibretroCoreToReturn { get; set; }

    public Task<string?> PickLibretroCoreAsync(string systemName) =>
        Task.FromResult(LibretroCoreToReturn);
    public Task<string?> PickRpcs3ConfigurationDirectoryAsync() =>
        Task.FromResult(Rpcs3ConfigurationDirectoryToReturn);
    public string? GoogleClientJsonPath { get; set; }

    public Task<string?> PickGoogleClientJsonAsync() => Task.FromResult(GoogleClientJsonPath);

    public Task<string?> PickCoverImageAsync(string gameTitle)
    {
        LastCoverGameTitle = gameTitle;
        return Task.FromResult(CoverImageToReturn);
    }
    public Task<PickedGameCover?> PickGameCoverAsync(GameCoverPickerContext context)
    {
        LastCoverPickerContext = context;
        LastCoverGameTitle = context.GameTitle;
        return Task.FromResult(
            PickedGameCoverToReturn ??
            (CoverImageToReturn is null ? null : new PickedGameCover(CoverImageToReturn)));
    }
    public Task<bool> ConfirmRemoveGameAsync(string gameTitle)
    {
        LastRemoveGameTitle = gameTitle;
        return Task.FromResult(ConfirmRemoveToReturn);
    }
    public Task<bool> ConfirmRemoveGamesAsync(int gameCount)
    {
        LastRemoveGameCount = gameCount;
        return Task.FromResult(ConfirmRemoveGamesToReturn);
    }
    public Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested) =>
        Task.FromResult(SystemToReturn);
    public Task<MetadataConsentChoice> PromptForMetadataConsentAsync(int gameCount)
    {
        MetadataConsentPrompts++;
        return Task.FromResult(MetadataConsentToReturn);
    }
    public RetroAchievementsSettingsContext? RetroAchievementsContext { get; private set; }
    public CloudSaveSyncSettingsContext? CloudSaveSyncContext { get; private set; }

    public Task ShowEmulatorSettingsAsync(
        IReadOnlyList<GameSystem> systems,
        IReadOnlyList<EmulatorDefinition> emulators,
        IEmulatorConfigurationStore configurations,
        LibraryMaintenanceActions maintenance,
        IMetadataPreferencesService metadataPreferences,
        RetroAchievementsSettingsContext? retroAchievements = null,
        CloudSaveSyncSettingsContext? cloudSaves = null,
        TexturePackSettingsContext? texturePacks = null,
        ScreenScraperSettingsContext? screenScraper = null)
    {
        SettingsShown++;
        TexturePacks = texturePacks;
        MaintenanceActions = maintenance;
        RetroAchievementsContext = retroAchievements;
        CloudSaveSyncContext = cloudSaves;
        if (SettingsException is not null)
            return Task.FromException(SettingsException);
        return Task.CompletedTask;
    }

    public Task ShowAchievementDetailsAsync(string gameTitle, int retroAchievementsGameId)
    {
        AchievementDetailsRequest = (gameTitle, retroAchievementsGameId);
        return Task.CompletedTask;
    }
}
