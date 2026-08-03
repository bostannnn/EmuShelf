using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Supplies exact Nintendo 3DS header evidence — the NCCH product code (the GameTDB cover key,
/// primary) and the title id (secondary) — from the uncompressed NCSD/NCCH dumps. Compressed, CIA,
/// and homebrew files carry no plaintext header identity, so they produce no identifiers and match
/// covers by filename instead. 3DS dumps are multi-gigabyte, so no whole-file checksum is taken.
/// </summary>
public sealed class Nintendo3dsRomIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game) =>
        Nintendo3dsRomReader.TryRead(game.Path) is not { } evidence
            ? []
            : CreateIdentifiers(evidence);

    internal static IReadOnlyList<GameIdentifier> CreateIdentifiers(Nintendo3dsEvidence evidence)
    {
        var identifiers = new List<GameIdentifier>();
        if (evidence.ProductCode is not null)
        {
            identifiers.Add(new GameIdentifier(
                GameIdentifierKind.Serial,
                evidence.ProductCode,
                "Nintendo 3DS NCCH product code",
                IsPrimary: true));
        }
        if (evidence.TitleId is not null)
        {
            identifiers.Add(new GameIdentifier(
                GameIdentifierKind.TitleId,
                evidence.TitleId,
                "Nintendo 3DS title id"));
        }
        return identifiers;
    }
}
