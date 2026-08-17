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
