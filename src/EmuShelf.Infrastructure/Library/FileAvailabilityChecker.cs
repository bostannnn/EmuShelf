using EmuShelf.Core.Library;

namespace EmuShelf.Infrastructure.Library;

/// <summary>
/// A game is available when its backing path exists. File-based systems point at a
/// file; directory-based systems (PS3, added in M5) point at a folder — both are
/// covered so availability doesn't regress when directory games arrive.
/// </summary>
public sealed class FileAvailabilityChecker : IAvailabilityChecker
{
    public bool IsAvailable(Game game) =>
        File.Exists(game.Path) || Directory.Exists(game.Path);
}
