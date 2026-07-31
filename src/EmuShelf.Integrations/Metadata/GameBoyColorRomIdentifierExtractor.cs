using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Supplies the exact SHA-1 of a validated raw Game Boy Color cartridge as its sole catalogue key.
/// The header carries no reliable commercial game code, so no title-id evidence is produced.
/// </summary>
public sealed class GameBoyColorRomIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game) =>
        GameBoyColorRomReader.TryRead(game.Path) is not { } evidence
            ? []
            : [new GameIdentifier(
                GameIdentifierKind.Sha1,
                evidence.Sha1,
                "Game Boy Color ROM",
                IsPrimary: true)];
}
