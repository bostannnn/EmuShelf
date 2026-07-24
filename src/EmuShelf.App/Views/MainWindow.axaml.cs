using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _gamepadViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Controller input is supplied by Steam Input as keyboard events. Capture it in the
        // tunnel before a focused game tile consumes Enter/Escape for its own button command.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_gamepadViewModel is not null)
            _gamepadViewModel.PropertyChanged -= OnGamepadViewModelPropertyChanged;

        _gamepadViewModel = DataContext as MainViewModel;
        if (_gamepadViewModel is not null)
            _gamepadViewModel.PropertyChanged += OnGamepadViewModelPropertyChanged;
    }

    // View-focused coordination only: the view model selects a logical platform; this window
    // reveals the corresponding realized tab without making layout/visual concerns business logic.
    private void OnGamepadViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainViewModel.SelectedSystem) or
            nameof(MainViewModel.CurrentLibraryScope) or nameof(MainViewModel.IsGamepadRailFocused) or
            nameof(MainViewModel.GamepadRailIndex) or nameof(MainViewModel.GamepadOverlay) or
            nameof(MainViewModel.FocusedGamepadAchievement)))
        {
            return;
        }

        Dispatcher.UIThread.Post(RevealGamepadRail, DispatcherPriority.Input);
        Dispatcher.UIThread.Post(RevealGamepadOverlayFocus, DispatcherPriority.Input);
    }

    // Visual focus/reveal is kept here; controller routing and modal state remain in the view model.
    private void RevealGamepadOverlayFocus()
    {
        if (_gamepadViewModel is not { IsGamepadMode: true })
            return;

        if (_gamepadViewModel.IsGamepadSearchOpen)
            GamepadSearchBox.Focus();
        else if (_gamepadViewModel.IsGamepadRenameOpen)
            GamepadRenameBox.Focus();
        else if (_gamepadViewModel.FocusedGamepadAchievement is { } achievement)
        {
            GamepadAchievementsScroller.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control => ReferenceEquals(control.DataContext, achievement))
                ?.BringIntoView();
        }
    }

    private void RevealGamepadRail()
    {
        if (_gamepadViewModel is null || !_gamepadViewModel.IsGamepadMode)
            return;

        Control? tab = _gamepadViewModel.IsGamepadRailFocused
            ? _gamepadViewModel.GamepadRailIndex switch
            {
                0 => GamepadAllGamesTab,
                1 => GamepadCollectionsTab,
                _ => GamepadRailScroller.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(button => button.DataContext is GamepadPlatformTabViewModel { IsRailFocused: true }),
            }
            : _gamepadViewModel.IsAllGamesSelected
                ? GamepadAllGamesTab
                : GamepadRailScroller.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(button => button.DataContext is GamepadPlatformTabViewModel { IsActive: true });
        if (tab is null)
            return;

        tab.BringIntoView();
        if (_gamepadViewModel.IsGamepadRailFocused)
            tab.Focus();
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

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || e.Source is TextBox)
            return;

        if (viewModel.IsGamepadMode)
        {
            // A Gamepad overlay is the only controller-input owner until it closes.
            if (viewModel.HasGamepadOverlay)
            {
                if (e.Key == Key.Escape)
                    viewModel.CloseGamepadOverlayCommand.Execute(null);
                else if (viewModel.IsGamepadAchievementsOpen && e.Key == Key.X)
                    viewModel.GamepadAchievementDetails?.RefreshCommand.Execute(null);
                else if (e.Key == Key.Up)
                    viewModel.MoveGamepadOverlayUpCommand.Execute(null);
                else if (e.Key == Key.Down)
                    viewModel.MoveGamepadOverlayDownCommand.Execute(null);
                else if (e.Key == Key.Enter && !viewModel.IsGamepadAchievementsOpen)
                    viewModel.ActivateGamepadOverlayCommand.Execute(null);
                else
                    return;
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.PageUp)
                viewModel.PreviousPlatformCommand.Execute(null);
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.PageDown)
                viewModel.NextPlatformCommand.Execute(null);
            else if (viewModel.IsGamepadRailFocused && e.Key == Key.Enter)
                viewModel.ActivateGamepadRailCommand.Execute(null);
            else if (e.Key == Key.Enter)
                viewModel.LaunchFocusedGameCommand.Execute(null);
            else if (e.Key == Key.Escape)
                viewModel.SetInterfaceModeCommand.Execute(EmuShelf.Core.Settings.InterfaceMode.Desktop);
            else if (e.Key == Key.X)
                viewModel.OpenGamepadSearchCommand.Execute(null);
            else if (e.Key == Key.Y)
                viewModel.OpenFocusedGameActionsCommand.Execute(null);
            else if (e.Key == Key.Left)
                viewModel.MoveGamepadFocusLeftCommand.Execute(null);
            else if (e.Key == Key.Right)
                viewModel.MoveGamepadFocusRightCommand.Execute(null);
            else if (e.Key == Key.Up)
                viewModel.MoveGamepadFocusUpCommand.Execute(null);
            else if (e.Key == Key.Down)
                viewModel.MoveGamepadFocusDownCommand.Execute(null);
            else
                return;
            e.Handled = true;
            return;
        }

        var isSelectionModifier = e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                                  e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.A && isSelectionModifier && viewModel.SelectAllGamesCommand.CanExecute(null))
        {
            viewModel.SelectAllGamesCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && viewModel.RemoveSelectedGamesCommand.CanExecute(null))
        {
            viewModel.RemoveSelectedGamesCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnGamepadTextInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.IsGamepadMode)
            return;

        if (e.Key == Key.Escape)
        {
            viewModel.CloseGamepadOverlayCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && viewModel.IsGamepadRenameOpen)
        {
            viewModel.SaveGamepadTitleCommand.Execute(null);
            e.Handled = true;
        }
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
            viewModel.SelectGame(
                game,
                e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta),
                e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        }
    }

    // View wiring only: report the grid area's width so the view model can size covers to fill a
    // whole number of columns (and re-fill when the sidebar collapses or the window resizes), then
    // widen the grid cells (MinItemWidth) to match — the layout otherwise pins cells to 188.
    private void OnLibrarySizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.LibraryViewportWidth = e.NewSize.Width;
        if (LibraryRepeater.Layout is UniformGridLayout layout)
            layout.MinItemWidth = viewModel.GridCoverWidth;
    }

    private void OnGamepadLibrarySizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.GamepadViewportWidth = e.NewSize.Width;
        if (GamepadRepeater.Layout is UniformGridLayout layout)
            layout.MinItemWidth = viewModel.GridCoverWidth;
    }

    // View wiring only: virtualization decides when a cover control is realized; the
    // game command owns the asynchronous load, caching, and stale-result handling.
    private void OnGameCoverAttached(object? sender, VisualTreeAttachmentEventArgs e)
        => RequestGameCover(sender);

    // A virtualized element may remain attached while ItemsRepeater gives it a new
    // data context after a collection reset. Request the replacement game's cover too.
    private void OnGameCoverDataContextChanged(object? sender, EventArgs e)
        => RequestGameCover(sender);

    private static void RequestGameCover(object? sender)
    {
        if (sender is Control { DataContext: GameViewModel game } control &&
            control.IsAttachedToVisualTree() &&
            game.LoadCoverCommand.CanExecute(game))
        {
            game.LoadCoverCommand.Execute(game);
        }
    }
}
