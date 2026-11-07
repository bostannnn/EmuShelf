using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views.Handheld;

namespace EmuShelf.App.Views;

/// <summary>
/// Handheld mode's bottom screen. A pure view onto <c>HandheldShellViewModel</c>; its motion (the travelling
/// selector, tracks, folder zoom, launch lift) is <see cref="HandheldLauncherMotion"/>. Where the platform has a
/// native Launcher (<see cref="HandheldNativeStage.Launcher"/>), <see cref="HandheldNativeLauncherFeed"/> pictures
/// this view's pieces for it.
/// </summary>
public partial class HandheldLauncherView : UserControl, IHandheldSelectorHost
{
    private readonly HandheldLauncherMotion _motion;
    private readonly HandheldNativeLauncherFeed? _feed;

    public HandheldLauncherView()
    {
        InitializeComponent();
        // View wiring only: the motion follows whichever shell the view shows.
        _motion = new HandheldLauncherMotion(this);
        // On a platform with a native Launcher this view stays laid out but invisible, and lends it pictures.
        _feed = HandheldNativeStage.Launcher is { } host ? new HandheldNativeLauncherFeed(this, _motion, host) : null;
        DataContextChanged += (_, _) =>
        {
            _motion.Attach(DataContext as HandheldShellViewModel);
            _feed?.Attach(DataContext as HandheldShellViewModel);
        };
    }

    void IHandheldSelectorHost.OnAnchorChanged(Control anchor, bool isFocused)
    {
        _motion.OnAnchorChanged(anchor, isFocused);
        _feed?.OnFocusChanged();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
