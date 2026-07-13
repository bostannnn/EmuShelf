namespace EmuShelf.Core.Library;

/// <summary>
/// Decides whether a game's backing file (or, for directory-based systems, folder)
/// is currently present. Missing games stay in the library, marked unavailable.
/// </summary>
public interface IAvailabilityChecker
{
    bool IsAvailable(Game game);
}
