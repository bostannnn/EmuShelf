using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Services;

/// <summary>
/// The seam the Settings view model uses to show and refresh the Install Emulators section, without
/// depending on the install service directly. Produced by <see cref="EmulatorInstallCoordinator.CreateSettingsContext"/>.
/// </summary>
public sealed class EmulatorManagerSettingsContext
{
    private readonly Func<CancellationToken, Task> _refresh;

    public EmulatorManagerSettingsContext(
        IReadOnlyList<EmulatorInstallRowViewModel> rows,
        Func<CancellationToken, Task> refresh)
    {
        Rows = rows;
        _refresh = refresh;
    }

    public IReadOnlyList<EmulatorInstallRowViewModel> Rows { get; }

    public Task RefreshAsync(CancellationToken cancellationToken = default) => _refresh(cancellationToken);
}
