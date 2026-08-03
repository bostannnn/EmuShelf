using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
/// Avalonia implementation of <see cref="IDialogService"/>. Resolves the main window
/// from the desktop lifetime so it doesn't need to be wired up after construction.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
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
        IClassicDesktopStyleApplicationLifetime lifetime,
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
        _lifetime = lifetime;
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

    private Window? Owner => _lifetime.MainWindow;
    private Window? _activeDialog;

    private TopLevel? PickerOwner => _activeDialog ?? Owner;

    public async Task<IReadOnlyList<string>> PickGameFilesAsync()
    {
        var owner = Owner;
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
        var owner = Owner;
        if (owner is null)
            return null;

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Add a games folder",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickEmulatorExecutableAsync(string emulatorName)
    {
        var owner = PickerOwner;
        if (owner is null)
            return null;

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

    public async Task<string?> PickGoogleClientJsonAsync()
    {
        var owner = Owner;
        if (owner is null)
            return null;

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the OAuth client JSON downloaded from Google",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Google OAuth client")
                {
                    Patterns = ["client_secret*.json", "*.json"],
                    AppleUniformTypeIdentifiers = ["public.json"],
                    MimeTypes = ["application/json"],
                },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
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
        var owner = Owner;
        if (owner is null)
            return null;
        if (_artworkSearch is null || _artworkDownloader is null)
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
        var owner = Owner;
        if (owner is null)
            return false;

        var viewModel = new RemoveGameViewModel(gameTitle);
        var dialog = new RemoveGameWindow { DataContext = viewModel };
        viewModel.CloseRequested += confirmed => dialog.Close(confirmed);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<bool> ConfirmRemoveGamesAsync(int gameCount)
    {
        var owner = Owner;
        if (owner is null)
            return false;

        var viewModel = new RemoveGameViewModel(gameCount);
        var dialog = new RemoveGameWindow { DataContext = viewModel };
        viewModel.CloseRequested += confirmed => dialog.Close(confirmed);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<MetadataConsentChoice> PromptForMetadataConsentAsync(int gameCount)
    {
        var owner = Owner;
        if (owner is null)
            return MetadataConsentChoice.NotNow;

        var viewModel = new MetadataConsentViewModel(gameCount);
        var dialog = new MetadataConsentWindow { DataContext = viewModel };
        viewModel.CloseRequested += choice => dialog.Close(choice);
        return await dialog.ShowDialog<MetadataConsentChoice>(owner);
    }

    public async Task<GameSystem?> PickSystemAsync(IReadOnlyList<GameSystem> systems, GameSystem? suggested)
    {
        var owner = Owner;
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
        IReadOnlyList<ThemeChoiceViewModel>? themeChoices = null)
    {
        var owner = Owner;
        if (owner is null)
            return;

        var configured = await Task.Run(() => systems.ToDictionary(
            system => system.Id,
            system => configurations.Get(system.Id),
            StringComparer.Ordinal));
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
            themeChoices);
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
        var owner = Owner;
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
        var owner = Owner;
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
        var owner = Owner;
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
