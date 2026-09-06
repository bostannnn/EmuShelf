using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmuShelf.App.Services;

namespace EmuShelf.App.ViewModels;

/// <summary>Which temporary surface, if any, sits above the resting spotlight surface.</summary>
public enum SecondScreenOverlayKind
{
    None,
    Drawer,
    Achievements,
}

/// <summary>One launchable app in the second-screen drawer.</summary>
public sealed class SecondScreenAppViewModel(string component, string label, Bitmap? icon)
{
    public string Component { get; } = component;
    public string Label { get; } = label;
    public Bitmap? Icon { get; } = icon;
    public bool HasIcon => Icon is not null;
}

/// <summary>One dock slot; empty when <see cref="Label"/> is null.</summary>
public sealed partial class SecondScreenSlotViewModel(int index) : ObservableObject
{
    public int Index { get; } = index;

    [ObservableProperty]
    public partial string? Label { get; set; }

    [ObservableProperty]
    public partial Bitmap? Icon { get; set; }

    public bool IsEmpty => string.IsNullOrEmpty(Label);
    public bool HasIcon => Icon is not null;

    partial void OnLabelChanged(string? value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnIconChanged(Bitmap? value) => OnPropertyChanged(nameof(HasIcon));
}

/// <summary>
/// Presentation state for the Thor companion surface, hosted as an embedded Avalonia top level on the
/// second display. The resting surface shows the focused (or running) game's fan art with its logo
/// staggered in on top; the Android head's <c>SecondScreenController</c> loads the art and drives the
/// crossfade via <see cref="FanartOpacity"/>/<see cref="LogoOpacity"/>. Carries no Android types so it
/// stays desktop-testable, and binds against the app's theme.
/// </summary>
public sealed partial class SecondScreenViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStandby))]
    [NotifyPropertyChangedFor(nameof(SpotlightLogoOpacity))]
    [NotifyPropertyChangedFor(nameof(ShowRestingBranding))]
    public partial SecondScreenOverlayKind Overlay { get; set; }

    // --- Playing-elsewhere standby ---

    /// <summary>
    /// True while a game is running on the *other* screen, so this idle EmuShelf surface should sit in a
    /// dim, low-burn-in standby (a near-black wash with the game's logo faintly visible) instead of the
    /// full-brightness browse spotlight. Set by the Android controller on game start / return; the same
    /// flag drives whichever surface is idle (Screen-2 while the game is on the built-in panel, or the
    /// built-in companion while the game is on Screen-2), since both bind this one view model.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStandby))]
    [NotifyPropertyChangedFor(nameof(SpotlightLogoOpacity))]
    [NotifyPropertyChangedFor(nameof(ShowRestingBranding))]
    public partial bool IsGameRunning { get; set; }

    /// <summary>
    /// Whether the dim standby wash is showing right now: a game is running, nothing sits above it, and the
    /// panel has not been touched recently. An open overlay (achievements / the app drawer) takes precedence,
    /// so opening achievements over a running game lifts the dim and closing it restores it — the requested
    /// behaviour — and a touch does the same for <see cref="WakeDuration"/> (see <see cref="NoteInteraction"/>).
    /// </summary>
    public bool IsStandby => IsGameRunning && Overlay == SecondScreenOverlayKind.None && !IsAwake;

    /// <summary>
    /// True while a recent touch is holding the dim off. Kept separate from <see cref="Overlay"/> because
    /// waking is not a mode: nothing is open, the surface is simply lit long enough to be used, and it goes
    /// back to sleep on its own.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStandby))]
    [NotifyPropertyChangedFor(nameof(SpotlightLogoOpacity))]
    [NotifyPropertyChangedFor(nameof(ShowRestingBranding))]
    public partial bool IsAwake { get; private set; }

    /// <summary>How long a touch keeps the panel lit. Long enough to see the dock and press what you came
    /// for — each press buys another window — and short enough that a panel left alone goes dark again
    /// almost immediately (the Thor's Screen-2 is burn-in-prone).</summary>
    internal TimeSpan WakeDuration { get; set; } = TimeSpan.FromSeconds(5);

    private DispatcherTimer? _wakeTimer;

    /// <summary>
    /// A touch landed on the companion. While a game is running the surface is dimmed to near-black, so the
    /// first touch has to be about SEEING what is there: the standby wash swallows it (it is hit-testable
    /// only while dimmed) and this lifts the dim instead of activating whatever sits under the finger.
    /// Every later touch — a dock slot, the trophy, a badge — passes through normally and lands here too,
    /// which restarts the countdown, so the panel stays lit for as long as it is being used.
    /// </summary>
    public void NoteInteraction()
    {
        // Nothing to wake from while the library is being browsed: the surface is already at full
        // brightness, and starting a timer per touch would just churn. Same while an overlay is up —
        // achievements / the drawer own the dim for as long as they are open, and the touch that
        // closes one must hand the wash straight back rather than buy five more seconds of light.
        if (!IsGameRunning || Overlay != SecondScreenOverlayKind.None)
            return;

        IsAwake = true;
        RestartWakeTimer();
    }

    /// <summary>Ends the wake window early (the game exited, or the countdown elapsed).</summary>
    private void ReturnToStandby()
    {
        StopWakeTimer();
        IsAwake = false;
    }

    private void RestartWakeTimer()
    {
        if (_wakeTimer is null)
        {
            _wakeTimer = new DispatcherTimer { Interval = WakeDuration };
            _wakeTimer.Tick += (_, _) => ReturnToStandby();
        }

        // Restart, not merely start: each touch buys the full window again.
        _wakeTimer.Stop();
        _wakeTimer.Interval = WakeDuration;
        _wakeTimer.Start();
    }

    private void StopWakeTimer() => _wakeTimer?.Stop();

    partial void OnIsGameRunningChanged(bool value)
    {
        // The wake window only exists to lift the standby dim, so it dies with the game session — both
        // when one starts (a stale window from the last one must not swallow the first dim) and when one
        // ends (the surface is fully lit again; a pending tick would be a no-op that keeps a timer alive).
        ReturnToStandby();
    }

    /// <summary>
    /// Opacity for the RESTING spotlight logo, which is a different element from the faint one the
    /// standby wash draws over it. Both are centred, but in different regions — the resting logo in the
    /// spotlight row, the standby one over the whole surface including the dock bar — so with both
    /// painted the two copies sat ~32px apart and the running game's logo appeared doubled on the
    /// companion screen. The standby copy owns the look while a game runs, so the resting one fades out
    /// under it (the Image's own opacity transition carries the crossfade).
    /// </summary>
    public double SpotlightLogoOpacity => IsStandby ? 0 : LogoOpacity;

    /// <summary>The no-artwork wordmark, likewise suppressed while standby draws its own faint copy.</summary>
    public bool ShowRestingBranding => ShowBranding && !IsStandby;

    // --- Resting spotlight (fan art + logo) ---

    [ObservableProperty]
    public partial Bitmap? FanartImage { get; set; }

    [ObservableProperty]
    public partial Bitmap? WheelImage { get; set; }

    [ObservableProperty]
    public partial double FanartOpacity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpotlightLogoOpacity))]
    public partial double LogoOpacity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRestingBranding))]
    public partial bool ShowBranding { get; set; } = true;

    public bool HasFanart => FanartImage is not null;
    public bool HasWheel => WheelImage is not null;

    partial void OnFanartImageChanging(Bitmap? value)
    {
        if (!ReferenceEquals(FanartImage, value))
            FanartImage?.Dispose();
    }

    partial void OnFanartImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasFanart));

    partial void OnWheelImageChanging(Bitmap? value)
    {
        if (!ReferenceEquals(WheelImage, value))
            WheelImage?.Dispose();
    }

    partial void OnWheelImageChanged(Bitmap? value) => OnPropertyChanged(nameof(HasWheel));

    /// <summary>Swaps the resting art. The controller sequences the crossfade via the opacity fields.</summary>
    public void SetSpotlight(Bitmap? fanart, Bitmap? wheel)
    {
        FanartImage = fanart;
        WheelImage = wheel;
        ShowBranding = fanart is null && wheel is null;
    }

    // --- Drawer / dock / achievements ---

    [ObservableProperty]
    public partial string DrawerTitle { get; set; } = "All apps";

    [ObservableProperty]
    public partial bool CanClearSlot { get; set; }

    [ObservableProperty]
    public partial string AchievementsTitle { get; set; } = "Achievements";

    [ObservableProperty]
    public partial string? AchievementsStatus { get; set; }

    /// <summary>Compact progress line for the panel header (e.g. "12 / 40 · 340 pts"); null hides it.</summary>
    [ObservableProperty]
    public partial string? AchievementsSummary { get; set; }

    [ObservableProperty]
    public partial bool CanRefresh { get; set; }

    // The tapped badge, whose title/description/meta the panel shows beneath the grid. A touch surface has
    // no hover, so this replaces the grid's (useless) per-tile tooltip. Its IsFocused drives the tile's
    // accent ring (reusing the gamepad row's logical-focus flag; the companion has its own row instances).
    [ObservableProperty]
    public partial AchievementRowViewModel? SelectedAchievement { get; set; }

    public bool HasSelectedAchievement => SelectedAchievement is not null;
    public bool HasSummary => !string.IsNullOrEmpty(AchievementsSummary);

    /// <summary>
    /// One compact line for the selected badge: points, plus the earned date when it is earned. The lock /
    /// hardcore state is intentionally omitted — the tile's dimming (locked) and gold ring (hardcore)
    /// already show it, so repeating it here is noise. Null when nothing is selected.
    /// </summary>
    public string? SelectedAchievementMeta
    {
        get
        {
            if (SelectedAchievement is not { } achievement)
                return null;
            var points = achievement.Points == 1 ? "1 pt" : $"{achievement.Points} pts";
            return achievement.EarnedAt is { } earned
                ? $"{points} · Earned {earned.ToLocalTime():d MMM}"
                : points;
        }
    }

    partial void OnAchievementsSummaryChanged(string? value) => OnPropertyChanged(nameof(HasSummary));

    partial void OnSelectedAchievementChanged(
        AchievementRowViewModel? oldValue,
        AchievementRowViewModel? newValue)
    {
        if (oldValue is not null)
            oldValue.IsFocused = false;
        if (newValue is not null)
            newValue.IsFocused = true;
        OnPropertyChanged(nameof(HasSelectedAchievement));
        OnPropertyChanged(nameof(SelectedAchievementMeta));
    }

    /// <summary>Selects a badge (tapped in the grid), moving the accent ring and the detail strip to it.</summary>
    public void SelectAchievement(AchievementRowViewModel? achievement)
    {
        if (!ReferenceEquals(SelectedAchievement, achievement))
            SelectedAchievement = achievement;
    }

    /// <summary>
    /// Raised when gamepad navigation moves the selection, carrying the flat badge index so the view can
    /// scroll that badge's row into view. Touch selection doesn't raise it — the tapped tile is already
    /// visible — so it fires only for D-pad moves.
    /// </summary>
    public event Action<int>? SelectionScrolledTo;

    /// <summary>
    /// Drives the achievement grid from the gamepad when the second screen owns input focus (the user last
    /// touched it). The D-pad walks the selection across the same column stride the grid renders — mirroring
    /// the couch achievements overlay — and B/Cancel closes the panel. Returns true when the action was
    /// consumed; false lets the caller fall back to the couch (so the gamepad is never dead when the panel
    /// isn't navigable). Only the achievements overlay is navigable here; the dock/drawer stay touch-only.
    /// </summary>
    public bool DispatchGamepadAction(GamepadAction action)
    {
        // Cancel always closes an open overlay (Back behaviour), even an empty/message achievements panel.
        if (action == GamepadAction.Cancel && Overlay != SecondScreenOverlayKind.None)
        {
            OverlayClosed?.Invoke();
            return true;
        }

        if (!IsAchievementsOpen || _achievements.Count == 0)
            return false;

        switch (action)
        {
            case GamepadAction.NavigateLeft:
                MoveSelectionHorizontal(-1);
                return true;
            case GamepadAction.NavigateRight:
                MoveSelectionHorizontal(1);
                return true;
            case GamepadAction.NavigateUp:
                MoveSelectionVertical(-1);
                return true;
            case GamepadAction.NavigateDown:
                MoveSelectionVertical(1);
                return true;
            // With a grid up the second screen owns the pad completely: every remaining action is swallowed
            // so nothing leaks to the couch behind (which would open an overlay on the main screen the user
            // isn't looking at — e.g. Y/Actions — leaving it stuck open and invisible). Confirm in
            // particular has nothing more to open: the selected badge's title/description/points already sit
            // in the detail strip. To browse to another game, touch the main screen (focus-follows-touch).
            // The fall-through for the couch is the empty/spotlight case, handled by the guard above.
            default:
                return true;
        }
    }

    private int SelectedIndex =>
        SelectedAchievement is { } selected ? _achievements.IndexOf(selected) : -1;

    private void MoveSelectionHorizontal(int direction)
    {
        var index = Math.Max(0, SelectedIndex);
        var columns = Math.Max(1, _achievementColumnCount);
        var column = index % columns;
        var target = index + Math.Sign(direction);
        // Guard the row edges so Right on the last column (or Left on the first) doesn't jump rows.
        if (target >= 0 && target < _achievements.Count &&
            (direction < 0 ? column > 0 : column < columns - 1))
        {
            MoveSelectionTo(target);
        }
    }

    private void MoveSelectionVertical(int direction)
    {
        var index = Math.Max(0, SelectedIndex);
        var columns = Math.Max(1, _achievementColumnCount);
        var target = index + (Math.Sign(direction) * columns);
        if (target >= 0 && target < _achievements.Count)
            MoveSelectionTo(target);
    }

    private void MoveSelectionTo(int index)
    {
        SelectAchievement(_achievements[index]);
        SelectionScrolledTo?.Invoke(index);
    }

    public ObservableCollection<SecondScreenSlotViewModel> Dock { get; } =
        new(Enumerable.Range(0, 5).Select(index => new SecondScreenSlotViewModel(index)));

    public ObservableCollection<SecondScreenAppViewModel> Apps { get; } = [];

    // Reuses the gamepad achievements row VM so the companion badge grid gets the same cached-badge
    // loading (off-thread, from IRetroAchievementsBadgeCache) and bitmap disposal, rather than a second
    // implementation. Badges are deferred (loadBadge:false) and requested per tile on attach, so only the
    // on-screen ones ever load.
    private readonly List<AchievementRowViewModel> _achievements = [];

    /// <summary>
    /// The achievement badges sliced into rows of <see cref="AchievementColumnCount"/> for a virtualized
    /// vertical list — the same shape as the gamepad grid, so a 400-achievement set realizes only its
    /// on-screen rows instead of every tile. Rebuilt (reference-only) when the set or the column count
    /// changes.
    /// </summary>
    public BulkObservableCollection<IReadOnlyList<AchievementRowViewModel>> AchievementRows { get; } = [];

    /// <summary>Total badge count across all rows; drives the empty state.</summary>
    public int AchievementCount => _achievements.Count;

    /// <summary>True once a set is loaded; drives the grid vs the centered empty/message state.</summary>
    public bool HasAchievements => _achievements.Count > 0;

    /// <summary>
    /// Shows the status line only when it is saying something over a loaded grid — "Refreshing…", offline,
    /// an error. The boring "Updated {time}" case is left null by the controller, and empty/message states
    /// use the centered message instead, so the header stays quiet in the normal loaded case.
    /// </summary>
    public bool HasInlineStatus => HasStatus && _achievements.Count > 0;

    private int _achievementColumnCount = 1;

    /// <summary>
    /// Columns the badge grid renders, derived from the viewport width by the view's SizeChanged. Setting
    /// it re-slices the flat set into rows so the rendered column count always matches.
    /// </summary>
    public int AchievementColumnCount
    {
        get => _achievementColumnCount;
        set
        {
            if (SetProperty(ref _achievementColumnCount, Math.Max(1, value)))
                RebuildAchievementRows();
        }
    }

    /// <summary>
    /// The edge length of each square badge tile, sized by the view's SizeChanged so the chosen column
    /// count fills the panel width exactly (no dead space on the right). Defaults to a compact value so the
    /// first frame — before layout runs — is already dense rather than showing a few oversized tiles.
    /// </summary>
    [ObservableProperty]
    public partial double AchievementTileSize { get; set; } = 72;

    /// <summary>Replaces the badge set (disposing the previous one's bitmaps) and re-slices it into rows.</summary>
    public void SetAchievements(IReadOnlyList<AchievementRowViewModel> achievements)
    {
        DisposeAchievements();
        _achievements.AddRange(achievements);
        RaiseAchievementCountDependents();
        RebuildAchievementRows();
        // Land on the first badge so the title/subtitle strip is populated the moment the panel opens (and
        // after every game-change follow), rather than sitting blank until the user taps.
        SelectAchievement(_achievements.Count > 0 ? _achievements[0] : null);
    }

    /// <summary>
    /// Empties the achievement grid, disposing each row's cached badge bitmap. Every rebuild (a game
    /// change follow, a refresh) and the presentation teardown route through here so scrolling the
    /// library with the panel open does not accumulate undisposed badge bitmaps.
    /// </summary>
    public void ClearAchievements()
    {
        DisposeAchievements();
        RaiseAchievementCountDependents();
        if (AchievementRows.Count > 0)
            AchievementRows.Clear();
    }

    private void RaiseAchievementCountDependents()
    {
        OnPropertyChanged(nameof(AchievementCount));
        OnPropertyChanged(nameof(HasAchievements));
        OnPropertyChanged(nameof(IsAchievementsGridRealized));
        OnPropertyChanged(nameof(HasInlineStatus));
    }

    private void DisposeAchievements()
    {
        // Drop the selection before disposing the rows it points at, so the detail strip never binds a
        // disposed badge bitmap.
        SelectedAchievement = null;
        foreach (var achievement in _achievements)
            achievement.Dispose();
        _achievements.Clear();
    }

    private void RebuildAchievementRows()
    {
        if (_achievements.Count == 0)
        {
            if (AchievementRows.Count > 0)
                AchievementRows.Clear();
            return;
        }

        var columns = Math.Max(1, _achievementColumnCount);
        var rows = new List<IReadOnlyList<AchievementRowViewModel>>((_achievements.Count + columns - 1) / columns);
        for (var start = 0; start < _achievements.Count; start += columns)
        {
            var take = Math.Min(columns, _achievements.Count - start);
            var row = new AchievementRowViewModel[take];
            for (var offset = 0; offset < take; offset++)
                row[offset] = _achievements[start + offset];
            rows.Add(row);
        }

        AchievementRows.ReplaceAll(rows);
    }

    public bool IsDrawerOpen => Overlay == SecondScreenOverlayKind.Drawer;
    public bool IsAchievementsOpen => Overlay == SecondScreenOverlayKind.Achievements;
    public bool HasStatus => !string.IsNullOrEmpty(AchievementsStatus);

    // The achievements sheet stays mounted (opacity-driven) so it can scale in when opened, but its badge
    // ListBox must only realize rows while the sheet is actually up — otherwise a 400-badge set would keep
    // its on-screen rows virtualized behind a closed, invisible overlay. Gate the grid on both.
    public bool IsAchievementsGridRealized => IsAchievementsOpen && HasAchievements;

    partial void OnOverlayChanged(SecondScreenOverlayKind value)
    {
        // An overlay taking over ends any wake window in flight, so closing it restores the dim at
        // once instead of whenever the touch that opened it happens to expire.
        if (value != SecondScreenOverlayKind.None)
            ReturnToStandby();
        OnPropertyChanged(nameof(IsDrawerOpen));
        OnPropertyChanged(nameof(IsAchievementsOpen));
        OnPropertyChanged(nameof(IsAchievementsGridRealized));
    }

    partial void OnAchievementsStatusChanged(string? value)
    {
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(HasInlineStatus));
    }

    // Android-side actions, wired once by the controller. The commands below are pure indirection so the
    // AXAML never needs an Android type.
    public Action? DrawerToggled { get; set; }
    public Action? AchievementsToggled { get; set; }
    public Action? OverlayClosed { get; set; }
    public Action? SlotCleared { get; set; }
    public Action? AchievementsRefreshed { get; set; }
    public Action<int>? SlotActivated { get; set; }
    public Action<int>? SlotEditRequested { get; set; }
    public Action<SecondScreenAppViewModel>? AppLaunched { get; set; }

    [RelayCommand]
    private void ToggleDrawer() => DrawerToggled?.Invoke();

    [RelayCommand]
    private void ToggleAchievements() => AchievementsToggled?.Invoke();

    [RelayCommand]
    private void CloseOverlay() => OverlayClosed?.Invoke();

    [RelayCommand]
    private void ClearSlot() => SlotCleared?.Invoke();

    [RelayCommand]
    private void Refresh() => AchievementsRefreshed?.Invoke();

    private DateTimeOffset _lastEditAt = DateTimeOffset.MinValue;

    [RelayCommand]
    private void ActivateSlot(SecondScreenSlotViewModel? slot)
    {
        // Take the slot (a reference type), not its int index: a RelayCommand<int> reports
        // CanExecute==false for a null/unset parameter, which disables the button and makes the tap do
        // nothing — the reason dock taps never launched. A reference-type parameter stays enabled.
        if (slot is null)
            return;
        // If a long-press just opened the picker, ignore a click that lands right after it (some input
        // stacks still fire one on release) so hold-to-manage doesn't also launch. The window is short so
        // an ordinary tap is never swallowed.
        if (DateTimeOffset.UtcNow - _lastEditAt < TimeSpan.FromMilliseconds(500))
            return;
        SlotActivated?.Invoke(slot.Index);
    }

    [RelayCommand]
    private void EditSlot(int index)
    {
        _lastEditAt = DateTimeOffset.UtcNow;
        SlotEditRequested?.Invoke(index);
    }

    [RelayCommand]
    private void LaunchApp(SecondScreenAppViewModel app) => AppLaunched?.Invoke(app);
}
