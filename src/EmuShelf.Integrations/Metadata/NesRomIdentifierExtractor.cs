using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Supplies the SHA-1 of the whole headered NES ROM as its sole exact catalogue key — the form the
/// No-Intro NES set uses. The iNES header carries no reliable commercial game code, so no title-id
/// evidence is produced.
/// </summary>
public sealed class NesRomIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game) =>
        NesRomReader.TryRead(game.Path) is not { } evidence
            ? []
            : [new GameIdentifier(
                GameIdentifierKind.Sha1,
                evidence.Sha1,
                "NES ROM",
                IsPrimary: true)];
}
