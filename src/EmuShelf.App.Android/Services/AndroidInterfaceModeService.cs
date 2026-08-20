using EmuShelf.Core.Settings;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Android is Gamepad-only: there is no desktop window shell to switch to, so the mode is fixed to
/// <see cref="InterfaceMode.Gamepad"/> and <see cref="SetModeAsync"/> is a no-op. Reported as a
/// command-line override so the shared UI treats the choice as forced and hides the mode toggle,
/// the same way Steam Gaming Mode does on desktop. Closing the gamepad "Switch to Desktop" escape
/// hatch fully is a follow-up (see the plan's A1 escape-hatch checklist); pinning the mode here is
/// what makes that hatch inert in the meantime.
/// </summary>
public sealed class AndroidInterfaceModeService : IInterfaceModeService
{
    public InterfaceMode Current => InterfaceMode.Gamepad;

    public bool IsCommandLineOverride => true;

    // Android has no window shell at all, so Desktop mode is absent rather than merely locked.
    public bool SupportsDesktopMode => false;

    public event EventHandler<InterfaceMode>? ModeChanged;

    public Task SetModeAsync(InterfaceMode mode, CancellationToken cancellationToken = default)
    {
        // The mode never changes on Android; keep the event referenced so the compiler does not warn
        // and any future host-driven change has a place to fire from.
        _ = ModeChanged;
        return Task.CompletedTask;
    }
}
