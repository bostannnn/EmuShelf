using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _gamepadViewModel;
    private GamepadScraperViewModel? _gamepadScraper;
    private Point? _lastGamepadPointerPosition;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Controller input is supplied by Steam Input as keyboard events. Capture it in the
        // tunnel before a focused game tile consumes Enter/Escape for its own button command.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        // ListBox handles pointer input internally. Observe it first so Grid and List always feed
        // the same view-model-owned desktop selection state, including right-click selection.
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_gamepadViewModel is not null)
            _gamepadViewModel.PropertyChanged -= OnGamepadViewModelPropertyChanged;

        SyncGamepadScraperSubscription(null);

        _gamepadViewModel = DataContext as MainViewModel;
        if (_gamepadViewModel is not null)
            _gamepadViewModel.PropertyChanged += OnGamepadViewModelPropertyChanged;
    }

    // The controller scraper overlay tracks its own focus index on the wrapped view model, so the
    // window observes that view model directly to move keyboard focus onto the focused text box.
    private void SyncGamepadScraperSubscription(GamepadScraperViewModel? scraper)
    {
        if (ReferenceEquals(_gamepadScraper, scraper))
            return;

        if (_gamepadScraper is not null)
            _gamepadScraper.PropertyChanged -= OnGamepadScraperPropertyChanged;
        _gamepadScraper = scraper;
        if (_gamepadScraper is not null)
            _gamepadScraper.PropertyChanged += OnGamepadScraperPropertyChanged;
    }

    private void OnGamepadScraperPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GamepadScraperViewModel.FocusIndex) or
            nameof(GamepadScraperViewModel.FocusedKind))
        {
            Dispatcher.UIThread.Post(RevealGamepadScraperFocus, DispatcherPriority.Input);
        }
    }

    // View-focused coordination only: the view model selects a logical platform; this window
    // reveals the corresponding realized tab without making layout/visual concerns business logic.
    private void OnGamepadViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Moving focus down/up past the visible rows must scroll the virtualized grid, otherwise the
        // focus ring walks off-screen and the library looks stuck at the last visible row.
        if (e.PropertyName is nameof(MainViewModel.FocusedGame))
        {
            Dispatcher.UIThread.Post(RevealFocusedGame, DispatcherPriority.Input);
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.GamepadScraperDetails))
        {
            SyncGamepadScraperSubscription(_gamepadViewModel?.GamepadScraperDetails);
            Dispatcher.UIThread.Post(RevealGamepadScraperFocus, DispatcherPriority.Input);
            return;
        }

        // A mode switch re-sizes the covers for the newly visible viewport without either grid
        // necessarily raising SizeChanged, so the cell width has to follow the value itself.
        if (e.PropertyName is nameof(MainViewModel.GridCoverWidth) &&
            _gamepadViewModel is { } sizingViewModel)
        {
            ApplyCellWidth(sizingViewModel);
            return;
        }

        if (e.PropertyName is not (nameof(MainViewModel.SelectedSystem) or
            nameof(MainViewModel.CurrentLibraryScope) or nameof(MainViewModel.GamepadOverlay) or
            nameof(MainViewModel.GamepadOverlaySelectionIndex) or nameof(MainViewModel.GamepadOverlayTitle) or
            nameof(MainViewModel.FocusedGamepadAchievement) or
            nameof(MainViewModel.GamepadAchievementLayoutRevision) or
            nameof(MainViewModel.IsGamepadControllerInputActive)))
        {
            return;
        }

        Dispatcher.UIThread.Post(RevealFocusedGame, DispatcherPriority.Input);
        Dispatcher.UIThread.Post(RevealGamepadRail, DispatcherPriority.Input);
        Dispatcher.UIThread.Post(RevealGamepadOverlayFocus, DispatcherPriority.Input);
    }

    // View-focused coordination only: the view model owns which game is focused; this window scrolls
    // that game's tile into view. The target row may not be realized yet under virtualization, so it
    // is realized on demand and laid out before being brought into view.
    private void RevealFocusedGame() => RevealFocusedGame(0);

    private void RevealFocusedGame(int attempt)
    {
        if (_gamepadViewModel is not { IsGamepadMode: true } viewModel ||
            viewModel.FocusedGame is not { } focused)
        {
            return;
        }

        var index = viewModel.Games.IndexOf(focused);
        if (index < 0)
            return;

        var element = GamepadRepeater.TryGetElement(index) ?? GamepadRepeater.GetOrCreateElement(index);
        if (element is null)
        {
            // The tile isn't realized yet (virtualization/layout not settled). Retry on the next
            // layout pass, bounded, so the selector ring is never left bound to an off-screen tile.
            if (attempt < 5)
                Dispatcher.UIThread.Post(() => RevealFocusedGame(attempt + 1), DispatcherPriority.Loaded);
            return;
        }

        GamepadRepeater.UpdateLayout();
        SyncGamepadColumnCountFromLayout();
        element.BringIntoView();
        if (viewModel.IsGamepadControllerInputActive && !viewModel.HasGamepadOverlay)
        {
            var gameButton = element as Button ?? element.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => ReferenceEquals(button.DataContext, focused));
            if (gameButton is not null)
                FocusManager?.Focus(gameButton, NavigationMethod.Directional);
        }
    }

    // The view lays the grid out, so it — not width arithmetic — is the source of truth for how many
    // columns are on screen. A momentarily stale, too-small count made Right/Left clamp partway across
    // a row ("stuck at the second column"); reading the realized tiles' rows corrects it regardless of
    // what made the arithmetic disagree. Runs off a settled layout (after UpdateLayout / on resize).
    private void SyncGamepadColumnCountFromLayout()
    {
        if (_gamepadViewModel is not { IsGamepadMode: true } viewModel || viewModel.Games.Count == 0)
            return;

        // The most-populated realized row is the true column count: virtualization always keeps at
        // least one full row realized (only the final row may be short), so its width is authoritative
        // even when the grid is scrolled.
        var rowCounts = new Dictionary<int, int>();
        for (var index = 0; index < viewModel.Games.Count; index++)
        {
            if (GamepadRepeater.TryGetElement(index) is not { } element)
                continue;
            var row = (int)Math.Round(element.Bounds.Y);
            rowCounts[row] = rowCounts.TryGetValue(row, out var count) ? count + 1 : 1;
        }

        if (rowCounts.Count > 0)
            viewModel.SetRenderedGamepadColumnCount(rowCounts.Values.Max());
    }

    private void SyncGamepadAchievementColumnCountFromLayout()
    {
        if (_gamepadViewModel is not { IsGamepadMode: true, IsGamepadAchievementsOpen: true } viewModel ||
            viewModel.GamepadAchievementDetails?.VisibleAchievements is not { Count: > 0 } achievements)
        {
            return;
        }

        var rowCounts = new Dictionary<int, int>();
        for (var index = 0; index < achievements.Count; index++)
        {
            if (GamepadAchievementsRepeater.TryGetElement(index) is not { } element)
                continue;
            var row = (int)Math.Round(element.Bounds.Y);
            rowCounts[row] = rowCounts.TryGetValue(row, out var count) ? count + 1 : 1;
        }

        if (rowCounts.Count > 0)
            viewModel.SetRenderedGamepadAchievementColumnCount(rowCounts.Values.Max());
    }

    // Visual focus/reveal is kept here; controller routing and modal state remain in the view model.
    private void RevealGamepadOverlayFocus() => RevealGamepadOverlayFocus(0);

    private void RevealGamepadOverlayFocus(int attempt)
    {
        if (_gamepadViewModel is not { IsGamepadMode: true } viewModel)
            return;

        if (viewModel.IsGamepadSearchOpen)
            GamepadSearchBox.Focus();
        else if (viewModel.IsGamepadRenameOpen)
            GamepadRenameBox.Focus();
        else if (viewModel.FocusedGamepadAchievement is { } achievement)
        {
            var index = viewModel.GamepadAchievementDetails?.VisibleAchievements.IndexOf(achievement) ?? -1;
            if (index < 0)
                return;

            // Do not manually realize the anchor before the overlay has a final viewport. On the
            // real compositor that can reserve cell 0 during the first measure, then place item 0
            // in cell 1 and leave a permanent top-left hole.
            GamepadAchievementsScroller.UpdateLayout();
            GamepadAchievementsRepeater.UpdateLayout();
            if (GamepadAchievementsScroller.Bounds.Width <= 0 ||
                GamepadAchievementsScroller.Bounds.Height <= 0)
            {
                if (attempt < 5)
                {
                    Dispatcher.UIThread.Post(
                        () => RevealGamepadOverlayFocus(attempt + 1),
                        DispatcherPriority.Loaded);
                }
                return;
            }

            var element = GamepadAchievementsRepeater.TryGetElement(index) ??
                GamepadAchievementsRepeater.GetOrCreateElement(index);
            GamepadAchievementsRepeater.UpdateLayout();
            if (element is null || element.Bounds.Width <= 0 || element.Bounds.Height <= 0)
            {
                if (attempt < 5)
                {
                    Dispatcher.UIThread.Post(
                        () => RevealGamepadOverlayFocus(attempt + 1),
                        DispatcherPriority.Loaded);
                }
                return;
            }

            SyncGamepadAchievementColumnCountFromLayout();
            var achievementControl = element as Control ?? element.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control => ReferenceEquals(control.DataContext, achievement));
            achievementControl?.BringIntoView();
            Dispatcher.UIThread.Post(SyncGamepadAchievementColumnCountFromLayout, DispatcherPriority.Loaded);
            if (viewModel.IsGamepadControllerInputActive && achievementControl is not null)
                FocusManager?.Focus(achievementControl, NavigationMethod.Directional);
        }
        else if (viewModel.HasGamepadOverlay && viewModel.IsGamepadControllerInputActive)
        {
            var focusedOption = GamepadOverlayOptions.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => button.DataContext is GamepadOverlayOptionViewModel { IsFocused: true });
            if (focusedOption is not null)
            {
                focusedOption.BringIntoView();
                FocusManager?.Focus(focusedOption, NavigationMethod.Directional);
            }
        }
    }

    // Visual focus only for the scraper overlay: keyboard focus follows the wrapped view model's
    // focus ring onto the matching text box (so the Steam on-screen keyboard types into it) or the
    // focused command button. D-pad routing and modal state stay in the view models.
    private void RevealGamepadScraperFocus()
    {
        if (_gamepadViewModel is not { IsGamepadMode: true, IsGamepadScraperOpen: true } viewModel ||
            viewModel.GamepadScraperDetails is not { } scraper)
        {
            return;
        }

        // Text targets take real keyboard focus so the Steam on-screen keyboard types into them.
        TextBox? textBox = scraper.FocusedKind switch
        {
            GamepadScraperTargetKind.Username => GamepadScraperUsernameBox,
            GamepadScraperTargetKind.Password => GamepadScraperPasswordBox,
            GamepadScraperTargetKind.SearchField => GamepadScraperSearchBox,
            _ => null,
        };
        if (textBox is not null)
        {
            textBox.BringIntoView();
            textBox.Focus();
            return;
        }

        // Everything else (toggle rows, candidates, command buttons) carries the .focused class:
        // scroll it into view so the ring never walks off-screen, and give buttons real focus.
        var focused = FindFocusedScraperControl();
        if (focused is null)
            return;

        focused.BringIntoView();
        if (focused is Button && viewModel.IsGamepadControllerInputActive)
            FocusManager?.Focus(focused, NavigationMethod.Directional);
    }

    private Control? FindFocusedScraperControl() =>
        GamepadOverlayHost.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(control => control.IsEffectivelyVisible &&
                (control is Button || control.Classes.Contains("gamepad-scraper-row")) &&
                control.Classes.Contains("focused"));

    // Steam Input delivers controller buttons as keys; while a scraper text box holds focus they
    // reach it here (the window-level tunnel ignores TextBox sources). Route D-pad/A/B to the same
    // scraper navigation the native pad drives, and let plain typing fall through to the box.
    private void OnGamepadScraperTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel { IsGamepadMode: true } viewModel ||
            viewModel.GamepadScraperDetails is not { } scraper)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                viewModel.BackFromGamepadOverlayCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                scraper.MoveFocus(-1);
                e.Handled = true;
                break;
            case Key.Down:
                scraper.MoveFocus(1);
                e.Handled = true;
                break;
            case Key.Enter:
                scraper.Activate();
                e.Handled = true;
                break;
        }
    }

    private static void OnGamepadAchievementAttached(object? sender, VisualTreeAttachmentEventArgs e) =>
        RequestGamepadAchievementBadge(sender);

    private static void OnGamepadAchievementDataContextChanged(object? sender, EventArgs e) =>
        RequestGamepadAchievementBadge(sender);

    private void OnGamepadAchievementPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: AchievementRowViewModel achievement } ||
            DataContext is not MainViewModel viewModel ||
            e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        viewModel.NotifyGamepadPointerInput();
        viewModel.FocusGamepadAchievementCommand.Execute(achievement);
        e.Handled = true;
    }

    private static void RequestGamepadAchievementBadge(object? sender)
    {
        if (sender is Control { DataContext: AchievementRowViewModel row } && row.Badge is null)
            _ = row.LoadBadgeAsync(row.BadgeName);
    }

    // The rail is a passive indicator: keep the active tab scrolled into view so the current
    // platform is visible after an LB/RB change. It never takes keyboard focus.
    private void RevealGamepadRail()
    {
        if (_gamepadViewModel is null || !_gamepadViewModel.IsGamepadMode)
            return;

        Control? tab = _gamepadViewModel.IsAllGamesSelected
            ? GamepadAllGamesTab
            : GamepadRailScroller.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => button.DataContext is GamepadPlatformTabViewModel { IsActive: true });
        tab?.BringIntoView();
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

    // Window chrome remains view wiring: it changes only the host window state and never enters
    // the library view model. ElementRole preserves native drag/caption semantics where supported.
    private void OnMinimizeWindowClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreWindowClick(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseWindowClick(object? sender, RoutedEventArgs e) => Close();

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
            // Steam Input delivers controller buttons as these keys; map them to the same logical
            // actions native pad input produces and route both through the one view-model dispatcher.
            if (MapKeyToGamepadAction(e) is { } action && viewModel.DispatchGamepadAction(action))
                e.Handled = true;
            return;
        }

        // Steam Input keyboard contract: LB/RB map to Ctrl+PageUp/Ctrl+PageDown for platform switching.
        static GamepadAction? MapKeyToGamepadAction(KeyEventArgs key)
        {
            if (key.KeyModifiers.HasFlag(KeyModifiers.Control) && key.Key == Key.PageUp)
                return GamepadAction.PreviousPlatform;
            if (key.KeyModifiers.HasFlag(KeyModifiers.Control) && key.Key == Key.PageDown)
                return GamepadAction.NextPlatform;

            return key.Key switch
            {
                Key.Enter => GamepadAction.Confirm,
                Key.Escape => GamepadAction.Cancel,
                Key.X => GamepadAction.Search,
                Key.Y => GamepadAction.Actions,
                Key.F10 => GamepadAction.Menu,
                Key.Left => GamepadAction.NavigateLeft,
                Key.Right => GamepadAction.NavigateRight,
                Key.Up => GamepadAction.NavigateUp,
                Key.Down => GamepadAction.NavigateDown,
                _ => null,
            };
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
        else if (e.Key == Key.Escape && viewModel.HasSelectedGames)
        {
            viewModel.ClearSelectionCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnGamepadTextInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.IsGamepadMode)
            return;

        if (e.Key == Key.Escape)
        {
            viewModel.BackFromGamepadOverlayCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && viewModel.IsGamepadRenameOpen)
        {
            viewModel.SaveGamepadTitleCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnGamepadPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MainViewModel { IsGamepadMode: true } viewModel)
            return;

        var position = e.GetPosition(this);
        if (_lastGamepadPointerPosition is { } previous)
        {
            var delta = position - previous;
            if (delta.X * delta.X + delta.Y * delta.Y >= 16)
                viewModel.NotifyGamepadPointerInput();
        }

        _lastGamepadPointerPosition = position;
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

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel { IsGamepadMode: false } viewModel ||
            e.Source is not Control source)
            return;

        var updateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (source.DataContext is GameViewModel game)
        {
            if (updateKind == PointerUpdateKind.RightButtonPressed)
            {
                if (!game.IsSelected)
                    viewModel.SelectGame(game);
            }
            else if (updateKind == PointerUpdateKind.LeftButtonPressed && e.ClickCount == 1 &&
                     !IsNestedButton(source))
            {
                viewModel.SelectGame(
                    game,
                    e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta),
                    e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            }
            return;
        }

        if (updateKind != PointerUpdateKind.LeftButtonPressed || !IsLibrarySurface(source))
            return;

        // Scrollbar interaction is navigation, not a click on the library's empty canvas.
        if (source is ScrollBar || source.GetVisualAncestors().Any(ancestor => ancestor is ScrollBar))
            return;

        viewModel.ClearSelectionCommand.Execute(null);
    }

    private static bool IsNestedButton(Control source) =>
        source is Button || source.GetVisualAncestors().Any(ancestor => ancestor is Button);

    private bool IsLibrarySurface(Control source) =>
        ReferenceEquals(source, LibraryGridScroller) || ReferenceEquals(source, LibraryList) ||
        source.GetVisualAncestors().Any(ancestor =>
            ReferenceEquals(ancestor, LibraryGridScroller) || ReferenceEquals(ancestor, LibraryList));

    private void OnGameContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: GameViewModel { IsSelected: false } game } &&
            DataContext is MainViewModel viewModel)
        {
            viewModel.SelectGame(game);
        }
    }

    // Context menus live in a detached popup. Invoke the concrete option's parameterless command
    // from the menu item itself instead of relying on command-parameter binding across that popup.
    private void OnSelectDiscMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: GameDiscOptionViewModel option } &&
            option.SelectDiscCommand.CanExecute(null))
        {
            option.SelectDiscCommand.Execute(null);
            e.Handled = true;
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
        ApplyCellWidth(viewModel);
    }

    private void OnGamepadLibrarySizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.GamepadViewportWidth = e.NewSize.Width;
        ApplyCellWidth(viewModel);
        // Correct the column count from the freshly relaid grid, so the first Left/Right after a
        // resize or first show can't clamp against a stale width estimate.
        Dispatcher.UIThread.Post(SyncGamepadColumnCountFromLayout, DispatcherPriority.Loaded);
    }

    private void OnGamepadAchievementsSizeChanged(object? sender, SizeChangedEventArgs e) =>
        Dispatcher.UIThread.Post(SyncGamepadAchievementColumnCountFromLayout, DispatcherPriority.Loaded);

    // Both grids take their cell width from the one cover width the view model computed for the
    // mode that is on screen. Applying it to both — rather than only to whichever grid raised
    // SizeChanged — means the visible grid's cells can never be left sized for the other mode,
    // which is what produced overlapping tiles and a column pushed off the edge after a switch.
    private void ApplyCellWidth(MainViewModel viewModel)
    {
        if (viewModel.GridCoverWidth <= 0)
            return;

        if (LibraryRepeater.Layout is UniformGridLayout desktopLayout)
            desktopLayout.MinItemWidth = viewModel.GridCoverWidth;
        if (GamepadRepeater.Layout is UniformGridLayout gamepadLayout)
            gamepadLayout.MinItemWidth = viewModel.GridCoverWidth;
    }

    // View wiring only: virtualization decides when a cover control is realized; the
    // game command owns the asynchronous load, caching, and stale-result handling.
    private void OnGameCoverAttached(object? sender, VisualTreeAttachmentEventArgs e)
        => RequestGameCover(sender);

    // A virtualized element may remain attached while ItemsRepeater gives it a new
    // data context after a collection reset. Request the replacement game's cover too.
    private void OnGameCoverDataContextChanged(object? sender, EventArgs e)
    {
        SnapCoverLayers(sender);
        RequestGameCover(sender);
    }

    /// <summary>
    /// Cuts the cover crossfade for one recycling.
    ///
    /// The fade exists to soften a cover arriving from disk. Recycling is not that: the control is
    /// being handed a different game, and animating it dissolves the previous game's artwork over
    /// the incoming tile. During fast scrolling or a run of LB/RB that reads as the wrong art
    /// briefly appearing. Dropping the transitions here lets the class bindings that follow this
    /// event apply as a straight cut; they are restored once that has happened, so the next real
    /// cover load still fades.
    /// </summary>
    private static void SnapCoverLayers(object? sender)
    {
        if (sender is not Control root)
            return;

        foreach (var layer in root.GetSelfAndVisualDescendants().OfType<Control>())
        {
            if (!layer.Classes.Contains("cover-image") && !layer.Classes.Contains("cover-placeholder"))
                continue;

            var transitions = layer.Transitions;
            if (transitions is null)
                continue;

            layer.Transitions = null;
            // Loaded runs after the bindings this event precedes, so the opacity change lands
            // while the transitions are detached and is applied instantly.
            Dispatcher.UIThread.Post(
                () => layer.Transitions ??= transitions,
                DispatcherPriority.Loaded);
        }
    }

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
