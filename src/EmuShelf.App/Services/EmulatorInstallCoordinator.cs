using EmuShelf.App.ViewModels;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Emulators;
using EmuShelf.Core.Launching;

namespace EmuShelf.App.Services;

/// <summary>
/// Builds and refreshes the per-emulator rows for the Install Emulators settings section, over
/// <see cref="IEmulatorInstallService"/>. One row per emulator that declares a release source; emulators
/// without one (none today) are omitted.
/// </summary>
public sealed class EmulatorInstallCoordinator
{
    private readonly IAppLogger _logger;

    public IReadOnlyList<EmulatorInstallRowViewModel> Rows { get; }

    public EmulatorInstallCoordinator(
        IEmulatorInstallService service,
        IReadOnlyList<EmulatorDefinition> definitions,
        IAppLogger logger,
        Func<string, string, Task>? onInstalled = null,
        Func<string, Task>? openDownloadPage = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        Rows = definitions
            .Where(definition => definition.ReleaseSource is not null)
            .Select(definition => new EmulatorInstallRowViewModel(
                definition.Id, definition.Name, service, logger, onInstalled, openDownloadPage))
            .ToList();
    }

    /// <summary>Refreshes every row's install status. Rows are refreshed one at a time to stay gentle on
    /// the GitHub API (each row is a separate release query).</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        foreach (var row in Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await row.RefreshAsync(cancellationToken);
        }
    }

    public EmulatorManagerSettingsContext CreateSettingsContext() => new(Rows, RefreshAsync);
}
