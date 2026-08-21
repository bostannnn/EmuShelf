using Avalonia.Controls;

namespace EmuShelf.App.Views;

/// <summary>
/// The first-run "choose a data folder" screen. A deliberately self-contained <see cref="UserControl"/>:
/// it is shown before the composition root exists (so no shared services, theme, or gamepad routing are
/// wired yet), driven only by its <c>OnboardingViewModel</c> DataContext. On Android it is the initial
/// single-view content and is replaced by the real shell once a folder is chosen.
/// </summary>
public partial class OnboardingView : UserControl
{
    public OnboardingView()
    {
        InitializeComponent();
    }
}
