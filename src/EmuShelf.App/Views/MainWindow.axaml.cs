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
    private MainViewModel? _gamepadViewModel;
    private GamepadScraperViewModel? _gamepadScraper;
    private GamepadHotkeysViewModel? _gamepadHotkeys;
    private int _requestedSettingsTextEntryRevision = -1;
    // False until the sliding rail pill has been snapped onto the active tab once; the first placement
    // must not animate in from the left edge.
    private bool _railIndicatorReady;

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

    // Cached ScrollViewer of the gamepad grid, used to centre the focused row on one fixed viewport line.
    private ScrollViewer? _gamepadScroller;

    // Smooth follow-scroll for the gamepad grid. A d-pad move EASES the offset so a held direction
    // glides continuously under the stationary centred selector; a scope switch / resize / far jump
    // still lands immediately. The target offset is measured ONCE per move from the realized row
    // (position-relative — never an absolute rowIndex*rowHeight, which desynced the panel's estimated
    // extent; see DECISIONS 2026-08-05), then the loop eases toward that fixed _gamepadScrollTarget with
    // pure arithmetic and no per-frame visual-tree reads (which re-enter layout and stack-overflow the
    // panel on short-cover rows). A held d-pad just retargets the one running loop.
    private bool _gamepadScrollAnimating;
    private int _gamepadScrollGeneration;
    private double _gamepadScrollTarget;
    private int? _lastRevealedRowIndex;

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

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_gamepadViewModel is not null)
            _gamepadViewModel.PropertyChanged -= OnGamepadViewModelPropertyChanged;

        SyncGamepadScraperSubscription(null);
        SyncGamepadHotkeysSubscription(null);

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

    // The controller Hotkeys overlay tracks its focus index on the wrapped view model, so — like the
    // scraper — the window observes that view model directly to scroll the focused matrix button into
    // view (its matrix can be taller than the viewport).
    private void SyncGamepadHotkeysSubscription(GamepadHotkeysViewModel? hotkeys)
    {
        if (ReferenceEquals(_gamepadHotkeys, hotkeys))
            return;

        if (_gamepadHotkeys is not null)
            _gamepadHotkeys.PropertyChanged -= OnGamepadHotkeysPropertyChanged;
        _gamepadHotkeys = hotkeys;
        if (_gamepadHotkeys is not null)
            _gamepadHotkeys.PropertyChanged += OnGamepadHotkeysPropertyChanged;
    }

    private void OnGamepadHotkeysPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GamepadHotkeysViewModel.FocusIndex))
            Dispatcher.UIThread.Post(RevealGamepadHotkeysFocus, DispatcherPriority.Input);
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
            // A d-pad move: ease the grid so a held direction glides. A far change (scope restore of a
            // deep row) is detected inside and snaps instead.
            RevealFocusedGame(animate: true);
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.GamepadScraperDetails))
        {
            SyncGamepadScraperSubscription(_gamepadViewModel?.GamepadScraperDetails);
            Dispatcher.UIThread.Post(RevealGamepadScraperFocus, DispatcherPriority.Input);
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.GamepadHotkeys))
        {
            SyncGamepadHotkeysSubscription(_gamepadViewModel?.GamepadHotkeys);
            Dispatcher.UIThread.Post(RevealGamepadHotkeysFocus, DispatcherPriority.Input);
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

        // A scope/platform switch replaces the whole grid, so the next focus reveal must be treated as
        // a fresh landing (snap to centre), not an ease from wherever the old content was scrolled.
        if (e.PropertyName is nameof(MainViewModel.SelectedSystem) or nameof(MainViewModel.CurrentLibraryScope))
            _lastRevealedRowIndex = null;

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

    // Fraction of the remaining distance the glide closes each frame. Tuned so a held d-pad (~one row
    // every 110ms) reads as one continuous scroll rather than a stack of per-row snaps, while still
    // settling within a few frames once the direction is released.
    private const double GamepadScrollSmoothing = 0.28;
    // Within this many pixels of centre the glide lands exactly and stops reposting, so an idle grid
    // burns no CPU.
    private const double GamepadScrollSettleThreshold = 0.5;
    // A d-pad step moves focus at most one row (up/down) or none (left/right). Anything further is a
    // discrete jump — a scope restore of a deep row, a pointer tap on a distant tile — and snaps rather
    // than easing across many screens (which would realize and flash a cover on every intermediate row).
    private const int GamepadMaxEaseRowStep = 2;

    // View-focused coordination only: the view model owns which game is focused; this window reveals that
    // game's ROW and keeps it centred on one fixed viewport line, so the selector sits in the same place
    // on every platform regardless of cover aspect ratio. A d-pad move (animate) EASES the offset toward
    // centre so a held direction glides; a scope switch / resize / far jump lands immediately. Positioning
    // is always position-relative — measured from the realized row, never a hand-written absolute offset,
    // which desynced the VirtualizingStackPanel's estimated extent on the real compositor (phantom space
    // above the content, selector left off-screen; see DECISIONS 2026-08-05).
    private void RevealFocusedGame() => RevealFocusedGame(animate: false, attempt: 0);

    private void RevealFocusedGame(bool animate) => RevealFocusedGame(animate, 0);

    private void RevealFocusedGame(bool animate, int attempt)
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

        var scroller = ResolveGamepadScroller();
        if (scroller is null || scroller.Viewport.Height <= 0)
        {
            // Layout is not ready (first reveal after a mode/scope switch, before the grid is measured).
            // Retry briefly, carrying the animate flag, so the initial selection still lands centred.
            if (attempt < 5)
                Dispatcher.UIThread.Post(() => RevealFocusedGame(animate, attempt + 1), DispatcherPriority.Loaded);
            return;
        }

        // Only a near move (a d-pad step) eases. A far change of row eases across many screens, which
        // realizes and flashes a cover on every row it passes, so it snaps instead. The very first reveal
        // in a scope (previous row unknown) also snaps.
        var previousRow = _lastRevealedRowIndex;
        _lastRevealedRowIndex = rowIndex;
        if (animate && previousRow is { } prev && Math.Abs(rowIndex - prev) <= GamepadMaxEaseRowStep)
        {
            // Preferred: the target row is realized. Measure its centre offset ONCE here (a realized-row
            // read, the safe context the snap uses) and ease toward that fixed number. The loop itself never
            // touches the visual tree — a per-frame TranslatePoint/Bounds read forces a re-entrant layout
            // that stack-overflows the virtualizing panel on short-cover rows. A held d-pad just retargets
            // the one running loop.
            if (GamepadRowList.ContainerFromIndex(rowIndex) is { } easeRow &&
                TryMeasureCentreDelta(scroller, easeRow, out var easeDelta))
            {
                StartOrRetargetGamepadScroll(scroller, Math.Max(0, scroller.Offset.Y + easeDelta));
                return;
            }

            // A fast held d-pad outran realization: the target row is not materialized yet. Rather than
            // ScrollIntoView (a hard jump that breaks the glide — the residual Up/Down jank Left/Right never
            // has), keep gliding by centring the still-realized PREVIOUS row and shifting one uniform
            // row-stride per row moved. Rows are uniform per view (the invariant the whole centring relies
            // on), so prev's own container height IS the stride, and the target lands the not-yet-realized
            // row on centre. This is position-relative to a realized row — the sanctioned pattern — so the
            // eased offset flows CONTINUOUSLY into the adjacent region (the panel realizes each row as the
            // offset enters it) and never teleports far, so it cannot desync the estimated extent the way an
            // absolute rowIndex*rowHeight jump did (DECISIONS 2026-08-05). Covers are pre-warmed
            // (PrefetchCoversAroundFocus), so the row the glide uncovers is already painted, not a blank pop.
            if (GamepadRowList.ContainerFromIndex(prev) is { Bounds.Height: > 0 } prevRow &&
                TryMeasureCentreDelta(scroller, prevRow, out var prevDelta))
            {
                var target = scroller.Offset.Y + prevDelta + (rowIndex - prev) * prevRow.Bounds.Height;
                StartOrRetargetGamepadScroll(scroller, Math.Max(0, target));
                return;
            }
        }

        // Snap path. Cancel any in-flight ease so it can't fight this landing. If the row is realized,
        // centre it with one relative nudge; if not (a far jump into an unrealized region), ScrollIntoView
        // realizes it, then a later pass — forced non-animate so it stays a snap — does the centring.
        CancelGamepadScroll();
        if (GamepadRowList.ContainerFromIndex(rowIndex) is { } rowContainer)
        {
            CentreRealizedRow(scroller, rowContainer);
        }
        else
        {
            GamepadRowList.ScrollIntoView(rowIndex);
            if (attempt < 5)
                Dispatcher.UIThread.Post(() => RevealFocusedGame(false, attempt + 1), DispatcherPriority.Loaded);
        }
    }

    // Vertical distance (px) from the row container's centre to the viewport centre. Positive means the
    // row is below centre. A realized-row read that must run in a safe context (input/loaded), never from
    // the Render-priority ease loop — reading it there forces a re-entrant layout that stack-overflows the
    // virtualizing panel on short-cover rows.
    private static bool TryMeasureCentreDelta(ScrollViewer scroller, Control rowContainer, out double delta)
    {
        delta = 0;
        if (rowContainer.TranslatePoint(new Point(0, 0), scroller) is not { } rowTopLeft)
            return false;

        delta = rowTopLeft.Y + rowContainer.Bounds.Height / 2 - scroller.Viewport.Height / 2;
        return true;
    }

    // Snap the realized row's centre onto the viewport centre with a SINGLE relative nudge — immune to the
    // extent-estimate drift that hand-written absolute offsets suffered. At the list ends the ScrollViewer
    // clamps the offset, so the first/last rows rest against the edge.
    private static void CentreRealizedRow(ScrollViewer scroller, Control rowContainer)
    {
        if (TryMeasureCentreDelta(scroller, rowContainer, out var delta) &&
            Math.Abs(delta) >= GamepadScrollSettleThreshold)
        {
            scroller.Offset = scroller.Offset.WithY(Math.Max(0, scroller.Offset.Y + delta));
        }
    }

    // Ease toward a FIXED target offset (measured once by the caller). Retargeting mid-glide only moves the
    // number the one running loop chases, so a fast d-pad hold produces one continuous scroll rather than a
    // stack of per-row snaps. The generation token lets CancelGamepadScroll invalidate a queued step.
    private void StartOrRetargetGamepadScroll(ScrollViewer scroller, double target)
    {
        _gamepadScroller = scroller;
        _gamepadScrollTarget = target;

        if (Math.Abs(scroller.Offset.Y - target) < GamepadScrollSettleThreshold)
        {
            scroller.Offset = scroller.Offset.WithY(target);
            CancelGamepadScroll();
            return;
        }

        if (_gamepadScrollAnimating)
            return; // the running loop chases the updated target — never start a second one

        _gamepadScrollAnimating = true;
        var generation = ++_gamepadScrollGeneration;
        Dispatcher.UIThread.Post(() => StepGamepadScroll(generation), DispatcherPriority.Render);
    }

    // Stop the glide: bumping the generation makes any queued step no-op, and clearing the flag lets the
    // next d-pad move start a fresh loop.
    private void CancelGamepadScroll()
    {
        _gamepadScrollAnimating = false;
        _gamepadScrollGeneration++;
    }

    // One glide frame: step the offset a fraction of the remaining distance to the FIXED target. Pure
    // offset arithmetic — it never reads the visual tree (no TranslatePoint/Bounds), so it cannot trigger
    // the re-entrant layout that stack-overflows the virtualizing panel on short rows. Reposts itself at
    // Render priority until it settles or the ScrollViewer clamps it at a list end.
    private void StepGamepadScroll(int generation)
    {
        if (generation != _gamepadScrollGeneration || !_gamepadScrollAnimating)
            return;

        if (_gamepadScroller is not { } scroller || !scroller.IsAttachedToVisualTree())
        {
            CancelGamepadScroll();
            return;
        }

        var current = scroller.Offset.Y;
        var delta = _gamepadScrollTarget - current;
        if (Math.Abs(delta) < GamepadScrollSettleThreshold)
        {
            scroller.Offset = scroller.Offset.WithY(_gamepadScrollTarget);
            CancelGamepadScroll();
            return;
        }

        var step = delta * GamepadScrollSmoothing;
        // Never crawl sub-pixel: guarantee at least a whole pixel of travel so a long tail can't stall,
        // and never overshoot the target.
        if (Math.Abs(step) < 1)
            step = Math.Sign(delta);
        var next = current + step;
        if ((delta > 0 && next > _gamepadScrollTarget) || (delta < 0 && next < _gamepadScrollTarget))
            next = _gamepadScrollTarget;

        scroller.Offset = scroller.Offset.WithY(Math.Max(0, next));
        // No usable movement means the offset is clamped at a list end (the target row can't be centred).
        // Stop rather than repost forever against the clamp.
        if (Math.Abs(scroller.Offset.Y - current) < 0.5)
        {
            CancelGamepadScroll();
            return;
        }

        Dispatcher.UIThread.Post(() => StepGamepadScroll(generation), DispatcherPriority.Render);
    }

    // Cache the grid's ScrollViewer; it is stable once the ListBox realizes, but re-resolve if the
    // cached instance was detached (e.g. after a template reload).
    private ScrollViewer? ResolveGamepadScroller()
    {
        if (_gamepadScroller is { } cached && cached.IsAttachedToVisualTree())
            return cached;

        _gamepadScroller = GamepadRowList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        return _gamepadScroller;
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
    // Visual focus only for the Hotkeys overlay: the two global buttons sit above the scroll and are
    // always visible, so only the focused per-emulator Apply / Revert button (inside the matrix
    // scroller, which can overflow) needs bringing into view. The .focused class carries the ring; no
    // keyboard focus is taken (the overlay has no text entry and the VM routes A directly).
    private void RevealGamepadHotkeysFocus()
    {
        if (_gamepadViewModel is not { IsGamepadMode: true, IsGamepadHotkeysOpen: true })
            return;

        GamepadHotkeysScroller.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.IsVisible && button.Classes.Contains("focused"))
            ?.BringIntoView();
    }

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

        RevealScraperRowWithLookahead(focused);
        if (focused is Button && viewModel.IsGamepadControllerInputActive)
            FocusManager?.Focus(focused, NavigationMethod.Directional);

        // On open the ring defaults to Apply (outside the list), so the field scroller's offset never
        // moves and no ScrollChanged fires — recompute the edge fade here so the "more below" cue is
        // present from the first frame, not only after the first scroll.
        if (GamepadScraperFieldsScroller is { IsEffectivelyVisible: true } fieldScroller)
            UpdateScraperScrollFade(fieldScroller);
    }

    // Scroll the focused row into view but keep a sliver of the neighbouring row peeking past it,
    // so a gamepad player sees the list is mid-scroll instead of a static page that only jumps at
    // the very edge. Falls back to a plain BringIntoView for controls outside a gamepad scroll
    // region (the pinned Apply/Refresh block, the connect form, terminal messages).
    private static void RevealScraperRowWithLookahead(Control focused)
    {
        var scroller = focused.FindAncestorOfType<ScrollViewer>();
        if (scroller is null || !scroller.Classes.Contains("gamepad-scroll") ||
            focused.TranslatePoint(new Point(0, 0), scroller) is not { } top)
        {
            focused.BringIntoView();
            return;
        }

        const double peek = 40; // roughly half a compact scraper row
        var viewport = scroller.Viewport.Height;
        var rowTop = top.Y;
        var rowBottom = top.Y + focused.Bounds.Height;

        double delta;
        if (rowTop - peek < 0)
            delta = rowTop - peek;
        else if (rowBottom + peek > viewport)
            delta = rowBottom + peek - viewport;
        else
            return;

        var max = Math.Max(0, scroller.Extent.Height - viewport);
        scroller.Offset = scroller.Offset.WithY(Math.Clamp(scroller.Offset.Y + delta, 0, max));
    }

    // Alpha-only gradients that fade the scraper field list toward whichever edge still has content
    // off-screen, so the "more below" cue is unmistakable on a controller. Alpha-only keeps them
    // theme-agnostic — the popover colour behind the list varies per palette.
    private static readonly IBrush ScraperFadeTop = BuildScraperFadeMask(fadeTop: true, fadeBottom: false);
    private static readonly IBrush ScraperFadeBottom = BuildScraperFadeMask(fadeTop: false, fadeBottom: true);
    private static readonly IBrush ScraperFadeBoth = BuildScraperFadeMask(fadeTop: true, fadeBottom: true);

    private static LinearGradientBrush BuildScraperFadeMask(bool fadeTop, bool fadeBottom)
    {
        const double edge = 0.06; // fraction of the viewport that fades on each active edge
        var opaque = Color.FromArgb(255, 0, 0, 0);
        var clear = Color.FromArgb(0, 0, 0, 0);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        };
        brush.GradientStops.Add(new GradientStop(fadeTop ? clear : opaque, 0));
        brush.GradientStops.Add(new GradientStop(opaque, edge));
        brush.GradientStops.Add(new GradientStop(opaque, 1 - edge));
        brush.GradientStops.Add(new GradientStop(fadeBottom ? clear : opaque, 1));
        return brush;
    }

    private void OnGamepadScraperScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer scroller)
            UpdateScraperScrollFade(scroller);
    }

    private static void UpdateScraperScrollFade(ScrollViewer scroller)
    {
        const double slack = 1.0;
        var canUp = scroller.Offset.Y > slack;
        var canDown = scroller.Offset.Y < scroller.Extent.Height - scroller.Viewport.Height - slack;
        scroller.OpacityMask = (canUp, canDown) switch
        {
            (true, true) => ScraperFadeBoth,
            (true, false) => ScraperFadeTop,
            (false, true) => ScraperFadeBottom,
            _ => null,
        };
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

        ActiveGamepadRailTab()?.BringIntoView();
        UpdateRailIndicator(animate: true);
    }

    private Control? ActiveGamepadRailTab()
    {
        if (_gamepadViewModel is null)
            return null;

        return _gamepadViewModel.IsAllGamesSelected
            ? GamepadAllGamesTab
            : GamepadRailScroller.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => button.DataContext is GamepadPlatformTabViewModel { IsActive: true });
    }

    // A resize — including the first paint when gamepad mode becomes visible — relays out the tabs, so
    // re-place the pill onto the active tab without a glide: there is no user move to animate.
    private void OnGamepadRailSizeChanged(object? sender, SizeChangedEventArgs e) =>
        UpdateRailIndicator(animate: false);

    // One selection pill sits behind the tabs and is moved to overlay the active tab, so a platform
    // switch reads as the highlight travelling left/right rather than popping in place per tab. The
    // active tab grows to show its name, so force layout before measuring, then size the pill to the
    // tab and drive a translate transform to it. Only the translate eases (the composited transition on
    // Border.gamepad-platform-indicator); Width/Height are applied instantly, because animating a layout
    // property is a per-frame UI-thread layout pass that stutters under the platform-switch relayout.
    private void UpdateRailIndicator(bool animate)
    {
        if (_gamepadViewModel is not { IsGamepadMode: true })
            return;

        var indicator = GamepadRailIndicator;
        if (indicator?.Parent is not Visual reference)
            return;

        var active = ActiveGamepadRailTab();
        if (active is null)
            return;

        GamepadRailTabs.UpdateLayout();
        if (active.Bounds.Width <= 0)
            return;

        var origin = active.TranslatePoint(new Point(0, 0), reference);
        if (origin is null)
            return;

        var builder = new TransformOperations.Builder(1);
        builder.AppendTranslate(origin.Value.X, origin.Value.Y);
        var target = builder.Build();

        // The first placement (and every resize) must appear already on the active tab, not glide in
        // from the left edge, so suspend the transitions for that one snap and restore them afterwards.
        if (!animate || !_railIndicatorReady)
        {
            var transitions = indicator.Transitions;
            indicator.Transitions = null;
            indicator.Width = active.Bounds.Width;
            indicator.Height = active.Bounds.Height;
            indicator.RenderTransform = target;
            indicator.IsVisible = true;
            indicator.UpdateLayout();
            indicator.Transitions = transitions;
            _railIndicatorReady = true;
            return;
        }

        indicator.Width = active.Bounds.Width;
        indicator.Height = active.Bounds.Height;
        indicator.RenderTransform = target;
        indicator.IsVisible = true;
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
