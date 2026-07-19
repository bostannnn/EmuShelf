using EmuShelf.Core.Library;

namespace EmuShelf.Core.Importing;

/// <summary>
/// Reads an emulator-owned library only when the caller explicitly asks to sync it. Implementors
/// must not scan arbitrary game folders or write to the external application.
/// </summary>
public interface IExternalLibrarySource
{
    ExternalLibrarySource Source { get; }

    Task<IReadOnlyList<ExternalLibraryGameEntry>> ReadGamesAsync(
        CancellationToken cancellationToken = default);
}
