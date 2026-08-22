using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Achievements;
using EmuShelf.App.Views;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Systems;

namespace EmuShelf.App.Services;

/// <summary>
/// Avalonia implementation of <see cref="IDialogService"/>. Owned by a <see cref="TopLevel"/> — the
/// desktop head passes its main window — so the file/folder pickers work under any Avalonia host,
/// including a future single-view (Android) surface. The modal dialogs still need a
/// <see cref="Window"/> parent and so remain desktop-only.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly TopLevel _owner;
    private readonly IAppLogger _logger;
    private readonly IRetroAchievementsDetailsService? _retroAchievementsDetails;
    private readonly IRetroAchievementsAccountService? _retroAchievementsAccount;
    private readonly IRetroAchievementsBadgeCache? _retroAchievementsBadges;
    private readonly IGameArtworkSearchProvider? _artworkSearch;
    private readonly IRemoteArtworkDownloader? _artworkDownloader;
    private readonly IScreenScraperPreviewService? _screenScraperPreview;
    private readonly IGameScrapeApplicationService? _scrapeApply;
    private readonly IScreenScraperAccountService? _screenScraperAccount;
    private readonly IScreenScraperBatchService? _batchScraper;
    private readonly ISettingsService? _settingsService;

    public DialogService(
        TopLevel owner,
        IAppLogger? logger = null,
        IRetroAchievementsDetailsService? retroAchievementsDetails = null,
        IRetroAchievementsAccountService? retroAchievementsAccount = null,
        IRetroAchievementsBadgeCache? retroAchievementsBadges = null,
        IGameArtworkSearchProvider? artworkSearch = null,
        IRemoteArtworkDownloader? artworkDownloader = null,
        IScreenScraperPreviewService? screenScraperPreview = null,
        IGameScrapeApplicationService? scrapeApply = null,
        IScreenScraperAccountService? screenScraperAccount = null,
        IScreenScraperBatchService? batchScraper = null,
        ISettingsService? settingsService = null)
    {
        _owner = owner;
        _logger = logger ?? NullAppLogger.Instance;
        _retroAchievementsDetails = retroAchievementsDetails;
        _retroAchievementsAccount = retroAchievementsAccount;
        _retroAchievementsBadges = retroAchievementsBadges;
        _artworkSearch = artworkSearch;
        _artworkDownloader = artworkDownloader;
        _screenScraperPreview = screenScraperPreview;
        _scrapeApply = scrapeApply;
        _screenScraperAccount = screenScraperAccount;
        _batchScraper = batchScraper;
        _settingsService = settingsService;
    }

    private Window? _activeDialog;

    /// <summary>Top level that owns the file/folder pickers — an active modal if one is open,
    /// otherwise the surface's top level. A <see cref="TopLevel"/> so a single-view host can drive
    /// the same pickers as a window.</summary>
    private TopLevel? PickerOwner => (TopLevel?)_activeDialog ?? _owner;

    /// <summary>Window that parents the modal dialogs. Desktop-only: null when the host is not a
    /// window, in which case the modal-dialog methods no-op (their existing null guard).</summary>
    private Window? DialogOwner => _activeDialog ?? _owner as Window;

    public async Task<IReadOnlyList<string>> PickGameFilesAsync()
    {
        var owner = PickerOwner;
        if (owner is null)
            return [];

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add games",
            AllowMultiple = true,
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
    }

    public async Task<string?> PickFolderAsync()
    {
        var owner = PickerOwner;
        if (owner is null)
            return null;

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a games folder",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveArchiveAsync(string suggestedFileName)
    {
        var owner = PickerOwner;
        if (owner is null)
            return null;

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export saves",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "zip",
            ShowOverwritePrompt = true,
            FileTypeChoices =
            [
                new FilePickerFileType("Zip archive")
                {
                    Patterns = ["*.zip"],
                    AppleUniformTypeIdentifiers = ["public.zip-archive"],
                    MimeTypes = ["application/zip"],
                },
            ],
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickEmulatorExecutableAsync(string emulatorName)
    {
        var owner = PickerOwner;
        if (owner is null)
            return null;

        // Avalonia's macOS open panel navigates into `.app` bundles instead of selecting them, so an
        // emulator shipped as a bundle (which is every macOS emulator) can't be chosen with the
        // cross-platform picker. Use a native NSOpenPanel that keeps bundles selectable; fall back to
        // the Avalonia picker only if the native panel is genuinely unavailable.
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                return MacOpenPanel.ChooseEmulator();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Native macOS open panel failed; using the standard picker. {ex.Message}");
            }
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {emulatorName} executable",
            AllowMultiple = false,
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickLibretroCoreAsync(string systemName)
    {
        var owner = PickerOwner;
        if (owner is null)
            return null;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Select {systemName} RetroArch core",
            AllowMultiple = false,
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickRpcs3ConfigurationDirectoryAsync()
    {
        var owner = PickerOwner;
        if (owner is null)
            return null;

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select RPCS3 configuration folder (contains games.yml)",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickCoverImageAsync(string gameTitle)
    {
        var owner = PickerOwner;
        if (owner is null)
            return null;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Choose cover for {gameTitle}",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Image files")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"],
                    AppleUniformTypeIdentifiers = ["public.image"],
                    MimeTypes = ["image/*"],
                },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<PickedGameCover?> PickGameCoverAsync(GameCoverPickerContext context)
    {
        var owner = DialogOwner;
        if (owner is null)
            return null;
        // When web image search is turned off (Settings → Artwork & Metadata), or no search provider is
        // wired, "Set cover" is a plain local-file pick — no web results are offered.
        var webSearchEnabled = _settingsService?.Load().Scraping.WebImageSearchEnabled ?? true;
        if (_artworkSearch is null || _artworkDownloader is null || !webSearchEnabled)
        {
            var path = await PickCoverImageAsync(context.GameTitle);
            return path is null ? null : new PickedGameCover(path);
        }

        var viewModel = new CoverSearchViewModel(
            context,
            _artworkSearch,
            _artworkDownloader,
            () => PickCoverImageAsync(context.GameTitle),
            _logger);
        var dialog = new CoverSearchWindow { DataContext = viewModel };
        viewModel.CloseRequested += result => dialog.Close(result);
        _activeDialog = dialog;
        try
        {
            return await dialog.ShowDialog<PickedGameCover?>(owner);
        }
        finally
        {
            _activeDialog = null;
            viewModel.Dispose();
        }
    }

    public async Task<bool> ConfirmRemoveGameAsync(string gameTitle)
    {
        var owner = DialogOwner;
        if (owner is null)
            return false;

        var viewModel = new RemoveGameViewModel(gameTitle);
        var dialog = new RemoveGameWindow { DataContext = viewModel };
        viewModel.CloseRequested += confirmed => dialog.Close(confirmed);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<bool> ConfirmRemoveGamesAsync(int gameCount)
    {
        var owner = DialogOwner;
        if (owner is null)
            return false;

        var viewModel = new RemoveGameViewModel(gameCount);
        var dialog = new RemoveGameWindow { DataContext = viewModel };
        viewModel.CloseRequested += confirmed => dialog.Close(confirmed);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<bool> ConfirmRescanRemovalsAsync(IReadOnlyList<string> gameTitles)
    {
        var owner = DialogOwner;
        if (owner is null || gameTitles.Count == 0)
            return false;

        var viewModel = new RescanRemovalsViewModel(gameTitles);
        var dialog = new RescanRemovalsWindow { DataContext = viewModel };
        viewModel.CloseRequested += confirmed => dialog.Close(confirmed);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<MetadataConsentChoice> PromptForMetadataConsentAsync(int gameCount)
    {
        var owner = DialogOwner;
        if (owner is null)
            return MetadataConsentChoice.NotNow;

        var viewModel = new MetadataConsentViewModel(gameCount);
        var dialog = new MetadataConsentWindow { DataContext = viewModel };
        viewModel.CloseRequested += choice => dialog.Close(choice);
        return await dialog.ShowDialog<MetadataConsentChoice>(owner);
    }

    public async Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested)
    {
        var owner = DialogOwner;
        if (owner is null)
            return null;

        var dialog = new SystemPickerWindow(systems, suggested);
        return await dialog.ShowDialog<GameSystem?>(owner);
    }

    public async Task ShowEmulatorSettingsAsync(
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
        var owner = DialogOwner;
        if (owner is null)
            return;

        var systemIds = systems.Select(system => system.Id).ToArray();
        var folderReader = maintenance.Folders?.GetAll;
        // One worker pass for every database read the panel needs: active configs, all profiles, and
        // every system's remembered folders. Building the rows on the UI thread afterwards no longer
        // opens a connection per system — the cold-open "slight delay" on the settings button.
        var (configured, profiles, libraryFolders, hotkeys) = await Task.Run(() =>
            (configurations.GetAll(systemIds),
             configurations.GetAllProfiles(systemIds),
             EmulatorSettingsViewModel.GroupLibraryFolders(folderReader?.Invoke()),
             createHotkeyContext?.Invoke()));
        var viewModel = new EmulatorSettingsViewModel(
            systems,
            emulators,
            configured,
            configurations,
            this,
            maintenance,
            metadataPreferences,
            _logger,
            retroAchievements,
            cloudSaves,
            texturePacks,
            screenScraper,
            hotkeys,
            themeChoices,
            ambientThemeFromArtwork,
            setAmbientThemeFromArtwork,
            profiles,
            updates,
            libraryFolders);
        var dialog = new EmulatorSettingsWindow { DataContext = viewModel };
        viewModel.CloseRequested += saved => dialog.Close(saved);

        _activeDialog = dialog;
        try
        {
            await dialog.ShowDialog<bool>(owner);
        }
        finally
        {
            _activeDialog = null;
        }
    }

    public async Task<bool> ShowScraperAsync(long gameId, string gameTitle)
    {
        var owner = DialogOwner;
        if (owner is null || _screenScraperPreview is null || _scrapeApply is null ||
            _screenScraperAccount is null || _settingsService is null)
        {
            return false;
        }

        var settings = _settingsService.Load().Scraping.ScreenScraper;
        var viewModel = new GameScraperViewModel(
            gameId, gameTitle, _screenScraperPreview, _scrapeApply, _screenScraperAccount, settings,
            _artworkDownloader, _logger);
        var dialog = new ScraperWindow { DataContext = viewModel };
        viewModel.CloseRequested += result => dialog.Close(result is not null);
        dialog.Opened += (_, _) => _ = viewModel.LoadAsync();

        _activeDialog = dialog;
        try
        {
            return await dialog.ShowDialog<bool>(owner);
        }
        finally
        {
            _activeDialog = null;
            viewModel.Dispose();
        }
    }

    public async Task<bool> ShowBatchScraperAsync(IReadOnlyList<long> gameIds, string systemName)
    {
        var owner = DialogOwner;
        if (owner is null || _batchScraper is null || _settingsService is null || gameIds.Count == 0)
            return false;

        var settings = _settingsService.Load().Scraping.ScreenScraper;
        var viewModel = new GameBatchScraperViewModel(gameIds, systemName, _batchScraper, settings, _logger);
        var dialog = new BatchScraperWindow { DataContext = viewModel };
        viewModel.CloseRequested += () => dialog.Close(viewModel.AppliedChanges);

        _activeDialog = dialog;
        try
        {
            return await dialog.ShowDialog<bool>(owner);
        }
        finally
        {
            _activeDialog = null;
        }
    }

    public async Task ShowAchievementDetailsAsync(string gameTitle, int retroAchievementsGameId)
    {
        var owner = DialogOwner;
        if (owner is null || _retroAchievementsDetails is null || _retroAchievementsAccount is null)
            return;

        // Reading the small SQLite cache happens before the window opens, on a worker, so the
        // first frame is already useful even if the optional API refresh is unavailable.
        var cached = await Task.Run(() => _retroAchievementsDetails.GetCached(retroAchievementsGameId));
        var viewModel = new AchievementDetailsViewModel(
            gameTitle,
            retroAchievementsGameId,
            _retroAchievementsDetails,
            _retroAchievementsAccount,
            _retroAchievementsBadges,
            cached,
            logger: _logger);
        var dialog = new AchievementDetailsWindow { DataContext = viewModel };
        viewModel.CloseRequested += dialog.Close;
        dialog.Opened += (_, _) => _ = viewModel.RefreshIfStaleAsync();
        dialog.Closed += (_, _) => viewModel.Dispose();

        _activeDialog = dialog;
        try
        {
            await dialog.ShowDialog(owner);
        }
        finally
        {
            _activeDialog = null;
        }
    }
}
