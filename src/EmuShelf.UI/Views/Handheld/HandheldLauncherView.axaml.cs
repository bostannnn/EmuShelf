using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views.Handheld;

namespace EmuShelf.App.Views;

/// <summary>
/// Handheld mode's bottom screen. A pure view onto <c>HandheldShellViewModel</c>; its motion (the travelling
/// selector, tracks, folder spill and gather, launch lift) is <see cref="HandheldLauncherMotion"/>.
/// </summary>
public partial class HandheldLauncherView : UserControl, IHandheldSelectorHost
{
    private readonly HandheldLauncherMotion _motion;

    public HandheldLauncherView()
    {
        InitializeComponent();
        // View wiring only: the motion follows whichever shell the view shows.
        _motion = new HandheldLauncherMotion(this);
        DataContextChanged += (_, _) => _motion.Attach(DataContext as HandheldShellViewModel);
    }

    void IHandheldSelectorHost.OnAnchorChanged(Control anchor, bool isFocused) => _motion.OnAnchorChanged(anchor, isFocused);

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
