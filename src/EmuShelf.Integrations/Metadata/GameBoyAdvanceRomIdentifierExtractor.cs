using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>Supplies exact raw GBA cartridge evidence; header game codes are not catalogue keys.</summary>
public sealed class GameBoyAdvanceRomIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game) =>
        GameBoyAdvanceRomReader.TryRead(game.Path) is not { } evidence
            ? []
            : CreateIdentifiers(evidence);

    internal static IReadOnlyList<GameIdentifier> CreateIdentifiers(GameBoyAdvanceRomEvidence evidence)
    {
        var identifiers = new List<GameIdentifier>();
        if (evidence.GameCode is not null)
        {
            identifiers.Add(new GameIdentifier(
                GameIdentifierKind.TitleId,
                evidence.GameCode,
                "Game Boy Advance header"));
        }
        identifiers.Add(new GameIdentifier(
            GameIdentifierKind.Sha1,
            evidence.Sha1,
            "Game Boy Advance ROM",
            IsPrimary: true));
        return identifiers;
    }
}
