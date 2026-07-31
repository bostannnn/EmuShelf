using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Supplies catalogue evidence for a Dreamcast disc. A GDI set offers Redump-compatible SHA-1
/// evidence for its data tracks: Redump hashes each track file separately and libretro's condensed
/// catalogue keeps only the largest one per game, so every data track is offered with the largest
/// first and the lookup stops at the first hit. A CHD offers only the IP.BIN product number, for
/// the reason given on <c>ExtractFromChd</c>.
/// </summary>
public sealed partial class DreamcastIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game) =>
        Path.GetExtension(game.Path).Equals(
            DreamcastDisc.ChdExtension,
            StringComparison.OrdinalIgnoreCase)
            ? ExtractFromChd(game)
            : ExtractFromGdi(game);

    private static IReadOnlyList<GameIdentifier> ExtractFromGdi(Game game) =>
        DreamcastGdiReader.TryRead(game.Path) is not { } evidence
            ? []
            :
            [
                .. evidence.DataTracks.Select((track, index) => new GameIdentifier(
                    GameIdentifierKind.Sha1,
                    track.Sha1,
                    $"Dreamcast track {track.TrackNumber:00}",
                    IsPrimary: index == 0)),
                // An explicitly labelled translation or patch can retain the retail product
                // number while changing the executable. Do not relabel that modified release as
                // its source game through the deliberately lower-confidence serial fallback.
                .. (IsExplicitlyLabeledModifiedRelease(game.Path) ? [] : evidence.ProductNumberAliases)
                    .Select(productNumber => new GameIdentifier(
                    GameIdentifierKind.Serial,
                    productNumber,
                    "Dreamcast IP.BIN product number")),
            ];

    // A CHD is keyed on its IP.BIN product number alone. Redump's SHA-1 covers a track file's raw
    // 2352-byte frames, and the CD codecs strip the sync and ECC bytes that this reader
    // deliberately does not regenerate, so a hash taken from a CHD would not be the catalogue's
    // hash for the same disc. Offering only the serial is honest about that; a container-specific
    // hash would silently never match. Reading it costs a few hunks, not a whole track.
    private static IReadOnlyList<GameIdentifier> ExtractFromChd(Game game) =>
        IsExplicitlyLabeledModifiedRelease(game.Path)
            ? []
            : DreamcastChdReader.ReadProductNumberAliases(game.Path)
                .Select(productNumber => new GameIdentifier(
                    GameIdentifierKind.Serial,
                    productNumber,
                    "Dreamcast IP.BIN product number"))
                .ToArray();

    private static bool IsExplicitlyLabeledModifiedRelease(string path)
    {
        // Only the set's own directory/name is an explicit label. Inspecting arbitrary parents
        // (for example a library root named "Patches") made ordinary retail sets unmatched.
        var labels = new[]
        {
            Path.GetFileNameWithoutExtension(path),
            Path.GetFileName(Path.GetDirectoryName(path)),
        };
        return labels.Any(label => label is not null && ModifiedReleaseLabel().IsMatch(label));
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"(?:^|[^\p{L}\p{N}])(?:translation|translated|patch|hack|English\s+v?\d+(?:\.\d+)*)(?:$|[^\p{L}\p{N}])",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex ModifiedReleaseLabel();
}
