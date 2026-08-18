using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmuShelf.App.Services;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Library;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Shell;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.ViewModels;

// No-op service implementations so MainViewModel's parameterless constructor (used by the
// XAML designer's Design.DataContext) works without touching disk or opening dialogs.

internal sealed class EmptyGameLibrary : IGameLibrary
{
    public IReadOnlyList<Game> GetGames(string? systemId = null) => [];
    public IReadOnlySet<string> GetPopulatedSystemIds() => new HashSet<string>();
    public IReadOnlyList<Game> GetRecentlyAddedGames(int limit) => [];
    public void RecordLaunchStarted(long gameId, DateTimeOffset startedAt) { }
    public void AddPlaytime(long gameId, TimeSpan duration) { }
    public int AddGames(IEnumerable<Game> games) => 0;
    public GameImportResult ReconcileImport(
        string systemId, IEnumerable<Game> entries, IReadOnlyList<string> suppressedPaths) =>
        GameImportResult.Empty;
    public ExternalLibraryImportResult ReconcileExternalLibrary(
        ExternalLibrarySource source,
        IReadOnlyList<ExternalLibraryGameEntry> entries) => new([], 0, 0);
    public void SetAvailability(long gameId, bool isAvailable) { }
    public void SetAvailabilities(IReadOnlyList<GameAvailabilityUpdate> updates) { }
    public void UpdateTitle(long gameId, string title) { }
    public void UpdateCoverPath(long gameId, string? coverPath) { }
    public IReadOnlyDictionary<string, long> GetDiscSelections() => new Dictionary<string, long>();
    public void SetDiscSelection(string titleSetKey, long gameId) { }
    public void RemoveGame(long gameId) { }
    public void RemoveGames(IReadOnlyList<long> gameIds) { }
    public IReadOnlyList<LibraryFolder> GetLibraryFolders(string? systemId = null) => [];
    public void AddLibraryFolder(string systemId, string folderPath) { }
    public LibraryFolderChangeResult ReplaceLibraryFolder(
        long folderId,
        string systemId,
        string replacementPath,
        IReadOnlyDictionary<long, string> verifiedGamePaths) => new(0);
    public void RemoveLibraryFolder(long folderId, string systemId) { }
}

internal sealed class NullFolderScanner : IFolderScanner
{
    public Task<GameEntrySelection> ScanAsync(
        string folderPath, GameSystem system,
        IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(GameEntrySelection.Empty);
}

internal sealed class NoImportRules : IGameImportRules
{
    public GameFileAnalysis AnalyzeFile(string path) =>
        new(path, [], new Dictionary<string, GameFileMatch>());
    public bool IsFolderCandidate(string path, GameSystem system) => false;
    public GameEntrySelection SelectGameEntries(
        IReadOnlyList<string> candidates, GameSystem system) => new(candidates, []);
}

internal sealed class AlwaysAvailableChecker : IAvailabilityChecker
{
    public bool IsAvailable(Game game) => true;
}

internal sealed class NullGameCoverService : IGameCoverService
{
    public Task<ImportedGameCover> ImportAsync(
        long gameId,
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ImportedGameCover>(new InvalidOperationException("Cover importing is unavailable."));

    public Task<string?> GetThumbnailAsync(
        long gameId,
        string coverPath,
        CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

    public Task DeleteOwnedCoverAsync(
        long gameId,
        string coverPath,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

internal sealed class NullAppThemeService : IAppThemeService
{
    public ThemePreference Current { get; private set; } = ThemePreference.System;

    public bool AmbientFromArtwork { get; private set; }

    public event EventHandler? AmbientFromArtworkChanged;

    public bool CrtScreenEffect { get; private set; } = true;

    public event EventHandler? CrtScreenEffectChanged;

    public Task SetCrtScreenEffectAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        CrtScreenEffect = enabled;
        CrtScreenEffectChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task SetThemeAsync(
        ThemePreference preference,
        CancellationToken cancellationToken = default)
    {
        Current = preference;
        return Task.CompletedTask;
    }

    public Task SetAmbientFromArtworkAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        AmbientFromArtwork = enabled;
        AmbientFromArtworkChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void ApplyArtworkPalette(ArtworkPalette palette)
    {
    }

    public void ClearArtworkPalette()
    {
    }
}

internal sealed class NullDialogService : IDialogService
{
    public Task<IReadOnlyList<string>> PickGameFilesAsync() => Task.FromResult<IReadOnlyList<string>>([]);
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickSaveArchiveAsync(string suggestedFileName) => Task.FromResult<string?>(null);
    public Task<string?> PickEmulatorExecutableAsync(string emulatorName) =>
        Task.FromResult<string?>(null);

    public Task<string?> PickLibretroCoreAsync(string systemName) => Task.FromResult<string?>(null);
    public Task<string?> PickRpcs3ConfigurationDirectoryAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickCoverImageAsync(string gameTitle) =>
        Task.FromResult<string?>(null);
    public Task<bool> ConfirmRemoveGameAsync(string gameTitle) =>
        Task.FromResult(false);
    public Task<bool> ConfirmRemoveGamesAsync(int gameCount) =>
        Task.FromResult(false);
    public Task<MetadataConsentChoice> PromptForMetadataConsentAsync(int gameCount) =>
        Task.FromResult(MetadataConsentChoice.NotNow);
    public Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested) =>
        Task.FromResult<GameSystem?>(null);
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
        Func<HotkeySettingsContext?>? createHotkeyContext = null) => Task.CompletedTask;
    public Task ShowAchievementDetailsAsync(string gameTitle, int retroAchievementsGameId) =>
        Task.CompletedTask;
}

internal sealed class NullEmulatorConfigurationStore : IEmulatorConfigurationStore
{
    public EmulatorConfiguration? Get(string systemId) => null;
    public void Save(EmulatorConfiguration configuration) { }
    public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations) { }
}

internal sealed class NullEmulatorLaunchService : IEmulatorLaunchService
{
    public Task<GameLaunchResult> LaunchAsync(
        Game game,
        string? displayName = null,
        Func<CancellationToken, Task>? beforeStart = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GameLaunchResult(false, "Emulator launching is unavailable."));
}

internal sealed class NullFileRevealService : IFileRevealService
{
    public Task RevealAsync(string path, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task OpenDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
