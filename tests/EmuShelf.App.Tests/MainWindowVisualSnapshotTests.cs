using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
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
                system.AccentColor));
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
}
