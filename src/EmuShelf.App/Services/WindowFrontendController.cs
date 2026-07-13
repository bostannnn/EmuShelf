using Avalonia.Controls;
using EmuShelf.Core.Launching;

namespace EmuShelf.App.Services;

public sealed class WindowFrontendController : IFrontendController
{
    private readonly Window _window;
    private WindowState _stateBeforeMinimize = WindowState.Normal;

    public WindowFrontendController(Window window)
    {
        _window = window;
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
