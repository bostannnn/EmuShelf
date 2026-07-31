using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Supplies an arcade archive's romset short id — the zip basename, which is how FinalBurn Neo
/// loads a set — as its sole catalogue key. Purely path-based: the archive is never opened, so this
/// stays as cheap as reading a filename and does no disk I/O.
/// </summary>
public sealed class ArcadeSetIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game)
    {
        var setName = Path.GetFileNameWithoutExtension(game.Path);
        return string.IsNullOrWhiteSpace(setName)
            ? []
            : [new GameIdentifier(
                GameIdentifierKind.ArcadeSetName,
                setName,
                "FBNeo set name",
                IsPrimary: true)];
    }
}
