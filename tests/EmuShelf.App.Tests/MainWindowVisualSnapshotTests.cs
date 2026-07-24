using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

public class MainWindowVisualSnapshotTests
{
    [AvaloniaFact]
    public async Task SearchButton_ExpandsFocusTargetAndCloseClearsFilter()
    {
        var viewModel = new MainViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();
        try
        {
            var trigger = window.FindControl<Button>("SearchTrigger");
            var expanded = window.FindControl<Border>("ExpandedSearch");
            var close = window.FindControl<Button>("CloseSearchButton");
            Assert.NotNull(trigger);
            Assert.NotNull(expanded);
            Assert.NotNull(close);
            Assert.False(viewModel.IsSearchOpen);

            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            Assert.True(viewModel.IsSearchOpen);
            Assert.True(expanded.IsVisible);

            viewModel.SearchText = "metal";
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(viewModel.IsSearchOpen);
            Assert.Equal(string.Empty, viewModel.SearchText);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RecycledCoverElement_RequestsReplacementDataContextCover()
    {
        var viewModel = new MainViewModel();
        await viewModel.ReloadGamesAsync();
        var system = KnownSystems.All.Single(candidate => candidate.Id == "gamecube");
        var first = new GameViewModel(
            new Game
            {
                Id = 1,
                SystemId = system.Id,
                Path = "/games/first.rvz",
                Title = "First",
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name,
            system.ShortName,
            system.AccentColor);
        viewModel.Games.ReplaceAll([first]);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;

        var replacementLoads = 0;
        var replacementCommand = new AsyncRelayCommand<GameViewModel?>(_ =>
        {
            replacementLoads++;
            return Task.CompletedTask;
        });
        var replacement = new GameViewModel(
            new Game
            {
                Id = 2,
                SystemId = system.Id,
                Path = "/games/replacement.rvz",
                Title = "Replacement",
                CoverPath = "/covers/replacement.png",
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name,
            system.ShortName,
            system.AccentColor,
            loadCoverCommand: replacementCommand);

        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 700,
            Height = 600,
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var coverCard = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Classes.Contains("cover-card"));

            // ItemsRepeater can perform this replacement without detaching the element.
            coverCard.DataContext = replacement;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            Assert.Equal(1, replacementLoads);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RenderLibraryInLightAndDarkThemes()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
        var systems = new[]
        {
            KnownSystems.All.Single(candidate => candidate.Id == "gamecube"),
            KnownSystems.All.Single(candidate => candidate.Id == "playstation"),
            KnownSystems.All.Single(candidate => candidate.Id == "playstation2"),
            KnownSystems.All.Single(candidate => candidate.Id == "wii"),
        };
        var viewModel = new MainViewModel();
        await viewModel.ReloadGamesAsync();
        await viewModel.ShowRecentlyAddedCommand.ExecuteAsync(null);
        viewModel.Games.Clear();
        for (var index = 1; index <= 8; index++)
        {
            var system = systems[(index - 1) % systems.Length];
            viewModel.Games.Add(new GameViewModel(
                new Game
                {
                    Id = index,
                    SystemId = system.Id,
                    Path = $"/Games/PlayStation/Library Game {index}.cue",
                    Title = index == 1
                        ? "Super Mario Strikers (USA)"
                        : $"Library Game {index}",
                    IsAvailable = index != 4,
                    DateAdded = DateTimeOffset.UtcNow,
                },
                system.Name,
                system.ShortName,
                system.AccentColor,
                coverAspectRatio: system.CoverAspectRatio));
        }
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.LibraryCountText = "8 games";
        viewModel.SelectedGame = viewModel.Games[0];

        try
        {
            foreach (var (variant, fileName) in new[]
                     {
                         (ThemeVariant.Light, "emushelf-m8-light.png"),
                         (ThemeVariant.Dark, "emushelf-m8-dark.png"),
                     })
            {
                Application.Current!.RequestedThemeVariant = variant;
                var window = new MainWindow
                {
                    DataContext = viewModel,
                    Width = 1180,
                    Height = 760,
                };
                window.Show();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                await Task.Delay(50);
                try
                {
                    using (var warmupFrame = window.CaptureRenderedFrame())
                    {
                        Assert.NotNull(warmupFrame);
                    }
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    Assert.Equal(new PixelSize(1180, 760), frame.PixelSize);
                    if (outputDirectory is not null)
                    {
                        Directory.CreateDirectory(outputDirectory);
                        using var output = File.Create(Path.Combine(outputDirectory, fileName));
                        frame.Save(output, PngBitmapEncoderOptions.Default);
                    }
                }
                finally
                {
                    window.Close();
                }
            }
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public async Task RenderGamepadLibraryAtDeckResolution()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
        var systems = new[]
        {
            KnownSystems.All.Single(candidate => candidate.Id == "playstation2"),
            KnownSystems.All.Single(candidate => candidate.Id == "nds"),
            KnownSystems.All.Single(candidate => candidate.Id == "snes"),
            KnownSystems.All.Single(candidate => candidate.Id == "psp"),
        };
        var viewModel = new MainViewModel();
        await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
        viewModel.IsGamepadMode = true;
        viewModel.Games.ReplaceAll(systems.Select((system, index) => new GameViewModel(
            new Game
            {
                Id = index + 1,
                SystemId = system.Id,
                Path = $"/Games/{system.Id}/Game {index + 1}.bin",
                Title = $"{system.Name} sample game",
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name,
            system.ShortName,
            system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio)));
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.LibraryCountText = "4 games";
        viewModel.FocusedGame = viewModel.Games[0];

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(1280, 800), frame.PixelSize);
            if (outputDirectory is not null)
            {
                Directory.CreateDirectory(outputDirectory);
                using var output = File.Create(Path.Combine(outputDirectory, "emushelf-gamepad-1280x800.png"));
                frame.Save(output, PngBitmapEncoderOptions.Default);
            }

            viewModel.OpenFocusedGameActionsCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-actions-1280x800.png");
            var achievementSnapshot = new RetroAchievementsDetailsSnapshot(
                new RetroAchievementsGameDetails(7, "PlayStation 2 sample game", 2, 1, 1,
                [
                    new RetroAchievementsAchievement(1, "First victory", "Win your first match.", 5, "", 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                    new RetroAchievementsAchievement(2, "Collector", "Find every hidden item.", 10, "", 2, null, null),
                ]),
                DateTimeOffset.UtcNow);
            viewModel.GamepadAchievementDetails = new AchievementDetailsViewModel(
                "PlayStation 2 sample game", 7, new SnapshotDetailsService(achievementSnapshot), new SnapshotAccount(), cached: achievementSnapshot);
            viewModel.GamepadOverlay = GamepadOverlayKind.Achievements;
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-achievements-1280x800.png");
            viewModel.OpenGamepadSearchCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-search-1280x800.png");
            viewModel.CloseGamepadOverlayCommand.Execute(null);
            await viewModel.RemoveFocusedGameCommand.ExecuteAsync(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-remove-1280x800.png");
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    private static async Task SaveGamepadOverlaySnapshotAsync(Window window, string? outputDirectory, string fileName)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(new PixelSize(1280, 800), frame.PixelSize);
        if (outputDirectory is not null)
        {
            Directory.CreateDirectory(outputDirectory);
            using var output = File.Create(Path.Combine(outputDirectory, fileName));
            frame.Save(output, PngBitmapEncoderOptions.Default);
        }
    }

    private sealed class SnapshotDetailsService(RetroAchievementsDetailsSnapshot snapshot) : IRetroAchievementsDetailsService
    {
        public event Action<RetroAchievementsDetailsSnapshot>? DetailsRefreshed { add { } remove { } }
        public RetroAchievementsDetailsSnapshot? GetCached(int retroAchievementsGameId) => snapshot;
        public Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>> RefreshAsync(RetroAchievementsCredentials credentials, int retroAchievementsGameId, CancellationToken cancellationToken = default, bool manual = false) =>
            Task.FromResult(RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>.Success(snapshot));
        public void Clear() { }
    }

    private sealed class SnapshotAccount : IRetroAchievementsAccountService
    {
        public RetroAchievementsAccount? Account => new("Snapshot", "ULID");
        public bool IsConnected => true;
        public RetroAchievementsCredentials? CurrentCredentials => new("Snapshot", "KEY", "ULID");
        public Task<RetroAchievementsConnectionResult> ConnectAsync(string username, string apiKey, CancellationToken cancellationToken = default) => Task.FromResult(RetroAchievementsConnectionResult.Connected);
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task RenderEmulatorSettingsInDarkTheme()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
        var viewModel = new EmulatorSettingsViewModel(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(
                system => system.Id,
                _ => (EmulatorConfiguration?)null,
                StringComparer.Ordinal),
            new NullEmulatorConfigurationStore(),
            new NullDialogService(),
            new LibraryMaintenanceActions(
                systemId => Task.FromResult($"{systemId} rescan complete"),
                () => Task.FromResult("All console folders rescanned")));

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new EmulatorSettingsWindow
        {
            DataContext = viewModel,
            Width = 780,
            Height = 620,
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(50);
            using (var warmupFrame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(warmupFrame);
            }
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(780, 620), frame.PixelSize);
            if (outputDirectory is not null)
            {
                Directory.CreateDirectory(outputDirectory);
                using var output = File.Create(Path.Combine(
                    outputDirectory,
                    "emushelf-m8-settings-dark.png"));
                frame.Save(output, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public async Task ConnectedRetroAchievementsSettings_ShowTheMatchRefreshAction()
    {
        var context = new RetroAchievementsSettingsContext(
            new RetroAchievementsAccount("Player", "ULID-9"),
            IsConnected: true,
            ConnectAsync: (_, _, _, _) => Task.FromResult(new RetroAchievementsConnectionSummary(
                RetroAchievementsConnectionResult.Connected)),
            DisconnectAsync: _ => Task.CompletedTask,
            RefreshMatchesAsync: (_, _) => Task.FromResult<RetroAchievementsLibrarySyncSummary?>(null));
        var viewModel = new EmulatorSettingsViewModel(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(
                system => system.Id,
                _ => (EmulatorConfiguration?)null,
                StringComparer.Ordinal),
            new NullEmulatorConfigurationStore(),
            new NullDialogService(),
            retroAchievements: context)
        {
            SelectedSection = SettingsSection.RetroAchievements,
        };
        var window = new EmulatorSettingsWindow { DataContext = viewModel };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var refresh = window.FindControl<Button>("RefreshRetroAchievementsMatchesButton");
            Assert.NotNull(refresh);
            Assert.True(refresh.IsVisible);
            Assert.True(refresh.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RenderMetadataConsentInDarkTheme()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new MetadataConsentWindow
        {
            DataContext = new MetadataConsentViewModel(3),
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(50);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(500, frame.PixelSize.Width);
            Assert.True(frame.PixelSize.Height > 200);
            if (outputDirectory is not null)
            {
                Directory.CreateDirectory(outputDirectory);
                using var output = File.Create(Path.Combine(
                    outputDirectory,
                    "emushelf-metadata-consent-dark.png"));
                frame.Save(output, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        }
    }
}
