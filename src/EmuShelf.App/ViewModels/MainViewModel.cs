using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Library;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const int SearchDebounceMs = 250;

    private readonly IGameLibrary _library;
    private readonly IFolderScanner _scanner;
    private readonly IGameImportRules _importRules;
    private readonly IAvailabilityChecker _availabilityChecker;
    private readonly IDialogService _dialogs;

    private readonly DispatcherTimer _searchDebounce;
    private readonly List<GameViewModel> _systemGames = [];

    // Bumped on every reload so a slow load that finishes after a newer one is discarded,
    // keeping the shown games in sync with the current selection.
    private int _loadGeneration;

    public ObservableCollection<GameSystem> Systems { get; }
    public ObservableCollection<GameViewModel> Games { get; } = [];

    [ObservableProperty]
    public partial GameSystem? SelectedSystem { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsGridView { get; set; } = true;

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial string LibraryCountText { get; set; } = "0 games";

    /// <summary>True when the current filter yields at least one game (drives the views).</summary>
    [ObservableProperty]
    public partial bool HasGames { get; set; }

    /// <summary>True only when the selected system has no games at all — drives the "add your first game" prompt.</summary>
    [ObservableProperty]
    public partial bool IsLibraryEmpty { get; set; } = true;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

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
        IReadOnlyList<GameSystem> systems)
    {
        _library = library;
        _scanner = scanner;
        _importRules = importRules;
        _availabilityChecker = availabilityChecker;
        _dialogs = dialogs;

        Systems = new ObservableCollection<GameSystem>(systems);

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchDebounceMs) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            ApplyFilter();
        };

        SelectedSystem = Systems.FirstOrDefault();
    }

    partial void OnSelectedSystemChanged(GameSystem? value) => _ = ReloadGamesAsync();

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    internal async Task ReloadGamesAsync()
    {
        var system = SelectedSystem;
        if (system is null)
            return;

        var generation = ++_loadGeneration;
        try
        {
            var games = await Task.Run(() => _library.GetGames(system.Id));

            // A newer reload (system switch, or the post-availability refresh) started while we
            // were reading — discard this stale result so it can't overwrite the current view.
            if (generation != _loadGeneration)
                return;

            _systemGames.Clear();
            foreach (var game in games)
                _systemGames.Add(new GameViewModel(game, system.Name, system.AccentColor));
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load library: {ex.Message}";
        }
    }

    internal void ApplyFilter()
    {
        var query = SearchText.Trim();
        IEnumerable<GameViewModel> filtered = _systemGames;
        if (query.Length > 0)
            filtered = _systemGames.Where(g =>
                g.Title.Contains(query, StringComparison.OrdinalIgnoreCase));

        Games.Clear();
        foreach (var game in filtered)
            Games.Add(game);

        HasGames = Games.Count > 0;
        IsLibraryEmpty = _systemGames.Count == 0;
        LibraryCountText = _systemGames.Count == 1 ? "1 game" : $"{_systemGames.Count} games";
    }

    [RelayCommand]
    private async Task AddGamesAsync()
    {
        if (IsBusy)
            return;

        var paths = await _dialogs.PickGameFilesAsync();
        if (paths.Count == 0)
            return;

        var suggested = paths
            .SelectMany(_importRules.SuggestSystems)
            .GroupBy(s => s.Id)
            .OrderByDescending(g => g.Count())
            .Select(g => g.First())
            .FirstOrDefault() ?? SelectedSystem;

        var system = await _dialogs.PickSystemAsync(Systems, suggested);
        if (system is null)
            return;

        IsBusy = true;
        try
        {
            var added = await ImportPathsAsync(system, paths);
            await ShowSystemAsync(system);
            StatusText = added == 1 ? "Added 1 game" : $"Added {added} games";
        }
        finally
        {
            IsBusy = false;
        }
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

            var candidates = await _scanner.ScanAsync(folder, system, progress);
            await Task.Run(() => _library.AddLibraryFolder(system.Id, folder));

            var added = await ImportPathsAsync(system, candidates);
            await ShowSystemAsync(system);
            StatusText = added == 1 ? "Added 1 game from folder" : $"Added {added} games from folder";
        }
        catch (Exception ex)
        {
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

    private async Task RescanAsync(IEnumerable<GameSystem> systems, GameSystem? systemToShow)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            var total = 0;
            foreach (var system in systems)
            {
                var folders = await Task.Run(() => _library.GetLibraryFolders(system.Id));
                foreach (var folder in folders)
                {
                    var progress = new Progress<ScanProgress>(p =>
                        StatusText = $"Rescanning {system.Name}… {p.CandidatesFound} found");
                    var candidates = await _scanner.ScanAsync(folder.Path, system, progress);
                    total += await ImportPathsAsync(system, candidates);
                }
            }

            await RefreshAvailabilityAsync();
            if (systemToShow is not null)
                await ShowSystemAsync(systemToShow);
            StatusText = total == 0 ? "Rescan complete — no new games" : $"Rescan added {total} game(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Rescan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<int> ImportPathsAsync(GameSystem system, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return 0;

        var now = DateTimeOffset.Now;
        var games = paths.Select(path => new Game
        {
            SystemId = system.Id,
            Path = path,
            Title = System.IO.Path.GetFileNameWithoutExtension(path),
            IsAvailable = true,
            DateAdded = now,
        });

        return await Task.Run(() => _library.AddGames(games));
    }

    private async Task ShowSystemAsync(GameSystem system)
    {
        if (SelectedSystem?.Id == system.Id)
            await ReloadGamesAsync();
        else
            SelectedSystem = system; // triggers reload via OnSelectedSystemChanged
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
            var changed = await Task.Run(() =>
            {
                var count = 0;
                foreach (var game in _library.GetGames())
                {
                    var available = _availabilityChecker.IsAvailable(game);
                    if (available != game.IsAvailable)
                    {
                        _library.SetAvailability(game.Id, available);
                        count++;
                    }
                }
                return count;
            });

            if (changed > 0)
                await ReloadGamesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Availability check failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        // Settings UI arrives with the emulator-configuration milestone (M6).
    }
}
