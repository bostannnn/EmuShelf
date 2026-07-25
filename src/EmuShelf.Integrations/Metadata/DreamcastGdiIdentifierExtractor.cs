using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>Supplies Redump-compatible SHA-1 evidence for the primary Dreamcast GDI data track.</summary>
public sealed class DreamcastGdiIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game) =>
        DreamcastGdiReader.TryRead(game.Path) is not { } evidence
            ? []
            :
            [new GameIdentifier(
                GameIdentifierKind.Sha1,
                evidence.DataTrackSha1,
                "Dreamcast data track",
                IsPrimary: true)];
}
