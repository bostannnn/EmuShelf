using Avalonia.Controls.ApplicationLifetimes;

namespace EmuShelf.App.Services;

/// <summary>Application-lifetime operations requested by view models without owning Avalonia's
/// desktop lifetime directly.</summary>
public interface IApplicationLifetimeService
{
    void Shutdown();
}

public sealed class ApplicationLifetimeService(
    IClassicDesktopStyleApplicationLifetime lifetime) : IApplicationLifetimeService
{
    public void Shutdown() => lifetime.Shutdown();
}

/// <summary>
/// Single-view (Android) equivalent. A mobile app has no desktop lifetime to shut down — the OS owns
/// the process — so a view-model "quit" runs an optional host-supplied action (e.g. finishing the
/// Activity) and otherwise no-ops rather than tearing the process down under Android's back.
/// </summary>
public sealed class SingleViewApplicationLifetimeService(
    Action? requestClose = null) : IApplicationLifetimeService
{
    public void Shutdown() => requestClose?.Invoke();
}
