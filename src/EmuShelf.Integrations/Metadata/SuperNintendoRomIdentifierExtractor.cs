using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Supplies the SHA-1 of the headerless Super Nintendo ROM as its sole exact catalogue key. The
/// SNES header carries no reliable commercial game code, so no title-id evidence is produced.
/// </summary>
public sealed class SuperNintendoRomIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game) =>
        SuperNintendoRomReader.TryRead(game.Path) is not { } evidence
            ? []
            : [new GameIdentifier(
                GameIdentifierKind.Sha1,
                evidence.Sha1,
                "Super Nintendo ROM",
                IsPrimary: true)];
}
