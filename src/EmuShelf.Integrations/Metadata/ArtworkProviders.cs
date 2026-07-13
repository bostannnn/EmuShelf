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

public sealed class LibretroArtworkProvider : IGameArtworkProvider
{
    private readonly string _playlistName;

    public string Id => "libretro-thumbnails";

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

        var filename = SanitizeFilename(match.CanonicalTitle) + ".png";
        var uri = $"https://thumbnails.libretro.com/{EscapePathSegment(_playlistName)}/" +
            $"Named_Boxarts/{EscapePathSegment(filename)}";
        return [new ArtworkCandidate(Id, new Uri(uri), ".png")];
    }

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
