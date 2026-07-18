using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Systems;
using EmuShelf.Integrations.Emulators;

namespace EmuShelf.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const int SearchDebounceMs = 250;

    private readonly IGameLibrary _library;
    private readonly IFolderScanner _scanner;
    private readonly IGameImportRules _importRules;
    private readonly IAvailabilityChecker _availabilityChecker;
    private readonly IDialogService _dialogs;
    private readonly IEmulatorLaunchService _launchService;
    private readonly IEmulatorConfigurationStore _emulatorConfigurations;
    private readonly IReadOnlyList<EmulatorDefinition> _emulators;
    private readonly IGameCoverService _covers;
    private readonly IAppThemeService _themeService;
    private readonly IGameMetadataService _metadataService;
    private readonly IMetadataPreferencesService _metadataPreferences;
    private readonly IRetroAchievementsIdentificationService? _retroAchievements;
    private readonly IAppLogger _logger;
    private readonly IReadOnlyDictionary<string, GameSystem> _systemsById;

    private readonly DispatcherTimer _searchDebounce;
    private readonly List<GameViewModel> _systemGames = [];
    private readonly HashSet<long> _deferredCoverLoads = [];
    private bool _isFrontendSuspended;
    private string _appliedSearchText = string.Empty;

    // Bumped on every reload so a slow load that finishes after a newer one is discarded,
    // keeping the shown games in sync with the current selection.
    private int _loadGeneration;
    private Task _selectedSystemLoad = Task.CompletedTask;

    public ObservableCollection<GameSystem> Systems { get; }
    public BulkObservableCollection<GameViewModel> Games { get; } = [];

    [ObservableProperty]
    public partial GameSystem? SelectedSystem { get; set; }

    [ObservableProperty]
    public partial LibraryScope CurrentLibraryScope { get; set; } = LibraryScope.System;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsGridView { get; set; } = true;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSearchOpen { get; set; }

    [ObservableProperty]
    public partial string LibraryCountText { get; set; } = "0 games";

    /// <summary>True when the current filter yields at least one game (drives the views).</summary>
    [ObservableProperty]
    public partial bool HasGames { get; set; }

    /// <summary>True only when the selected system has no games at all — drives the "add your first game" prompt.</summary>
    [ObservableProperty]
    public partial bool IsLibraryEmpty { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSearchEmpty { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial ThemePreference CurrentTheme { get; set; }

    [ObservableProperty]
    public partial GameViewModel? SelectedGame { get; set; }

    public bool IsSystemTheme => CurrentTheme == ThemePreference.System;
    public bool IsLightTheme => CurrentTheme == ThemePreference.Light;
    public bool IsDarkTheme => CurrentTheme == ThemePreference.Dark;
    public bool IsAllGamesSelected => CurrentLibraryScope == LibraryScope.AllGames;
    public bool IsRecentlyAddedSelected => CurrentLibraryScope == LibraryScope.RecentlyAdded;
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusText);
    public string LibraryTitle => CurrentLibraryScope switch
    {
        LibraryScope.AllGames => "All Games",
        LibraryScope.RecentlyAdded => "Recently Added",
        _ => SelectedSystem?.Name ?? "Library",
    };
    public string LibraryShortName => CurrentLibraryScope switch
    {
        LibraryScope.AllGames => "ALL",
        LibraryScope.RecentlyAdded => "NEW",
        _ => SelectedSystem?.ShortName ?? "LIB",
    };
    public string LibraryAccentColor => SelectedSystem?.AccentColor ?? "#E04B52";
    public string EmptyLibraryTitle => CurrentLibraryScope switch
    {
        LibraryScope.AllGames => "Your game library is empty",
        LibraryScope.RecentlyAdded => "No recently added games",
        _ => $"Your {SelectedSystem?.Name ?? "game"} library is empty",
    };
    public string EmptyLibraryDescription => CurrentLibraryScope == LibraryScope.RecentlyAdded
        ? "Games you import will appear here in newest-first order."
        : "Add game files or a dedicated folder to begin building this shelf.";
    public string ThemeDescription => CurrentTheme switch
    {
        ThemePreference.Light => "Light appearance",
        ThemePreference.Dark => "Dark appearance",
        _ => "Follow system appearance",
    };

    /// <summary>Design-time / fallback constructor. The real app injects services.</summary>
    public MainViewModel()
        : this(
            new EmptyGameLibrary(),
            new NullFolderScanner(),
            new NoImportRules(),
            new AlwaysAvailableChecker(),
            new NullDialogService(),
            KnownSystems.All)
    {
    }

    public MainViewModel(
        IGameLibrary library,
        IFolderScanner scanner,
        IGameImportRules importRules,
        IAvailabilityChecker availabilityChecker,
        IDialogService dialogs,
        IReadOnlyList<GameSystem> systems,
        IEmulatorLaunchService? launchService = null,
        IEmulatorConfigurationStore? emulatorConfigurations = null,
        IReadOnlyList<EmulatorDefinition>? emulators = null,
        IGameCoverService? covers = null,
        IAppThemeService? themeService = null,
        IGameMetadataService? metadataService = null,
        IMetadataPreferencesService? metadataPreferences = null,
        IAppLogger? logger = null,
        IRetroAchievementsIdentificationService? retroAchievements = null)
    {
        _library = library;
        _scanner = scanner;
        _importRules = importRules;
        _availabilityChecker = availabilityChecker;
        _dialogs = dialogs;
        _launchService = launchService ?? new NullEmulatorLaunchService();
        _emulatorConfigurations = emulatorConfigurations ?? new NullEmulatorConfigurationStore();
        _emulators = emulators ?? KnownEmulators.All;
        _covers = covers ?? new NullGameCoverService();
        _themeService = themeService ?? new NullAppThemeService();
        _metadataService = metadataService ?? new NullGameMetadataService();
        _metadataPreferences = metadataPreferences ?? new NullMetadataPreferencesService();
        _retroAchievements = retroAchievements;
        _logger = logger ?? NullAppLogger.Instance;
        CurrentTheme = _themeService.Current;

        Systems = new ObservableCollection<GameSystem>(systems);
        _systemsById = systems.ToDictionary(system => system.Id, StringComparer.Ordinal);

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ApplyFilter();
        };

        SelectedSystem = Systems.FirstOrDefault();
    }

    partial void OnSelectedSystemChanged(GameSystem? value)
    {
        if (value is not null)
            CurrentLibraryScope = LibraryScope.System;
        NotifyLibraryPresentationChanged();
        _selectedSystemLoad = ReloadGamesAsync();
    }

    partial void OnCurrentLibraryScopeChanged(LibraryScope value)
    {
        OnPropertyChanged(nameof(IsAllGamesSelected));
        OnPropertyChanged(nameof(IsRecentlyAddedSelected));
        NotifyLibraryPresentationChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    partial void OnStatusTextChanged(string value) =>
        OnPropertyChanged(nameof(HasStatusMessage));

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        _searchDebounce.Stop();
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearStatus() => StatusText = string.Empty;

    [RelayCommand]
    private Task ShowAllGamesAsync() => ShowCollectionAsync(LibraryScope.AllGames);

    [RelayCommand]
    private Task ShowRecentlyAddedAsync() => ShowCollectionAsync(LibraryScope.RecentlyAdded);

    private async Task ShowCollectionAsync(LibraryScope scope)
    {
        CurrentLibraryScope = scope;
        if (SelectedSystem is not null)
        {
            SelectedSystem = null;
            await _selectedSystemLoad;
        }
        else
        {
            await ReloadGamesAsync();
        }
    }

    private void NotifyLibraryPresentationChanged()
    {
        OnPropertyChanged(nameof(LibraryTitle));
        OnPropertyChanged(nameof(LibraryShortName));
        OnPropertyChanged(nameof(LibraryAccentColor));
        OnPropertyChanged(nameof(EmptyLibraryTitle));
        OnPropertyChanged(nameof(EmptyLibraryDescription));
    }

    partial void OnCurrentThemeChanged(ThemePreference value)
    {
        OnPropertyChanged(nameof(IsSystemTheme));
        OnPropertyChanged(nameof(IsLightTheme));
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(ThemeDescription));
    }

    partial void OnSelectedGameChanged(GameViewModel? oldValue, GameViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
            newValue.IsSelected = true;
    }

    internal async Task ReloadGamesAsync()
    {
        var system = SelectedSystem;
        var scope = CurrentLibraryScope;
        if (scope == LibraryScope.System && system is null)
            return;

        var generation = ++_loadGeneration;
        try
        {
            var artworkBySystem = (scope == LibraryScope.System && system is not null
                    ? [system.Id]
                    : _systemsById.Keys)
                .ToDictionary(
                    systemId => systemId,
                    PlatformArtwork.ForSystem,
                    StringComparer.Ordinal);
            var games = await Task.Run(() =>
            {
                var loaded = scope switch
                {
                    LibraryScope.RecentlyAdded => _library.GetRecentlyAddedGames(30),
                    LibraryScope.System => _library.GetGames(system!.Id),
                    _ => _library.GetGames(),
                };

                var viewModels = new List<GameViewModel>(loaded.Count);
                foreach (var game in loaded)
                {
                    if (!_systemsById.TryGetValue(game.SystemId, out var gameSystem))
                        continue;

                    artworkBySystem.TryGetValue(game.SystemId, out var artwork);
                    viewModels.Add(new GameViewModel(
                        game,
                        gameSystem.Name,
                        gameSystem.ShortName,
                        gameSystem.AccentColor,
                        LaunchGameCommand,
                        SaveGameTitleCommand,
                        SetGameCoverCommand,
                        RemoveGameCommand,
                        LoadGameCoverCommand,
                        artwork,
                        gameSystem.CoverAspectRatio));
                }
                return viewModels;
            });

            // A newer reload (system switch, or the post-availability refresh) started while we
            // were reading — discard this stale result so it can't overwrite the current view.
            if (generation != _loadGeneration)
            {
                foreach (var game in games)
                    game.Dispose();
                return;
            }

            SelectedGame = null;
            foreach (var existingGame in _systemGames)
                existingGame.Dispose();
            _systemGames.Clear();
            _systemGames.AddRange(games);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger.Error("Could not load the current library view.", ex);
            StatusText = $"Could not load library: {ex.Message}";
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadGameCoverAsync(GameViewModel? game)
    {
        if (game is null || game.CoverPath is null || game.HasCoverImage || game.IsCoverLoading)
            return;

        if (_isFrontendSuspended)
        {
            _deferredCoverLoads.Add(game.Id);
            return;
        }

        var generation = _loadGeneration;
        var coverPath = game.CoverPath;
        var coverRevision = game.CoverRevision;
        game.IsCoverLoading = true;
        try
        {
            var thumbnailPath = await _covers.GetThumbnailAsync(game.Id, coverPath);
            if (thumbnailPath is null)
                return;

            var image = await Task.Run(() => new Bitmap(thumbnailPath));
            if (generation == _loadGeneration &&
                coverRevision == game.CoverRevision &&
                _systemGames.Contains(game) &&
                !_isFrontendSuspended)
            {
                game.CoverImage = image;
            }
            else
            {
                image.Dispose();
                if (_isFrontendSuspended)
                    _deferredCoverLoads.Add(game.Id);
            }
        }
        catch (Exception ex)
        {
            if (generation == _loadGeneration &&
                coverRevision == game.CoverRevision &&
                _systemGames.Contains(game))
            {
                if (_isFrontendSuspended)
                {
                    _deferredCoverLoads.Add(game.Id);
                    return;
                }
                _logger.Warning($"Could not load the cover for game id {game.Id}.", ex);
                StatusText = $"Could not load cover for {game.Title}: {ex.Message}";
            }
        }
        finally
        {
            game.IsCoverLoading = false;
        }
    }

    internal void ApplyFilter()
    {
        var query = SearchText.Trim();
        _appliedSearchText = query;
        IEnumerable<GameViewModel> filtered = _systemGames;
        if (query.Length > 0)
            filtered = _systemGames.Where(g =>
                g.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

        Games.ReplaceAll(filtered);

        HasGames = Games.Count > 0;
        IsLibraryEmpty = _systemGames.Count == 0;
        IsSearchEmpty = _systemGames.Count > 0 && Games.Count == 0;
        LibraryCountText = _systemGames.Count == 1 ? "1 game" : $"{_systemGames.Count} games";
        NotifyLibraryPresentationChanged();
    }

    [RelayCommand]
    private async Task AddGamesAsync()
    {
        if (IsBusy)
            return;

        var paths = await _dialogs.PickGameFilesAsync();
        if (paths.Count == 0)
            return;

        var previousStatus = StatusText;
        IsBusy = true;
        try
        {
            StatusText = paths.Count == 1 ? "Inspecting game…" : $"Inspecting {paths.Count} files…";
            var analyses = await Task.Run(() => paths
                .Select(_importRules.AnalyzeFile)
                .ToList());

            var suggested = analyses
                .SelectMany(analysis => analysis.SuggestedSystems)
                .GroupBy(system => system.Id)
                .OrderByDescending(group => group.Count())
                .Select(group => group.First())
                .FirstOrDefault() ?? SelectedSystem;

            var system = await _dialogs.PickSystemAsync(Systems, suggested);
            if (system is null)
            {
                StatusText = previousStatus;
                return;
            }

            var accepted = analyses
                .Where(analysis => analysis.MatchFor(system.Id) is
                    GameFileMatch.Compatible or GameFileMatch.Unrecognized)
                .Select(analysis => analysis.Path)
                .ToList();
            var incompatible = analyses.Count(analysis =>
                analysis.MatchFor(system.Id) == GameFileMatch.Incompatible);
            var unsupported = analyses.Count(analysis =>
                analysis.MatchFor(system.Id) == GameFileMatch.Unsupported);
            var confirmedUnrecognized = analyses.Count(analysis =>
                analysis.MatchFor(system.Id) == GameFileMatch.Unrecognized);

            var selection = await Task.Run(() => _importRules.SelectGameEntries(accepted, system));
            var importResult = await ReconcileImportAsync(system, selection);
            await ShowSystemAsync(system);
            StatusText = BuildAddGamesStatus(
                importResult.AddedCount,
                incompatible,
                unsupported,
                confirmedUnrecognized,
                system.Name);
            await MaybeStartMetadataForImportAsync(importResult.AddedGameIds);
        }
        catch (Exception ex)
        {
            _logger.Error("Game import failed.", ex);
            StatusText = $"Import failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildAddGamesStatus(
        int added,
        int incompatible,
        int unsupported,
        int confirmedUnrecognized,
        string systemName)
    {
        var status = added == 1 ? "Added 1 game" : $"Added {added} games";
        var skipped = incompatible + unsupported;
        if (incompatible > 0 && unsupported == 0)
        {
            status += incompatible == 1
                ? $" — skipped 1 file not recognized as {systemName}"
                : $" — skipped {incompatible} files not recognized as {systemName}";
        }
        else if (unsupported > 0 && incompatible == 0)
        {
            status += unsupported == 1
                ? " — skipped 1 unsupported file"
                : $" — skipped {unsupported} unsupported files";
        }
        else if (skipped > 0)
        {
            status += $" — skipped {skipped} files " +
                $"({incompatible} not recognized as {systemName}, {unsupported} unsupported)";
        }

        if (confirmedUnrecognized > 0)
        {
            status += confirmedUnrecognized == 1
                ? $"; used confirmed {systemName} system for 1 unrecognized file"
                : $"; used confirmed {systemName} system for {confirmedUnrecognized} unrecognized files";
        }

        return status;
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (IsBusy)
            return;

        var folder = await _dialogs.PickFolderAsync();
        if (folder is null)
            return;

        var system = await _dialogs.PickSystemAsync(Systems, SelectedSystem);
        if (system is null)
            return;

        IsBusy = true;
        try
        {
            var progress = new Progress<ScanProgress>(p =>
                StatusText = $"Scanning {system.Name}… {p.CandidatesFound} found");

            var selection = await _scanner.ScanAsync(folder, system, progress);
            await Task.Run(() => _library.AddLibraryFolder(system.Id, folder));

            var importResult = await ReconcileImportAsync(system, selection);
            await ShowSystemAsync(system);
            StatusText = importResult.AddedCount == 1
                ? "Added 1 game from folder"
                : $"Added {importResult.AddedCount} games from folder";
            await MaybeStartMetadataForImportAsync(importResult.AddedGameIds);
        }
        catch (Exception ex)
        {
            _logger.Error($"Folder scan failed for system {system.Id}.", ex);
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task RescanSystemAsync() =>
        SelectedSystem is { } system ? RescanAsync([system], system) : Task.CompletedTask;

    [RelayCommand]
    private Task RescanAllAsync() => RescanAsync(Systems, SelectedSystem);

    private async Task<string> RescanSystemFromSettingsAsync(string systemId)
    {
        var system = Systems.FirstOrDefault(candidate => candidate.Id == systemId);
        if (system is null)
            return "That console is no longer available.";

        await RescanAsync([system], SelectedSystem);
        return StatusText;
    }

    private async Task<string> RescanAllFromSettingsAsync()
    {
        await RescanAsync(Systems, SelectedSystem);
        return StatusText;
    }

    private async Task RescanAsync(IEnumerable<GameSystem> systems, GameSystem? systemToShow)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var total = 0;
            var addedIds = new List<long>();
            foreach (var system in systems)
            {
                var folders = await Task.Run(() => _library.GetLibraryFolders(system.Id));
                foreach (var folder in folders)
                {
                    var progress = new Progress<ScanProgress>(p =>
                        StatusText = $"Rescanning {system.Name}… {p.CandidatesFound} found");
                    var selection = await _scanner.ScanAsync(folder.Path, system, progress);
                    var importResult = await ReconcileImportAsync(system, selection);
                    total += importResult.AddedCount;
                    addedIds.AddRange(importResult.AddedGameIds);
                }
            }

            await UpdateAvailabilityAsync();
            if (systemToShow is not null)
                await ShowSystemAsync(systemToShow);
            else
                await ReloadGamesAsync();
            StatusText = total == 0 ? "Rescan complete — no new games" : $"Rescan added {total} game(s)";
            if (addedIds.Count > 0 && _metadataPreferences.AutomaticallyFetchAfterImport)
                _ = EnrichImportedGamesAsync(addedIds);
        }
        catch (Exception ex)
        {
            _logger.Error("Library rescan failed.", ex);
            StatusText = $"Rescan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<GameImportResult> ReconcileImportAsync(
        GameSystem system,
        GameEntrySelection selection)
    {
        if (selection.EntryPaths.Count == 0 && selection.SuppressedPaths.Count == 0)
            return GameImportResult.Empty;

        var now = DateTimeOffset.Now;
        var games = selection.EntryPaths.Select(path => new Game
        {
            SystemId = system.Id,
            Path = path,
            Title = System.IO.Path.GetFileNameWithoutExtension(path),
            TitleOrigin = GameTitleOrigin.Filename,
            IsAvailable = true,
            DateAdded = now,
        });

        return await Task.Run(() =>
            _library.ReconcileImport(system.Id, games, selection.SuppressedPaths));
    }

    private async Task ShowSystemAsync(GameSystem system)
    {
        if (SelectedSystem?.Id == system.Id)
            await ReloadGamesAsync();
        else
        {
            SelectedSystem = system;
            await _selectedSystemLoad;
        }
    }

    /// <summary>
    /// Startup/after-rescan availability pass: stat every game's path off the UI thread and
    /// persist changes, then reload the current view from the updated DB. No discovery scan.
    /// Reloading (rather than patching in place) makes the result independent of whether the
    /// initial load has finished — the generation-guarded reload always reflects the DB.
    /// </summary>
    public async Task RefreshAvailabilityAsync()
    {
        try
        {
            var changed = await UpdateAvailabilityAsync();

            if (changed > 0)
                await ReloadGamesAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Library availability check failed.", ex);
            StatusText = $"Availability check failed: {ex.Message}";
        }
    }

    private Task<int> UpdateAvailabilityAsync() => Task.Run(() =>
    {
        var updates = new List<GameAvailabilityUpdate>();
        foreach (var game in _library.GetGames())
        {
            var available = _availabilityChecker.IsAvailable(game);
            if (available != game.IsAvailable)
                updates.Add(new GameAvailabilityUpdate(game.Id, available));
        }
        _library.SetAvailabilities(updates);
        return updates.Count;
    });

    [RelayCommand]
    private async Task LaunchGameAsync(GameViewModel? game)
    {
        if (game is null || IsBusy)
            return;

        if (!game.IsAvailable)
        {
            StatusText = $"Cannot launch {game.Title}: its game file could not be found.";
            return;
        }

        IsBusy = true;
        StatusText = $"Launching {game.Title}…";
        SuspendFrontendUiWork();
        try
        {
            var result = await _launchService.LaunchAsync(game.Model);
            if (!result.Succeeded)
                _logger.Warning($"Launch did not start or complete successfully: {result.StatusText}");
            StatusText = result.StatusText;
        }
        catch (OperationCanceledException)
        {
            StatusText = $"Launch cancelled for {game.Title}";
        }
        catch (Exception ex)
        {
            _logger.Error($"Unexpected launch failure for game id {game.Id}.", ex);
            StatusText = $"Could not launch {game.Title}: {ex.Message}";
        }
        finally
        {
            ResumeFrontendUiWork();
            IsBusy = false;
        }
    }

    private void SuspendFrontendUiWork()
    {
        _isFrontendSuspended = true;
        _searchDebounce.Stop();
    }

    private void ResumeFrontendUiWork()
    {
        _isFrontendSuspended = false;
        if (!string.Equals(SearchText.Trim(), _appliedSearchText, StringComparison.Ordinal))
            ApplyFilter();

        if (_deferredCoverLoads.Count == 0)
            return;

        var pendingIds = _deferredCoverLoads.ToArray();
        _deferredCoverLoads.Clear();
        foreach (var gameId in pendingIds)
        {
            var game = _systemGames.FirstOrDefault(candidate => candidate.Id == gameId);
            if (game is not null)
                _ = LoadGameCoverCommand.ExecuteAsync(game);
        }
    }

    [RelayCommand]
    private async Task SaveGameTitleAsync(GameViewModel? game)
    {
        if (game is null || IsBusy)
            return;

        var title = game.DraftTitle.Trim();
        if (title.Length == 0)
        {
            StatusText = "A game title cannot be empty.";
            return;
        }

        if (string.Equals(title, game.Title, StringComparison.Ordinal))
        {
            game.CompleteTitleEdit(title);
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => _library.UpdateTitle(game.Id, title));
            game.CompleteTitleEdit(title);
            await ReloadGamesAsync();
            StatusText = $"Renamed game to {title}";
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not rename game id {game.Id}.", ex);
            StatusText = $"Could not rename {game.Title}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetGameCoverAsync(GameViewModel? game)
    {
        if (game is null || IsBusy)
            return;

        var sourcePath = await _dialogs.PickCoverImageAsync(game.Title);
        if (sourcePath is null)
            return;

        IsBusy = true;
        StatusText = $"Preparing cover for {game.Title}…";
        var previousCoverPath = game.CoverPath;
        try
        {
            var imported = await _covers.ImportAsync(game.Id, sourcePath);
            try
            {
                await Task.Run(() => _library.UpdateCoverPath(game.Id, imported.CoverPath));
            }
            catch
            {
                try
                {
                    await _covers.DeleteOwnedCoverAsync(game.Id, imported.CoverPath);
                }
                catch (Exception cleanupException)
                {
                    // The database still points to the previous cover. A failed best-effort
                    // cleanup can leave only an unreferenced EmuShelf-owned staged file.
                    _logger.Warning(
                        $"Could not remove an uncommitted cover for game id {game.Id}.",
                        cleanupException);
                }
                throw;
            }

            var currentGame = _systemGames.FirstOrDefault(candidate => candidate.Id == game.Id);
            if (currentGame is not null)
                currentGame.ApplyCoverPath(imported.CoverPath);

            Exception? previewFailure = null;
            if (currentGame is not null)
            {
                try
                {
                    var thumbnailPath = await _covers.GetThumbnailAsync(game.Id, imported.CoverPath)
                        ?? throw new IOException("The cached cover thumbnail is unavailable.");
                    var image = await Task.Run(() => new Bitmap(thumbnailPath));
                    var latestGame = _systemGames.FirstOrDefault(candidate => candidate.Id == game.Id);
                    if (latestGame is not null &&
                        string.Equals(latestGame.CoverPath, imported.CoverPath, StringComparison.Ordinal))
                    {
                        latestGame.CoverImage = image;
                    }
                    else
                    {
                        image.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Could not preview the new cover for game id {game.Id}.", ex);
                    previewFailure = ex;
                }
            }

            Exception? cleanupFailure = null;
            if (previousCoverPath is not null &&
                !string.Equals(previousCoverPath, imported.CoverPath, StringComparison.Ordinal))
            {
                try
                {
                    await _covers.DeleteOwnedCoverAsync(game.Id, previousCoverPath);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Could not remove the previous cover for game id {game.Id}.", ex);
                    cleanupFailure = ex;
                }
            }

            var warnings = new List<string>();
            if (previewFailure is not null)
                warnings.Add($"the preview could not be loaded: {previewFailure.Message}");
            if (cleanupFailure is not null)
                warnings.Add($"the previous EmuShelf cover could not be removed: {cleanupFailure.Message}");

            StatusText = warnings.Count == 0
                ? $"Updated cover for {game.Title}"
                : $"Updated cover for {game.Title}, but {string.Join("; ", warnings)}";
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not set a cover for game id {game.Id}.", ex);
            StatusText = $"Could not set cover for {game.Title}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemoveGameAsync(GameViewModel? game)
    {
        if (game is null || IsBusy ||
            !await _dialogs.ConfirmRemoveGameAsync(game.Title))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await Task.Run(() => _library.RemoveGame(game.Id));
            await ReloadGamesAsync();
            StatusText = $"Removed {game.Title} from the library — game files were not touched";
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not remove game id {game.Id} from the library.", ex);
            StatusText = $"Could not remove {game.Title}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        try
        {
            await _dialogs.ShowEmulatorSettingsAsync(
                Systems,
                _emulators,
                _emulatorConfigurations,
                new LibraryMaintenanceActions(
                    RescanSystemFromSettingsAsync,
                    RescanAllFromSettingsAsync,
                    FetchMetadataForSystemFromSettingsAsync,
                    FetchAllMetadataFromSettingsAsync),
                _metadataPreferences);
        }
        catch (Exception ex)
        {
            _logger.Error("Could not open emulator settings.", ex);
            StatusText = $"Could not open emulator settings: {ex.Message}";
        }
    }

    private async Task MaybeStartMetadataForImportAsync(IReadOnlyList<long> addedGameIds)
    {
        if (addedGameIds.Count == 0)
            return;

        // Local RetroAchievements hashing is independent of network-metadata consent and
        // runs quietly in the background so links are ready when the feature surfaces.
        if (_retroAchievements is not null)
            _ = IdentifyForRetroAchievementsAsync(addedGameIds);

        var shouldFetch = _metadataPreferences.AutomaticallyFetchAfterImport;
        if (!shouldFetch && !_metadataPreferences.ConsentPromptShown)
        {
            var choice = await _dialogs.PromptForMetadataConsentAsync(addedGameIds.Count);
            shouldFetch = choice is MetadataConsentChoice.FetchOnce or MetadataConsentChoice.Always;
            try
            {
                await _metadataPreferences.RecordConsentAsync(choice);
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not persist the metadata consent preference.", ex);
                StatusText += " — metadata preference could not be saved";
            }
        }

        if (shouldFetch)
            _ = EnrichImportedGamesAsync(addedGameIds);
    }

    private async Task EnrichImportedGamesAsync(IReadOnlyList<long> gameIds)
    {
        try
        {
            StatusText = gameIds.Count == 1
                ? "Fetching metadata for 1 new game…"
                : $"Fetching metadata for {gameIds.Count} new games…";
            var summary = await _metadataService.EnrichAsync(gameIds);
            await ReloadGamesAsync();
            StatusText = summary.ToStatusText();
        }
        catch (Exception ex)
        {
            _logger.Error("Automatic metadata enrichment failed.", ex);
            StatusText = $"Metadata failed: {ex.Message}";
        }
    }

    private async Task IdentifyForRetroAchievementsAsync(IReadOnlyList<long> gameIds)
    {
        try
        {
            await _retroAchievements!.IdentifyAsync(gameIds);
        }
        catch (Exception ex)
        {
            _logger.Warning("RetroAchievements identification for imported games failed.", ex);
        }
    }

    private Task<string> FetchMetadataForSystemFromSettingsAsync(string systemId) =>
        FetchMissingMetadataFromSettingsAsync(systemId);

    private Task<string> FetchAllMetadataFromSettingsAsync() =>
        FetchMissingMetadataFromSettingsAsync(null);

    private async Task<string> FetchMissingMetadataFromSettingsAsync(string? systemId)
    {
        // Clicking a manual fetch is itself an explicit one-time opt-in. Remember that
        // decision so the first-import prompt is not shown later for the same user.
        if (!_metadataPreferences.ConsentPromptShown)
            await _metadataPreferences.RecordConsentAsync(MetadataConsentChoice.FetchOnce);

        var summary = await _metadataService.EnrichMissingAsync(systemId);
        await ReloadGamesAsync();
        StatusText = summary.ToStatusText();
        return StatusText;
    }

    [RelayCommand]
    private async Task SetThemeAsync(ThemePreference preference)
    {
        if (preference == CurrentTheme)
            return;

        try
        {
            await _themeService.SetThemeAsync(preference);
            CurrentTheme = _themeService.Current;
            StatusText = $"Appearance set to {preference.ToString().ToLowerInvariant()}";
        }
        catch (Exception ex)
        {
            _logger.Error("Could not persist the appearance preference.", ex);
            CurrentTheme = _themeService.Current;
            StatusText = $"Appearance changed for this session, but could not be saved: {ex.Message}";
        }
    }
}
