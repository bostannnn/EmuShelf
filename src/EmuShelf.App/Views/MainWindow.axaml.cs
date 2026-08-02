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

        _gamepadViewModel = DataContext as MainViewModel;
        if (_gamepadViewModel is not null)
            _gamepadViewModel.PropertyChanged += OnGamepadViewModelPropertyChanged;
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

        // A mode switch re-sizes the covers for the newly visible viewport without either grid
        // necessarily raising SizeChanged, so the cell width has to follow the value itself.
        if (e.PropertyName is nameof(MainViewModel.GridCoverWidth) &&
            _gamepadViewModel is { } sizingViewModel)
        {
            ApplyCellWidth(sizingViewModel);
            // The covers just resized, so the selector's cover no longer sits where it did.
            Dispatcher.UIThread.Post(UpdateGamepadSelector, DispatcherPriority.Loaded);
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
    // that game's tile into view. The target row may not be realized yet under virtualization, so it
    // is realized on demand and laid out before being brought into view.
    private void RevealFocusedGame() => RevealFocusedGame(0);

    // Mirror the gamepad grid's XAML layout so reveal/selector geometry matches what the layout
    // renders. Keep these in sync with MainWindow.axaml (repeater Margin, UniformGridLayout spacing,
    // the tile's fixed title row) and MainViewModel.GamepadGridSideGutter.
    private const double GamepadColumnSpacing = 28; // UniformGridLayout MinColumnSpacing
    private const double GamepadRowSpacing = 28;    // UniformGridLayout MinRowSpacing
    private const double GamepadTitleRowHeight = 58; // the tile's fixed title row height
    private const double GamepadRevealMargin = 34;   // >= EmuFocusGlow blur radius, so focus never clips

    // Deterministic reveal: the target scroll offset comes from the focused index, the column count,
    // and the uniform row height — not from reading a possibly-stale element rectangle after
    // BringIntoView. That arithmetic can't be pre-empted by a competing layout pass or lost to a rapid
    // d-pad repeat, which is what stranded the selector on an off-screen tile ("the selector
    // disappears"). The focused tile is then realized so the overlay ring can hug its exact cover
    // bounds; a geometry fallback keeps the ring on screen for the frame before realization settles.
    private void RevealFocusedGame(int attempt)
    {
        if (_gamepadViewModel is not { IsGamepadMode: true } viewModel ||
            viewModel.FocusedGame is not { } focused)
        {
            GamepadSelectorRing.IsVisible = false;
            return;
        }

        var index = viewModel.Games.IndexOf(focused);
        if (index < 0)
        {
            GamepadSelectorRing.IsVisible = false;
            return;
        }

        var columns = Math.Max(1, viewModel.GamepadColumnCount);
        var row = index / columns;
        var rowHeight = focused.ShelfCoverHeight + GamepadTitleRowHeight;
        var rowTop = row * (rowHeight + GamepadRowSpacing);
        var rowBottom = rowTop + rowHeight;

        var viewport = GamepadLibraryScroller.Viewport.Height;
        if (viewport > 0)
        {
            var offsetY = GamepadLibraryScroller.Offset.Y;
            var target = offsetY;
            // Keep a full glow radius of clearance top and bottom so the focused tile's accent glow
            // is never shaved by the scroller's clip; scroll only when the row is actually outside it.
            if (rowTop < offsetY + GamepadRevealMargin)
                target = Math.Max(0, rowTop - GamepadRevealMargin);
            else if (rowBottom > offsetY + viewport - GamepadRevealMargin)
                target = rowBottom - viewport + GamepadRevealMargin;
            if (Math.Abs(target - offsetY) > 0.5)
                GamepadLibraryScroller.Offset = GamepadLibraryScroller.Offset.WithY(Math.Max(0, target));
        }

        var element = GamepadRepeater.TryGetElement(index) ?? GamepadRepeater.GetOrCreateElement(index);
        GamepadRepeater.UpdateLayout();
        SyncGamepadColumnCountFromLayout();
        RequestVisibleGamepadCovers();
        UpdateGamepadSelector();

        if (element is null)
        {
            // The tile isn't realized yet (layout not settled). Retry, bounded; the overlay ring is
            // already placed by the geometry fallback, so focus stays visible while we wait.
            if (attempt < 5)
            {
                Dispatcher.UIThread.Post(() => RevealFocusedGame(attempt + 1), DispatcherPriority.Loaded);
            }
            else
            {
                viewModel.LogGamepadGridFault(
                    $"focused tile index {index} did not realize after {attempt + 1} attempts " +
                    $"(columns={columns}, rowTop={rowTop:F0}, viewport={viewport:F0}); ring placed by geometry.");
            }
            return;
        }

        if (viewModel.IsGamepadControllerInputActive && !viewModel.HasGamepadOverlay)
        {
            var gameButton = element as Button ?? element.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => ReferenceEquals(button.DataContext, focused));
            if (gameButton is not null)
                FocusManager?.Focus(gameButton, NavigationMethod.Directional);
        }
    }

    // Covers load off per-element attach/data-context events, which race during rapid LB/RB
    // recycling — a tile handed a new game while detached, or whose event was missed, can stay blank
    // ("empty spaces instead of game covers"). This is the settle-time backstop: once the grid has
    // laid out, request the cover for every realized tile. LoadCover is idempotent (a tile that
    // already has, or is loading, its cover is skipped), so this only fills the ones that were missed.
    private void RequestVisibleGamepadCovers()
    {
        if (_gamepadViewModel is not { IsGamepadMode: true } viewModel)
            return;

        for (var index = 0; index < viewModel.Games.Count; index++)
        {
            if (GamepadRepeater.TryGetElement(index) is { } element)
                RequestGameCover(element);
        }
    }

    // Positions the overlay focus ring over the focused game's cover. It prefers the realized cover
    // element's real bounds (exact, matches the layout) and falls back to computed geometry when the
    // tile is not yet realized, so the ring is present the instant focus moves and can never be
    // virtualized away. Because the ring shares the scroller's content, it then scrolls glued to the
    // cover with no per-frame updates. Called from reveal and after resizes/cover re-sizing.
    private void UpdateGamepadSelector()
    {
        if (_gamepadViewModel is not { IsGamepadMode: true } viewModel ||
            viewModel.FocusedGame is not { } focused)
        {
            GamepadSelectorRing.IsVisible = false;
            return;
        }

        var index = viewModel.Games.IndexOf(focused);
        if (index < 0)
        {
            GamepadSelectorRing.IsVisible = false;
            return;
        }

        if (GamepadRepeater.TryGetElement(index) is { } element &&
            element.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(border => border.Classes.Contains("gamepad-cover-frame")) is { Bounds.Width: > 0 } cover &&
            cover.TranslatePoint(new Point(0, 0), GamepadSelectorLayer) is { } topLeft)
        {
            PlaceSelector(topLeft.X, topLeft.Y, cover.Bounds.Width, cover.Bounds.Height);
            return;
        }

        // Fallback — compute the cover's position from the layout geometry (UniformGridLayout packing
        // plus the repeater's side gutter and the bottom-aligned cover shelf), so a not-yet-realized
        // focused tile still shows the ring rather than blinking out.
        var columns = Math.Max(1, viewModel.GamepadColumnCount);
        var cellWidth = focused.CoverWidth > 0 ? focused.CoverWidth : viewModel.GridCoverWidth;
        if (cellWidth <= 0)
        {
            GamepadSelectorRing.IsVisible = false;
            viewModel.LogGamepadGridFault(
                $"cannot place selector for focused index {index}: no cover width yet (columns={columns}).");
            return;
        }

        var column = index % columns;
        var row = index / columns;
        var coverHeight = focused.CoverHeight > 0 ? focused.CoverHeight : Math.Round(cellWidth / focused.CoverAspectRatio);
        var rowHeight = focused.ShelfCoverHeight + GamepadTitleRowHeight;
        var x = MainViewModel.GamepadGridSideGutterPixels + column * (cellWidth + GamepadColumnSpacing);
        // Covers are bottom-aligned within the shared shelf, so the cover top sits below the cell top.
        var y = row * (rowHeight + GamepadRowSpacing) + Math.Max(0, focused.ShelfCoverHeight - coverHeight);
        PlaceSelector(x, y, cellWidth, coverHeight);
    }

    private void PlaceSelector(double x, double y, double width, double height)
    {
        Canvas.SetLeft(GamepadSelectorRing, x);
        Canvas.SetTop(GamepadSelectorRing, y);
        GamepadSelectorRing.Width = width;
        GamepadSelectorRing.Height = height;
        GamepadSelectorRing.IsVisible = true;
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

    // Runs immediately before a directional move: refreshes the column count from the realized layout
    // (the fix — nav then uses what is on screen, not a stale width estimate) and logs the exact
    // geometry (the diagnostic — one Deck run shows why a move is blocked or the selector is missing).
    private void PrepareGamepadNavigation(GamepadAction action)
    {
        SyncGamepadColumnCountFromLayout();
        if (_gamepadViewModel is not { IsGamepadMode: true } viewModel || viewModel.FocusedGame is not { } focused)
            return;

        var index = viewModel.Games.IndexOf(focused);
        var columns = viewModel.GamepadColumnCount;
        var column = columns > 0 && index >= 0 ? index % columns : -1;

        // The realized width of the focused tile's own row — the ground truth the column count must
        // match (unless the focused tile sits in the short final row).
        var focusedRowRealized = 0;
        if (index >= 0 && GamepadRepeater.TryGetElement(index) is { } focusedElement)
        {
            var focusedRow = (int)Math.Round(focusedElement.Bounds.Y);
            for (var i = 0; i < viewModel.Games.Count; i++)
            {
                if (GamepadRepeater.TryGetElement(i) is { } element &&
                    (int)Math.Round(element.Bounds.Y) == focusedRow)
                {
                    focusedRowRealized++;
                }
            }
        }

        viewModel.LogGamepadGrid(
            $"nav {action}: index={index}/{viewModel.Games.Count}, columns={columns}, column={column}, " +
            $"focusedRowRealized={focusedRowRealized}, viewportW={viewModel.GamepadViewportWidth:F0}, " +
            $"coverW={viewModel.GridCoverWidth:F0}, selectorVisible={GamepadSelectorRing.IsVisible}");
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
                // Grid navigation is index%columns math, so the column count must match what is
                // actually on screen at the moment of the move. Refresh it from the realized layout
                // (and log the geometry) right before dispatching a directional action, so a stale or
                // over-estimated count can't make a mid-row tile read as column 0 (Left then does
                // nothing while Right/Up/Down work).
                if (action is GamepadAction.NavigateLeft or GamepadAction.NavigateRight or
                    GamepadAction.NavigateUp or GamepadAction.NavigateDown)
                {
                    PrepareGamepadNavigation(action);
                }

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
        ApplyCellWidth(viewModel);
        // Correct the column count from the freshly relaid grid, so the first Left/Right after a
        // resize or first show can't clamp against a stale width estimate, then reposition the
        // selector for the new cover size. A persistent arithmetic-vs-rendered disagreement is the
        // signature of the right-column clip, so it is logged for a Deck run.
        Dispatcher.UIThread.Post(
            () =>
            {
                var arithmetic = viewModel.GamepadColumnCount;
                SyncGamepadColumnCountFromLayout();
                if (viewModel.GamepadColumnCount != arithmetic)
                {
                    viewModel.LogGamepadGridFault(
                        $"column count arithmetic={arithmetic} but rendered={viewModel.GamepadColumnCount} " +
                        $"at viewport width {e.NewSize.Width:F0}; using rendered.");
                }

                UpdateGamepadSelector();
            },
            DispatcherPriority.Loaded);
    }

    private void OnGamepadAchievementsSizeChanged(object? sender, SizeChangedEventArgs e) =>
        Dispatcher.UIThread.Post(SyncGamepadAchievementColumnCountFromLayout, DispatcherPriority.Loaded);

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
