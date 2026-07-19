using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Supplies the title id that RPCS3 exposes as a stable external-library entry id. It is
/// normalized as a PlayStation product code because the Redump catalog indexes PS3 titles by
/// that serial, not by a filename or the installed game's directory.
/// </summary>
public sealed class PlayStation3IdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game)
    {
        var titleId = game.ExternalSourceEntryId;
        if (!IsTitleId(titleId))
            return [];

        return
        [
            new GameIdentifier(
                GameIdentifierKind.Serial,
                PlayStationIdentifierExtractor.NormalizeProductCode(titleId!),
                "RPCS3 title id",
                IsPrimary: true),
        ];
    }

    private static bool IsTitleId(string? value) =>
        value is { Length: 9 } &&
        value[..4].All(character => character is >= 'A' and <= 'Z') &&
        value[4..].All(char.IsAsciiDigit);
}
