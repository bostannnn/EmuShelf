using System;
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

/// <summary>One achievement row on the second-screen panel.</summary>
public sealed class SecondScreenAchievementViewModel(string title, string detail, bool earned)
{
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public bool Earned { get; } = earned;
    public double RowOpacity => Earned ? 1.0 : 0.55;
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

    [ObservableProperty]
    public partial bool CanRefresh { get; set; }

    public ObservableCollection<SecondScreenSlotViewModel> Dock { get; } =
        new(Enumerable.Range(0, 5).Select(index => new SecondScreenSlotViewModel(index)));

    public ObservableCollection<SecondScreenAppViewModel> Apps { get; } = [];

    public ObservableCollection<SecondScreenAchievementViewModel> Achievements { get; } = [];

    public bool IsDrawerOpen => Overlay == SecondScreenOverlayKind.Drawer;
    public bool IsAchievementsOpen => Overlay == SecondScreenOverlayKind.Achievements;
    public bool HasStatus => !string.IsNullOrEmpty(AchievementsStatus);

    partial void OnOverlayChanged(SecondScreenOverlayKind value)
    {
        OnPropertyChanged(nameof(IsDrawerOpen));
        OnPropertyChanged(nameof(IsAchievementsOpen));
    }

    partial void OnAchievementsStatusChanged(string? value) => OnPropertyChanged(nameof(HasStatus));

    // Android-side actions, wired once by the controller. The commands below are pure indirection so the
    // AXAML never needs an Android type.
    public Action? DrawerToggled { get; set; }
    public Action? AchievementsToggled { get; set; }
    public Action? OverlayClosed { get; set; }
    public Action? SlotCleared { get; set; }
    public Action? AchievementsRefreshed { get; set; }
    public Action<int>? SlotActivated { get; set; }
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

    [RelayCommand]
    private void ActivateSlot(int index) => SlotActivated?.Invoke(index);

    [RelayCommand]
    private void LaunchApp(SecondScreenAppViewModel app) => AppLaunched?.Invoke(app);
}
