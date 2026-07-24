using Avalonia.Controls;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

public sealed class WindowFrontendController : IFrontendController
{
    private readonly Window _window;
    private readonly IInterfaceModeService? _interfaceMode;
    private WindowState _stateBeforeMinimize = WindowState.Normal;

    public WindowFrontendController(Window window, IInterfaceModeService? interfaceMode = null)
    {
        _window = window;
        _interfaceMode = interfaceMode;
    }

    public void SuspendForGame() => Minimize();

    public void ResumeAfterGame()
    {
        _window.WindowState = _interfaceMode?.Current == InterfaceMode.Gamepad
            ? WindowState.FullScreen
            : _stateBeforeMinimize;
        _window.Activate();
    }

    public void Minimize()
    {
        _stateBeforeMinimize = _window.WindowState;
        _window.WindowState = WindowState.Minimized;
    }

    public void Restore()
    {
        _window.WindowState = _stateBeforeMinimize;
        _window.Activate();
    }
}
