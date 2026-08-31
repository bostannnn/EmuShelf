using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;

namespace EmuShelf.App.Controls;

public partial class GamepadGridTile : UserControl
{
    /// <summary>
    /// Height of the title strip under the cover. The single source for the tile's fixed vertical
    /// chrome: <see cref="GamepadGridPanel"/> computes every row band and tile rect from it, and the
    /// constructor applies it to the XAML root grid's second row so the rendered strip can never
    /// drift from the panel's arithmetic.
    /// </summary>
    internal const double LabelStripHeight = 58;

    public GamepadGridTile()
    {
        InitializeComponent();
        TileRoot.RowDefinitions[1] = new RowDefinition(LabelStripHeight, GridUnitType.Pixel);
    }

    // View wiring only: controller focus still belongs to MainViewModel, while the pooled tile owns
    // the pointer click that selects its current DataContext.
    private void OnClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GameViewModel game ||
            this.FindAncestorOfType<GamepadShellView>()?.DataContext is not MainViewModel viewModel ||
            !viewModel.FocusGameCommand.CanExecute(game))
        {
            return;
        }

        viewModel.FocusGameCommand.Execute(game);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e) =>
        GameCoverInteractions.HandleDoubleTapped(sender, e);

    private static void OnCoverAttached(object? sender, VisualTreeAttachmentEventArgs e) =>
        GameCoverInteractions.CoverAttached(sender);

    private static void OnCoverDataContextChanged(object? sender, EventArgs e) =>
        GameCoverInteractions.CoverDataContextChanged(sender);
}
