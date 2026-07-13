using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnOpenSearchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.IsSearchOpen = true;
        Dispatcher.UIThread.Post(
            () => SearchBox.Focus(),
            DispatcherPriority.Input);
    }

    private void OnCloseSearchClick(object? sender, RoutedEventArgs e)
    {
        CloseSearch();
        e.Handled = true;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        CloseSearch();
        e.Handled = true;
    }

    private void CloseSearch()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.ClearSearchCommand.Execute(null);
        viewModel.IsSearchOpen = false;
    }

    // View wiring only: both grid and list items forward the double-tap gesture to the
    // same command exposed by their game view model.
    private void OnGameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: GameViewModel game }
            && game.LaunchCommand.CanExecute(game))
        {
            game.LaunchCommand.Execute(game);
            e.Handled = true;
        }
    }

    private void OnGameTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: GameViewModel game } &&
            DataContext is MainViewModel viewModel)
        {
            viewModel.SelectedGame = game;
        }
    }

    // View wiring only: virtualization decides when a cover control is realized; the
    // game command owns the asynchronous load, caching, and stale-result handling.
    private void OnGameCoverAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { DataContext: GameViewModel game }
            && game.LoadCoverCommand.CanExecute(game))
        {
            game.LoadCoverCommand.Execute(game);
        }
    }
}
