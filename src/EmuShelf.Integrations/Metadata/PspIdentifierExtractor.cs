using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Re-reads PSP product-code evidence for legacy entries that predate import metadata. Current
/// imports already persist this same serial, so normal enrichment performs no second disc read.
/// </summary>
public sealed class PspIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game) =>
        PspGameMetadataReader.TryRead(game.Path)?.DiscId is { } discId
            ?
            [
                new GameIdentifier(
                    GameIdentifierKind.Serial,
                    PlayStationIdentifierExtractor.NormalizeProductCode(discId),
                    "PSP PARAM.SFO",
                    IsPrimary: true),
            ]
            : [];
}
