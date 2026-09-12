using System.Globalization;
using EmuShelf.Core.Metadata;

namespace EmuShelf.Integrations.Metadata;

/// <summary>Publisher-provided Steam library capsules, keyed by the exported app id.</summary>
public sealed class SteamArtworkProvider : IGameArtworkProvider
{
    public string Id => "steam";

    public IReadOnlyList<ArtworkCandidate> GetCandidates(IReadOnlyList<GameIdentifier> identifiers, GameCatalogMatch? match)
    {
        var value = identifiers.FirstOrDefault(i => i.Kind == GameIdentifierKind.SteamAppId)?.Value;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            return [];
        return [new(Id, new Uri($"https://cdn.akamai.steamstatic.com/steam/apps/{id}/library_600x900.jpg"), ".jpg")];
    }
}
