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
    private int _requestedSettingsTextEntryRevision = -1;

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
        //
        // Reveal SYNCHRONOUSLY, not via a posted job. Controller input (the SDL poll timer and Steam
        // Input keys) arrives at DispatcherPriority.Input, and a reveal posted at that same priority is
        // starved while input floods — so the ring froze on the old tile while FocusedGame kept moving.
        // The user then read the stale ring as the selection: correct moves looked like "Left does
        // nothing" or "Up jumped a column." Placing the ring inline the instant focus changes keeps it
        // glued to the focused tile no matter how fast the d-pad repeats. FocusedGame only ever changes
        // from an input handler or the input timer tick, never mid-layout, so the reveal's UpdateLayout
        // is safe to run here.
        if (e.PropertyName is nameof(MainViewModel.FocusedGame))
        {
            RevealFocusedGame();
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.GamepadScraperDetails))
        {
            SyncGamepadScraperSubscription(_gamepadViewModel?.GamepadScraperDetails);
            Dispatcher.UIThread.Post(RevealGamepadScraperFocus, DispatcherPriority.Input);
            return;
        }

        // A mode switch re-sizes the covers for the newly visible viewport without either grid
        // necessarily raising SizeChanged, so the cell width has to follow the value itself. A resize
        // also re-groups the rows into a new column count, moving the focused game to a different row —
        // re-reveal it so the selection can't be left scrolled off-screen after the relayout.
        if (e.PropertyName is nameof(MainViewModel.GridCoverWidth) &&
            _gamepadViewModel is { } sizingViewModel)
        {
            ApplyCellWidth(sizingViewModel);
            Dispatcher.UIThread.Post(RevealFocusedGame, DispatcherPriority.Loaded);
            return;
        }

        if (e.PropertyName is not (nameof(MainViewModel.SelectedSystem) or
            nameof(MainViewModel.CurrentLibraryScope) or nameof(MainViewModel.GamepadOverlay) or
            nameof(MainViewModel.GamepadOverlaySelectionIndex) or nameof(MainViewModel.GamepadOverlayTitle) or
            nameof(MainViewModel.FocusedGamepadAchievement) or
            nameof(MainViewModel.GamepadAchievementLayoutRevision) or
            nameof(MainViewModel.GamepadSettingsFocusRevision) or
            nameof(MainViewModel.IsGamepadSettingsTextEntryOpen) or
            nameof(MainViewModel.IsGamepadSettingsConfirmationOpen) or
            nameof(MainViewModel.IsGamepadControllerInputActive)))
        {
            return;
        }

        Dispatcher.UIThread.Post(RevealFocusedGame, DispatcherPriority.Input);
        Dispatcher.UIThread.Post(RevealGamepadRail, DispatcherPriority.Input);
        Dispatcher.UIThread.Post(RevealGamepadOverlayFocus, DispatcherPriority.Input);
    }

    // View-focused coordination only: the view model owns which game is focused; this window scrolls
    // that game's ROW into view. Rows are virtualized in a ListBox, so ScrollIntoView reliably realizes
    // and reveals the target row (no manual offset math, no phantom cells). The focus ring is part of
    // the tile, so it appears on the focused cover the instant that row realizes.
    private void RevealFocusedGame() => RevealFocusedGame(0);

    private void RevealFocusedGame(int attempt)
    {
        if (_gamepadViewModel is not { IsGamepadMode: true } viewModel ||
            viewModel.FocusedGame is not { } focused)
            return;

        var index = viewModel.Games.IndexOf(focused);
        if (index < 0)
            return;

        var columns = Math.Max(1, viewModel.GamepadColumnCount);
        var rowIndex = index / columns;
        if (rowIndex >= GamepadRowList.ItemCount)
            return;

        GamepadRowList.ScrollIntoView(rowIndex);
        // ScrollIntoView lands the row flush against the viewport edge, which shaves the focus glow.
        // Nudge the scroll a glow-radius clear of the edge once the row has realized.
        Dispatcher.UIThread.Post(() => NudgeGlowClearance(rowIndex), DispatcherPriority.Loaded);

        // Take keyboard focus on the tile within the realized row for directional routing. The row may
        // need a layout pass after ScrollIntoView before its container exists; retry briefly if so.
        if (viewModel.IsGamepadControllerInputActive && !viewModel.HasGamepadOverlay)
        {
            var rowContainer = GamepadRowList.ContainerFromIndex(rowIndex);
            if (rowContainer is null)
            {
                if (attempt < 5)
                    Dispatcher.UIThread.Post(() => RevealFocusedGame(attempt + 1), DispatcherPriority.Loaded);
                return;
            }

            var gameButton = rowContainer.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => ReferenceEquals(button.DataContext, focused));
            if (gameButton is not null)
                FocusManager?.Focus(gameButton, NavigationMethod.Directional);
        }
    }

    private const double GamepadGlowMargin = 26; // >= EmuFocusGlow blur radius

    // Keep a glow-radius of clearance between the focused row and the viewport edge. Only acts when the
    // row settled against an edge (e.g. a d-pad move that scrolled); when the row is comfortably in view
    // this is a no-op, so quiet moves never jitter the scroll.
    private void NudgeGlowClearance(int rowIndex)
    {
        if (GamepadRowList.ContainerFromIndex(rowIndex) is not { } rowContainer)
            return;
        if (GamepadRowList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroller ||
            scroller.Viewport.Height <= 0)
            return;
        if (rowContainer.TranslatePoint(new Point(0, 0), scroller) is not { } rowTopLeft)
            return;

        var rowTop = rowTopLeft.Y;
        var rowBottom = rowTop + rowContainer.Bounds.Height;
        var offsetY = scroller.Offset.Y;
        if (rowTop < GamepadGlowMargin)
            scroller.Offset = scroller.Offset.WithY(Math.Max(0, offsetY - (GamepadGlowMargin - rowTop)));
        else if (rowBottom > scroller.Viewport.Height - GamepadGlowMargin)
            scroller.Offset = scroller.Offset.WithY(offsetY + (rowBottom - (scroller.Viewport.Height - GamepadGlowMargin)));
    }

    // Visual focus/reveal is kept here; controller routing and modal state remain in the view model.
    private void RevealGamepadOverlayFocus() => RevealGamepadOverlayFocus(0);

    private void RevealGamepadOverlayFocus(int attempt)
    {
        if (_gamepadViewModel is not { IsGamepadMode: true } viewModel)
            return;

        if (viewModel.IsGamepadSettingsTextEntryOpen && viewModel.GamepadSettings is { } textSettings)
        {
            var textBox = textSettings.IsSecretEntry
                ? GamepadSettingsSecretBox
                : GamepadSettingsTextBox;
            FocusManager?.Focus(textBox, NavigationMethod.Directional);
            if (_requestedSettingsTextEntryRevision != textSettings.TextEntryRevision)
            {
                _requestedSettingsTextEntryRevision = textSettings.TextEntryRevision;
                Dispatcher.UIThread.Post(textSettings.RequestOnScreenKeyboard, DispatcherPriority.Loaded);
            }
        }
        else if (viewModel.IsGamepadSettingsConfirmationOpen && viewModel.GamepadSettings is { } confirmationSettings)
        {
            var button = confirmationSettings.IsConfirmChoiceSelected
                ? GamepadSettingsConfirmButton
                : GamepadSettingsKeepButton;
            FocusManager?.Focus(button, NavigationMethod.Directional);
        }
        else if (viewModel.IsGamepadSettingsOpen &&
            viewModel.GamepadSettings is { IsRailFocused: true })
        {
            // The section rail owns focus; the dimmed content keeps no active control.
        }
        else if (viewModel.IsGamepadSettingsOpen && viewModel.GamepadSettings is { IsThemesSection: true })
        {
            if (viewModel.IsGamepadControllerInputActive)
            {
                var card = GamepadThemeGallery.GetVisualDescendants()
                    .OfType<Button>()
                    .FirstOrDefault(button =>
                        button.DataContext is ThemeChoiceViewModel choice && choice.IsFocused);
                if (card is not null)
                {
                    card.BringIntoView();
                    FocusManager?.Focus(card, NavigationMethod.Directional);
                }
            }
        }
        else if (viewModel.IsGamepadSettingsOpen && viewModel.GamepadSettings?.FocusedRow is { } settingsRow)
        {
            if (settingsRow.IsSaveRow)
            {
                if (viewModel.IsGamepadControllerInputActive)
                    FocusManager?.Focus(GamepadSettingsSaveButton, NavigationMethod.Directional);
                return;
            }

            var index = viewModel.GamepadSettings.Rows.IndexOf(settingsRow);
            if (index < 0)
                return;

            GamepadSettingsScroller.UpdateLayout();
            GamepadSettingsRows.UpdateLayout();
            var element = GamepadSettingsRows.TryGetElement(index) ?? GamepadSettingsRows.GetOrCreateElement(index);
            if (element is null)
            {
                if (attempt < 5)
                    Dispatcher.UIThread.Post(() => RevealGamepadOverlayFocus(attempt + 1), DispatcherPriority.Loaded);
                return;
            }
            element.BringIntoView();
            var rowButton = element as Button ?? element.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => ReferenceEquals(button.DataContext, settingsRow));
            if (viewModel.IsGamepadControllerInputActive && rowButton is not null)
                FocusManager?.Focus(rowButton, NavigationMethod.Directional);
        }
        else if (viewModel.IsGamepadSearchOpen)
            GamepadSearchBox.Focus();
        else if (viewModel.IsGamepadRenameOpen)
            GamepadRenameBox.Focus();
        else if (viewModel.FocusedGamepadAchievement is { } achievement)
        {
            var index = viewModel.GamepadAchievementDetails?.VisibleAchievements.IndexOf(achievement) ?? -1;
            if (index < 0)
                return;

            // Rows are virtualized in a ListBox, so ScrollIntoView reliably realizes and reveals the
            // target row (no manual offset math, no phantom cells). The focus ring is IsFocused on the
            // tile, so it appears the instant that row realizes.
            var columns = Math.Max(1, viewModel.GamepadAchievementColumnCount);
            var rowIndex = index / columns;
            if (rowIndex >= GamepadAchievementRowList.ItemCount)
            {
                if (attempt < 5)
                {
                    Dispatcher.UIThread.Post(
                        () => RevealGamepadOverlayFocus(attempt + 1),
                        DispatcherPriority.Loaded);
                }
                return;
            }

            GamepadAchievementRowList.ScrollIntoView(rowIndex);

            if (!viewModel.IsGamepadControllerInputActive)
                return;

            // The row may need a layout pass after ScrollIntoView before its container exists; retry
            // briefly if so, then take keyboard focus on the focused tile for directional routing.
            var rowContainer = GamepadAchievementRowList.ContainerFromIndex(rowIndex);
            if (rowContainer is null)
            {
                if (attempt < 5)
                    Dispatcher.UIThread.Post(() => RevealGamepadOverlayFocus(attempt + 1), DispatcherPriority.Loaded);
                return;
            }

            var achievementControl = rowContainer.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(control => ReferenceEquals(control.DataContext, achievement));
            if (achievementControl is not null)
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
            if (MapKeyToGamepadAction(e) is { } action)
            {
                if (viewModel.DispatchGamepadAction(action))
                    e.Handled = true;
            }
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
            if (viewModel.IsGamepadSettingsOpen && viewModel.GamepadSettings is { } settings)
                settings.Dispatch(GamepadAction.Cancel);
            else
                viewModel.BackFromGamepadOverlayCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && viewModel.IsGamepadSettingsOpen && viewModel.GamepadSettings is { } settings)
        {
            settings.Dispatch(GamepadAction.Confirm);
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
        // Setting the viewport width above already recomputed GamepadColumnCount arithmetically
        // (UpdateCoverLayout), matching what UniformGridLayout will render. The focus ring is part of
        // each tile, so it re-sizes with its cover automatically — nothing to reposition here.
        ApplyCellWidth(viewModel);
    }

    private void OnGamepadAchievementsSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        // Setting the width recomputes GamepadAchievementColumnCount arithmetically and re-slices the
        // achievement rows to match — the rendered column count then always equals the navigation
        // stride, with no reading of realized tile bounds.
        viewModel.GamepadAchievementViewportWidth = e.NewSize.Width;
    }

    private void OnGamepadSettingsScrollerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // StackLayout otherwise measures each virtualized child at its desired width. One explicit
        // cross-axis width keeps every controller setting in the same aligned column.
        GamepadSettingsRows.Width = Math.Max(0, e.NewSize.Width - 18);
        Dispatcher.UIThread.Post(() => RevealGamepadOverlayFocus(), DispatcherPriority.Loaded);
    }

    // Both grids take their cell width from the one cover width the view model computed for the
    // mode that is on screen. Applying it to both — rather than only to whichever grid raised
    // SizeChanged — means the visible grid's cells can never be left sized for the other mode,
    // which is what produced overlapping tiles and a column pushed off the edge after a switch.
    private void ApplyCellWidth(MainViewModel viewModel)
    {
        if (viewModel.GridCoverWidth <= 0)
            return;

        // Only the desktop grid is a virtualizing ItemsRepeater whose cell width must be pushed to the
        // layout. The gamepad grid is a UniformGrid; its tiles take their width from the CoverWidth
        // binding and their column count from GamepadColumnCount, so there is nothing to set here.
        if (LibraryRepeater.Layout is UniformGridLayout desktopLayout)
            desktopLayout.MinItemWidth = viewModel.GridCoverWidth;
    }

    // View wiring only: virtualization decides when a cover control is realized; the
    // game command owns the asynchronous load, caching, and stale-result handling.
    private void OnGameCoverAttached(object? sender, VisualTreeAttachmentEventArgs e)
        => RequestGameCover(sender);

    // A recycled tile stays attached while the virtualizing list hands it a different game (desktop
    // ItemsRepeater, or a gamepad row scrolling in). Cut the crossfade and load the new game's cover.
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
