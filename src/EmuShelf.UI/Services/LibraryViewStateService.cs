using System.Linq;
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
        // Skip the read-merge-rewrite of settings.json when nothing this service owns has changed since
        // our last save. LibraryView is written only here, so our cached snapshot is authoritative for
        // it and skipping cannot drop another service's section. This turns browsing that lands back on
        // the same view (or a change event that carries no real change) into zero disk writes.
        if (IsUnchanged(state, _settings.LibraryView))
            return;

        try
        {
            _settings = _settingsService.Update(latest => latest with { LibraryView = state });
        }
        catch (Exception ex)
        {
            // Losing the remembered view is a cosmetic disappointment on the next launch, not a
            // reason to interrupt the user mid-session with a failure they cannot act on.
            _logger.Warning($"Could not persist the library view state: {ex.Message}");
        }
    }

    // A single shared empty list so the record comparison below sees the same reference on both sides.
    private static readonly IReadOnlyList<LibraryColumnSetting> SharedEmptyColumns = [];

    // LibraryViewSettings is a record, but ListColumns is an IReadOnlyList whose default equality is by
    // reference — and each BuildLibraryViewState() hands over a fresh list — so a plain `==` would treat
    // two otherwise-identical states as different. Compare the scalar fields via record equality (which
    // covers any field added later for free) by first normalising both lists to one shared reference,
    // then compare the columns structurally.
    private static bool IsUnchanged(LibraryViewSettings a, LibraryViewSettings b) =>
        a with { ListColumns = SharedEmptyColumns } == b with { ListColumns = SharedEmptyColumns }
        && a.ListColumns.SequenceEqual(b.ListColumns);
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
