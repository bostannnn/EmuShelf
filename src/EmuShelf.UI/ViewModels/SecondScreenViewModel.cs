using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    public partial SecondScreenOverlayKind Overlay { get; set; }

    // --- App-owned keyboard, mirrored here from the main head during couch text entry ---

    /// <summary>
    /// The couch keyboard while it is being entered on the main screen — the Android controller pushes the
    /// main head's live <c>GamepadKeyboardViewModel</c> here so the Thor's second screen hosts the keys,
    /// leaving the search field and results fully visible on the main panel. Null the rest of the time.
    /// Rendered above every other surface (it is its own top-most layer), so whatever sits underneath —
    /// spotlight, dock, achievements — is revealed unchanged when it closes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeyboardOpen))]
    public partial GamepadKeyboardViewModel? Keyboard { get; set; }

    public bool IsKeyboardOpen => Keyboard is not null;

    // --- Resting spotlight (fan art + logo) ---

    [ObservableProperty]
    public partial Bitmap? FanartImage { get; set; }

    [ObservableProperty]
    public partial Bitmap? WheelImage { get; set; }

    [ObservableProperty]
    public partial double FanartOpacity { get; set; }

    [ObservableProperty]
    public partial double LogoOpacity { get; set; }

    [ObservableProperty]
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

    partial void OnOverlayChanged(SecondScreenOverlayKind value)
    {
        OnPropertyChanged(nameof(IsDrawerOpen));
        OnPropertyChanged(nameof(IsAchievementsOpen));
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
