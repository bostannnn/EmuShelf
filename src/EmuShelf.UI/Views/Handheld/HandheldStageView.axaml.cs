using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmuShelf.App.Controls;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views.Handheld;

namespace EmuShelf.App.Views;

/// <summary>
/// Handheld mode's top screen. A pure view onto <c>HandheldShellViewModel</c>; its motion (centre pop, folder
/// flap, launch landing) is <see cref="HandheldStageMotion"/>. Where the platform has a native Stage
/// (<see cref="HandheldNativeStage"/>), <see cref="HandheldNativeStageFeed"/> pictures this view's pieces for it.
/// </summary>
public partial class HandheldStageView : UserControl
{
    public HandheldStageView()
    {
        InitializeComponent();
        // View wiring only: a GPU path that cannot start tells the Stage to keep flat covers.
        if (this.FindControl<Media3DControl>("Medium") is { } medium)
        {
            medium.InitializationFailed += (_, _) =>
            {
                if (DataContext is HandheldShellViewModel shell)
                    shell.Stage.IsMediumUnavailable = true;
            };
        }
        var motion = new HandheldStageMotion(this);
        // On a platform with a native Stage this view stays laid out but invisible, and lends it its pieces.
        var feed = HandheldNativeStage.Host is { } host ? new HandheldNativeStageFeed(this, motion, host) : null;
        DataContextChanged += (_, _) =>
        {
            motion.Attach(DataContext as HandheldShellViewModel);
            feed?.Attach(DataContext as HandheldShellViewModel);
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
