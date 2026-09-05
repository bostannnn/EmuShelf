using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// The pre-launch "which screen?" chooser as it is actually rendered: two screen cards over a remember
/// toggle. The D-pad walk is view-model state (see MainViewModelTests), but the thing the player reads is
/// which control is LIT — so this drives the real overlay in a window and asserts the highlight moves,
/// which a view-model assertion alone cannot catch when a binding is wrong.
/// </summary>
public class GamepadLaunchScreenChooserTests
{
    [AvaloniaFact]
    public async Task Chooser_HighlightFollowsTheDpadAcrossCardsAndTheRememberRow()
    {
        var viewModel = CreateChooserViewModel(out var game);
        await viewModel.LaunchGameCommand.ExecuteAsync(game);
        Assert.Equal(GamepadOverlayKind.LaunchScreen, viewModel.GamepadOverlay);

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            await Pump();
            Assert.Equal("gamepad-screen-card", FocusedChooserControl(window));

            // The cards are a ROW: Right lights the external card, Left comes back.
            viewModel.DispatchGamepadAction(GamepadAction.NavigateRight);
            await Pump();
            Assert.Equal(1, viewModel.GamepadOverlaySelectionIndex);
            Assert.Equal("gamepad-screen-card", FocusedChooserControl(window));
            Assert.Contains("focused", CardButtons(window)[1].Classes);
            Assert.DoesNotContain("focused", CardButtons(window)[0].Classes);

            // Down leaves the pair entirely: only the remember row may be lit.
            viewModel.DispatchGamepadAction(GamepadAction.NavigateDown);
            await Pump();
            Assert.Equal(2, viewModel.GamepadOverlaySelectionIndex);
            Assert.All(CardButtons(window), card => Assert.DoesNotContain("focused", card.Classes));
            Assert.Equal("gamepad-remember-row", FocusedChooserControl(window));
        }
        finally
        {
            window.Close();
        }
    }

    private static IReadOnlyList<Button> CardButtons(Window window) => window.GetVisualDescendants()
        .OfType<Button>()
        .Where(button => button.Classes.Contains("gamepad-screen-card"))
        .ToList();

    /// <summary>The class of whichever chooser control currently carries the highlight — "none" if the
    /// highlight is nowhere, or a joined list if it is somehow on two controls at once.</summary>
    private static string FocusedChooserControl(Window window)
    {
        var lit = window.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("focused") &&
                (button.Classes.Contains("gamepad-screen-card") || button.Classes.Contains("gamepad-remember-row")))
            .Select(button => button.Classes.Contains("gamepad-screen-card")
                ? "gamepad-screen-card"
                : "gamepad-remember-row")
            .ToList();
        return lit.Count == 0 ? "none" : string.Join("+", lit);
    }

    private static MainViewModel CreateChooserViewModel(out GameViewModel game)
    {
        var system = KnownSystems.All.Single(candidate => candidate.Id == "megadrive");
        var viewModel = new MainViewModel(
            new EmptyGameLibrary(),
            new NullFolderScanner(),
            new NoImportRules(),
            new AlwaysAvailableChecker(),
            new FakeDialogService(),
            KnownSystems.All,
            externalDisplays: new AlwaysExternalDisplayProbe())
        {
            IsGamepadMode = true,
        };
        game = new GameViewModel(
            new Game
            {
                Id = 1,
                SystemId = system.Id,
                Path = "/Games/megadrive/Aladdin.md",
                Title = "Aladdin",
                IsAvailable = true,
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name, system.ShortName, system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio);
        viewModel.Games.ReplaceAll([game]);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = game;
        return viewModel;
    }

    private static async Task Pump()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private sealed class AlwaysExternalDisplayProbe : IExternalDisplayProbe
    {
        public bool HasExternalDisplay => true;
    }
}
