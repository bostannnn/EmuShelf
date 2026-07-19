using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>Supplies exact raw Nintendo DS cartridge evidence; header game codes are not catalogue keys.</summary>
public sealed class NintendoDsRomIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game) =>
        NintendoDsRomReader.TryRead(game.Path) is not { } evidence
            ? []
            : CreateIdentifiers(evidence);

    internal static IReadOnlyList<GameIdentifier> CreateIdentifiers(NintendoDsRomEvidence evidence)
    {
        var identifiers = new List<GameIdentifier>();
        if (evidence.GameCode is not null)
        {
            identifiers.Add(new GameIdentifier(
                GameIdentifierKind.TitleId,
                evidence.GameCode,
                "Nintendo DS header"));
        }
        identifiers.Add(new GameIdentifier(
            GameIdentifierKind.Sha1,
            evidence.Sha1,
            "Nintendo DS ROM",
            IsPrimary: true));
        return identifiers;
    }
}
