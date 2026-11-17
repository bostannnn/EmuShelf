using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Views.Handheld;

/// <summary>
/// The companion's Big Type layers (M47 B15): the Shelf, the Wall and the Reel. View wiring only: taps handed to the view model, and
/// <see cref="HandheldScreensMotion"/> attached to show and hide the layers.
/// </summary>
public partial class HandheldBigTypeScreens : UserControl
{
    public HandheldBigTypeScreens()
    {
        InitializeComponent();
        var motion = new HandheldScreensMotion(this);
        DataContextChanged += (_, _) => motion.Attach(DataContext as HandheldCompanionViewModel);
    }

    private HandheldCompanionViewModel? Companion => DataContext as HandheldCompanionViewModel;

    private void OnHintTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: HandheldHint hint })
            Companion?.TapHint(hint);
        e.Handled = true;
    }

    private void OnAllFilterTapped(object? sender, TappedEventArgs e) => Companion?.SelectFilter(AchievementDisplayFilter.All);

    private void OnLockedFilterTapped(object? sender, TappedEventArgs e) => Companion?.SelectFilter(AchievementDisplayFilter.Locked);

    private void OnUnlockedFilterTapped(object? sender, TappedEventArgs e) => Companion?.SelectFilter(AchievementDisplayFilter.Unlocked);

    private void OnPreviousFilterTapped(object? sender, TappedEventArgs e) => Companion?.TapHint(new HandheldHint("LB", string.Empty));

    private void OnNextFilterTapped(object? sender, TappedEventArgs e) => Companion?.TapHint(new HandheldHint("RB", string.Empty));

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
