using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Supplies Redump-compatible SHA-1 evidence for a Dreamcast GDI set's data tracks. Redump hashes
/// each track file separately and libretro's condensed catalogue keeps only the largest one per
/// game, so every data track is offered with the largest first: the catalogue lookup tries them in
/// that order and stops at the first hit.
/// </summary>
public sealed partial class DreamcastGdiIdentifierExtractor : IGameIdentifierExtractor
{
    public IReadOnlyList<GameIdentifier> Extract(Game game) =>
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
