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

public partial class GamepadShellView : UserControl
{
    public GamepadShellView()
    {
        InitializeComponent();
        // The gamepad shell reacts to the shared MainViewModel it inherits as DataContext from its
        // host (MainWindow on desktop, MainView on Android); wiring the subscriptions here is the same
        // contract the window used, moved to the view that owns the gamepad tree.
        DataContextChanged += OnDataContextChanged;

        // Drop those subscriptions when this view leaves the tree for good. On Android the supported
        // MainViewFactory hosting builds a FRESH GamepadShellView per activity, so without this the
        // long-lived MainViewModel would keep firing PropertyChanged into dead views across recreations
        // (a leak the old single-view reuse never had). Detach only ever happens at teardown — the couch
        // root is IsVisible-gated, never removed from the tree on a Desktop/Gamepad mode switch — so this
        // never runs mid-session on either head.
        DetachedFromVisualTree += OnDetachedFromVisualTreeCleanup;
    }

    // FocusManager and RenderScaling are TopLevel concerns; the window exposed them directly, a
    // UserControl reaches them through its hosting TopLevel so the moved method bodies are unchanged.
    private IFocusManager? FocusManager => TopLevel.GetTopLevel(this)?.FocusManager;

    // The gamepad grid is a UniformGrid whose tiles size from their CoverWidth binding, so unlike the
    // desktop grid there is no layout width to push — only the HiDPI cover decode scale to keep in
    // sync. (The desktop ApplyCellWidth, which also drives the ItemsRepeater cell width, stays on the
    // window.)
    private void ApplyGamepadCoverRenderScale(MainViewModel viewModel) =>
        viewModel.CoverRenderScale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

    private MainViewModel? _gamepadViewModel;
    private GamepadScraperViewModel? _gamepadScraper;
    private GamepadCoverSearchViewModel? _gamepadCoverSearch;
    private GamepadHotkeysViewModel? _gamepadHotkeys;
    private int _requestedSettingsTextEntryRevision = -1;
    private int _requestedGamepadTextEntryRevision = -1;
    // False until the sliding rail pill has been snapped onto the active tab once; the first placement
    // must not animate in from the left edge.
    private bool _railIndicatorReady;

    // Cached ScrollViewer of the gamepad grid, used to centre the focused row on one fixed viewport line.
    private ScrollViewer? _gamepadScroller;

    // Smooth follow-scroll for the gamepad grid. A d-pad move EASES the offset so a held direction
    // glides continuously under the stationary centred selector; a scope switch / resize / far jump
    // still lands immediately. The target offset is measured ONCE per move from the realized row
    // (position-relative — never an absolute rowIndex*rowHeight, which desynced the panel's estimated
    // extent; see DECISIONS 2026-08-05), then the glide eases toward that fixed _gamepadScrollTarget with
    // pure arithmetic and no per-frame visual-tree reads (which re-enter layout and stack-overflow the
    // panel on short-cover rows). A held d-pad just retargets the one running glide. The glide is ticked by
    // the compositor's own per-frame callback (TopLevel.RequestAnimationFrame), NOT a self-reposted
    // Dispatcher job — on Android's compositor consecutive Render-priority posts drained within a single
    // paint, so every ease step ran before one frame was shown and each row landed as a hard snap; see
    // DECISIONS 2026-08-23.
    private bool _gamepadScrollAnimating;
    private int _gamepadScrollGeneration;
    private double _gamepadScrollTarget;
    // Timestamp of the previous glide frame, so the ease can scale by the real time between vsync
    // callbacks (frame-rate independent). Null on the first frame of a glide and whenever it settles.
    private TimeSpan? _gamepadScrollLastFrameTime;
    // The per-frame RAF callback, built once per glide (it captures that glide's generation), so a held
    // d-pad's continuous glide reuses one delegate and allocates nothing further on the scroll hot path.
    private Action<TimeSpan>? _gamepadScrollFrameCallback;
    private int? _lastRevealedRowIndex;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_gamepadViewModel is not null)
            _gamepadViewModel.PropertyChanged -= OnGamepadViewModelPropertyChanged;

        SyncGamepadScraperSubscription(null);
        SyncGamepadCoverSearchSubscription(null);
        SyncGamepadHotkeysSubscription(null);

        _gamepadViewModel = DataContext as MainViewModel;
        if (_gamepadViewModel is not null)
            _gamepadViewModel.PropertyChanged += OnGamepadViewModelPropertyChanged;
    }

    // Mirror of OnDataContextChanged's teardown, run when the view is permanently detached (activity
    // teardown / window close) so the long-lived view model does not retain this dead view. Also stops
    // any in-flight glide loop bound to it. See the ctor for why detach here is always terminal.
    private void OnDetachedFromVisualTreeCleanup(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_gamepadViewModel is not null)
            _gamepadViewModel.PropertyChanged -= OnGamepadViewModelPropertyChanged;
        _gamepadViewModel = null;

        SyncGamepadScraperSubscription(null);
        SyncGamepadCoverSearchSubscription(null);
        SyncGamepadHotkeysSubscription(null);
        CancelGamepadScroll();
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

    // Mirrors the scraper: the window observes the wrapped cover-search view model directly so the
    // query field takes real keyboard focus (for the Steam/OS on-screen keyboard) and the focused
    // cover tile scrolls into view.
    private void SyncGamepadCoverSearchSubscription(GamepadCoverSearchViewModel? coverSearch)
    {
        if (ReferenceEquals(_gamepadCoverSearch, coverSearch))
            return;

        if (_gamepadCoverSearch is not null)
            _gamepadCoverSearch.PropertyChanged -= OnGamepadCoverSearchPropertyChanged;
        _gamepadCoverSearch = coverSearch;
        if (_gamepadCoverSearch is not null)
            _gamepadCoverSearch.PropertyChanged += OnGamepadCoverSearchPropertyChanged;
    }

    private void OnGamepadCoverSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GamepadCoverSearchViewModel.FocusIndex) or
            nameof(GamepadCoverSearchViewModel.FocusedKind))
        {
            Dispatcher.UIThread.Post(RevealGamepadCoverSearchFocus, DispatcherPriority.Input);
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
            // deep row) is detected inside and snaps instead. Both are cheap no-ops for the layout that
            // is not on screen (RevealFocusedGame acts on the hidden grid, CentreShelf on the hidden strip).
            RevealFocusedGame(animate: true);
            CentreShelf();
            return;
        }

        // Entering the shelf (or any couch-layout switch): re-centre from scratch once the newly visible
        // strip has laid out and its viewport has a real width. A fresh landing, so it snaps.
        if (e.PropertyName is nameof(MainViewModel.GamepadLayout))
        {
            _lastShelfIndex = null;
            Dispatcher.UIThread.Post(() => CentreShelf(viewportWidth: null, forceSnap: true), DispatcherPriority.Loaded);
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.GamepadScraperDetails))
        {
            SyncGamepadScraperSubscription(_gamepadViewModel?.GamepadScraperDetails);
            Dispatcher.UIThread.Post(RevealGamepadScraperFocus, DispatcherPriority.Input);
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.GamepadCoverSearchDetails))
        {
            SyncGamepadCoverSearchSubscription(_gamepadViewModel?.GamepadCoverSearchDetails);
            Dispatcher.UIThread.Post(RevealGamepadCoverSearchFocus, DispatcherPriority.Input);
            return;
        }

        if (e.PropertyName is nameof(MainViewModel.GamepadHotkeys))
        {
            SyncGamepadHotkeysSubscription(_gamepadViewModel?.GamepadHotkeys);
            Dispatcher.UIThread.Post(RevealGamepadHotkeysFocus, DispatcherPriority.Input);
            return;
        }

        // A re-pack (mode switch, resize, filter) rebuilds the justified rows and can move the focused
        // game to a different row, so re-reveal it — otherwise the selection can be left scrolled
        // off-screen — and refresh the HiDPI cover decode scale for the newly visible viewport.
        if (e.PropertyName is nameof(MainViewModel.GamepadGridLayoutRevision) &&
            _gamepadViewModel is { } sizingViewModel)
        {
            ApplyGamepadCoverRenderScale(sizingViewModel);
            Dispatcher.UIThread.Post(RevealFocusedGame, DispatcherPriority.Loaded);
            return;
        }

        // A scope/platform switch replaces the whole grid, so the next focus reveal must be treated as
        // a fresh landing (snap to centre), not an ease from wherever the old content was scrolled.
        if (e.PropertyName is nameof(MainViewModel.SelectedSystem) or nameof(MainViewModel.CurrentLibraryScope))
        {
            _lastRevealedRowIndex = null;
            _lastShelfIndex = null; // the new scope's first shelf centring is a fresh landing (snap)
        }

        if (e.PropertyName is not (nameof(MainViewModel.SelectedSystem) or
            nameof(MainViewModel.CurrentLibraryScope) or nameof(MainViewModel.GamepadOverlay) or
            nameof(MainViewModel.GamepadOverlaySelectionIndex) or nameof(MainViewModel.GamepadOverlayTitle) or
            // The View mode / Sort picker rows share the option list's scroll region, so moving the ring
            // onto them (which changes the region, not the option index) must re-run the reveal to keep
            // them on-screen on a short couch panel.
            nameof(MainViewModel.IsGamepadViewModeRowFocused) or nameof(MainViewModel.IsGamepadSortRowFocused) or
            nameof(MainViewModel.FocusedGamepadAchievement) or
            nameof(MainViewModel.GamepadAchievementLayoutRevision) or
            nameof(MainViewModel.GamepadSettingsFocusRevision) or
            nameof(MainViewModel.IsGamepadSettingsTextEntryOpen) or
            nameof(MainViewModel.IsGamepadSettingsConfirmationOpen) or
            nameof(MainViewModel.IsGamepadSettingsChoicePickerOpen) or
            nameof(MainViewModel.IsGamepadControllerInputActive)))
        {
            return;
        }

        Dispatcher.UIThread.Post(RevealFocusedGame, DispatcherPriority.Input);
        Dispatcher.UIThread.Post(RevealGamepadRail, DispatcherPriority.Input);
        Dispatcher.UIThread.Post(RevealGamepadOverlayFocus, DispatcherPriority.Input);
    }

    // Rate the glide closes the remaining distance, per SECOND. Frame-rate independent: the per-frame
    // fraction is derived from the real delta between vsync callbacks, so the feel is identical at 60 or
    // 120 Hz. ~19/s reproduces the old hand-tuned 0.28-per-frame feel at 60 Hz — a held d-pad (~one row
    // every 110ms) reads as one continuous scroll, and it settles within a few frames once released.
    private const double GamepadScrollDecayPerSecond = 19.0;
    // Within this many pixels of centre the glide lands exactly and stops reposting, so an idle grid
    // burns no CPU.
    private const double GamepadScrollSettleThreshold = 0.5;
    // A d-pad step moves focus at most one row (up/down) or none (left/right). Anything further is a
    // discrete jump — a scope restore of a deep row, a pointer tap on a distant tile — and snaps rather
    // than easing across many screens (which would realize and flash a cover on every intermediate row).
    private const int GamepadMaxEaseRowStep = 2;

    // ---- Physical-media shelf flat fallback centring ----
    // When GL is unavailable, the shelf lays games out in fixed-width slots; GamepadShelfStrip is translated so
    // the focused slot lands on the viewport centre. Slots are uniform, so the target is exact arithmetic
    // (index * slot), independent of each cover's own aspect and of realization — no scroll offset, no
    // realized-container reads. See docs/couch-physical-media-shelf.md.

    /// <summary>Uniform per-game slot width on the shelf. MUST match the item slot Width in the XAML.</summary>
    public const double ShelfSlotWidth = 410;

    // A d-pad step moves the shelf one game; a larger jump (platform/scope switch) snaps rather than
    // whooshing the strip across the whole library and flashing every intermediate cover.
    private const int GamepadMaxShelfEaseStep = 4;
    private int? _lastShelfIndex;

    private void CentreShelf() => CentreShelf(viewportWidth: null, forceSnap: false);

    private void CentreShelf(double? viewportWidth, bool forceSnap)
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.ShowGamepadShelf)
            return;
        if (viewModel.FocusedGame is not { } focused)
            return;
        var index = viewModel.Games.IndexOf(focused);
        if (index < 0)
            return;
        var width = viewportWidth ?? GamepadShelfViewport.Bounds.Width;
        if (width <= 0)
            return;

        var builder = new TransformOperations.Builder(1);
        builder.AppendTranslate(width / 2 - (index * ShelfSlotWidth + ShelfSlotWidth / 2), 0);
        var target = builder.Build();

        // Glide only for a near step; a far jump (or an explicit snap on resize / first show) lands
        // immediately by suspending the strip's transition for that one placement — the same snap path
        // the rail indicator uses.
        var previous = _lastShelfIndex;
        _lastShelfIndex = index;
        if (forceSnap || previous is not { } prev || Math.Abs(index - prev) > GamepadMaxShelfEaseStep)
        {
            var transitions = GamepadShelfStrip.Transitions;
            GamepadShelfStrip.Transitions = null;
            GamepadShelfStrip.RenderTransform = target;
            GamepadShelfStrip.UpdateLayout();
            GamepadShelfStrip.Transitions = transitions;
            return;
        }
        GamepadShelfStrip.RenderTransform = target;
    }

    private void OnGamepadShelfViewportSizeChanged(object? sender, SizeChangedEventArgs e) =>
        CentreShelf(e.NewSize.Width, forceSnap: true);

    // View wiring only: the effect-on tube could not bring up a GL context (a driver that will not serve
    // GLES 3.0, a remote session), so rule out the tube for the session. Effect-on falls back to flat
    // covers; the effect-off in-place host is untouched and can still render there.
    private void OnGamepadShelfHeroFailed(object? sender, Exception exception) =>
        (DataContext as MainViewModel)?.DisableShelfHero(exception);

    // View wiring only: the effect-off in-place host could not bring up a GL context — the Steam Deck
    // case. Rule out only that path; the effect-off shelf then falls back to the tube drawn flat, so
    // the models stay 3D and the tube (effect-on) is never disabled by it.
    private void OnGamepadInlineShelfFailed(object? sender, Exception exception) =>
        (DataContext as MainViewModel)?.DisableInlineShelf(exception);

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

        // The packer stamped each cover with the justified row it landed in, so nav and the rendered
        // rows share one geometry (no arithmetic column count to drift out of sync).
        var rowIndex = focused.GridRowIndex;
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
        _gamepadScrollLastFrameTime = null;
        // Build the per-frame callback ONCE for this glide: it captures this generation, so a stale
        // in-flight frame from a superseded glide still no-ops (generation guard), while every subsequent
        // frame of a held d-pad's glide reuses the same delegate instead of allocating a new closure.
        var generation = ++_gamepadScrollGeneration;
        _gamepadScrollFrameCallback = now => StepGamepadScroll(generation, now);
        RequestGamepadScrollFrame();
    }

    // Stop the glide: bumping the generation makes any queued step no-op, and clearing the flag lets the
    // next d-pad move start a fresh loop.
    private void CancelGamepadScroll()
    {
        _gamepadScrollAnimating = false;
        _gamepadScrollGeneration++;
        _gamepadScrollLastFrameTime = null;
        _gamepadScrollFrameCallback = null;
    }

    // Drive the glide from the compositor's own per-frame callback rather than a self-reposted Dispatcher
    // job. TopLevel.RequestAnimationFrame fires once immediately before each rendered frame, so the offset
    // advances in lock-step with vsync — the continuous-offset model a Flutter/canvas grid uses. A
    // Render-priority Dispatcher repost does NOT guarantee one tick per painted frame on Android's
    // compositor, so intermediate offsets were never shown and each row landed as a hard snap ("rows jump
    // up and down"). Frames are requested only while a glide is in flight and stop on settle, so an idle
    // grid still forces no frames.
    private void RequestGamepadScrollFrame()
    {
        if (_gamepadScrollFrameCallback is not { } callback)
            return;

        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            // No hosting compositor to tick us (detached mid-glide): land on the target at once so focus is
            // never left off-centre, then stop.
            if (_gamepadScroller is { } scroller && scroller.IsAttachedToVisualTree())
                scroller.Offset = scroller.Offset.WithY(Math.Max(0, _gamepadScrollTarget));
            CancelGamepadScroll();
            return;
        }

        topLevel.RequestAnimationFrame(callback);
    }

    // One glide frame: step the offset a time-scaled fraction of the remaining distance to the FIXED
    // target. Pure offset arithmetic — it never reads the visual tree (no TranslatePoint/Bounds), so it
    // cannot trigger the re-entrant layout that stack-overflows the virtualizing panel on short rows. Runs
    // from RequestAnimationFrame and re-requests the next frame until it settles or the ScrollViewer clamps
    // it at a list end.
    private void StepGamepadScroll(int generation, TimeSpan now)
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
            scroller.Offset = scroller.Offset.WithY(Math.Max(0, _gamepadScrollTarget));
            CancelGamepadScroll();
            return;
        }

        // The first frame has no prior timestamp: record the clock and wait one frame so the ease has a
        // real delta to scale by. Every later frame closes GamepadScrollDecayPerSecond of the remaining
        // distance per second, so the glide feels identical regardless of the refresh rate.
        if (_gamepadScrollLastFrameTime is { } last)
        {
            // Clamp dt so a stall (backgrounded, GC pause) can't teleport the offset in one giant jump.
            // A non-advancing frame clock (some headless harnesses) reports dt <= 0; treat it as one 60 Hz
            // frame so the glide still progresses instead of stalling on the sub-pixel floor.
            // Cap at ~2 frames (33 ms) rather than 50 ms so that catching up after a stall (e.g. the heavy
            // row-realization frame) is a gentle step, not a single large lurch that reads as its own jump.
            var dt = (now - last).TotalSeconds;
            dt = dt <= 0 ? 1.0 / 60.0 : Math.Min(dt, 1.0 / 30.0);
            var step = delta * (1 - Math.Exp(-GamepadScrollDecayPerSecond * dt));
            // Never crawl sub-pixel (a long tail can't stall) and never overshoot the target.
            if (Math.Abs(step) < 1)
                step = Math.Sign(delta);
            var next = current + step;
            if ((delta > 0 && next > _gamepadScrollTarget) || (delta < 0 && next < _gamepadScrollTarget))
                next = _gamepadScrollTarget;

            scroller.Offset = scroller.Offset.WithY(Math.Max(0, next));
            // No usable movement means the offset is clamped at a list end (the target row can't be
            // centred). Stop rather than re-request forever against the clamp.
            if (Math.Abs(scroller.Offset.Y - current) < 0.5)
            {
                CancelGamepadScroll();
                return;
            }
        }

        _gamepadScrollLastFrameTime = now;
        RequestGamepadScrollFrame();
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

    // Forces the system on-screen keyboard up once per Search/Rename open. The reveal runs on every couch
    // property change while the overlay is up, so guard on the view model's per-open revision to raise the
    // IME exactly once — a gamepad open needs the explicit summon (directional focus won't raise it), while
    // a screen tap has already raised it and a re-summon would be a no-op. Posted at Loaded so the box has
    // taken focus first, matching the couch Settings text-entry path.
    private void RaiseGamepadTextEntryKeyboard(MainViewModel viewModel, string title)
    {
        if (_requestedGamepadTextEntryRevision == viewModel.GamepadTextEntryRevision)
            return;

        _requestedGamepadTextEntryRevision = viewModel.GamepadTextEntryRevision;
        Dispatcher.UIThread.Post(() => viewModel.RequestOnScreenKeyboard(title), DispatcherPriority.Loaded);
    }

    // Visual focus/reveal is kept here; controller routing and modal state remain in the view model.
    private void RevealGamepadOverlayFocus() => RevealGamepadOverlayFocus(0);

    private void RevealGamepadOverlayFocus(int attempt)
    {
        if (_gamepadViewModel is not { IsGamepadMode: true } viewModel)
            return;

        // The View mode / Sort picker shares the option list's scroll region (see GamepadShellView.axaml).
        // On a short couch panel the picker can be scrolled off, so bring the focused row back into view
        // when the ring lands on it — the option branch below only reveals option buttons.
        if (viewModel.IsGamepadSystemMenuOpen)
        {
            if (viewModel.IsGamepadViewModeRowFocused)
                GamepadViewModeRow.BringIntoView();
            else if (viewModel.IsGamepadSortRowFocused)
                GamepadSortRow.BringIntoView();
        }

        // The section rail scrolls, so keep the selected section visible however the section changed
        // (LB/RB from the content column, or Up/Down while the rail itself is focused).
        if (viewModel.IsGamepadSettingsOpen)
            RevealSelectedGamepadSection();

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
        else if (viewModel.IsGamepadSettingsChoicePickerOpen && viewModel.GamepadSettings is { } choiceSettings)
        {
            GamepadSettingsChoiceOptions.UpdateLayout();
            var option = GamepadSettingsChoiceOptions.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button =>
                    button.DataContext is GamepadChoiceOptionViewModel choice && choice.IsFocused);
            if (option is not null)
            {
                option.BringIntoView();
                if (viewModel.IsGamepadControllerInputActive)
                    FocusManager?.Focus(option, NavigationMethod.Directional);
            }
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
        // Focusing the text box is what raises the OS keyboard on a screen tap; a gamepad-driven open also
        // forces the system IME up (RaiseGamepadTextEntryKeyboard), since directional focus alone won't.
        else if (viewModel.IsGamepadSearchOpen)
        {
            GamepadSearchBox.Focus();
            RaiseGamepadTextEntryKeyboard(viewModel, "Search your library");
        }
        else if (viewModel.IsGamepadRenameOpen)
        {
            GamepadRenameBox.Focus();
            RaiseGamepadTextEntryKeyboard(viewModel, "Enter a new title");
        }
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

    // The section rail scrolls when it holds more sections than fit the column, so keep the current
    // section's button in view. The selected button carries the "selected" style class (bound to the
    // matching IsXxxSection flag), so find it after a layout pass and bring it into view.
    private void RevealSelectedGamepadSection()
    {
        GamepadSettingsNavScroller.UpdateLayout();
        GamepadSettingsNavScroller.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Classes.Contains("gamepad-settings-nav")
                && button.Classes.Contains("selected"))
            ?.BringIntoView();
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

    private void RevealGamepadCoverSearchFocus()
    {
        if (_gamepadViewModel is not { IsGamepadMode: true, IsGamepadCoverSearchOpen: true } viewModel ||
            viewModel.GamepadCoverSearchDetails is not { } coverSearch)
        {
            return;
        }

        // The query field takes real keyboard focus so the Steam on-screen keyboard types into it.
        if (coverSearch.FocusedKind == GamepadCoverSearchTargetKind.SearchField)
        {
            GamepadCoverSearchBox.BringIntoView();
            GamepadCoverSearchBox.Focus();
            return;
        }

        // Search, cover tiles, and "Choose a file" carry the .focused class: scroll into view, and
        // give buttons real focus so the ring reads correctly under directional navigation.
        var focused = GamepadOverlayHost.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.IsEffectivelyVisible && button.Classes.Contains("focused"));
        if (focused is null)
            return;

        focused.BringIntoView();
        if (viewModel.IsGamepadControllerInputActive)
            FocusManager?.Focus(focused, NavigationMethod.Directional);
    }

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

    // Steam Input delivers controller buttons as keys; while the cover-search query box holds focus
    // they reach it here. Route D-pad/A/B to the same navigation the native pad drives, and let plain
    // typing fall through to the box.
    private void OnGamepadCoverSearchTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel { IsGamepadMode: true } viewModel ||
            viewModel.GamepadCoverSearchDetails is not { } coverSearch)
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
                coverSearch.MoveFocus(-1);
                e.Handled = true;
                break;
            case Key.Down:
                coverSearch.MoveFocus(1);
                e.Handled = true;
                break;
            case Key.Enter:
                coverSearch.Activate();
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

    // View wiring only: both grid and list items forward the double-tap gesture to the
    // same command exposed by their game view model.
    private void OnGameDoubleTapped(object? sender, TappedEventArgs e) =>
        GameCoverInteractions.HandleDoubleTapped(sender, e);

    private void OnGamepadLibrarySizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        // Both the cover grid and the shelf raise this, and switching couch layout collapses the
        // outgoing one (IsVisible=false → width 0) while the incoming one lays out. A zero-width
        // reading is that collapse, never a real viewport, so ignore it — otherwise the order of the
        // two events would decide whether the surviving layout keeps its cover size or snaps to the
        // minimum. The visible layout's real-width event is the one that counts.
        if (e.NewSize.Width <= 0)
            return;

        viewModel.GamepadViewportWidth = e.NewSize.Width;
        // Setting the viewport width above already recomputed GamepadColumnCount arithmetically
        // (UpdateCoverLayout), matching what UniformGridLayout will render. The focus ring is part of
        // each tile, so it re-sizes with its cover automatically — nothing to reposition here.
        ApplyGamepadCoverRenderScale(viewModel);
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

    // View wiring only, shared with the gamepad shell — see GameCoverInteractions.
    private void OnGameCoverAttached(object? sender, VisualTreeAttachmentEventArgs e)
        => GameCoverInteractions.CoverAttached(sender);

    private void OnGameCoverDataContextChanged(object? sender, EventArgs e)
        => GameCoverInteractions.CoverDataContextChanged(sender);
}
