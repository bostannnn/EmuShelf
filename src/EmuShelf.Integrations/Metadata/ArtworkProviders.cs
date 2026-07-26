using System.Text;
using EmuShelf.Core.Metadata;

namespace EmuShelf.Integrations.Metadata;

public sealed class XlenoreArtworkProvider : IGameArtworkProvider
{
    private readonly string _baseUri;

    public string Id { get; }

    public XlenoreArtworkProvider(string id, string baseUri)
    {
        Id = id;
        _baseUri = baseUri.TrimEnd('/');
    }

    public IReadOnlyList<ArtworkCandidate> GetCandidates(
        IReadOnlyList<GameIdentifier> identifiers,
        GameCatalogMatch? match) => identifiers
        .Where(identifier => identifier.Kind == GameIdentifierKind.Serial)
        .Select(identifier => PlayStationIdentifierExtractor.NormalizeProductCode(identifier.Value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(serial => new ArtworkCandidate(
            Id,
            new Uri($"{_baseUri}/{Uri.EscapeDataString(serial)}.jpg"),
            ".jpg"))
        .ToArray();
}

/// <summary>
/// GameCube/Wii covers addressed by the six-character disc id, the same source Dolphin
/// uses. The disc id's fourth character selects a region/language folder; English and US
/// folders are tried as fallbacks because a cover may only exist under one of them.
/// </summary>
public sealed class GameTdbArtworkProvider : IGameArtworkProvider
{
    private const string BaseUri = "https://art.gametdb.com/wii/cover";

    public string Id => "gametdb";

    public IReadOnlyList<ArtworkCandidate> GetCandidates(
        IReadOnlyList<GameIdentifier> identifiers,
        GameCatalogMatch? match) => identifiers
        .Where(identifier => identifier.Kind == GameIdentifierKind.DiscId &&
                             identifier.Value.Length >= 4)
        .Select(identifier => identifier.Value.ToUpperInvariant())
        .Distinct(StringComparer.Ordinal)
        .SelectMany(id => RegionFolders(id[3])
            .Select(folder => new ArtworkCandidate(
                Id,
                new Uri($"{BaseUri}/{folder}/{Uri.EscapeDataString(id)}.png"),
                ".png")))
        .ToArray();

    private static IEnumerable<string> RegionFolders(char regionCode)
    {
        var primary = regionCode switch
        {
            'E' => "US",
            'J' => "JA",
            'W' => "ZH", // Taiwanese releases share the Japanese region but a 'W' id
            'K' => "KO",
            'D' => "DE",
            'F' => "FR",
            'S' => "ES",
            'I' => "IT",
            'H' => "NL",
            _ => "EN", // P/X/Y/Z/U and unknowns: generic PAL English
        };
        // Preserve order, drop duplicates so a US or EN game is not requested twice.
        return new[] { primary, "EN", "US" }.Distinct(StringComparer.Ordinal);
    }
}

public sealed class GameTdbPlayStation3ArtworkProvider : IGameArtworkProvider
{
    private const string BaseUri = "https://art.gametdb.com/ps3";

    // `coverHQ` is a partial set: many releases only ever received the standard-resolution
    // `cover`. Preferring the high-resolution set and falling back to the standard one keeps the
    // better image where it exists without losing the games it never covered.
    private static readonly string[] CoverSets = ["coverHQ", "cover"];

    public string Id => "gametdb-ps3";

    public IReadOnlyList<ArtworkCandidate> GetCandidates(
        IReadOnlyList<GameIdentifier> identifiers,
        GameCatalogMatch? match) => identifiers
        .Where(identifier => identifier.Kind == GameIdentifierKind.Serial)
        .Select(identifier => identifier.Value.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant())
        .Where(serial => serial.Length == 9)
        .Distinct(StringComparer.Ordinal)
        .SelectMany(serial => CoverSets
            .SelectMany(set => RegionFolders(serial)
                .Select(region => new ArtworkCandidate(
                    Id,
                    new Uri($"{BaseUri}/{set}/{region}/{Uri.EscapeDataString(serial)}.jpg"),
                    ".jpg"))))
        .ToArray();

    private static IEnumerable<string> RegionFolders(string serial)
    {
        var primary = serial[..4] switch
        {
            "BLUS" or "BCUS" or "NPUB" => "US",
            "BLES" or "BCES" or "NPEB" => "EN",
            "BCJS" or "BLJM" or "BLJS" or "NPJB" => "JA",
            "BCAS" or "BLAS" => "AS",
            _ => "EN",
        };
        return new[] { primary, "EN", "US", "JA" }
            .Distinct(StringComparer.Ordinal);
    }
}

public sealed class LibretroArtworkProvider : IArtworkTitleIndexProvider
{
    private const string PspPlaylist = "Sony - PlayStation Portable";
    private static readonly IReadOnlyDictionary<string, string> PspArtworkTitleAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Lumines - Puzzle Fusion"] = "Lumines",
            ["Metal Gear Acid"] = "Metal Gear Ac!d",
            ["Metal Gear Acid 2"] = "Metal Gear Ac!d 2",
            ["Persona 2 - Batsu - Eternal Punishment"] = "Persona 2 - Batsu",
        };

    private readonly string _playlistName;

    public string Id => "libretro-thumbnails";

    public string ArtworkIndexKey => _playlistName;

    public LibretroArtworkProvider(string playlistName)
    {
        _playlistName = playlistName;
    }

    public IReadOnlyList<ArtworkCandidate> GetCandidates(
        IReadOnlyList<GameIdentifier> identifiers,
        GameCatalogMatch? match)
    {
        if (match is null)
            return [];

        return [CreateCandidate(match.CanonicalTitle)];
    }

    public ArtworkCandidate CreateCandidate(string title)
    {
        var filename = SanitizeFilename(title) + ".png";
        var uri = $"https://thumbnails.libretro.com/{EscapePathSegment(_playlistName)}/" +
            $"Named_Boxarts/{EscapePathSegment(filename)}";
        return new ArtworkCandidate(Id, new Uri(uri), ".png");
    }

    public IReadOnlyList<string> GetIndexedTitleQueries(GameCatalogMatch match)
    {
        // The aliases are keyed by product title, so the catalogue's region and language tags are
        // dropped before the lookup — "Persona 2 - Batsu - Eternal Punishment (Japan)" is the same
        // release as the alias entry for "Persona 2 - Batsu - Eternal Punishment".
        if (!string.Equals(match.CatalogId, "libretro-database", StringComparison.Ordinal) ||
            !string.Equals(_playlistName, PspPlaylist, StringComparison.Ordinal) ||
            !PspArtworkTitleAliases.TryGetValue(ProductTitle(match.CanonicalTitle), out var alias))
        {
            return [match.CanonicalTitle];
        }

        return [match.CanonicalTitle, alias];
    }

    private static string ProductTitle(string title) => title.Split(['(', '['], 2)[0].TrimEnd();

    internal static string SanitizeFilename(string title)
    {
        const string invalid = "&*/:`<>?\\|\"";
        var result = new StringBuilder(title.Length);
        foreach (var character in title)
            result.Append(invalid.Contains(character) ? '_' : character);
        return result.ToString();
    }

    private static string EscapePathSegment(string value) => Uri.EscapeDataString(value);
}
