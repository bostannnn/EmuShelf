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
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Systems;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Rpcs3;

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
    private readonly IGameMetadataStore? _metadataStore;
    private readonly IMetadataPreferencesService _metadataPreferences;
    private readonly IRetroAchievementsIdentificationService? _retroAchievements;
    private readonly IRetroAchievementsReadStore? _retroAchievementsRead;
    private readonly IRetroAchievementsAccountService? _retroAccount;
    private readonly IRetroAchievementsMatchingService? _retroMatching;
    private readonly IRetroAchievementsProgressService? _retroProgress;
    private readonly IRetroAchievementsDetailsService? _retroDetails;
    private readonly IRetroAchievementsRefreshService? _retroRefresh;
    // Coordinates the full identify → match → progress sequence. Individual services also
    // serialize their own work, but this prevents an import finishing halfway through a connect
    // and leaving newly hashed games unmatched.
    private readonly SemaphoreSlim _retroAchievementsPipeline = new(1, 1);
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
        : SelectedSystem?.Id == "playstation3"
            ? "Sync the explicitly selected RPCS3 library from Settings to add PlayStation 3 games."
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
        IRetroAchievementsIdentificationService? retroAchievements = null,
        IRetroAchievementsReadStore? retroAchievementsRead = null,
        IRetroAchievementsAccountService? retroAccount = null,
        IRetroAchievementsMatchingService? retroMatching = null,
        IRetroAchievementsProgressService? retroProgress = null,
        IRetroAchievementsDetailsService? retroDetails = null,
        IRetroAchievementsRefreshService? retroRefresh = null,
        IGameMetadataStore? metadataStore = null)
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
        _metadataStore = metadataStore;
        _metadataPreferences = metadataPreferences ?? new NullMetadataPreferencesService();
        _retroAchievements = retroAchievements;
        _retroAchievementsRead = retroAchievementsRead;
        _retroAccount = retroAccount;
        _retroMatching = retroMatching;
        _retroProgress = retroProgress;
        _retroDetails = retroDetails;
        _retroRefresh = retroRefresh;
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
                        gameSystem.CoverAspectRatio,
                        OpenAchievementDetailsCommand));
                }

                ApplyAchievementDisplays(viewModels);
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

    // Resolves each game's achievement presentation from the cached links + progress and the
    // current connection state, so the grid mark and list column render from local data alone
    // (no network). Runs on the load worker before the view models reach the bound collection.
    private void ApplyAchievementDisplays(IReadOnlyList<GameViewModel> viewModels)
    {
        if (_retroAchievementsRead is null || viewModels.Count == 0)
            return;

        try
        {
            var links = _retroAchievementsRead.GetAllLinks();
            var progress = _retroAchievementsRead.GetAllProgress();
            var connected = _retroAccount?.IsConnected ?? false;
            foreach (var viewModel in viewModels)
            {
                links.TryGetValue(viewModel.Id, out var link);
                RetroAchievementsProgressSnapshot? snapshot = null;
                if (link?.RetroAchievementsGameId is { } raGameId)
                    progress.TryGetValue(raGameId, out snapshot);
                viewModel.ApplyAchievementsDisplay(
                    RetroAchievementsDisplay.For(viewModel.SystemId, connected, link, snapshot));
                viewModel.ApplyAchievementLink(
                    link is { HasAchievements: true, RetroAchievementsGameId: { } linkedGameId }
                        ? linkedGameId
                        : null);
            }
        }
        catch (Exception ex)
        {
            // The library must still render if the achievement tables are unreadable.
            _logger.Warning("Could not resolve RetroAchievements display state.", ex);
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

            if (system.Id == "playstation3")
            {
                StatusText = "PlayStation 3 games are imported only from RPCS3. Use Settings to sync its library.";
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

        if (system.Id == "playstation3")
        {
            StatusText = "PlayStation 3 games are imported only from RPCS3. Use Settings to sync its library.";
            return;
        }

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
    private Task RescanSystemAsync()
    {
        if (SelectedSystem?.Id == "playstation3")
        {
            StatusText = "Use Settings to sync the explicitly selected RPCS3 library.";
            return Task.CompletedTask;
        }

        return SelectedSystem is { } system ? RescanAsync([system], system) : Task.CompletedTask;
    }

    [RelayCommand]
    private Task RescanAllAsync() => RescanAsync(NonRpcs3Systems(), SelectedSystem);

    private async Task<string> RescanSystemFromSettingsAsync(string systemId)
    {
        var system = Systems.FirstOrDefault(candidate => candidate.Id == systemId);
        if (system is null)
            return "That console is no longer available.";

        if (system.Id == "playstation3")
            return "Use Sync RPCS3 library to refresh PlayStation 3 games.";

        await RescanAsync([system], SelectedSystem);
        return StatusText;
    }

    private async Task<string> RescanAllFromSettingsAsync()
    {
        await RescanAsync(NonRpcs3Systems(), SelectedSystem);
        return StatusText;
    }

    private IEnumerable<GameSystem> NonRpcs3Systems() =>
        Systems.Where(system => system.Id != "playstation3");

    private async Task<string> SyncRpcs3LibraryFromSettingsAsync()
    {
        if (IsBusy)
            return "Library work is already in progress.";

        var configurationDirectory = await _dialogs.PickRpcs3ConfigurationDirectoryAsync();
        if (configurationDirectory is null)
            return "RPCS3 library sync cancelled.";

        IsBusy = true;
        try
        {
            StatusText = "Reading the RPCS3 game list…";
            var source = new Rpcs3LibrarySource(configurationDirectory);
            var result = await new ExternalLibrarySyncService(_library).SyncAsync(source);
            var playStation3 = Systems.FirstOrDefault(system => system.Id == "playstation3");
            if (playStation3 is not null)
                await ShowSystemAsync(playStation3);

            StatusText = BuildRpcs3SyncStatus(result);
            return StatusText;
        }
        catch (Rpcs3LibraryFormatException ex)
        {
            _logger.Warning($"RPCS3 library sync was rejected: {ex.Message}");
            StatusText = $"RPCS3 library sync failed: {ex.Message}";
            return StatusText;
        }
        catch (Exception ex)
        {
            _logger.Error("RPCS3 library sync failed.", ex);
            StatusText = $"RPCS3 library sync failed: {ex.Message}";
            return StatusText;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildRpcs3SyncStatus(ExternalLibraryImportResult result)
    {
        var changes = new List<string>();
        if (result.AddedCount > 0)
            changes.Add(result.AddedCount == 1 ? "1 added" : $"{result.AddedCount} added");
        if (result.UpdatedCount > 0)
            changes.Add(result.UpdatedCount == 1 ? "1 updated" : $"{result.UpdatedCount} updated");
        if (result.MarkedSourceMissingCount > 0)
        {
            changes.Add(result.MarkedSourceMissingCount == 1
                ? "1 source-missing"
                : $"{result.MarkedSourceMissingCount} source-missing");
        }

        return changes.Count == 0
            ? "RPCS3 library sync complete — no changes"
            : $"RPCS3 library sync complete — {string.Join(", ", changes)}";
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
            foreach (var system in systems.Where(system => system.Id != "playstation3"))
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

        var preparedEntries = await Task.Run(() => selection.EntryPaths
            .Select(path => new PreparedImportEntry(
                path,
                _importRules.ReadImportMetadata(path, system)))
            .ToArray());
        var now = DateTimeOffset.Now;
        var games = preparedEntries.Select(entry => new Game
        {
            SystemId = system.Id,
            Path = entry.Path,
            Title = entry.Metadata.EmbeddedTitle ?? System.IO.Path.GetFileNameWithoutExtension(entry.Path),
            TitleOrigin = entry.Metadata.EmbeddedTitle is null
                ? GameTitleOrigin.Filename
                : GameTitleOrigin.Embedded,
            IsAvailable = true,
            DateAdded = now,
        }).ToArray();

        var result = await Task.Run(() =>
            _library.ReconcileImport(system.Id, games, selection.SuppressedPaths));
        if (_metadataStore is not null && result.AddedGameIds.Count > 0)
        {
            var metadataByPath = preparedEntries.ToDictionary(
                entry => entry.Path,
                entry => entry.Metadata,
                StringComparer.OrdinalIgnoreCase);
            await Task.Run(() =>
            {
                foreach (var gameId in result.AddedGameIds)
                {
                    var imported = _metadataStore.GetGame(gameId);
                    if (imported is not null &&
                        metadataByPath.TryGetValue(imported.Path, out var metadata) &&
                        metadata.Identifiers.Count > 0)
                    {
                        _metadataStore.ReplaceIdentifiers(gameId, metadata.Identifiers);
                    }
                }
            });
        }

        return result;
    }

    private sealed record PreparedImportEntry(string Path, GameImportMetadata Metadata);

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

    /// <summary>
    /// Startup's only RetroAchievements refresh path. It shares the account pipeline with
    /// connect/disconnect work and leaves the current library display untouched when the cached
    /// summary is still within its fifteen-minute freshness window.
    /// </summary>
    public async Task RefreshRetroAchievementsProgressAtStartupAsync()
    {
        if (_retroRefresh is null)
            return;

        await _retroAchievementsPipeline.WaitAsync();
        try
        {
            var refreshed = await _retroRefresh.RefreshSummaryAtStartupIfStaleAsync();
            if (refreshed is not null)
                await ReloadGamesAsync();
        }
        catch (Exception ex)
        {
            // Startup cache refresh is optional. Keep the library usable and let its existing
            // display state explain any stale cached values.
            _logger.Warning("RetroAchievements startup progress refresh failed.", ex);
        }
        finally
        {
            _retroAchievementsPipeline.Release();
        }
    }

    private Task<int> UpdateAvailabilityAsync() => Task.Run(() =>
    {
        var updates = new List<GameAvailabilityUpdate>();
        foreach (var game in _library.GetGames())
        {
            // An external source owns this state. A generic startup stat must not revive an
            // entry that a later source sync retained as source-missing.
            if (game.ExternalSourceId is not null)
                continue;

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
            StatusText = game.UnavailableLaunchStatus;
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
            if (result.ProcessExited && game.RetroAchievementsGameId is { } retroAchievementsGameId)
                _ = RefreshRetroAchievementsAfterTrackedExitAsync(retroAchievementsGameId);
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

    private async Task RefreshRetroAchievementsAfterTrackedExitAsync(int retroAchievementsGameId)
    {
        if (_retroRefresh is null)
            return;

        try
        {
            var response = await _retroRefresh.RefreshAfterTrackedExitAsync(retroAchievementsGameId);
            if (response?.IsSuccess == true)
                await ReloadGamesAsync();
        }
        catch (Exception ex)
        {
            // A game has already finished; this read-only follow-up must never change its launch
            // result or surface as a launch failure. Cached progress remains visible.
            _logger.Warning(
                $"RetroAchievements post-exit refresh failed for game {retroAchievementsGameId}.", ex);
        }
    }

    [RelayCommand]
    private async Task OpenAchievementDetailsAsync(GameViewModel? game)
    {
        if (game?.RetroAchievementsGameId is not { } retroAchievementsGameId)
            return;

        try
        {
            await _dialogs.ShowAchievementDetailsAsync(game.Title, retroAchievementsGameId);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not open achievements for game id {game.Id}.", ex);
            StatusText = $"Could not open achievements for {game.Title}: {ex.Message}";
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
                    FetchAllMetadataFromSettingsAsync,
                    SyncRpcs3LibraryFromSettingsAsync),
                _metadataPreferences,
                _retroAccount is null
                    ? null
                    : new RetroAchievementsSettingsContext(
                        _retroAccount.Account,
                        _retroAccount.IsConnected,
                        ConnectRetroAchievementsAsync,
                        DisconnectRetroAchievementsAsync));
        }
        catch (Exception ex)
        {
            _logger.Error("Could not open emulator settings.", ex);
            StatusText = $"Could not open emulator settings: {ex.Message}";
        }
    }

    // Connect pipeline: validate the account, identify the existing library, resolve hashes
    // against console catalogues, refresh progress, and reload so marks and columns appear.
    // Matching and progress failures are non-fatal — the validated account stays connected.
    internal async Task<RetroAchievementsConnectionSummary> ConnectRetroAchievementsAsync(
        string username,
        string apiKey,
        IProgress<RetroAchievementsLibrarySyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_retroAccount is null)
            return new RetroAchievementsConnectionSummary(
                RetroAchievementsConnectionResult.ServerError);

        var result = await _retroAccount.ConnectAsync(username, apiKey, cancellationToken);
        if (result != RetroAchievementsConnectionResult.Connected)
            return new RetroAchievementsConnectionSummary(result);

        var gameIds = await Task.Run(
            () => _library.GetGames().Select(game => game.Id).ToArray(),
            cancellationToken);
        var sync = await SynchronizeRetroAchievementsAsync(gameIds, progress, cancellationToken);
        return new RetroAchievementsConnectionSummary(result, sync);
    }

    internal async Task DisconnectRetroAchievementsAsync(CancellationToken cancellationToken)
    {
        if (_retroAccount is null)
            return;

        // Account-scoped progress must not be cleared while an import-triggered sync still has
        // the old credentials. The same lock also makes any queued import recheck the now-
        // disconnected account before it reads a game or writes new progress.
        await _retroAchievementsPipeline.WaitAsync(cancellationToken);
        try
        {
            await _retroAccount.DisconnectAsync(cancellationToken);
            _retroProgress?.Clear();
            _retroDetails?.Clear();
            await ReloadGamesAsync();
        }
        finally
        {
            _retroAchievementsPipeline.Release();
        }
    }

    private async Task MaybeStartMetadataForImportAsync(IReadOnlyList<long> addedGameIds)
    {
        if (addedGameIds.Count == 0)
            return;

        // Disc identification is now explicitly gated on a connected account. Existing games
        // are backfilled once on connect; later imports use the same serialized full pipeline.
        if (_retroAchievements is not null && _retroAccount?.IsConnected == true)
            _ = SynchronizeImportedRetroAchievementsAsync(addedGameIds);

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

    private async Task<RetroAchievementsLibrarySyncSummary?> SynchronizeRetroAchievementsAsync(
        IReadOnlyList<long> gameIds,
        IProgress<RetroAchievementsLibrarySyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_retroAchievements is null)
            return null;

        await _retroAchievementsPipeline.WaitAsync(cancellationToken);
        try
        {
            // An import can have been queued while connected, then waited behind a different
            // sync until after disconnect. Do not identify media or recreate account-scoped
            // cache rows for a no-longer-connected account.
            if (_retroAccount?.IsConnected != true)
                return null;

            var identification = await _retroAchievements.IdentifyAsync(
                gameIds,
                cancellationToken,
                progress);

            RetroAchievementsMatchSummary? matching = null;
            RetroAchievementsProgressRefreshSummary? achievementProgress = null;
            var credentials = _retroAccount?.CurrentCredentials;

            if (_retroMatching is not null && credentials is not null)
            {
                try
                {
                    matching = await _retroMatching.MatchAsync(
                        credentials,
                        forceRefreshCatalogues: false,
                        cancellationToken,
                        progress);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A validated account remains connected if a cache/network operation fails;
                    // stale links and cached progress are still useful to the library display.
                    _logger.Warning("RetroAchievements catalogue matching failed.", ex);
                }
            }

            if (_retroProgress is not null && credentials is not null)
            {
                try
                {
                    achievementProgress = await _retroProgress.RefreshAllAsync(
                        credentials,
                        cancellationToken,
                        progress);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warning("RetroAchievements progress refresh failed.", ex);
                }
            }

            await ReloadGamesAsync();
            return new RetroAchievementsLibrarySyncSummary(
                identification,
                matching,
                achievementProgress);
        }
        finally
        {
            _retroAchievementsPipeline.Release();
        }
    }

    private async Task SynchronizeImportedRetroAchievementsAsync(IReadOnlyList<long> gameIds)
    {
        try
        {
            await SynchronizeRetroAchievementsAsync(gameIds, progress: null, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            // Import-triggered work has no cancellation source today, but retain the same
            // cancellation semantics as the explicit Settings connection path.
        }
        catch (Exception ex)
        {
            _logger.Warning("RetroAchievements synchronization for imported games failed.", ex);
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
