using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

public sealed class NintendoDiscIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game)
    {
        // A Wii WAD (WiiWare/VC/channel) carries its identity in an embedded TMD, not a disc header,
        // so it takes a separate read from the shared GameCube/Wii disc detector.
        if (Path.GetExtension(game.Path).Equals(".wad", StringComparison.OrdinalIgnoreCase))
            return ExtractWad(game.Path);

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

    private static IReadOnlyList<GameIdentifier> ExtractWad(string path)
    {
        if (WiiWadReader.TryRead(path) is not { } evidence)
            return [];

        var identifiers = new List<GameIdentifier>();
        // The four-character game code is the GameTDB cover key, the same id-addressed route the disc
        // path uses, so it is the primary evidence. WiiWare/VC/channel titles are not in the Wii
        // Redump DAT, so this never resolves a catalogue title — the cover comes from the code and
        // the title from the filename, exactly as a homebrew/CIA 3DS file does. A system title/IOS
        // WAD carries no game code and yields only the title id (or nothing).
        if (evidence.GameCode is not null)
        {
            identifiers.Add(new GameIdentifier(
                GameIdentifierKind.DiscId,
                evidence.GameCode,
                "WadTmd",
                IsPrimary: true));
        }
        if (evidence.TitleId is not null)
            identifiers.Add(new GameIdentifier(GameIdentifierKind.TitleId, evidence.TitleId, "WadTitleId"));
        return identifiers;
    }
}
