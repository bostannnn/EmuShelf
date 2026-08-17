using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using EmuShelf.App.Controls;

namespace EmuShelf.App.Android.Views;

public partial class MainView : UserControl
{
    private TextBlock? _statusLine;

    public MainView()
    {
        InitializeComponent();

        _statusLine = this.FindControl<TextBlock>("StatusLine");
        var shelf = this.FindControl<MediaShelf3DControl>("ShelfProbe");
        if (shelf is not null)
        {
            // Assert GL initialised rather than trusting the eye: on Avalonia's Android backend an
            // unavailable EGL context makes OpenGlControlBase log-and-return-false without throwing and
            // fall back to Software, exactly the silent failure that hid the macOS/Metal bug. Surface
            // both outcomes on screen so the skeleton's GL answer is unambiguous in a screenshot.
            shelf.InitializationSucceeded += (_, _) => SetStatus("GL: OpenGL ES context OK ✓");
            shelf.InitializationFailed += (_, ex) => SetStatus("GL: FAILED — " + ex.Message);
        }
    }

    private void SetStatus(string text)
    {
        if (_statusLine is not null)
            _statusLine.Text = text;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
