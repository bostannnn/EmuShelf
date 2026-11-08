using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.Core.Handheld;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Systems;
using EmuShelf.App.Services;
using EmuShelf.Infrastructure.Settings;
using EmuShelf.Integrations.Emulators.Android;

namespace EmuShelf.App.ViewModels;

// Handheld mode (M45): the mode flag, the shell's lifetime, and the library seam the shell browses
// through. The shell owns all Handheld state; this side only adapts the library, the cover pipeline and
// the launch core to it, so the Handheld views never reach into the Gamepad shell's scope state.
public partial class MainViewModel
{
    private IDeviceStatusService? _handheldDeviceStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsGamepadMainScreenCompanion))]
    public partial bool IsHandheldMode { get; set; }

    /// <summary>The Handheld shell, alive while Handheld mode is on; null in Desktop and Gamepad mode.</summary>
    [ObservableProperty]
    public partial HandheldShellViewModel? Handheld { get; private set; }

    /// <summary>Whether this platform can run Handheld mode (a dual-screen Android handheld).</summary>
    public bool SupportsHandheldMode => _interfaceModeService?.SupportsHandheldMode ?? false;

    /// <summary>The status pill's battery/Wi-Fi source. The Android head sets it; elsewhere it stays unknown.</summary>
    public IDeviceStatusService? HandheldDeviceStatus
    {
        get => _handheldDeviceStatus;
        set
        {
            _handheldDeviceStatus = value;
            if (Handheld is { } handheld)
            {
                handheld.DeviceStatus = value ?? UnknownDeviceStatusService.Instance;
                handheld.RefreshStatus();
            }
        }
    }

    /// <summary>
    /// The companion dock the Handheld dock shows. The Android head sets it once Screen-2's presentation
    /// exists, and it is handed to every Handheld shell created afterwards.
    /// </summary>
    public SecondScreenViewModel? HandheldCompanion
    {
        get => _handheldCompanion;
        set
        {
            _handheldCompanion = value;
            if (Handheld is { } handheld)
                handheld.Companion = value;
        }
    }

    private SecondScreenViewModel? _handheldCompanion;

    partial void OnIsHandheldModeChanged(bool value)
    {
        if (value)
        {
            if (Handheld is not null)
                return;
            var handheld = new HandheldShellViewModel(new HandheldLibrary(this), _handheldDeviceStatus,
                layoutStore: _settingsDirectory is { } settings
                    ? new FileHandheldHomeLayoutStore(Path.Combine(settings, "handheld-home.json"), _logger)
                    : null,
                screensSwapped: _settingsService?.Load().HandheldScreensSwapped ?? false,
                saveScreensSwapped: SaveHandheldScreensSwapped)
            {
                Companion = _handheldCompanion,
                LaunchLead = HandheldMotion.LaunchLead,
            };
            Handheld = handheld;
            handheld.ShowStatus(StatusText, StatusSeverity);
            AnnounceHandheldUpdate();
            handheld.SetLaunchSaveSync(IsBlockingLaunchSaveSync);
            handheld.StartStatusUpdates();
            _ = handheld.InitializeAsync();
        }
        else if (Handheld is { } handheld)
        {
            Handheld = null;
            handheld.Dispose();
        }
    }

    // An update found by the launch check: Gamepad mode shows it in Settings → About only; Handheld mode says so on the Stage
    // once (M45 H6c), as soon as the shell exists or the check finds one.
    private bool _handheldUpdateAnnounced;

    private void AnnounceHandheldUpdate()
    {
        if (_handheldUpdateAnnounced || Handheld is not { } handheld || _updates is not { IsBannerVisible: true } updates)
            return;
        _handheldUpdateAnnounced = true;
        handheld.Announce($"{updates.Headline}. Install it from Settings → About.", HandheldGlyph.Update, TimeSpan.FromSeconds(10));
    }

    private void OnUpdatesChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppUpdateCoordinator.IsBannerVisible))
            AnnounceHandheldUpdate();
    }

    // Handheld mode's Settings (M45 H6a): the same projection Gamepad mode's overlay shows, laid across the two screens by
    // the shell. One at a time; the device probes it caches are re-read when EmuShelf comes back to the foreground.
    private GamepadSettingsViewModel? _handheldSettings;
    private bool _openingHandheldSettings;

    private async Task<GamepadSettingsViewModel?> CreateHandheldSettingsAsync()
    {
        if (!IsHandheldMode || _openingHandheldSettings || _handheldSettings is not null)
            return null;
        if (IsBusy)
        {
            // Gamepad mode's Settings waits for the same thing; say so rather than leave the tile looking dead.
            SetStatus("Settings can open once the current task finishes.");
            return null;
        }
        _openingHandheldSettings = true;
        try
        {
            // No Hotkeys overlay (Android has none) and no "Run setup again": Handheld mode's first run is its own (H6d).
            var settings = await CreateGamepadSettingsProjectionAsync(setup: null, openHotkeys: null, runSetup: null);
            if (!IsHandheldMode)
            {
                settings.Dispose();
                return null;
            }
            _handheldSettings = settings;
            EmuShelf.App.App.ForegroundReturned += RefreshHandheldSettingsDeviceState;
            return settings;
        }
        catch (Exception ex)
        {
            _logger.Error("Could not open Handheld settings.", ex);
            SetStatus($"Could not open Settings: {ex.Message}", StatusSeverity.Error);
            return null;
        }
        finally
        {
            _openingHandheldSettings = false;
        }
    }

    private void CloseHandheldSettings(GamepadSettingsViewModel settings, bool saved)
    {
        if (ReferenceEquals(_handheldSettings, settings))
        {
            EmuShelf.App.App.ForegroundReturned -= RefreshHandheldSettingsDeviceState;
            _handheldSettings = null;
        }
        settings.Dispose();
        if (saved)
            SetStatus("Settings saved.");
    }

    private void RefreshHandheldSettingsDeviceState() => _handheldSettings?.RefreshDeviceState();

    /// <summary>X swapped the Handheld screens: remembered for the next start (M45 H5).</summary>
    private void SaveHandheldScreensSwapped(bool swapped)
    {
        if (_settingsService is not { } settings)
            return;
        // Saves run one after another, so two quick swaps can never land in the wrong order.
        _handheldSwapSave = _handheldSwapSave.ContinueWith(_ =>
        {
            try
            {
                settings.Update(latest => latest with { HandheldScreensSwapped = swapped });
            }
            catch (Exception ex)
            {
                _logger.Warning("Could not remember the Handheld screen swap.", ex);
            }
        }, TaskScheduler.Default);
    }

    private Task _handheldSwapSave = Task.CompletedTask;

    /// <summary>Rebuilds the Handheld home after the library changed (an import, a removal, a rescan).</summary>
    private void RefreshHandheldHome()
    {
        if (Handheld is { } handheld)
            _ = handheld.InitializeAsync();
    }

    [RelayCommand]
    private async Task SwitchToHandheldModeAsync()
    {
        CloseGamepadOverlay();
        await SetInterfaceModeAsync(InterfaceMode.Handheld);
    }

    /// <summary>Builds the shared view model for one disc set — the same object every library view uses.</summary>
    private GameViewModel CreateGameViewModel(GameDiscSet titleSet, GameSystem gameSystem, IImage? artwork)
    {
        var game = titleSet.DisplayGame;
        return new GameViewModel(
            game,
            gameSystem.Name,
            gameSystem.ShortName,
            gameSystem.AccentColor,
            LaunchGameCommand,
            SaveGameTitleCommand,
            SetGameCoverCommand,
            LoadGameCoverCommand,
            artwork,
            gameSystem.CoverAspectRatio,
            OpenAchievementDetailsCommand,
            RemoveSelectedGamesCommand,
            ScrapeSelectedGamesCommand,
            titleSet.Discs,
            titleSet.SelectedDisc,
            titleSet.DisplayTitle,
            titleSet.SelectionKey,
            LaunchSelectedDiscFromLibraryAsync,
            ScrapeGameCommand,
            ShowGameInFolderCommand,
            OpenTextureFolderCommand,
            TexturePackProviderRegistry.Find(game.SystemId) is not null,
            OpenPlayHistoryCommand);
    }

    /// <summary>The library as Handheld mode browses it: platforms, a platform's games, covers, launching.</summary>
    private sealed class HandheldLibrary(MainViewModel owner) : IHandheldLibrary
    {
        // On-disk thumbnails are this wide; a tile never decodes wider than its source.
        private const int ThumbnailWidth = CoverThumbnailNativeWidth;

        public async Task<IReadOnlyList<HandheldPlatform>> GetPlatformsAsync(CancellationToken cancellationToken = default)
        {
            // Navigation order, captured on the UI thread; counts are title sets, so a multi-disc game is one.
            var systems = owner.Systems.ToList();
            var library = owner._library;
            return await Task.Run<IReadOnlyList<HandheldPlatform>>(() =>
            {
                var titleSets = GameDiscSetBuilder.Build(library.GetGames(), library.GetDiscSelections());
                var counts = titleSets
                    .GroupBy(set => set.DisplayGame.SystemId, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
                return systems
                    .Where(system => counts.ContainsKey(system.Id))
                    .Select(system => new HandheldPlatform(
                        system.Id, system.Name, system.ShortName, system.AccentColor, counts[system.Id], system.Era, system.Backdrops))
                    .ToList();
            }, cancellationToken);
        }

        public Task<IReadOnlyList<HandheldGameEntry>> GetGamesAsync(string systemId, CancellationToken cancellationToken = default) =>
            ReadGamesAsync(systemId, int.MaxValue, cancellationToken);

        public Task<IReadOnlyList<HandheldGameEntry>> GetFirstGamesAsync(string systemId, int count, CancellationToken cancellationToken = default) =>
            ReadGamesAsync(systemId, count, cancellationToken);

        // Title sets are cheap; a view model per title is not, so only the first `count` get one.
        private async Task<IReadOnlyList<HandheldGameEntry>> ReadGamesAsync(string systemId, int count, CancellationToken cancellationToken)
        {
            if (!owner._systemsById.ContainsKey(systemId))
                return [];
            return await Task.Run<IReadOnlyList<HandheldGameEntry>>(() =>
            {
                var library = owner._library;
                var titleSets = GameDiscSetBuilder.Build(library.GetGames(systemId), library.GetDiscSelections()).Take(count).ToList();
                return ToEntries(titleSets);
            }, cancellationToken);
        }

        // Entries (with their shared view models) for title sets of any systems, in the order given.
        private List<HandheldGameEntry> ToEntries(IReadOnlyList<GameDiscSet> titleSets)
        {
            var known = titleSets.Where(set => owner._systemsById.ContainsKey(set.DisplayGame.SystemId)).ToList();
            var viewModels = known.Select(set =>
            {
                var system = owner._systemsById[set.DisplayGame.SystemId];
                return owner.CreateGameViewModel(set, system, PlatformArtwork.ForSystem(system.Id));
            }).ToList();
            owner.ApplyAchievementDisplays(viewModels);
            owner.ApplyScrapedTitles(viewModels);
            return known.Zip(viewModels, (set, viewModel) => new HandheldGameEntry(
                viewModel.Id,
                viewModel.DisplayTitle,
                set.DisplayGame.SystemId,
                owner._systemsById[set.DisplayGame.SystemId].Name,
                LastPlayed(set),
                set.Discs.Sum(disc => disc.Game.PlayCount),
                set.Discs.Aggregate(TimeSpan.Zero, (total, disc) => total + disc.Game.Playtime),
                viewModel.Model.IsAvailable,
                viewModel.ShowAchievementMark ? viewModel.GamepadAchievementCountText : null,
                viewModel,
                viewModel.ShowAchievementMark ? viewModel.GamepadAchievementProgressRatio : 0)).ToList();
        }

        private static DateTimeOffset? LastPlayed(GameDiscSet set) => set.Discs.Max(disc => disc.Game.LastPlayedAt);

        // Every title set in the library; a game's id on home is its title set's display game (its first disc).
        private IReadOnlyList<GameDiscSet> AllTitleSets() =>
            GameDiscSetBuilder.Build(owner._library.GetGames(), owner._library.GetDiscSelections());

        public async Task<IReadOnlyList<HandheldGameEntry>> GetGamesByIdAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken = default)
        {
            if (ids.Count == 0)
                return [];
            return await Task.Run<IReadOnlyList<HandheldGameEntry>>(() =>
            {
                var byId = AllTitleSets().ToDictionary(set => set.DisplayGame.Id);
                var sets = ids.Distinct().Where(byId.ContainsKey).Select(id => byId[id]).ToList();
                cancellationToken.ThrowIfCancellationRequested();
                return ToEntries(sets);
            }, cancellationToken);
        }

        public Task<IReadOnlySet<long>> GetGameIdsAsync(CancellationToken cancellationToken = default) =>
            Task.Run<IReadOnlySet<long>>(() => AllTitleSets().Select(set => set.DisplayGame.Id).ToHashSet(), cancellationToken);

        public Task<IReadOnlyList<long>> GetRecentGameIdsAsync(int count, CancellationToken cancellationToken = default) =>
            Task.Run<IReadOnlyList<long>>(() => AllTitleSets()
                .Where(set => LastPlayed(set) is not null)
                .OrderByDescending(LastPlayed)
                .Take(count)
                .Select(set => set.DisplayGame.Id)
                .ToList(), cancellationToken);

        public void ReleaseGames(IReadOnlyList<HandheldGameEntry> games)
        {
            foreach (var game in games)
                game.ViewModel?.Dispose();
        }

        public async Task<Bitmap?> LoadCoverAsync(HandheldGameEntry game, int decodeWidth, bool fullSize, CancellationToken cancellationToken)
        {
            if (game.ViewModel?.CoverPath is not { } coverPath)
                return null;
            // Bounded like the library's own cover loads, so a folder's first screen of tiles cannot start a
            // dozen decodes at once.
            await owner._coverDecodeGate.WaitAsync(cancellationToken);
            try
            {
                var path = fullSize ? coverPath : await owner._covers.GetThumbnailAsync(game.Id, coverPath, cancellationToken);
                if (path is null || !File.Exists(path))
                    return null;
                var width = fullSize ? decodeWidth : Math.Min(decodeWidth, ThumbnailWidth);
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var stream = File.OpenRead(path);
                    return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A bad or unreadable cover leaves the tile on its placeholder; it is not worth a toast.
                owner._logger.Warning($"Handheld: could not decode the cover for {game.Title}.", ex);
                return null;
            }
            finally
            {
                owner._coverDecodeGate.Release();
            }
        }

        // Fan art is drawn full-bleed on the 1920×1080 Stage; logos sit in a box about 800×560.
        private const int FanartMaxWidth = 1920;
        private const int FanartMaxHeight = 1080;
        private const int LogoMaxWidth = 900;
        private const int LogoMaxHeight = 600;

        public async Task<Bitmap?> LoadArtAsync(HandheldGameEntry game, HandheldArtKind kind, CancellationToken cancellationToken)
        {
            if (game.ViewModel is not { } viewModel)
                return null;
            // The same scraped-media resolution the Gamepad spotlight hero uses, cached on the shared view model.
            if (!viewModel.AreSpotlightDetailsLoaded)
            {
                var resolved = await Task.Run(() => owner.ResolveSpotlightDetails(viewModel.Id), cancellationToken);
                if (!viewModel.AreSpotlightDetailsLoaded)
                    viewModel.ApplySpotlightDetails(resolved.FanartPath, resolved.WheelPath, resolved.RatingText, resolved.Facts);
            }
            var (path, width, height) = kind switch
            {
                HandheldArtKind.Logo => (viewModel.WheelPath, LogoMaxWidth, LogoMaxHeight),
                _ => (viewModel.FanartPath, FanartMaxWidth, FanartMaxHeight),
            };
            if (path is not { Length: > 0 } source)
                return null;
            await owner._coverDecodeGate.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return File.Exists(source) ? SafeImageDecoder.DecodeToFit(source, width, height) : null;
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                owner._logger.Warning($"Handheld: could not decode the {kind} art for {game.Title}.", ex);
                return null;
            }
            finally
            {
                owner._coverDecodeGate.Release();
            }
        }

        public async Task<HandheldTitleDetails> GetTitleDetailsAsync(HandheldGameEntry game, CancellationToken cancellationToken = default)
        {
            var viewModel = game.ViewModel;
            var isDualScreen = owner._systemsById.TryGetValue(game.SystemId, out var system) && system.IsDualScreen;
            cancellationToken.ThrowIfCancellationRequested();
            var screen = await owner._launchScreens.ResolveAsync(game.SystemId, isDualScreen);
            cancellationToken.ThrowIfCancellationRequested();
            var emulator = await Task.Run(() => DescribeEmulator(game.SystemId), cancellationToken);
            var discs = viewModel?.Discs.Select(disc => disc.Number).ToList() ?? [1];
            return new HandheldTitleDetails(
                emulator,
                discs,
                viewModel?.SelectedDiscNumber ?? 1,
                screen,
                owner._launchScreens.HasSecondScreen,
                isDualScreen);
        }

        // The emulator that will run this system's games, by the name Settings shows: the saved choice, else
        // the platform's default (on Android, the first choice in the catalogue, which is the launch default).
        private string? DescribeEmulator(string systemId)
        {
            EmulatorConfiguration? configuration;
            try
            {
                configuration = owner._emulatorConfigurations.Get(systemId);
            }
            catch (Exception ex)
            {
                owner._logger.Warning($"Handheld: could not read the emulator for {systemId}.", ex);
                return null;
            }
            if (OperatingSystem.IsAndroid())
            {
                if (!AndroidEmulatorChoiceCatalog.BySystem.TryGetValue(systemId, out var choices) || choices.Count == 0)
                    return null;
                var chosen = configuration is null
                    ? null
                    : choices.FirstOrDefault(choice => choice.Matches(configuration.EmulatorId, configuration.CorePath));
                return (chosen ?? choices[0]).DisplayName;
            }
            var emulatorId = configuration?.EmulatorId;
            return owner._emulators.FirstOrDefault(emulator => string.Equals(emulator.Id, emulatorId, StringComparison.Ordinal))?.Name
                ?? owner._emulators.FirstOrDefault(emulator => emulator.Supports(systemId))?.Name;
        }

        public async Task<bool> SelectDiscAsync(HandheldGameEntry game, int discNumber)
        {
            if (game.ViewModel is not { } viewModel ||
                viewModel.Discs.FirstOrDefault(disc => disc.Number == discNumber) is not { } disc)
                return false;
            return await owner.RememberSelectedDiscAsync(viewModel, disc);
        }

        public void RememberLaunchScreen(string systemId, GameLaunchScreen screen) =>
            owner._launchScreens.Remember(systemId, screen);

        public Task<bool> LaunchAsync(HandheldGameEntry game, GameLaunchScreen screen) =>
            game.ViewModel is { } viewModel
                ? owner.LaunchGameCoreAsync(viewModel, screenOverride: screen)
                : Task.FromResult(false);

        public Task SwitchModeAsync(InterfaceMode mode) => owner.SetInterfaceModeAsync(mode);

        public Task<GamepadSettingsViewModel?> CreateSettingsAsync() => owner.CreateHandheldSettingsAsync();

        public void CloseSettings(GamepadSettingsViewModel settings, bool saved) => owner.CloseHandheldSettings(settings, saved);

        public void Quit() => owner._applicationLifetime?.Shutdown();

        // ---- M45 H6c: the Y sheet's actions, Search, Add games and the screens behind them, all Gamepad mode's ----

        public bool IsBusy => owner.IsBusy;

        public Task<IReadOnlyList<long>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            var text = query.Trim();
            if (text.Length == 0)
                return Task.FromResult<IReadOnlyList<long>>([]);
            return Task.Run<IReadOnlyList<long>>(() =>
            {
                var scraped = owner.ReadProviderTitles();
                return AllTitleSets()
                    .Where(set => owner._systemsById.ContainsKey(set.DisplayGame.SystemId))
                    .Select(set => (Set: set, Shown: ShownTitle(set, scraped)))
                    .Where(match =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // As Gamepad mode's search: the name shown, or the library's own title.
                        return match.Shown.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                            match.Set.DisplayTitle.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                            match.Set.DisplayGame.Title.Contains(text, StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderBy(match => match.Shown, StringComparer.OrdinalIgnoreCase)
                    .Select(match => match.Set.DisplayGame.Id)
                    .ToList();
            }, cancellationToken);
        }

        // The name a game's tile shows: a scraped title unless you renamed it yourself (GameViewModel.DisplayTitle).
        private static string ShownTitle(GameDiscSet set, IReadOnlyDictionary<long, string> scraped) =>
            scraped.TryGetValue(set.DisplayGame.Id, out var title) && !string.IsNullOrWhiteSpace(title) &&
            set.DisplayGame.TitleOrigin != GameTitleOrigin.User
                ? title
                : set.DisplayTitle;

        public async Task<IReadOnlyList<long>> GetPlatformGameIdsAsync(string systemId, CancellationToken cancellationToken = default)
        {
            if (!owner._systemsById.ContainsKey(systemId))
                return [];
            var library = owner._library;
            return await Task.Run<IReadOnlyList<long>>(() => GameDiscSetBuilder.Build(library.GetGames(systemId), library.GetDiscSelections())
                .Select(set => set.DisplayGame.Id)
                .ToList(), cancellationToken);
        }

        public bool HasAchievementDetails(HandheldGameEntry game) => game.ViewModel?.CanOpenAchievementDetails ?? false;

        public Task<AchievementDetailsViewModel?> CreateAchievementsAsync(HandheldGameEntry game) =>
            game.ViewModel is { } viewModel ? owner.CreateAchievementDetailsAsync(viewModel) : Task.FromResult<AchievementDetailsViewModel?>(null);

        public async Task<bool> RenameAsync(HandheldGameEntry game, string title)
        {
            if (game.ViewModel is not { } viewModel)
                return false;
            if (owner.IsBusy)
            {
                owner.SetStatus("Renaming can happen once the current task finishes.");
                return false;
            }
            viewModel.DraftTitle = title;
            await owner.SaveGameTitleAsync(viewModel);
            return string.Equals(viewModel.Title, title.Trim(), StringComparison.Ordinal);
        }

        public async Task<bool> RemoveFromLibraryAsync(HandheldGameEntry game)
        {
            if (game.ViewModel is not { } viewModel)
                return false;
            return await owner.RemoveGameCoreAsync(viewModel);
        }

        public GamepadScraperViewModel? CreateScraper(HandheldGameEntry game) =>
            game.ViewModel is { } viewModel ? owner.CreateGamepadScraper(viewModel) : null;

        public async Task CloseScraperAsync(GamepadScraperViewModel scraper)
        {
            // As Gamepad mode's overlay closing: an apply that wrote something reloads the library.
            var reload = scraper.HasAppliedChanges;
            scraper.Dispose();
            if (reload)
                await owner.ReloadGamesAsync();
        }

        public GamepadBatchScraperViewModel? CreateBatchScraper(IReadOnlyList<long> gameIds, string name) =>
            owner.CreateGamepadBatchScraper(gameIds, name);

        public async Task CloseBatchScraperAsync(GamepadBatchScraperViewModel batch)
        {
            var reload = batch.Batch.AppliedChanges;
            batch.Dispose();
            if (reload)
                await owner.ReloadGamesAsync();
        }

        public GamepadCoverSearchViewModel? CreateCoverSearch(HandheldGameEntry game) =>
            game.ViewModel is { } viewModel && owner.CanGamepadCoverSearch ? owner.CreateGamepadCoverSearch(viewModel, offersLocalFile: false) : null;

        public string CoverSearchUnavailableReason => owner._artworkSearch is null || owner._artworkDownloader is null
            ? "Web image search is not available in this build."
            : "Web image search is off. Turn it on in Settings → Artwork & metadata.";

        public Task ApplyCoverAsync(HandheldGameEntry game, PickedGameCover cover) =>
            game.ViewModel is { } viewModel ? owner.ImportPickedCoverAsync(viewModel, cover) : Task.CompletedTask;

        public Task<string?> PickImportFolderAsync() => owner._dialogs.PickFolderAsync();

        public IReadOnlyList<HandheldImportSystem> ImportSystems => owner.Systems
            .Where(system => system.Id != "playstation3")
            .Select(system => new HandheldImportSystem(system.Id, system.Name, system.ShortName, system.AccentColor))
            .ToList();

        public Task ImportFolderAsync(string folder, string systemId) =>
            owner._systemsById.TryGetValue(systemId, out var system) ? owner.ImportFolderForSystemAsync(folder, system) : Task.CompletedTask;
    }
}
