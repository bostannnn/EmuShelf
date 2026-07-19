using EmuShelf.Core.Library;

namespace EmuShelf.Core.Importing;

/// <summary>
/// Coordinates explicit, read-only source refreshes. Reading completes before SQLite changes
/// begin, so a cancelled or unsupported source cannot partially alter the library.
/// </summary>
public sealed class ExternalLibrarySyncService
{
    private readonly IGameLibrary _library;

    public ExternalLibrarySyncService(IGameLibrary library)
    {
        _library = library;
    }

    public async Task<ExternalLibraryImportResult> SyncAsync(
        IExternalLibrarySource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        var entries = await source.ReadGamesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await Task.Run(
            () => _library.ReconcileExternalLibrary(source.Source, entries),
            cancellationToken).ConfigureAwait(false);
    }
}
