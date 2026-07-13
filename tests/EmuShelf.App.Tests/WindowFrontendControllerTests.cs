using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;

namespace EmuShelf.App.Tests;

public class WindowFrontendControllerTests
{
    [AvaloniaTheory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    [InlineData(WindowState.FullScreen)]
    public void Restore_ReturnsWindowToItsPreLaunchState(WindowState initialState)
    {
        var window = new Window { WindowState = initialState };
        var controller = new WindowFrontendController(window);

        controller.Minimize();
        Assert.Equal(WindowState.Minimized, window.WindowState);

        controller.Restore();
        Assert.Equal(initialState, window.WindowState);
    }
}
