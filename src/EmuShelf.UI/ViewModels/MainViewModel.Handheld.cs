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
using EmuShelf.Core.Library;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Systems;
using EmuShelf.App.Services;

namespace EmuShelf.App.ViewModels;

// Handheld mode (M45): the mode flag, the shell's lifetime, and the library seam the shell browses
// through. The shell owns all Handheld state; this side only adapts the library, the cover pipeline and
// the launch core to it, so the Handheld views never reach into the Gamepad shell's scope state.
public partial class MainViewModel
{
    private IDeviceStatusService? _handheldDeviceStatus;

    [ObservableProperty]
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
            var handheld = new HandheldShellViewModel(new HandheldLibrary(this), _handheldDeviceStatus)
            {
                Companion = _handheldCompanion,
            };
            Handheld = handheld;
            handheld.ShowStatus(StatusText);
            handheld.StartStatusUpdates();
            _ = handheld.InitializeAsync();
        }
        else if (Handheld is { } handheld)
        {
            Handheld = null;
            handheld.Dispose();
        }
    }

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
            TexturePackProviderRegistry.Find(game.SystemId) is not null);
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
                        system.Id, system.Name, system.ShortName, system.AccentColor, counts[system.Id]))
                    .ToList();
            }, cancellationToken);
        }

        public async Task<IReadOnlyList<HandheldGameEntry>> GetGamesAsync(string systemId, CancellationToken cancellationToken = default)
        {
            if (!owner._systemsById.TryGetValue(systemId, out var system))
                return [];
            var artwork = PlatformArtwork.ForSystem(systemId);
            return await Task.Run<IReadOnlyList<HandheldGameEntry>>(() =>
            {
                var library = owner._library;
                var titleSets = GameDiscSetBuilder.Build(library.GetGames(systemId), library.GetDiscSelections());
                var viewModels = titleSets.Select(set => owner.CreateGameViewModel(set, system, artwork)).ToList();
                owner.ApplyAchievementDisplays(viewModels);
                owner.ApplyScrapedTitles(viewModels);
                return titleSets.Zip(viewModels, (set, viewModel) => new HandheldGameEntry(
                    viewModel.Id,
                    viewModel.DisplayTitle,
                    systemId,
                    system.Name,
                    set.Discs.Max(disc => disc.Game.LastPlayedAt),
                    set.Discs.Sum(disc => disc.Game.PlayCount),
                    set.Discs.Aggregate(TimeSpan.Zero, (total, disc) => total + disc.Game.Playtime),
                    viewModel.Model.IsAvailable,
                    viewModel.ShowAchievementMark ? viewModel.GamepadAchievementCountText : null,
                    viewModel)).ToList();
            }, cancellationToken);
        }

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

        public Task LaunchAsync(HandheldGameEntry game) =>
            game.ViewModel is { } viewModel ? owner.LaunchGameCommand.ExecuteAsync(viewModel) : Task.CompletedTask;

        public Task SwitchModeAsync(InterfaceMode mode) => owner.SetInterfaceModeAsync(mode);

        public void Quit() => owner._applicationLifetime?.Shutdown();
    }
}
