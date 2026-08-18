using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Services;
using EmuShelf.Core.Settings;

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

    [AvaloniaFact]
    public void ResumeAfterGame_InGamepadMode_RestoresFullscreen()
    {
        var window = new Window { WindowState = WindowState.FullScreen };
        var controller = new WindowFrontendController(window, new FixedInterfaceModeService(InterfaceMode.Gamepad));

        controller.SuspendForGame();
        Assert.Equal(WindowState.Minimized, window.WindowState);

        controller.ResumeAfterGame();
        Assert.Equal(WindowState.FullScreen, window.WindowState);
    }

    private sealed class FixedInterfaceModeService(InterfaceMode current) : IInterfaceModeService
    {
        public InterfaceMode Current { get; } = current;
        public bool IsCommandLineOverride => false;
        public bool SupportsDesktopMode => true;
        public event EventHandler<InterfaceMode>? ModeChanged { add { } remove { } }
        public Task SetModeAsync(InterfaceMode mode, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
