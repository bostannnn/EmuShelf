using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmuShelf.App.Diagnostics;

namespace EmuShelf.App.Android.Views;

/// <summary>
/// The Android head's single-view root. A thin host for the shared <c>GamepadShellView</c>; all couch
/// UI, GL, and view-model wiring live in that shared view. Couch input is handled at the Activity level
/// (see <c>MainActivity.DispatchKeyEvent</c>), because Android gamepad buttons arrive as Activity key
/// events that Avalonia's own <c>KeyDown</c> does not surface.
/// </summary>
public partial class MainView : UserControl
{
    public MainView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
#if DEBUG
        // Debug builds show the renderer overlays (FPS + ms/frame + dirty rects) from first frame, so the
        // fan-on-scroll cost is visible without hunting for the L3 toggle. Release starts clean; L3 still
        // cycles them there for the Debug vs Release comparison. See RenderOverlayDiagnostics.
        RenderOverlayDiagnostics.SetEnabled(TopLevel.GetTopLevel(this), true);
#endif
    }
}
