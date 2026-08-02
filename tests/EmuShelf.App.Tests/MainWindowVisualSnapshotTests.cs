using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

public class MainWindowVisualSnapshotTests
{
    [AvaloniaFact]
    public async Task DesktopChrome_KeepsToolbarAndCaptionControlsVisibleInsideWindow()
    {
        var window = new MainWindow
        {
            DataContext = new MainViewModel(),
            Width = 1000,
            Height = 720,
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.True(window.ExtendClientAreaToDecorationsHint);

            var search = window.FindControl<Button>("SearchTrigger");
            var navigation = window.FindControl<Button>("NavigationToggle");
            var grid = window.FindControl<ToggleButton>("GridViewToggle");
            var list = window.FindControl<ToggleButton>("ListViewToggle");
            var gamepad = window.FindControl<Button>("GamepadModeButton");
            var settings = window.FindControl<Button>("SettingsButton");
            var captions = window.FindControl<StackPanel>("CaptionButtons");
            var minimize = window.FindControl<Button>("MinimizeWindowButton");
            var maximize = window.FindControl<Button>("MaximizeWindowButton");
            var close = window.FindControl<Button>("CloseWindowButton");
            Assert.NotNull(search);
            Assert.NotNull(navigation);
            Assert.NotNull(grid);
            Assert.NotNull(list);
            Assert.NotNull(gamepad);
            Assert.NotNull(settings);
            Assert.NotNull(captions);
            Assert.NotNull(minimize);
            Assert.NotNull(maximize);
            Assert.NotNull(close);
            Assert.True(search.IsVisible);
            Assert.True(captions.IsVisible);
            Assert.Equal(3 * 46, captions.Bounds.Width, 1);

            var captionOrigin = captions.TranslatePoint(default, window);
            Assert.NotNull(captionOrigin);
            Assert.True(captionOrigin.Value.X >= 0);
            Assert.True(captionOrigin.Value.X + captions.Bounds.Width <= window.Bounds.Width + 1);

            foreach (var control in new Control[]
                     {
                         navigation, grid, list, search, gamepad, settings,
                         minimize, maximize, close,
                     })
            {
                AssertPointerCanReach(window, control);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertPointerCanReach(Window window, Control target)
    {
        var center = new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);
        var position = target.TranslatePoint(center, window);
        Assert.True(position.HasValue, $"Could not translate {target.Name} into window coordinates.");

        var role = target.GetVisualAncestors()
            .Prepend(target)
            .Select(WindowDecorationProperties.GetElementRole)
            .FirstOrDefault(candidate => candidate != WindowDecorationsElementRole.None);
        Assert.True(
            role == WindowDecorationsElementRole.User,
            $"{target.Name} must remain an interactive client control inside the custom title bar; actual role: {role}.");
    }

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
    public async Task DesktopPointerSelection_IsSharedByGridListContextMenuAndSelectionBar()
    {
        var viewModel = new MainViewModel();
        await viewModel.ReloadGamesAsync();
        var system = KnownSystems.All.Single(candidate => candidate.Id == "gamecube");
        viewModel.Games.ReplaceAll(Enumerable.Range(1, 3).Select(index => new GameViewModel(
            new Game
            {
                Id = index,
                SystemId = system.Id,
                Path = $"/games/Game {index}.rvz",
                Title = $"Game {index}",
                IsAvailable = true,
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name,
            system.ShortName,
            system.AccentColor)));
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;

        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1000,
            Height = 720,
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var tiles = window.GetVisualDescendants()
                .OfType<StackPanel>()
                .Where(control => control.Classes.Contains("game-tile"))
                .ToArray();
            Assert.Equal(3, tiles.Length);

            Click(window, tiles[0]);
            Assert.Equal(["Game 1"], SelectedTitles(viewModel));

            Click(window, tiles[2], RawInputModifiers.Control);
            Assert.Equal(["Game 1", "Game 3"], SelectedTitles(viewModel));

            Click(window, tiles[1], RawInputModifiers.Control | RawInputModifiers.Shift);
            Assert.Equal(["Game 1", "Game 2", "Game 3"], SelectedTitles(viewModel));

            var selectionBar = window.FindControl<Border>("SelectionBar");
            var removeButton = window.FindControl<Button>("RemoveSelectionButton");
            Assert.NotNull(selectionBar);
            Assert.NotNull(removeButton);
            Assert.True(selectionBar.IsVisible);
            Assert.Equal("Remove 3 selected games…", removeButton.Content);

            var contextMenu = tiles[0].ContextMenu;
            Assert.NotNull(contextMenu);
            contextMenu.Open(tiles[0]);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var destructiveItems = contextMenu.Items
                .OfType<MenuItem>()
                .Where(item => item.Header?.ToString()?.StartsWith("Remove", StringComparison.Ordinal) == true)
                .ToArray();
            var destructiveItem = Assert.Single(destructiveItems);
            Assert.Equal("Remove 3 selected games…", destructiveItem.Header);
            Click(window, destructiveItem);
            Assert.Equal(["Game 1", "Game 2", "Game 3"], SelectedTitles(viewModel));
            contextMenu.Close();

            viewModel.IsGridView = false;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var rows = window.GetVisualDescendants()
                .OfType<Grid>()
                .Where(control => control.Classes.Contains("game-row"))
                .ToArray();
            Assert.Equal(3, rows.Length);

            Click(window, rows[1]);
            Assert.Equal(["Game 2"], SelectedTitles(viewModel));
            Assert.Equal("Remove from library…", removeButton.Content);

            Click(window, rows[0], button: MouseButton.Right);
            Assert.Equal(["Game 1"], SelectedTitles(viewModel));
            rows[0].ContextMenu?.Close();

            var list = rows[0].GetVisualAncestors().OfType<ListBox>().Single();
            var emptyPoint = list.TranslatePoint(new Point(100, list.Bounds.Height - 8), window);
            Assert.NotNull(emptyPoint);
            window.MouseDown(emptyPoint.Value, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(emptyPoint.Value, MouseButton.Left, RawInputModifiers.None);
            Assert.Empty(SelectedTitles(viewModel));

            Click(window, rows[2]);
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Empty(SelectedTitles(viewModel));
        }
        finally
        {
            window.Close();
        }

        static string[] SelectedTitles(MainViewModel viewModel) => viewModel.Games
            .Where(game => game.IsSelected)
            .Select(game => game.Title)
            .ToArray();

        static void Click(
            MainWindow window,
            Control control,
            RawInputModifiers modifiers = RawInputModifiers.None,
            MouseButton button = MouseButton.Left)
        {
            var point = control.TranslatePoint(
                new Point(Math.Max(1, control.Bounds.Width / 2), Math.Max(1, control.Bounds.Height / 2)),
                window);
            Assert.NotNull(point);
            window.MouseDown(point.Value, button, modifiers);
            window.MouseUp(point.Value, button, modifiers);
        }
    }

    [AvaloniaFact]
    public async Task DesktopList_WideCoverDoesNotOverlapTitle()
    {
        var viewModel = new MainViewModel { IsGridView = false };
        await viewModel.ReloadGamesAsync();
        var system = KnownSystems.All.Single(candidate => candidate.Id == "snes");
        viewModel.Games.ReplaceAll([
            new GameViewModel(
                new Game
                {
                    Id = 1,
                    SystemId = system.Id,
                    Path = "/games/Addams Family, The (USA).sfc",
                    Title = "Addams Family, The (USA)",
                    IsAvailable = true,
                    DateAdded = DateTimeOffset.UtcNow,
                },
                system.Name,
                system.ShortName,
                system.AccentColor)
        ]);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;

        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 900,
            Height = 620,
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var list = window.FindControl<ListBox>("LibraryList");
            var row = window.GetVisualDescendants()
                .OfType<Grid>()
                .Single(control => control.Classes.Contains("game-row"));
            var cover = row.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => ReferenceEquals(control.DataContext, viewModel.Games[0]));
            var title = row.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Text == viewModel.Games[0].Title);

            Assert.NotNull(list);
            Assert.InRange(row.Bounds.Width, 1, list.Bounds.Width);
            var coverOrigin = cover.TranslatePoint(default, row);
            var titleOrigin = title.TranslatePoint(default, row);
            Assert.NotNull(coverOrigin);
            Assert.NotNull(titleOrigin);
            Assert.True(
                coverOrigin.Value.X + cover.Bounds.Width < titleOrigin.Value.X,
                $"Cover ended at {coverOrigin.Value.X + cover.Bounds.Width}, title began at {titleOrigin.Value.X}.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DesktopGrid_SelectionRingAndDiscChoiceStayInsideTheTile()
    {
        var system = KnownSystems.All.Single(candidate => candidate.Id == "playstation");
        var disc1 = new Game
        {
            Id = 1,
            SystemId = system.Id,
            Path = "/games/Xenogears (Disc 1).chd",
            Title = "Xenogears (Disc 1)",
            IsAvailable = true,
            DateAdded = DateTimeOffset.UtcNow,
        };
        var disc2 = disc1 with
        {
            Id = 2,
            Path = "/games/Xenogears (Disc 2).chd",
            Title = "Xenogears (Disc 2)",
        };
        var multiDisc = new GameViewModel(
            disc1,
            system.Name,
            system.ShortName,
            system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio,
            discs: [new GameDisc(1, disc1), new GameDisc(2, disc2)],
            selectedDisc: new GameDisc(1, disc1),
            displayTitle: "Xenogears");
        multiDisc.IsSelected = true;
        var singleDisc = new GameViewModel(
            disc1 with { Id = 3, Path = "/games/Vagrant Story.chd", Title = "Vagrant Story" },
            system.Name,
            system.ShortName,
            system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio);
        var viewModel = new MainViewModel();
        await viewModel.ReloadGamesAsync();
        viewModel.Games.ReplaceAll([multiDisc, singleDisc]);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;

        var window = new MainWindow { DataContext = viewModel, Width = 1000, Height = 720 };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            var tiles = window.GetVisualDescendants()
                .OfType<StackPanel>()
                .Where(control => control.Classes.Contains("game-tile"))
                .ToArray();
            var selectedTile = tiles.Single(tile => ReferenceEquals(tile.DataContext, multiDisc));
            var regularTile = tiles.Single(tile => ReferenceEquals(tile.DataContext, singleDisc));
            var initialTileHeight = selectedTile.Bounds.Height;

            var hoverPoint = selectedTile.TranslatePoint(
                new Point(selectedTile.Bounds.Width / 2, selectedTile.Bounds.Height / 2),
                window);
            Assert.NotNull(hoverPoint);
            window.MouseMove(hoverPoint.Value);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            Assert.True(selectedTile.IsPointerOver);
            Assert.NotNull(selectedTile.RenderTransform);
            Assert.False(selectedTile.RenderTransform.Value.IsIdentity);
            var hoverOffset = selectedTile.RenderTransform.Value.Transform(default);
            Assert.Equal(0, hoverOffset.X, 1);
            Assert.InRange(hoverOffset.Y, -selectedTile.Margin.Top, 0);

            multiDisc.SetSelectedDisc(new GameDisc(2, disc2));
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            var cover = selectedTile.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Classes.Contains("cover-card"));
            var ring = selectedTile.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Classes.Contains("selection-ring"));
            var badgeText = selectedTile.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Text == "Disc 2 of 2");

            Assert.Equal(initialTileHeight, selectedTile.Bounds.Height, 1);
            Assert.Equal(regularTile.Bounds.Height, selectedTile.Bounds.Height, 1);
            var coverOrigin = cover.TranslatePoint(default, selectedTile);
            var ringOrigin = ring.TranslatePoint(default, selectedTile);
            Assert.NotNull(coverOrigin);
            Assert.NotNull(ringOrigin);
            Assert.True(ringOrigin.Value.X >= coverOrigin.Value.X);
            Assert.True(ringOrigin.Value.Y >= coverOrigin.Value.Y);
            Assert.True(ringOrigin.Value.X + ring.Bounds.Width <= coverOrigin.Value.X + cover.Bounds.Width);
            Assert.True(ringOrigin.Value.Y + ring.Bounds.Height <= coverOrigin.Value.Y + cover.Bounds.Height);
            Assert.Contains(
                badgeText.GetVisualAncestors(),
                ancestor => ancestor is Border border && border.Classes.Contains("disc-badge"));
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
    public async Task GamepadEmptyLibrary_ExposesMenuWithoutOfferingGameActions()
    {
        var viewModel = new MainViewModel
        {
            IsGamepadMode = true,
            HasGames = false,
            IsLibraryEmpty = true,
        };
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1280,
            Height = 800,
        };
        window.Show();
        try
        {
            await PumpAsync();
            var menuButton = window.FindControl<Button>("GamepadEmptyMenuButton");
            Assert.NotNull(menuButton);
            Assert.True(menuButton.IsVisible);

            Assert.NotNull(menuButton.Command);
            menuButton.Command.Execute(menuButton.CommandParameter);
            await PumpAsync();

            Assert.Equal(GamepadOverlayKind.SystemMenu, viewModel.GamepadOverlay);
            var actionsShortcut = window.FindControl<StackPanel>("GamepadSystemMenuActionsShortcut");
            Assert.NotNull(actionsShortcut);
            Assert.False(actionsShortcut.IsVisible);
        }
        finally
        {
            window.Close();
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
        var sharedShelfHeight = gamepadGames.Max(game => game.CoverHeight);
        foreach (var game in gamepadGames)
            game.ApplyCoverLayout(game.CoverWidth, sharedShelfHeight);
        viewModel.Games.ReplaceAll(gamepadGames);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.LibraryCountText = "4 games";
        viewModel.FocusedGame = viewModel.Games[0];
        viewModel.FocusedGame.ApplyAchievementsDisplay(new RetroAchievementsDisplay(
            ShowMark: true,
            ColumnText: "3/62",
            Tooltip: "3 of 62 unlocked."));

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(new PixelSize(1280, 800), frame.PixelSize);
            var gamepadTitles = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(control => control.Classes.Contains("gamepad-tile-title"))
                .ToArray();
            Assert.Equal(4, gamepadTitles.Length);
            var titleBaselines = gamepadTitles
                .Select(control => control.TranslatePoint(default, window)?.Y)
                .ToArray();
            Assert.DoesNotContain(titleBaselines, value => value is null);
            Assert.InRange(titleBaselines.Max()!.Value - titleBaselines.Min()!.Value, 0, 1);
            var focusedDock = window.FindControl<Border>("GamepadFocusedDock");
            var achievementWidget = window.FindControl<Border>("GamepadAchievementWidget");
            var achievementTrack = window.FindControl<Border>("GamepadAchievementTrack");
            var achievementFill = window.FindControl<Border>("GamepadAchievementFill");
            var playButton = window.FindControl<Button>("GamepadPlayButton");
            var subtitle = window.FindControl<TextBlock>("GamepadFocusedSubtitle");
            Assert.NotNull(focusedDock);
            Assert.NotNull(achievementWidget);
            Assert.NotNull(achievementTrack);
            Assert.NotNull(achievementFill);
            Assert.NotNull(playButton);
            Assert.NotNull(subtitle);
            Assert.True(focusedDock.IsVisible);
            Assert.True(achievementWidget.IsVisible);
            Assert.True(playButton.IsVisible);
            Assert.Single(
                focusedDock.GetVisualDescendants().OfType<Border>(),
                control => control.Classes.Contains("gamepad-key"));
            Assert.Equal(122, achievementTrack.Bounds.Width, 1);
            Assert.Equal(4, achievementTrack.Bounds.Height, 1);
            Assert.Equal(3d / 62d, Assert.IsType<ScaleTransform>(achievementFill.RenderTransform).ScaleX, 8);
            Assert.Equal("Final Fantasy X (Disc 2).chd", subtitle.Text);
            Assert.InRange(focusedDock.Bounds.Height, 102, 106);
            Assert.Equal(playButton.Bounds.Height, achievementWidget.Bounds.Height, 1);
            Assert.InRange(playButton.Bounds.Height, 59, 61);
            var widgetOrigin = achievementWidget.TranslatePoint(default, window);
            var playOrigin = playButton.TranslatePoint(default, window);
            var trackOrigin = achievementTrack.TranslatePoint(default, achievementWidget);
            Assert.NotNull(widgetOrigin);
            Assert.NotNull(playOrigin);
            Assert.NotNull(trackOrigin);
            Assert.Equal(playOrigin.Value.Y, widgetOrigin.Value.Y, 1);
            Assert.True(trackOrigin.Value.X >= 0);
            Assert.True(trackOrigin.Value.Y >= 0);
            Assert.True(trackOrigin.Value.X + achievementTrack.Bounds.Width <= achievementWidget.Bounds.Width);
            Assert.True(trackOrigin.Value.Y + achievementTrack.Bounds.Height <= achievementWidget.Bounds.Height);
            Assert.Contains(focusedDock.GetVisualDescendants().OfType<Border>(),
                control => control.Classes.Contains("action-a"));
            Assert.DoesNotContain(focusedDock.GetVisualDescendants().OfType<Border>(),
                control => control.Classes.Contains("action-b") ||
                           control.Classes.Contains("action-x") ||
                           control.Classes.Contains("action-y"));
            if (outputDirectory is not null)
            {
                Directory.CreateDirectory(outputDirectory);
                using var output = File.Create(Path.Combine(outputDirectory, "emushelf-gamepad-1280x800.png"));
                frame.Save(output, PngBitmapEncoderOptions.Default);
            }

            viewModel.FocusedGame!.Title =
                "Shin Megami Tensei: Persona 3 FES — The Journey and The Answer";
            viewModel.OpenFocusedGameActionsCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-actions-1280x800.png");
            AssertGamepadOverlayHeightBelow(window, 600);
            AssertGamepadOverlayTitleFits(window, viewModel.GamepadOverlayTitle);
            Assert.True(viewModel.GamepadOverlayOptions.Single(option => option.Label == "Remove").IsDestructive);
            viewModel.OpenFocusedDiscSelectionCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-disc-selection-1280x800.png");
            AssertGamepadOverlayHeightBelow(window, 440);
            AssertGamepadOverlayTitleFits(window, viewModel.GamepadOverlayTitle);
            var achievementRows = Enumerable.Range(1, 24)
                .Select(index => new RetroAchievementsAchievement(
                    index,
                    index == 1 ? "First victory" : $"Achievement {index}",
                    index == 1 ? "Win your first match without using a continue." : $"Complete challenge {index}.",
                    5 + index % 4 * 5,
                    $"badge-{index}",
                    index,
                    index <= 7 ? DateTimeOffset.UtcNow.AddDays(-index) : null,
                    index <= 3 ? DateTimeOffset.UtcNow.AddDays(-index) : null))
                .ToArray();
            var achievementSnapshot = new RetroAchievementsDetailsSnapshot(
                new RetroAchievementsGameDetails(7, "PlayStation 2 sample game", 24, 7, 3, achievementRows),
                DateTimeOffset.UtcNow);
            viewModel.GamepadAchievementDetails = new AchievementDetailsViewModel(
                "PlayStation 2 sample game", 7, new SnapshotDetailsService(achievementSnapshot), new SnapshotAccount(), cached: achievementSnapshot);
            var badgePaths = new[]
            {
                "playstation2.png",
                "playstation3.png",
                "psp.png",
                "wii.png",
            };
            for (var index = 0; index < viewModel.GamepadAchievementDetails.Achievements.Count; index++)
            {
                using var stream = AssetLoader.Open(new Uri(
                    $"avares://EmuShelf/Assets/PlatformConsoleArt/{badgePaths[index % badgePaths.Length]}"));
                viewModel.GamepadAchievementDetails.Achievements[index].Badge = new Bitmap(stream);
            }
            viewModel.GamepadOverlay = GamepadOverlayKind.Achievements;
            viewModel.FocusedGamepadAchievement = viewModel.GamepadAchievementDetails.VisibleAchievements[0];
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-achievements-1280x800.png");
            var achievementOverlay = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Classes.Contains("gamepad-overlay"));
            Assert.InRange(achievementOverlay.Bounds.Width, 1080, 1120);
            Assert.InRange(achievementOverlay.Bounds.Height, 620, 640);
            var achievementTabs = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(control => control.Classes.Contains("gamepad-achievement-tab"))
                .ToArray();
            Assert.Equal(3, achievementTabs.Length);
            Assert.All(achievementTabs, tab => Assert.Equal(42, tab.Bounds.Height, 1));
            var achievementTiles = window.GetVisualDescendants()
                .OfType<Border>()
                .Where(control => control.Classes.Contains("gamepad-achievement"))
                .ToArray();
            Assert.True(achievementTiles.Length >= 18);
            Assert.All(achievementTiles, tile => Assert.Equal(tile.Bounds.Width, tile.Bounds.Height, 1));
            Assert.Equal("First victory", viewModel.FocusedGamepadAchievement.Title);
            var achievementRepeater = window.FindControl<ItemsRepeater>("GamepadAchievementsRepeater");
            Assert.NotNull(achievementRepeater);
            var pointerTarget = Assert.IsAssignableFrom<Control>(achievementRepeater.TryGetElement(1));
            var pointerPosition = pointerTarget.TranslatePoint(
                new Point(pointerTarget.Bounds.Width / 2, pointerTarget.Bounds.Height / 2),
                window);
            Assert.NotNull(pointerPosition);
            window.MouseDown(pointerPosition.Value, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(pointerPosition.Value, MouseButton.Left, RawInputModifiers.None);
            await PumpAsync();
            Assert.Same(viewModel.GamepadAchievementDetails.VisibleAchievements[1],
                viewModel.FocusedGamepadAchievement);
            Assert.False(viewModel.IsGamepadControllerInputActive);

            viewModel.FocusedGamepadAchievement = viewModel.GamepadAchievementDetails.VisibleAchievements[7];
            viewModel.DispatchGamepadAction(GamepadAction.NextPlatform);
            await PumpAsync();
            Assert.Equal(AchievementDisplayFilter.Locked, viewModel.GamepadAchievementDetails.SelectedFilter);
            Assert.Equal(17, viewModel.GamepadAchievementDetails.VisibleAchievements.Count);
            Assert.Equal(8, viewModel.FocusedGamepadAchievement?.AchievementId);
            Assert.Same(viewModel.FocusedGamepadAchievement, achievementRepeater.TryGetElement(0)?.DataContext);
            viewModel.DispatchGamepadAction(GamepadAction.NextPlatform);
            await PumpAsync();
            Assert.Equal(AchievementDisplayFilter.Unlocked, viewModel.GamepadAchievementDetails.SelectedFilter);
            Assert.Equal(7, viewModel.GamepadAchievementDetails.VisibleAchievements.Count);
            // This broad visual fixture injects details directly instead of opening through the
            // production command, so mirror the production focus fallback after filtering out id 8.
            viewModel.FocusedGamepadAchievement = viewModel.GamepadAchievementDetails.VisibleAchievements[0];
            var focusedGridIndex = viewModel.GamepadAchievementDetails.VisibleAchievements
                .IndexOf(viewModel.FocusedGamepadAchievement!);
            Assert.Equal(0, focusedGridIndex);

            foreach (var expectedSort in new[]
                     {
                         AchievementDisplaySort.Points,
                         AchievementDisplaySort.UnlockedFirst,
                         AchievementDisplaySort.RecentlyUnlocked,
                         AchievementDisplaySort.Default,
                     })
            {
                viewModel.DispatchGamepadAction(GamepadAction.Actions);
                await PumpAsync();

                Assert.Equal(expectedSort, viewModel.GamepadAchievementDetails.SelectedSort);
                Assert.Equal(
                    focusedGridIndex,
                    viewModel.GamepadAchievementDetails.VisibleAchievements
                        .IndexOf(viewModel.FocusedGamepadAchievement!));
                Assert.Single(
                    viewModel.GamepadAchievementDetails.VisibleAchievements,
                    row => row.IsFocused);

                if (expectedSort == AchievementDisplaySort.Points)
                {
                    await SaveGamepadOverlaySnapshotAsync(
                        window,
                        outputDirectory,
                        "emushelf-gamepad-achievements-sorted-1280x800.png");
                }

                var positions = new HashSet<(int X, int Y)>();
                for (var index = 0;
                     index < viewModel.GamepadAchievementDetails.VisibleAchievements.Count;
                     index++)
                {
                    var element = achievementRepeater.TryGetElement(index);
                    Assert.NotNull(element);
                    Assert.Same(
                        viewModel.GamepadAchievementDetails.VisibleAchievements[index],
                        element.DataContext);
                    Assert.Equal(element.Bounds.Width, element.Bounds.Height, 1);
                    Assert.True(positions.Add(
                        ((int)Math.Round(element.Bounds.X), (int)Math.Round(element.Bounds.Y))),
                        $"achievement index {index} overlaps another sorted grid element");
                }
            }
            viewModel.OpenGamepadSearchCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-search-1280x800.png");
            AssertGamepadOverlayHeightBelow(window, 460);
            viewModel.CloseGamepadOverlayCommand.Execute(null);
            await viewModel.RemoveFocusedGameCommand.ExecuteAsync(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-remove-1280x800.png");
            viewModel.CloseGamepadOverlayCommand.Execute(null);
            viewModel.OpenGamepadMenuCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-menu-1280x800.png");
            AssertGamepadOverlayHeightBelow(window, 500);
            var systemMenuShortcuts = window.FindControl<Grid>("GamepadSystemMenuShortcuts");
            Assert.NotNull(systemMenuShortcuts);
            Assert.True(systemMenuShortcuts.IsVisible);
            Assert.Contains(systemMenuShortcuts.GetVisualDescendants().OfType<Border>(),
                control => control.IsVisible && control.Classes.Contains("action-x"));
            Assert.Contains(systemMenuShortcuts.GetVisualDescendants().OfType<Border>(),
                control => control.IsVisible && control.Classes.Contains("action-y"));
            Assert.True(viewModel.GamepadOverlayOptions.Single(option => option.Label == "Quit EmuShelf").IsDestructive);
            viewModel.RequestDesktopModeFromGamepadCommand.Execute(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-desktop-confirmation-1280x800.png");
            viewModel.BackFromGamepadOverlayCommand.Execute(null);
            await viewModel.RequestSettingsFromGamepadCommand.ExecuteAsync(null);
            await SaveGamepadOverlaySnapshotAsync(window, outputDirectory, "emushelf-gamepad-settings-1280x800.png");
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
    public async Task RenderGamepadLibraryAtLivingRoomResolution()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
        var systems = KnownSystems.All.Take(10).ToArray();
        var viewModel = new MainViewModel();
        await viewModel.ShowAllGamesCommand.ExecuteAsync(null);
        viewModel.IsGamepadMode = true;
        viewModel.GamepadPlatforms.Clear();
        foreach (var system in systems)
            viewModel.GamepadPlatforms.Add(new GamepadPlatformTabViewModel(system));

        var games = Enumerable.Range(0, 18)
            .Select(index =>
            {
                var system = systems[index % systems.Length];
                return new GameViewModel(
                    new Game
                    {
                        Id = index + 1,
                        SystemId = system.Id,
                        Path = $"/Games/{system.Id}/Sample Game {index + 1}.bin",
                        Title = $"Sample Game {index + 1}",
                        IsAvailable = true,
                        DateAdded = DateTimeOffset.UtcNow,
                    },
                    system.Name,
                    system.ShortName,
                    system.AccentColor,
                    coverAspectRatio: system.CoverAspectRatio);
            })
            .ToArray();
        const double coverWidth = 202;
        var shelfHeight = games.Max(game => Math.Round(coverWidth / game.CoverAspectRatio));
        foreach (var game in games)
            game.ApplyCoverLayout(coverWidth, shelfHeight);
        games[6].ApplyAchievementsDisplay(new RetroAchievementsDisplay(
            ShowMark: true,
            ColumnText: "12/50",
            Tooltip: "12 of 50 unlocked."));
        viewModel.Games.ReplaceAll(games);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.LibraryCountText = "18 games";
        viewModel.FocusedGame = games[6];

        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new MainWindow { DataContext = viewModel, Width = 1920, Height = 1080 };
        window.Show();
        try
        {
            await PumpAsync();

            var platformButtons = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("gamepad-platform"))
                .ToArray();
            Assert.Equal(systems.Length + 1, platformButtons.Length);
            Assert.All(platformButtons, button => Assert.InRange(button.Bounds.Height, 53, 55));

            var achievementWidget = window.FindControl<Border>("GamepadAchievementWidget");
            var playButton = window.FindControl<Button>("GamepadPlayButton");
            var focusedDock = window.FindControl<Border>("GamepadFocusedDock");
            var libraryScroller = window.FindControl<ScrollViewer>("GamepadLibraryScroller");
            Assert.NotNull(achievementWidget);
            Assert.NotNull(playButton);
            Assert.NotNull(focusedDock);
            Assert.NotNull(libraryScroller);
            Assert.Equal(ScrollBarVisibility.Hidden, libraryScroller.VerticalScrollBarVisibility);
            Assert.InRange(focusedDock.Bounds.Height, 102, 106);
            Assert.Equal(playButton.Bounds.Height, achievementWidget.Bounds.Height, 1);
            Assert.InRange(playButton.Bounds.Height, 59, 61);

            var widgetOrigin = achievementWidget.TranslatePoint(default, window);
            var playOrigin = playButton.TranslatePoint(default, window);
            var dockOrigin = focusedDock.TranslatePoint(default, window);
            Assert.NotNull(widgetOrigin);
            Assert.NotNull(playOrigin);
            Assert.NotNull(dockOrigin);
            Assert.Equal(playOrigin.Value.Y, widgetOrigin.Value.Y, 1);
            Assert.True(dockOrigin.Value.Y + focusedDock.Bounds.Height <= window.Bounds.Height + 1);

            await SaveGamepadOverlaySnapshotAsync(
                window,
                outputDirectory,
                "emushelf-gamepad-1920x1080.png",
                new PixelSize(1920, 1080));
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        }
    }

    [AvaloniaFact]
    public async Task GamepadAchievements_LargeVirtualizedGridStartsInTheTopLeftCell()
    {
        var system = KnownSystems.All.Single(candidate => candidate.Id == "nds");
        var game = new GameViewModel(
            new Game
            {
                Id = 86,
                SystemId = system.Id,
                Path = "/games/large-achievement-set.nds",
                Title = "Large achievement set",
                IsAvailable = true,
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name,
            system.ShortName,
            system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio);
        game.ApplyAchievementLink(8600);
        var achievements = Enumerable.Range(1, 86)
            .Select(index => new RetroAchievementsAchievement(
                index,
                $"Achievement {index}",
                $"Complete challenge {index}.",
                5 + index % 4 * 5,
                "",
                index,
                index <= 7 ? DateTimeOffset.UtcNow.AddDays(-index) : null,
                null))
            .ToArray();
        var snapshot = new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(8600, game.Title, 86, 7, 0, achievements),
            DateTimeOffset.UtcNow);
        var viewModel = new MainViewModel(
            new EmptyGameLibrary(),
            new NullFolderScanner(),
            new NoImportRules(),
            new AlwaysAvailableChecker(),
            new NullDialogService(),
            KnownSystems.All,
            retroAccount: new SnapshotAccount(),
            retroDetails: new SnapshotDetailsService(snapshot))
        {
            IsGamepadMode = true,
            HasGames = true,
            IsLibraryEmpty = false,
            FocusedGame = game,
        };
        viewModel.Games.Add(game);

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await viewModel.OpenFocusedAchievementsCommand.ExecuteAsync(null);
            await PumpAsync();

            var badgePaths = new[] { "playstation2.png", "playstation3.png", "psp.png", "wii.png" };
            for (var index = 0; index < viewModel.GamepadAchievementDetails!.Achievements.Count; index++)
            {
                using var stream = AssetLoader.Open(new Uri(
                    $"avares://EmuShelf/Assets/PlatformConsoleArt/{badgePaths[index % badgePaths.Length]}"));
                viewModel.GamepadAchievementDetails.Achievements[index].Badge = new Bitmap(stream);
            }
            await PumpAsync();

            var repeater = window.FindControl<ItemsRepeater>("GamepadAchievementsRepeater");
            Assert.NotNull(repeater);
            AssertTopLeftCellIsOccupied(repeater, achievements.Length);

            viewModel.FocusedGamepadAchievement = viewModel.GamepadAchievementDetails.VisibleAchievements[7];
            var revisionBeforeFilter = viewModel.GamepadAchievementLayoutRevision;
            viewModel.DispatchGamepadAction(GamepadAction.NextPlatform);
            await PumpAsync();
            Assert.Equal(AchievementDisplayFilter.Locked, viewModel.GamepadAchievementDetails.SelectedFilter);
            Assert.Equal(8, viewModel.FocusedGamepadAchievement?.AchievementId);
            Assert.Equal(0, viewModel.GamepadAchievementDetails.VisibleAchievements
                .IndexOf(viewModel.FocusedGamepadAchievement!));
            Assert.Equal(revisionBeforeFilter + 1, viewModel.GamepadAchievementLayoutRevision);
            Assert.Same(viewModel.FocusedGamepadAchievement, repeater.TryGetElement(0)?.DataContext);
            AssertTopLeftCellIsOccupied(repeater, viewModel.GamepadAchievementDetails.VisibleAchievements.Count);

            viewModel.DispatchGamepadAction(GamepadAction.PreviousPlatform);
            await PumpAsync();
            Assert.Equal(AchievementDisplayFilter.All, viewModel.GamepadAchievementDetails.SelectedFilter);

            for (var cycle = 0; cycle < Enum.GetValues<AchievementDisplaySort>().Length; cycle++)
            {
                viewModel.DispatchGamepadAction(GamepadAction.Actions);
                await PumpAsync();
                AssertTopLeftCellIsOccupied(repeater, achievements.Length);
            }

            await SaveGamepadOverlaySnapshotAsync(
                window,
                Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR"),
                "emushelf-gamepad-achievements-large-1280x800.png");
        }
        finally
        {
            viewModel.CloseGamepadOverlayCommand.Execute(null);
            window.Close();
        }

        static void AssertTopLeftCellIsOccupied(ItemsRepeater repeater, int count)
        {
            var realized = Enumerable.Range(0, count)
                .Select(repeater.TryGetElement)
                .Where(element => element is not null)
                .Cast<Control>()
                .ToArray();
            Assert.True(realized.Length > 12);
            var first = repeater.TryGetElement(0);
            Assert.NotNull(first);
            Assert.Equal(realized.Min(element => element.Bounds.X), first.Bounds.X, 1);
            Assert.Equal(realized.Min(element => element.Bounds.Y), first.Bounds.Y, 1);
        }
    }

    [AvaloniaFact]
    public async Task GamepadAchievements_EmptyCacheShowsUsefulStateInsteadOfBlankPanel()
    {
        var system = KnownSystems.All.Single(candidate => candidate.Id == "playstation");
        var game = new GameViewModel(
            new Game
            {
                Id = 77,
                SystemId = system.Id,
                Path = "/games/achievements.cue",
                Title = "Achievement sample",
                IsAvailable = true,
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name,
            system.ShortName,
            system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio);
        game.ApplyAchievementLink(7007);
        var viewModel = new MainViewModel(
            new EmptyGameLibrary(),
            new NullFolderScanner(),
            new NoImportRules(),
            new AlwaysAvailableChecker(),
            new NullDialogService(),
            KnownSystems.All,
            retroAccount: new DisconnectedSnapshotAccount(),
            retroDetails: new EmptySnapshotDetailsService())
        {
            IsGamepadMode = true,
            HasGames = true,
            IsLibraryEmpty = false,
            FocusedGame = game,
        };
        viewModel.Games.Add(game);

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await viewModel.OpenFocusedAchievementsCommand.ExecuteAsync(null);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            Assert.Equal(GamepadOverlayKind.Achievements, viewModel.GamepadOverlay);
            Assert.NotNull(viewModel.GamepadAchievementDetails);
            Assert.False(viewModel.GamepadAchievementDetails.HasAchievements);
            Assert.Contains("Connect RetroAchievements", viewModel.GamepadAchievementDetails.StatusText);
            var emptyTitle = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Text == "No achievement details cached");
            var emptyDescription = window.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(control => control.Text?.StartsWith("Reconnect to load") == true);
            Assert.True(emptyTitle.IsVisible);
            Assert.True(emptyDescription.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task GamepadAchievements_ScrollBodyAndHintsStayInsideMinimumWindow()
    {
        var achievements = Enumerable.Range(1, 12)
            .Select(index => new RetroAchievementsAchievement(
                index,
                $"Achievement {index}",
                "A deliberately wrapping description that keeps each achievement row realistic.",
                5,
                "",
                index,
                null,
                null))
            .ToArray();
        var snapshot = new RetroAchievementsDetailsSnapshot(
            new RetroAchievementsGameDetails(7, "A long achievement game title", 12, 0, 0, achievements),
            DateTimeOffset.UtcNow);
        var viewModel = new MainViewModel { IsGamepadMode = true };
        viewModel.GamepadAchievementDetails = new AchievementDetailsViewModel(
            "A long achievement game title",
            7,
            new SnapshotDetailsService(snapshot),
            new SnapshotAccount(),
            cached: snapshot);
        viewModel.GamepadOverlay = GamepadOverlayKind.Achievements;
        viewModel.FocusedGamepadAchievement = viewModel.GamepadAchievementDetails.Achievements[0];

        var window = new MainWindow { DataContext = viewModel, Width = 900, Height = 560 };
        window.Show();
        try
        {
            await PumpAsync();
            var host = window.FindControl<Panel>("GamepadOverlayHost");
            var hints = window.FindControl<StackPanel>("GamepadOverlayHints");
            var scroller = window.FindControl<ScrollViewer>("GamepadAchievementsScroller");
            var overlay = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Classes.Contains("gamepad-overlay"));
            Assert.NotNull(host);
            Assert.NotNull(hints);
            Assert.NotNull(scroller);

            var overlayOrigin = overlay.TranslatePoint(default, window);
            var hostOrigin = host.TranslatePoint(default, window);
            var hintsOrigin = hints.TranslatePoint(default, overlay);
            Assert.NotNull(overlayOrigin);
            Assert.NotNull(hostOrigin);
            Assert.NotNull(hintsOrigin);
            Assert.True(overlayOrigin.Value.Y >= hostOrigin.Value.Y);
            Assert.True(overlayOrigin.Value.Y + overlay.Bounds.Height <= hostOrigin.Value.Y + host.Bounds.Height + 1);
            Assert.True(hintsOrigin.Value.Y + hints.Bounds.Height <= overlay.Bounds.Height - overlay.Padding.Bottom + 1);
            Assert.InRange(scroller.Bounds.Height, 1, 419);
            var cards = scroller.GetVisualDescendants()
                .OfType<Border>()
                .Where(control => control.Classes.Contains("gamepad-achievement"))
                .ToArray();
            var scrollBar = scroller.GetVisualDescendants()
                .OfType<ScrollBar>()
                .Single(control => control.Orientation == Orientation.Vertical);
            Assert.InRange(cards.Length, 1, achievements.Length - 1);
            Assert.Equal(ScrollBarVisibility.Hidden, scroller.VerticalScrollBarVisibility);
            Assert.Equal(0, scrollBar.Bounds.Width, 1);
            var cardOrigin = cards[0].TranslatePoint(default, scroller);
            Assert.NotNull(cardOrigin);
            Assert.True(
                cardOrigin.Value.X >= 0 && cardOrigin.Value.X + cards[0].Bounds.Width <= scroller.Bounds.Width + 1,
                "achievement cards should remain inside the clipped scroller viewport");
            await SaveGamepadOverlaySnapshotAsync(
                window,
                Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR"),
                "emushelf-gamepad-achievements-900x560.png",
                new PixelSize(900, 560));
        }
        finally
        {
            viewModel.CloseGamepadOverlayCommand.Execute(null);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task GamepadLongOptionList_RevealsTheFocusedOption()
    {
        var system = KnownSystems.All.Single(candidate => candidate.Id == "playstation2");
        var discs = Enumerable.Range(1, 12)
            .Select(number => new GameDisc(number, new Game
            {
                Id = number,
                SystemId = system.Id,
                Path = $"/Games/playstation2/Sample (Disc {number}).chd",
                Title = $"Sample (Disc {number})",
                DateAdded = DateTimeOffset.UtcNow,
            }))
            .ToArray();
        var game = new GameViewModel(
            discs[0].Game,
            system.Name,
            system.ShortName,
            system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio,
            discs: discs,
            selectedDisc: discs[0],
            displayTitle: "Sample",
            discSelectionKey: "playstation2\u001FSAMPLE");
        var viewModel = new MainViewModel { IsGamepadMode = true };
        viewModel.Games.ReplaceAll([game]);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = game;

        var window = new MainWindow { DataContext = viewModel, Width = 900, Height = 560 };
        window.Show();
        try
        {
            viewModel.OpenFocusedDiscSelectionCommand.Execute(null);
            await PumpAsync();
            var scroller = window.FindControl<ScrollViewer>("GamepadOverlayOptionsScroller");
            Assert.NotNull(scroller);
            var initialOffset = scroller.Offset.Y;

            for (var step = 0; step < 10; step++)
            {
                viewModel.MoveGamepadOverlayDownCommand.Execute(null);
                await PumpAsync();
            }

            Assert.Equal(10, viewModel.GamepadOverlaySelectionIndex);
            Assert.True(
                scroller.Offset.Y > initialOffset,
                $"options should scroll to reveal the focused row (offset stayed at {scroller.Offset.Y})");
        }
        finally
        {
            viewModel.CloseGamepadOverlayCommand.Execute(null);
            window.Close();
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
            var bodyOrigin = body.TranslatePoint(default, window);
            var actionsOrigin = actions.TranslatePoint(default, window);
            Assert.NotNull(bodyOrigin);
            Assert.NotNull(actionsOrigin);
            var bodyBottom = bodyOrigin.Value.Y + body.Bounds.Height;
            Assert.True(
                bodyBottom < actionsOrigin.Value.Y,
                $"confirmation body ended at {bodyBottom}, actions started at {actionsOrigin.Value.Y}");
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
                                  control.Classes.Contains("gamepad-play-action"))
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
            // The controller focus ring is no longer a per-tile layer, so it can never appear inside a
            // hovered non-focused tile: it is the single GamepadSelectorRing overlay, positioned over
            // whichever tile is focused. Assert it is absent from this hovered short tile, and take the
            // external overlay for the focus assertions below.
            Assert.Empty(shortButton.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("gamepad-focus-ring")));
            var focusRing = window.FindControl<Border>("GamepadSelectorRing");
            Assert.NotNull(focusRing);
            viewModel.NotifyGamepadPointerInput();
            var pseudoClasses = (IPseudoClasses)shortButton.Classes;
            pseudoClasses.Add(":pointerover");
            await PumpAsync();

            Assert.False(viewModel.IsGamepadControllerInputActive);
            Assert.Equal(1, hoverRing.Opacity);
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
            // The focus frame is drawn at the cover bounds (no overflow) so it is never clipped.
            Assert.Equal(coverFrame.Bounds.Height, focusRing.Bounds.Height, 1);
            Assert.Equal(coverFrame.Bounds.Width, focusRing.Bounds.Width, 1);
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

    private static async Task SaveGamepadOverlaySnapshotAsync(
        Window window,
        string? outputDirectory,
        string fileName,
        PixelSize? expectedSize = null)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(expectedSize ?? new PixelSize(1280, 800), frame.PixelSize);
        if (outputDirectory is not null)
        {
            Directory.CreateDirectory(outputDirectory);
            using var output = File.Create(Path.Combine(outputDirectory, fileName));
            frame.Save(output, PngBitmapEncoderOptions.Default);
        }
    }

    private static void AssertGamepadOverlayHeightBelow(Window window, double previousFixedHeight)
    {
        var overlay = window.GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("gamepad-overlay"));
        Assert.InRange(overlay.Bounds.Height, 1, previousFixedHeight - 1);
    }

    private static void AssertGamepadOverlayTitleFits(Window window, string title)
    {
        var overlay = window.GetVisualDescendants()
            .OfType<Border>()
            .Single(control => control.Classes.Contains("gamepad-overlay"));
        var titleBlock = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(control => control.IsVisible && control.Text == title);
        var origin = titleBlock.TranslatePoint(default, overlay);
        Assert.NotNull(origin);
        Assert.True(origin.Value.X >= 0);
        Assert.True(origin.Value.X + titleBlock.Bounds.Width <= overlay.Bounds.Width + 1);
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

    private sealed class DisconnectedSnapshotAccount : IRetroAchievementsAccountService
    {
        public RetroAchievementsAccount? Account => null;
        public bool IsConnected => false;
        public RetroAchievementsCredentials? CurrentCredentials => null;
        public Task<RetroAchievementsConnectionResult> ConnectAsync(
            string username,
            string apiKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(RetroAchievementsConnectionResult.Offline);
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptySnapshotDetailsService : IRetroAchievementsDetailsService
    {
        public event Action<RetroAchievementsDetailsSnapshot>? DetailsRefreshed { add { } remove { } }
        public RetroAchievementsDetailsSnapshot? GetCached(int retroAchievementsGameId) => null;
        public Task<RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>> RefreshAsync(
            RetroAchievementsCredentials credentials,
            int retroAchievementsGameId,
            CancellationToken cancellationToken = default,
            bool manual = false) =>
            Task.FromResult(RetroAchievementsResponse<RetroAchievementsDetailsSnapshot>.Failure(
                RetroAchievementsRequestStatus.Offline));
        public void Clear() { }
    }

    [AvaloniaFact]
    public async Task GamepadSettingsAt1280x800_KeepRowsFocusAndConfirmationInsideTheOverlay()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
        var configuration = new CloudSaveSyncSettings
        {
            Enabled = true,
            RemoteName = "emushelf-gdrive",
            CloudFolder = "EmuShelf/Saves",
        };
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
                Override: null,
                LastSuccessUtc: null,
                LastError: null,
                SupportsSaveStates: descriptor.SystemId is "playstation2" or "psp")).ToArray(),
            (systemId, _) => Task.FromResult<string?>(@"D:\Saves\" + systemId),
            (_, _, _, _, _, _) => Task.FromResult(CloudSaveSyncConnectResult.Connected),
            _ => Task.CompletedTask,
            (_, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([]))),
            (_, _, _, _) => Task.FromResult(CloudSaveSyncOutcome.Completed(new SaveSyncReport([]))),
            (_, _) => { },
            _ => Task.FromResult(true));
        var maintenance = new LibraryMaintenanceActions(
            _ => Task.FromResult(string.Empty),
            () => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty),
            () => Task.FromResult(string.Empty),
            () => true,
            _ => Task.CompletedTask);
        var retroAchievements = new RetroAchievementsSettingsContext(
            null,
            false,
            (_, _, _, _) => Task.FromResult(new RetroAchievementsConnectionSummary(
                RetroAchievementsConnectionResult.Connected)),
            _ => Task.CompletedTask,
            (_, _) => Task.FromResult<RetroAchievementsLibrarySyncSummary?>(null));
        var textureResult = new TexturePackInventoryResult(
            TexturePackLibraryMap.Empty,
            [new TexturePackPlatformState(
                "gamecube",
                "GameCube",
                @"D:\Dolphin\Load\Textures",
                false,
                TexturePackRootStatus.Ready,
                false,
                TexturePackLoadingStatus.Enabled,
                null)]);
        var texturePacks = new TexturePackSettingsContext(
            () => textureResult,
            () => true,
            _ => Task.FromResult(textureResult),
            (_, _) => { },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["gamecube"] = "Use the Dolphin-detected folder",
            },
            () => new Dictionary<long, string>());
        var desktopSettings = new EmulatorSettingsViewModel(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(
                system => system.Id,
                _ => (EmulatorConfiguration?)null,
                StringComparer.Ordinal),
            new NullEmulatorConfigurationStore(),
            new NullDialogService(),
            maintenance,
            retroAchievements: retroAchievements,
            cloudSaves: cloudSaves,
            texturePacks: texturePacks);
        var gamepadSettings = new GamepadSettingsViewModel(desktopSettings)
        {
            SelectedSection = SettingsSection.General,
        };
        var paritySections = new[]
        {
            (SettingsSection.General, "general."),
            (SettingsSection.RetroAchievements, "retro."),
            (SettingsSection.Saves, "saves."),
            (SettingsSection.TexturePacks, "textures."),
        };
        var desktopFieldIds = new Dictionary<SettingsSection, string[]>();
        foreach (var (section, prefix) in paritySections)
            desktopFieldIds[section] = await CaptureDesktopFieldIdsAsync(section, prefix);
        desktopSettings.SelectedSection = SettingsSection.General;
        var viewModel = new MainViewModel
        {
            IsGamepadMode = true,
            GamepadSettings = gamepadSettings,
            GamepadOverlay = GamepadOverlayKind.Settings,
        };
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1280,
            Height = 800,
        };
        window.Show();
        try
        {
            await PumpAsync();
            await SaveGamepadOverlaySnapshotAsync(
                window,
                outputDirectory,
                "emushelf-gamepad-settings-general-1280x800.png");

            var overlay = window.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Classes.Contains("gamepad-overlay"));
            var host = window.FindControl<Panel>("GamepadOverlayHost");
            var scroller = window.FindControl<ScrollViewer>("GamepadSettingsScroller");
            var repeater = window.FindControl<ItemsRepeater>("GamepadSettingsRows");
            Assert.NotNull(host);
            Assert.NotNull(scroller);
            Assert.NotNull(repeater);
            Assert.Equal(host.Bounds.Width, overlay.Bounds.Width, 1);
            Assert.Equal(host.Bounds.Height, overlay.Bounds.Height, 1);

            var switches = window.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.IsVisible && border.Classes.Contains("gamepad-settings-switch"))
                .ToArray();
            Assert.Equal(2, switches.Length);
            Assert.All(switches, toggle =>
            {
                Assert.InRange(toggle.Bounds.Width, 138, 142);
                Assert.InRange(toggle.Bounds.Height, 46, 50);
            });
            AssertGamepadSettingsParity(SettingsSection.General, "general.");
            var navigationButtons = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.IsVisible && button.Classes.Contains("gamepad-settings-nav"))
                .ToArray();
            Assert.Equal(4, navigationButtons.Length);
            Assert.All(
                navigationButtons,
                button => Assert.Equal(navigationButtons[0].Bounds.Width, button.Bounds.Width, 1));
            var saveButton = window.FindControl<Button>("GamepadSettingsSaveButton");
            Assert.NotNull(saveButton);
            Assert.Equal(navigationButtons[0].Bounds.Width, saveButton.Bounds.Width, 1);

            gamepadSettings.SelectedSection = SettingsSection.RetroAchievements;
            await PumpAsync();
            await SaveGamepadOverlaySnapshotAsync(
                window,
                outputDirectory,
                "emushelf-gamepad-settings-retro-1280x800.png");
            AssertGamepadSettingsParity(SettingsSection.RetroAchievements, "retro.");

            gamepadSettings.SelectedSection = SettingsSection.TexturePacks;
            await PumpAsync();
            await SaveGamepadOverlaySnapshotAsync(
                window,
                outputDirectory,
                "emushelf-gamepad-settings-textures-1280x800.png");
            AssertGamepadSettingsParity(SettingsSection.TexturePacks, "textures.");
            var choices = window.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.IsVisible && border.Classes.Contains("gamepad-settings-affordance"))
                .ToArray();
            Assert.True(choices.Length >= 2);

            gamepadSettings.SelectedSection = SettingsSection.Saves;
            await PumpAsync();
            await SaveGamepadOverlaySnapshotAsync(
                window,
                outputDirectory,
                "emushelf-gamepad-settings-saves-1280x800.png");
            AssertGamepadSettingsParity(SettingsSection.Saves, "saves.");

            var visibleRows = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.IsVisible && button.Classes.Contains("gamepad-settings-row"))
                .ToArray();
            Assert.True(visibleRows.Length >= 3);
            Assert.Single(
                visibleRows,
                button => button.DataContext is GamepadSettingsRowViewModel { IsFocused: true });
            Assert.All(visibleRows, row => Assert.InRange(row.Bounds.Height, 84, 102));
            // Full-width rows fill the repeater; grouped rows under a platform header are indented.
            var fullRowWidth = scroller.Bounds.Width - 18;
            Assert.All(visibleRows, row => Assert.Equal(
                row.Classes.Contains("grouped") ? fullRowWidth - 24 : fullRowWidth,
                row.Bounds.Width,
                1));

            Assert.True(saveButton.IsVisible);
            Assert.DoesNotContain(saveButton, visibleRows);

            for (var index = 0; index < 12; index++)
                viewModel.DispatchGamepadAction(GamepadAction.NavigateDown);
            await PumpAsync();
            var focused = window.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.IsVisible &&
                    button.Classes.Contains("gamepad-settings-row") &&
                    button.DataContext is GamepadSettingsRowViewModel { IsFocused: true });
            var focusedOrigin = focused.TranslatePoint(default, scroller);
            Assert.NotNull(focusedOrigin);
            Assert.True(focusedOrigin.Value.Y >= -1);
            Assert.True(focusedOrigin.Value.Y + focused.Bounds.Height <= scroller.Bounds.Height + 1);

            // Saves is grouped by platform: headers exist, focus never lands on one, and member rows
            // carry their platform id so the leading artwork can render.
            Assert.Contains(gamepadSettings.Rows, row => row.IsHeader);
            Assert.False(gamepadSettings.FocusedRow!.IsHeader);
            Assert.All(
                gamepadSettings.Rows.Where(row => row.IsGrouped),
                row => Assert.False(string.IsNullOrEmpty(row.SystemId)));

            window.Height = 720;
            await PumpAsync();
            await SaveGamepadOverlaySnapshotAsync(
                window,
                outputDirectory,
                "emushelf-gamepad-settings-saves-1280x720.png",
                new PixelSize(1280, 720));
            Assert.Equal(host.Bounds.Width, overlay.Bounds.Width, 1);
            Assert.Equal(host.Bounds.Height, overlay.Bounds.Height, 1);
            var resizedFocusedOrigin = focused.TranslatePoint(default, scroller);
            Assert.NotNull(resizedFocusedOrigin);
            Assert.True(resizedFocusedOrigin.Value.Y >= -1);
            Assert.True(resizedFocusedOrigin.Value.Y + focused.Bounds.Height <= scroller.Bounds.Height + 1);

            window.Width = 2048;
            window.Height = 1152;
            await PumpAsync();
            await SaveGamepadOverlaySnapshotAsync(
                window,
                outputDirectory,
                "emushelf-gamepad-settings-saves-2048x1152.png",
                new PixelSize(2048, 1152));
            Assert.Equal(host.Bounds.Width, overlay.Bounds.Width, 1);
            Assert.Equal(host.Bounds.Height, overlay.Bounds.Height, 1);
            var wideVisibleRows = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.IsVisible && button.Classes.Contains("gamepad-settings-row"))
                .ToArray();
            Assert.True(wideVisibleRows.Length >= visibleRows.Length);
            var wideFullRowWidth = scroller.Bounds.Width - 18;
            Assert.All(wideVisibleRows, row => Assert.Equal(
                row.Classes.Contains("grouped") ? wideFullRowWidth - 24 : wideFullRowWidth,
                row.Bounds.Width,
                1));

            var replaceLocal = gamepadSettings.Rows.First(row =>
                row.Key.EndsWith("replace-local", StringComparison.Ordinal));
            await replaceLocal.SelectCommand.ExecuteAsync(null);
            await PumpAsync();
            var keep = window.FindControl<Button>("GamepadSettingsKeepButton");
            var confirm = window.FindControl<Button>("GamepadSettingsConfirmButton");
            Assert.NotNull(keep);
            Assert.NotNull(confirm);
            Assert.True(keep.IsVisible);
            Assert.True(confirm.IsVisible);
            Assert.Contains("focused", keep.Classes);
            Assert.DoesNotContain("focused", confirm.Classes);
            foreach (var button in new[] { keep, confirm })
            {
                var origin = button.TranslatePoint(default, overlay);
                Assert.NotNull(origin);
                Assert.True(origin.Value.X >= 0 && origin.Value.Y >= 0);
                Assert.True(origin.Value.X + button.Bounds.Width <= overlay.Bounds.Width + 1);
                Assert.True(origin.Value.Y + button.Bounds.Height <= overlay.Bounds.Height + 1);
            }

            gamepadSettings.Dispatch(GamepadAction.Cancel);
            desktopSettings.ConnectedAccountName = "Parity Player";
            desktopFieldIds[SettingsSection.RetroAchievements] = await CaptureDesktopFieldIdsAsync(
                SettingsSection.RetroAchievements,
                "retro.");
            gamepadSettings.SelectedSection = SettingsSection.RetroAchievements;
            await PumpAsync();
            AssertGamepadSettingsParity(SettingsSection.RetroAchievements, "retro.");

            desktopSettings.IsCloudConnected = false;
            desktopFieldIds[SettingsSection.Saves] = await CaptureDesktopFieldIdsAsync(
                SettingsSection.Saves,
                "saves.");
            gamepadSettings.SelectedSection = SettingsSection.Saves;
            await PumpAsync();
            AssertGamepadSettingsParity(SettingsSection.Saves, "saves.");

            void AssertGamepadSettingsParity(SettingsSection section, string prefix)
            {
                // The controller list is intentionally virtualized, so the visual tree contains
                // only the current viewport. Compare Desktop's visible controls with the complete
                // controller projection, then separately verify that realized rows expose the same
                // stable ids for accessibility and routing.
                var gamepadIds = gamepadSettings.Rows
                    .Select(row => row.ParityId)
                    .Where(id => id?.StartsWith(prefix, StringComparison.Ordinal) == true)
                    .Select(id => id!)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
                Assert.Equal(desktopFieldIds[section], gamepadIds);

                var realizedRows = window.GetVisualDescendants()
                    .OfType<Button>()
                    .Where(button => button.IsVisible &&
                        button.Classes.Contains("gamepad-settings-row") &&
                        button.DataContext is GamepadSettingsRowViewModel row &&
                        !string.IsNullOrEmpty(row.ParityId))
                    .ToArray();
                Assert.NotEmpty(realizedRows);
                Assert.All(realizedRows, button =>
                {
                    var row = Assert.IsType<GamepadSettingsRowViewModel>(button.DataContext);
                    Assert.Equal(row.ParityId, AutomationProperties.GetAutomationId(button));
                });
            }

        }
        finally
        {
            gamepadSettings.Dispose();
            window.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        }

        async Task<string[]> CaptureDesktopFieldIdsAsync(SettingsSection section, string prefix)
        {
            desktopSettings.SelectedSection = section;
            var desktopWindow = new EmulatorSettingsWindow
            {
                DataContext = desktopSettings,
                Width = 1100,
                Height = 800,
            };
            desktopWindow.Show();
            try
            {
                await PumpAsync();
                return desktopWindow.GetVisualDescendants()
                    .OfType<Control>()
                    .Where(control => control.IsVisible &&
                        control.GetVisualAncestors().All(ancestor => ancestor.IsVisible))
                    .Select(AutomationProperties.GetAutomationId)
                    .Where(id => id?.StartsWith(prefix, StringComparison.Ordinal) == true)
                    .Select(id => id!)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            }
            finally
            {
                desktopWindow.Close();
            }
        }
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
            (_, _, _, _, _, _) => Task.FromResult(CloudSaveSyncConnectResult.Connected),
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
