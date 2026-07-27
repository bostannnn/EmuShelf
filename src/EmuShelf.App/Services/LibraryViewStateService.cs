using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Settings;

namespace EmuShelf.App.Services;

/// <summary>Reads the library's restored presentation and persists changes to it.</summary>
public interface ILibraryViewStateService
{
    LibraryViewSettings Current { get; }

    Task SaveAsync(LibraryViewSettings state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes on the calling thread. Used at shutdown, where there is no longer a reliable moment
    /// to resume a continuation on — see <c>MainViewModel.FlushPendingLibraryViewStateSave</c>.
    /// </summary>
    void Save(LibraryViewSettings state);
}

/// <summary>
/// Persists the library's presentation to the portable settings file, merging against the latest
/// snapshot on every write so switching to list view cannot revert a theme or consent change made
/// by one of the other independent settings services.
/// </summary>
public sealed class LibraryViewStateService : ILibraryViewStateService
{
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private AppSettings _settings;

    public LibraryViewSettings Current => _settings.LibraryView;

    public LibraryViewStateService(
        ISettingsService settingsService,
        AppSettings settings,
        IAppLogger? logger = null)
    {
        _settingsService = settingsService;
        _settings = settings;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public Task SaveAsync(
        LibraryViewSettings state,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Save(state), cancellationToken);

    public void Save(LibraryViewSettings state)
    {
        try
        {
            // Merge against the latest snapshot on every write. The window-layout service writes
            // its own section of the same file, and at shutdown both run — read-modify-write is
            // what keeps whichever goes second from dropping the other's change.
            var latest = _settingsService.Load();
            _settings = latest with { LibraryView = state };
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            // Losing the remembered view is a cosmetic disappointment on the next launch, not a
            // reason to interrupt the user mid-session with a failure they cannot act on.
            _logger.Warning($"Could not persist the library view state: {ex.Message}");
        }
    }
}

internal sealed class NullLibraryViewStateService : ILibraryViewStateService
{
    public LibraryViewSettings Current { get; } = new();

    public Task SaveAsync(LibraryViewSettings state, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void Save(LibraryViewSettings state)
    {
    }
}
