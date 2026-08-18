using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.ComponentModel;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views;

public partial class MainWindow : Window
{

    // Rubber-band (marquee) selection for the desktop library. A left-press on the empty canvas arms
    // it; the first drag past this threshold begins it. Origin/current are in LibraryContentPanel
    // (viewport) space, the same space the box is drawn and realized tiles are translated into. The
    // origin is additionally anchored to scroll-content space via _marqueeOriginScrollOffset, so an
    // edge auto-scroll grows the box over revealed rows instead of shedding scrolled-past ones.
    private const double MarqueeDragThreshold = 4;
    private const double MarqueeAutoScrollZone = 28;
    private const double MarqueeAutoScrollMaxSpeed = 26;
    private bool _marqueeArmed;
    private bool _marqueeActive;
    private bool _marqueeAdditive;
    private Point _marqueeOrigin;
    private Point _marqueeCurrent;
    private double _marqueeOriginScrollOffset;
    private double _marqueeAutoScrollVelocity;
    private IPointer? _marqueePointer;
    private ScrollViewer? _libraryListScroller;
    private DispatcherTimer? _marqueeAutoScrollTimer;

    // The window state to return to when leaving a keyboard-toggled Desktop fullscreen.
    private WindowState _preFullScreenState = WindowState.Maximized;
    private KeyboardShelfRotation? _keyboardRotation;

    public MainWindow()
    {
        InitializeComponent();
        // Controller input is supplied by Steam Input as keyboard events. Capture it in the
        // tunnel before a focused game tile consumes Enter/Escape for its own button command.
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        // Held-key rotation needs the release as well as the press, and needs to see it before a
        // focused control can swallow it.
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
        // Keys stop being held the moment the window stops receiving them; without this a rotation
        // begun with Shift+Arrow would keep spinning after an Alt-Tab. Deactivated, not LostFocus:
        // LostFocus is a bubbling routed event, so a child control losing focus reaches the window
        // too and would drop a rotation the player is still holding.
        Deactivated += (_, _) => _keyboardRotation?.Stop();
        // ListBox handles pointer input internally. Observe it first so Grid and List always feed
        // the same view-model-owned desktop selection state, including right-click selection.
        AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Drive the rubber-band from the window so the drag is tracked over the ListBox and tiles
        // alike; both handlers cheaply no-op unless a marquee is armed or active.
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        // If something steals the drag mid-flight, drop the rubber-band so its box can't get stuck.
        AddHandler(PointerCaptureLostEvent, OnWindowPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        // The list header sits outside the scrolling row area, so translate it to match the rows'
        // horizontal offset when the table scrolls sideways (many columns exceed the viewport).
        LibraryList.AddHandler(ScrollViewer.ScrollChangedEvent, OnLibraryScrollChanged);
    }

    private void OnLibraryScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var rows = e.Source as ScrollViewer
            ?? (_libraryListScroller ??= LibraryList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault());
        if (rows is null)
            return;

        // Keep the frozen header aligned with the rows' horizontal scroll offset.
        if (LibraryHeaderScroller.Offset.X != rows.Offset.X)
            LibraryHeaderScroller.Offset = LibraryHeaderScroller.Offset.WithX(rows.Offset.X);

        // The row scroller's viewport is the exact usable width (it already excludes the vertical
        // scrollbar, and is full-width under macOS overlay scrollbars), so the flex column fills the
        // row precisely with no permanent right gap. ScrollChanged also fires when the viewport
        // changes (e.g. the vertical scrollbar appears), so this stays current.
        if (DataContext is MainViewModel viewModel && rows.Viewport.Width > 0)
            viewModel.ListViewportWidth = Math.Max(0, rows.Viewport.Width - 24);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Closing during a drag would otherwise leave the auto-scroll timer running and rooting us.
        ResetMarquee();
        base.OnClosed(e);
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

    // Toggle Desktop fullscreen, remembering the pre-fullscreen state so exiting returns to it rather
    // than always dropping to a fixed state. Minimized is never a sensible thing to restore into, so a
    // toggle from a minimized window comes back maximized.
    private void ToggleFullScreen()
    {
        if (WindowState == WindowState.FullScreen)
        {
            WindowState = _preFullScreenState;
            return;
        }

        _preFullScreenState = WindowState == WindowState.Minimized ? WindowState.Maximized : WindowState;
        WindowState = WindowState.FullScreen;
    }

    private void OnCloseWindowClick(object? sender, RoutedEventArgs e) => Close();

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        CloseSearch();
        e.Handled = true;
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (_keyboardRotation is { } rotation && rotation.Release(e.Key))
            e.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || e.Source is TextBox)
            return;

        if (viewModel.IsGamepadMode)
        {
            // Rotation is checked first: Shift+Arrow and Shift+Enter would otherwise fall through
            // to plain navigation and Confirm, which on Enter means launching the game.
            if (KeyboardShelfRotation.IsRotationKey(e.Key, e.KeyModifiers))
            {
                _keyboardRotation ??= new KeyboardShelfRotation(viewModel.ApplyRightStickRotation);
                e.Handled = _keyboardRotation.Press(e.Key, e.KeyModifiers);
                return;
            }

            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                // The keyboard's R3: return the medium to its resting three-quarter pose.
                e.Handled = viewModel.DispatchGamepadAction(GamepadAction.ResetRotation);
                return;
            }

            // Steam Input delivers controller buttons as these keys; map them (via the shared contract
            // both heads use) to the same logical actions native pad input produces and route both
            // through the one view-model dispatcher.
            if (GamepadKeyMap.Map(e.Key, e.KeyModifiers) is { } action)
            {
                if (viewModel.DispatchGamepadAction(action))
                    e.Handled = true;
            }
            return;
        }

        // Fullscreen toggle for Desktop mode. The window has no title bar, so there is no green
        // fullscreen button or menu item (macOS) and no maximize control beyond the custom caption
        // buttons — leaving the user with no way to fill the screen. F11 is the cross-platform
        // convention; Cmd+Ctrl+F is the macOS system-standard "Enter Full Screen" shortcut, reached
        // for here precisely because the native control is hidden. Gamepad mode is already full screen
        // and returns above, so this only applies to Desktop.
        if (e.Key == Key.F11 ||
            (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Meta) &&
             e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            ToggleFullScreen();
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
        else if (e.Key == Key.Escape && viewModel.HasSelectedGames)
        {
            viewModel.ClearSelectionCommand.Execute(null);
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
    private void OnGameDoubleTapped(object? sender, TappedEventArgs e) =>
        GameCoverInteractions.HandleDoubleTapped(sender, e);

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Gamepad mode swallows the mouse by making the whole gamepad surface non-hit-testable (see
        // GamepadRoot in the XAML), so these desktop-only pointer handlers never see events there.
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

        // Arm a rubber-band. Selection is deferred: a press that never drags past the threshold
        // falls back to the historical empty-canvas behavior (clear) when released. Mouse only — a
        // touch or pen drag on the canvas must stay a pan/scroll, not paint a box.
        if (viewModel.IsBusy || viewModel.Games.Count == 0 || e.Pointer.Type != PointerType.Mouse)
            return;

        _marqueeArmed = true;
        _marqueeActive = false;
        _marqueeAdditive = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        _marqueePointer = e.Pointer;
        _marqueeOrigin = e.GetPosition(LibraryContentPanel);
        _marqueeCurrent = _marqueeOrigin;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if ((!_marqueeArmed && !_marqueeActive) || DataContext is not MainViewModel viewModel)
            return;

        _marqueeCurrent = e.GetPosition(LibraryContentPanel);

        if (!_marqueeActive)
        {
            // Require a deliberate drag before committing so a plain click still clears the canvas.
            var delta = _marqueeCurrent - _marqueeOrigin;
            if (Math.Abs(delta.X) < MarqueeDragThreshold && Math.Abs(delta.Y) < MarqueeDragThreshold)
                return;

            _marqueeActive = true;
            _marqueePointer?.Capture(LibraryContentPanel);
            _marqueeOriginScrollOffset = GetActiveScroller(viewModel)?.Offset.Y ?? 0;
            viewModel.BeginMarqueeSelection(_marqueeAdditive);
            MarqueeBox.IsVisible = true;
        }

        UpdateMarquee(viewModel);
        UpdateMarqueeAutoScroll(viewModel);
        e.Handled = true;
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_marqueeArmed && !_marqueeActive)
            return;

        var wasActive = _marqueeActive;
        var wasAdditive = _marqueeAdditive;
        ResetMarquee();

        if (DataContext is not MainViewModel viewModel)
            return;

        if (wasActive)
        {
            viewModel.EndMarqueeSelection();
            e.Handled = true;
        }
        else if (!wasAdditive)
        {
            // Click on the empty canvas with no drag: clear, as the library always has.
            viewModel.ClearSelectionCommand.Execute(null);
        }
    }

    // Paint the box and report which realized tiles it touches. The top edge is anchored to the
    // content (shifted by how far the view has scrolled since the drag began) so revealing rows by
    // auto-scroll extends the box over them; the bottom edge tracks the pointer. Off-screen tiles are
    // not enumerated, so the view model leaves their (already-claimed) state alone.
    private void UpdateMarquee(MainViewModel viewModel)
    {
        var scrollShift = (GetActiveScroller(viewModel)?.Offset.Y ?? _marqueeOriginScrollOffset)
            - _marqueeOriginScrollOffset;
        var originY = _marqueeOrigin.Y - scrollShift;

        var x = Math.Min(_marqueeOrigin.X, _marqueeCurrent.X);
        var y = Math.Min(originY, _marqueeCurrent.Y);
        var box = new Rect(x, y,
            Math.Abs(_marqueeCurrent.X - _marqueeOrigin.X),
            Math.Abs(_marqueeCurrent.Y - originY));

        Canvas.SetLeft(MarqueeBox, box.X);
        Canvas.SetTop(MarqueeBox, box.Y);
        MarqueeBox.Width = box.Width;
        MarqueeBox.Height = box.Height;

        var realized = new List<GameViewModel>();
        var inBox = new List<GameViewModel>();
        for (var index = 0; index < viewModel.Games.Count; index++)
        {
            var container = viewModel.IsGridView
                ? LibraryRepeater.TryGetElement(index)
                : LibraryList.ContainerFromIndex(index);
            if (container is null ||
                container.TranslatePoint(default, LibraryContentPanel) is not { } topLeft)
                continue;

            var game = viewModel.Games[index];
            realized.Add(game);
            if (box.Intersects(new Rect(topLeft, container.Bounds.Size)))
                inBox.Add(game);
        }

        viewModel.UpdateMarqueeSelection(realized, inBox);
    }

    // The visible scroller for the current layout: the grid's own ScrollViewer, or the one the
    // ListBox templates in. The list one is cached — it appears once the template is applied.
    private ScrollViewer? GetActiveScroller(MainViewModel viewModel)
    {
        if (viewModel.IsGridView)
            return LibraryGridScroller;

        return _libraryListScroller ??= LibraryList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    }

    // Steer an edge auto-scroll from the pointer's depth into the top/bottom margin of the viewport,
    // then run (or stop) the timer that applies it. Kept as a pure computation for unit testing.
    private void UpdateMarqueeAutoScroll(MainViewModel viewModel)
    {
        var scroller = GetActiveScroller(viewModel);
        _marqueeAutoScrollVelocity = scroller is not null &&
            scroller.TranslatePoint(default, LibraryContentPanel) is { } origin
                ? ComputeAutoScrollVelocity(
                    _marqueeCurrent.Y, origin.Y, origin.Y + scroller.Bounds.Height,
                    MarqueeAutoScrollZone, MarqueeAutoScrollMaxSpeed)
                : 0;

        if (_marqueeAutoScrollVelocity != 0)
            StartMarqueeAutoScroll();
        else
            StopMarqueeAutoScroll();
    }

    internal static double ComputeAutoScrollVelocity(
        double pointerY, double viewportTop, double viewportBottom, double edgeZone, double maxSpeed)
    {
        // Too short to carve two non-overlapping zones — don't guess a direction.
        if (viewportBottom - viewportTop <= edgeZone * 2)
            return 0;

        if (pointerY < viewportTop + edgeZone)
            return -maxSpeed * Math.Min(edgeZone, viewportTop + edgeZone - pointerY) / edgeZone;

        if (pointerY > viewportBottom - edgeZone)
            return maxSpeed * Math.Min(edgeZone, pointerY - (viewportBottom - edgeZone)) / edgeZone;

        return 0;
    }

    private void StartMarqueeAutoScroll()
    {
        if (_marqueeAutoScrollTimer is not null)
            return;

        _marqueeAutoScrollTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _marqueeAutoScrollTimer.Tick += OnMarqueeAutoScrollTick;
        _marqueeAutoScrollTimer.Start();
    }

    private void StopMarqueeAutoScroll()
    {
        if (_marqueeAutoScrollTimer is null)
            return;

        _marqueeAutoScrollTimer.Stop();
        _marqueeAutoScrollTimer.Tick -= OnMarqueeAutoScrollTick;
        _marqueeAutoScrollTimer = null;
        _marqueeAutoScrollVelocity = 0;
    }

    private void OnMarqueeAutoScrollTick(object? sender, EventArgs e)
    {
        if (!_marqueeActive || DataContext is not MainViewModel viewModel ||
            GetActiveScroller(viewModel) is not { } scroller || _marqueeAutoScrollVelocity == 0)
        {
            StopMarqueeAutoScroll();
            return;
        }

        var maxOffset = Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height);
        var newY = Math.Clamp(scroller.Offset.Y + _marqueeAutoScrollVelocity, 0, maxOffset);
        if (newY == scroller.Offset.Y)
            return;

        scroller.Offset = scroller.Offset.WithY(newY);
        // Rows revealed by this scroll realize on the next layout pass; the following tick hit-tests
        // them, and claimed rows that scrolled off keep their state, so the selection converges.
        UpdateMarquee(viewModel);
    }

    private void OnWindowPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Ignore the hand-off we trigger ourselves: starting the drag takes capture from whatever the
        // press landed on, which raises this on that element. Reset only on a genuine loss of ours.
        if ((_marqueeArmed || _marqueeActive) && !ReferenceEquals(e.Pointer.Captured, LibraryContentPanel))
            ResetMarquee();
    }

    private void ResetMarquee()
    {
        // Clear state before releasing capture: the release re-enters this via PointerCaptureLost,
        // which then early-returns instead of recursing.
        var pointer = _marqueePointer;
        _marqueePointer = null;
        _marqueeArmed = false;
        _marqueeActive = false;
        _marqueeAdditive = false;
        MarqueeBox.IsVisible = false;
        MarqueeBox.Width = 0;
        MarqueeBox.Height = 0;
        StopMarqueeAutoScroll();
        pointer?.Capture(null);
    }

    private static bool IsNestedButton(Control source) =>
        source is Button || source.GetVisualAncestors().Any(ancestor => ancestor is Button);

    // The grid scroller and repeater are hit-test transparent in their gaps, so a press between
    // covers falls through to the content panel's brush. Treating the panel itself as a surface (but
    // not its descendants, which would swallow toast/banner clicks) makes those gaps clear the
    // selection and, now, start a rubber-band.
    private bool IsLibrarySurface(Control source) =>
        ReferenceEquals(source, LibraryContentPanel) ||
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

    // The desktop grid reports its width through OnLibrarySizeChanged, but that scroller is collapsed
    // in list mode; the list view reports its own width here so the flex (Title) column can fill the
    // row. View wiring only — the view model owns the column-width arithmetic (M40).
    private void OnListViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Fallback estimate for the first layout, before the row scroller reports its exact viewport:
        // the container width minus the item padding (24) and an approximate vertical scrollbar (16).
        // OnLibraryScrollChanged then supplies the precise value.
        if (DataContext is MainViewModel viewModel)
            viewModel.ListViewportWidth = Math.Max(0, e.NewSize.Width - 24 - 16);
    }

    // Column resize (M40): dragging a header cell's right-edge grip sets that fixed column's width;
    // the view model absorbs the change into the flex column and persists it. View wiring only.
    private LibraryColumn? _resizingColumn;
    private double _resizeStartX;
    private double _resizeStartWidth;

    private void OnColumnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: LibraryColumn column })
            return;

        _resizingColumn = column;
        _resizeStartX = e.GetPosition(this).X;
        _resizeStartWidth = column.Width;
        e.Pointer.Capture((Control)sender);
        e.Handled = true;
    }

    private void OnColumnResizeMoved(object? sender, PointerEventArgs e)
    {
        if (_resizingColumn is not { } column || !ReferenceEquals(e.Pointer.Captured, sender))
            return;

        var delta = e.GetPosition(this).X - _resizeStartX;
        column.Width = Math.Clamp(_resizeStartWidth + delta, column.MinWidth, column.MaxWidth);
        e.Handled = true;
    }

    private void OnColumnResizeReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizingColumn is null)
            return;

        _resizingColumn = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // Both grids take their cell width from the one cover width the view model computed for the
    // mode that is on screen. Applying it to both — rather than only to whichever grid raised
    // SizeChanged — means the visible grid's cells can never be left sized for the other mode,
    // which is what produced overlapping tiles and a column pushed off the edge after a switch.
    private void ApplyCellWidth(MainViewModel viewModel)
    {
        // Covers decode to their displayed pixel size, which needs the window's device-pixel ratio to
        // stay crisp on a HiDPI display. This runs on every layout/resize — before the covers realize on
        // first show — so the decode width is right by the time a cover loads.
        viewModel.CoverRenderScale = RenderScaling;

        if (viewModel.GridCoverWidth <= 0)
            return;

        // Only the desktop grid is a virtualizing ItemsRepeater whose cell width must be pushed to the
        // layout. The gamepad grid is a UniformGrid; its tiles take their width from the CoverWidth
        // binding and their column count from GamepadColumnCount, so there is nothing to set here.
        if (LibraryRepeater.Layout is UniformGridLayout desktopLayout)
            desktopLayout.MinItemWidth = viewModel.GridCoverWidth;
    }

    // View wiring only, shared with the gamepad shell — see GameCoverInteractions.
    private void OnGameCoverAttached(object? sender, VisualTreeAttachmentEventArgs e)
        => GameCoverInteractions.CoverAttached(sender);

    private void OnGameCoverDataContextChanged(object? sender, EventArgs e)
        => GameCoverInteractions.CoverDataContextChanged(sender);
}
