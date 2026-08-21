using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// The gamepad "Add games — choose system" chooser must actually show its system list.
/// </summary>
/// <remarks>
/// Found driving a real import on the AYN Thor (docs/android-port-plan.md, Milestone S backlog): the
/// <see cref="GamepadOverlayKind.ImportSystem"/> overlay rendered its title and the A/B hint legend but
/// the option list between them collapsed to zero height, so no system was pickable — the import could
/// only be completed by counting D-pad presses blind. The options collection is populated and its styles
/// are innocent; the fault is the shared overlay body starving to nothing when the system-menu picker
/// header (the only thing giving the centred, content-sized overlay Border real vertical extent) is
/// absent — which it is for every overlay except the system menu. This reproduces the collapse on the
/// short couch panel the Thor presents.
/// </remarks>
public class GamepadImportChooserLayoutTests
{
    [AvaloniaTheory]
    [InlineData(1080)]
    [InlineData(480)] // the Thor presents the couch shell at ~468 dip tall
    public async Task ImportSystemChooser_ShowsItsSystemList(double windowHeight)
    {
        var dialogs = new FakeDialogService { FolderToReturn = Path.GetTempPath() };
        var viewModel = new MainViewModel(
            new EmptyGameLibrary(),
            new NullFolderScanner(),
            new NoImportRules(),
            new AlwaysAvailableChecker(),
            dialogs,
            KnownSystems.All)
        {
            IsGamepadMode = true,
        };
        await viewModel.ReloadGamesAsync();

        // Drive the real gamepad-native import entry point: it runs the (faked) folder pick, then opens
        // the system chooser populated with every importable console.
        await viewModel.AddFolderFromGamepadCommand.ExecuteAsync(null);
        Assert.Equal(GamepadOverlayKind.ImportSystem, viewModel.GamepadOverlay);
        Assert.True(
            viewModel.GamepadOverlayOptions.Count > 10,
            $"Chooser should list every console; had {viewModel.GamepadOverlayOptions.Count}.");

        var window = new MainWindow
        {
            DataContext = viewModel,
            Width = 1280,
            Height = windowHeight,
        };
        window.Show();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);

            // Scope strictly to THIS overlay's option list (GamepadOverlayOptions) — the class
            // "gamepad-modal-option" is also worn by hidden Settings/Scraper buttons elsewhere in the
            // realised-but-collapsed overlay tree, which would otherwise be mistaken for chooser rows.
            var optionsList = window.FindNamed<ItemsControl>("GamepadOverlayOptions");
            Assert.NotNull(optionsList);
            Assert.True(
                optionsList.Bounds.Height > 0,
                "The system-chooser option list collapsed to zero height.");

            var optionButtons = optionsList.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("gamepad-modal-option"))
                .ToList();
            Assert.NotEmpty(optionButtons);
            Assert.All(optionButtons, button => Assert.True(
                button.Bounds.Height > 0,
                $"A system-chooser option ('{Label(button)}') measured to zero height."));

            // The first option occupies real space at the top of the overlay (not a zero-height region).
            var scrim = window.GetVisualDescendants()
                .OfType<Grid>()
                .First(grid => grid.Classes.Contains("gamepad-scrim"));
            var firstTop = optionButtons[0].TranslatePoint(default, scrim)!.Value.Y;
            Assert.True(
                firstTop >= -1 && firstTop < scrim.Bounds.Height,
                $"The first chooser option sits at y={firstTop}, outside the {scrim.Bounds.Height}px overlay.");
        }
        finally
        {
            window.Close();
        }
    }

    private static string Label(Button button) =>
        button.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text ?? button.Content?.ToString() ?? "?";
}
