using EmuShelf.Core.Launching;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Desktop minimizes EmuShelf while a game runs and restores it after. On Android the OS owns
/// foreground/background transitions when an emulator Activity is launched, so "minimize" maps to an
/// optional host action (moving the task to the back) and "restore" is a no-op — the system brings
/// EmuShelf forward on return. The real return signal is <c>onTopResumedActivityChanged</c> and must
/// survive process death; that is Milestone B, not the walking skeleton.
/// </summary>
public sealed class AndroidFrontendController(Action? moveToBack = null) : IFrontendController
{
    public void Minimize() => moveToBack?.Invoke();

    public void Restore()
    {
        // No-op: Android returns EmuShelf to the foreground on its own when the emulator task ends.
    }
}
