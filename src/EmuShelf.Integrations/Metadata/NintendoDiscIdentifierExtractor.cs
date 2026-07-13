using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

public sealed class NintendoDiscIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game)
    {
        var info = NintendoDiscDetector.ReadInfo(game.Path);
        if (info is null || info.DiscId.Length == 0)
            return [];

        return
        [
            new GameIdentifier(
                GameIdentifierKind.DiscId,
                info.DiscId,
                "DiscHeader",
                IsPrimary: true),
        ];
    }
}
