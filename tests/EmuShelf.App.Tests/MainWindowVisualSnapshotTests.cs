using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
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
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Settings;
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
        var gamepadGames = systems.Select((system, index) => new GameViewModel(
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
            coverAspectRatio: system.CoverAspectRatio)).ToArray();
        var disc1 = new Game
        {
            Id = 101,
            SystemId = systems[0].Id,
            Path = "/Games/playstation2/Final Fantasy X (Disc 1).chd",
            Title = "Final Fantasy X (Disc 1)",
            DateAdded = DateTimeOffset.UtcNow,
        };
        var disc2 = disc1 with
        {
            Id = 102,
            Path = "/Games/playstation2/Final Fantasy X (Disc 2).chd",
            Title = "Final Fantasy X (Disc 2)",
        };
        gamepadGames[0] = new GameViewModel(
            disc1,
            systems[0].Name,
            systems[0].ShortName,
            systems[0].AccentColor,
            coverAspectRatio: systems[0].CoverAspectRatio,
            discs: [new GameDisc(1, disc1), new GameDisc(2, disc2)],
            selectedDisc: new GameDisc(2, disc2),
            displayTitle: "Final Fantasy X",
            discSelectionKey: "playstation2\u001FFINAL FANTASY X");
        viewModel.Games.ReplaceAll(gamepadGames);
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
            viewModel.OpenFocusedDiscSelectionCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-disc-selection-1280x800.png");
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
            viewModel.FocusedGamepadAchievement = viewModel.GamepadAchievementDetails.Achievements[0];
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-achievements-1280x800.png");
            viewModel.OpenGamepadSearchCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-search-1280x800.png");
            viewModel.CloseGamepadOverlayCommand.Execute(null);
            await viewModel.RemoveFocusedGameCommand.ExecuteAsync(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-remove-1280x800.png");
            viewModel.CloseGamepadOverlayCommand.Execute(null);
            viewModel.OpenGamepadMenuCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-menu-1280x800.png");
            viewModel.RequestDesktopModeFromGamepadCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-desktop-confirmation-1280x800.png");
            viewModel.BackFromGamepadOverlayCommand.Execute(null);
            viewModel.RequestSettingsFromGamepadCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-settings-handoff-1280x800.png");
            viewModel.BackFromGamepadOverlayCommand.Execute(null);
            viewModel.RequestQuitFromGamepadCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-quit-confirmation-1280x800.png");
        }
        finally
        {
            window.Close();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public async Task GamepadFocusMovingDownScrollsTheVirtualizedGrid()
    {
        // Regression: focus moved past the visible rows but the grid never scrolled, so the focus
        // ring walked off-screen and the library appeared stuck at the last visible row.
        var system = KnownSystems.All.Single(candidate => candidate.Id == "playstation2");
        var viewModel = new MainViewModel();
        await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
        viewModel.IsGamepadMode = true;
        viewModel.Games.ReplaceAll(Enumerable.Range(0, 60).Select(index => new GameViewModel(
            new Game
            {
                Id = index + 1,
                SystemId = system.Id,
                Path = $"/Games/{system.Id}/Game {index + 1}.bin",
                Title = $"Sample game {index + 1}",
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name,
            system.ShortName,
            system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio)));
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            viewModel.FocusedGame = viewModel.Games[0];
            await PumpAsync();

            var scroller = window.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .Single(candidate => candidate.Name == "GamepadLibraryScroller");
            var initialOffset = scroller.Offset.Y;

            // Walk down far enough to leave the first viewport regardless of the resolved column count.
            for (var step = 0; step < 6; step++)
            {
                viewModel.MoveGamepadFocusDownCommand.Execute(null);
                await PumpAsync();
            }

            Assert.True(
                viewModel.Games.IndexOf(viewModel.FocusedGame!) > viewModel.GamepadColumnCount,
                "focus should have moved past the first row");
            Assert.True(
                scroller.Offset.Y > initialOffset,
                $"grid should have scrolled to reveal the focused game (offset stayed at {scroller.Offset.Y}).");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task GamepadConfirmationBodyAndActionsUseSeparateLayoutRows()
    {
        var system = KnownSystems.All.Single(candidate => candidate.Id == "playstation2");
        var viewModel = new MainViewModel { IsGamepadMode = true };
        var game = new GameViewModel(
            new Game
            {
                Id = 1,
                SystemId = system.Id,
                Path = "/Games/playstation2/Sample.iso",
                Title = "Sample game",
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name,
            system.ShortName,
            system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio);
        viewModel.Games.ReplaceAll([game]);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = game;

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await viewModel.RemoveFocusedGameCommand.ExecuteAsync(null);
            await PumpAsync();

            var body = window.FindControl<StackPanel>("GamepadRemoveBody");
            var actions = window.FindControl<ItemsControl>("GamepadOverlayOptions");
            Assert.NotNull(body);
            Assert.NotNull(actions);
            Assert.True(
                body.Bounds.Bottom < actions.Bounds.Top,
                $"confirmation body ended at {body.Bounds.Bottom}, actions started at {actions.Bounds.Top}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task GamepadCustomFocusSurfacesDisableTheFluentFocusAdorner()
    {
        var system = KnownSystems.All.Single(candidate => candidate.Id == "playstation2");
        var viewModel = new MainViewModel { IsGamepadMode = true };
        var game = new GameViewModel(
            new Game
            {
                Id = 1,
                SystemId = system.Id,
                Path = "/Games/playstation2/Sample.iso",
                Title = "Sample game",
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name,
            system.ShortName,
            system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio);
        viewModel.Games.ReplaceAll([game]);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = game;

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await PumpAsync();
            var shelfFocusSurfaces = window.GetVisualDescendants()
                .OfType<Control>()
                .Where(control => control.Classes.Contains("gamepad-game") ||
                                  control.Classes.Contains("gamepad-platform") ||
                                  control.Classes.Contains("gamepad-footer-action"))
                .ToArray();
            Assert.NotEmpty(shelfFocusSurfaces);
            Assert.All(shelfFocusSurfaces, control => Assert.Null(control.FocusAdorner));

            viewModel.FocusedGame = game;
            Assert.Same(game, viewModel.FocusedGame);
            viewModel.OpenFocusedGameActionsCommand.Execute(null);
            Assert.Equal(GamepadOverlayKind.Actions, viewModel.GamepadOverlay);
            Assert.NotEmpty(viewModel.GamepadOverlayOptions);
            await PumpAsync();
            var overlayOptions = window.FindControl<ItemsControl>("GamepadOverlayOptions");
            Assert.NotNull(overlayOptions);
            var overlayFocusSurfaces = overlayOptions.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.NotEmpty(overlayFocusSurfaces);
            Assert.All(overlayFocusSurfaces, control => Assert.Null(control.FocusAdorner));
            Assert.All(
                overlayFocusSurfaces,
                control => Assert.True(
                    control.Bounds.Width >= overlayOptions.Bounds.Width - 1,
                    $"overlay option width {control.Bounds.Width} should fill {overlayOptions.Bounds.Width}"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task GamepadPointerHoverFollowsTheShortCoverInsteadOfTheMixedShelfCell()
    {
        var tallSystem = KnownSystems.All.Single(candidate => candidate.Id == "playstation2");
        var shortSystem = KnownSystems.All.Single(candidate => candidate.Id == "nds");
        var viewModel = new MainViewModel { IsGamepadMode = true };
        var tallGame = new GameViewModel(
            new Game
            {
                Id = 1,
                SystemId = tallSystem.Id,
                Path = "/Games/playstation2/Tall.iso",
                Title = "Tall cover",
                DateAdded = DateTimeOffset.UtcNow,
            },
            tallSystem.Name,
            tallSystem.ShortName,
            tallSystem.AccentColor,
            coverAspectRatio: tallSystem.CoverAspectRatio);
        var shortGame = new GameViewModel(
            new Game
            {
                Id = 2,
                SystemId = shortSystem.Id,
                Path = "/Games/nds/Short.nds",
                Title = "Short cover",
                DateAdded = DateTimeOffset.UtcNow,
            },
            shortSystem.Name,
            shortSystem.ShortName,
            shortSystem.AccentColor,
            coverAspectRatio: shortSystem.CoverAspectRatio);
        const double coverWidth = 188;
        var shelfHeight = Math.Max(
            Math.Round(coverWidth / tallSystem.CoverAspectRatio),
            Math.Round(coverWidth / shortSystem.CoverAspectRatio));
        tallGame.ApplyCoverLayout(coverWidth, shelfHeight);
        shortGame.ApplyCoverLayout(coverWidth, shelfHeight);
        viewModel.Games.ReplaceAll([tallGame, shortGame]);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await PumpAsync();
            // Initial desktop bindings settle when the window attaches. Install the synthetic
            // mixed All Games shelf afterward so the headless render sees the visible cards.
            await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
            viewModel.Games.ReplaceAll([tallGame, shortGame]);
            viewModel.HasGames = true;
            viewModel.IsLibraryEmpty = false;
            viewModel.LibraryCountText = "2 games";
            viewModel.FocusedGame = tallGame;
            await PumpAsync();

            var gameButtons = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("gamepad-game"))
                .ToArray();
            Assert.Equal(2, gameButtons.Length);
            var shortButton = gameButtons[1];
            var coverFrame = shortButton.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("gamepad-cover-frame"));
            var hoverRing = shortButton.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("gamepad-hover-ring"));
            var focusRing = shortButton.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("gamepad-focus-ring"));
            viewModel.NotifyGamepadPointerInput();
            var pseudoClasses = (IPseudoClasses)shortButton.Classes;
            pseudoClasses.Add(":pointerover");
            await PumpAsync();

            Assert.False(viewModel.IsGamepadControllerInputActive);
            Assert.Equal(1, hoverRing.Opacity);
            Assert.False(focusRing.IsVisible);
            Assert.Equal(shortGame.CoverHeight, coverFrame.Bounds.Height, 1);
            Assert.Equal(coverFrame.Bounds.Height, hoverRing.Bounds.Height, 1);
            Assert.True(
                shortButton.Bounds.Height > hoverRing.Bounds.Height + 20,
                "the regression requires a button hit target taller than the short cover");
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(shortButton.Background).Color.A);
            await SaveGamepadOverlaySnapshotAsync(
                window,
                Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR"),
                "emushelf-gamepad-short-cover-hover-1280x800.png");

            pseudoClasses.Add(":pressed");
            await PumpAsync();
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(shortButton.Background).Color.A);
            pseudoClasses.Remove(":pressed");
            pseudoClasses.Remove(":pointerover");

            viewModel.FocusedGame = shortGame;
            await PumpAsync();
            Assert.True(focusRing.IsVisible);
            Assert.Equal(coverFrame.Bounds.Height, focusRing.Bounds.Height, 1);
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    // The reveal is posted at Input priority, so drain that queue (Background is lower) and then
    // let a render/layout pass settle before asserting on scroll offsets.
    private static async Task PumpAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
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
    public async Task RenderSaveSyncSettingsInDarkTheme()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
        var configuration = new CloudSaveSyncSettings
        {
            Enabled = true,
            RemoteName = "emushelf-gdrive",
            CloudFolder = "EmuShelf/Saves",
            Pcsx2ConfigDirectory = @"D:\Emulators\PCSX2",
            PpssppMemoryStickDirectory = @"D:\Emulators\PPSSPP\memstick",
        }.NormalizeSaveLocations();
        var cloudSaves = new CloudSaveSyncSettingsContext(
            configuration,
            IsRcloneAvailable: true,
            RcloneExpectedPath: @"D:\EmuShelf\rclone.exe",
            SyncLogPath: @"D:\EmuShelf\Logs\save-sync.log",
            GetPlatforms: () => SaveProviderRegistry.All.Select(descriptor => new CloudSaveSyncPlatformContext(
                descriptor.SystemId,
                descriptor.DisplayName,
                descriptor.SaveShapeDescription,
                descriptor.OverridePlaceholder,
                configuration.GetOverride(descriptor.SystemId),
                LastSuccessUtc: null,
                LastError: null)).ToArray(),
            (systemId, _) => Task.FromResult<string?>(systemId == "psp"
                ? @"D:\Emulators\PPSSPP\memstick\PSP\SAVEDATA"
                : @"D:\Emulators\PCSX2\memcards"),
            (_, _, _, _) => Task.FromResult(CloudSaveSyncConnectResult.Connected),
            _ => Task.CompletedTask,
            (_, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([]))),
            (_, _, _, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([]))),
            (_, _) => { },
            _ => Task.FromResult(true));
        var viewModel = new EmulatorSettingsViewModel(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(
                system => system.Id,
                _ => (EmulatorConfiguration?)null,
                StringComparer.Ordinal),
            new NullEmulatorConfigurationStore(),
            new NullDialogService(),
            cloudSaves: cloudSaves)
        {
            SelectedSection = SettingsSection.Saves,
        };

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
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(780, 620), frame.PixelSize);
            if (outputDirectory is not null)
            {
                Directory.CreateDirectory(outputDirectory);
                using var output = File.Create(Path.Combine(outputDirectory, "emushelf-save-sync-settings-dark.png"));
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
