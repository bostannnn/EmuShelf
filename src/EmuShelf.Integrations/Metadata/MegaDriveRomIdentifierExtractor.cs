using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>Supplies the exact SHA-1 of a supported Mega Drive ROM's normalized cartridge bytes.</summary>
public sealed class MegaDriveRomIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game) =>
        MegaDriveRomReader.TryRead(game.Path) is not { } evidence
            ? []
            :
            [new GameIdentifier(
                GameIdentifierKind.Sha1,
                evidence.Sha1,
                "Mega Drive normalized ROM",
                IsPrimary: true)];
}
