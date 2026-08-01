using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Integrations.Metadata;

public static class KnownScreenScraperProfiles
{
    public static IReadOnlyList<ScreenScraperSystemProfile> All { get; } = Build();

    private static IReadOnlyList<ScreenScraperSystemProfile> Build()
    {
        var profiles = new List<ScreenScraperSystemProfile>();
        foreach (var system in KnownSystems.All)
        {
            if (!ScreenScraperSystemMap.TryGetSystemId(system.Id, out var providerSystemId) ||
                !KnownScreenScraperFingerprintProfiles.TryGet(system.Id, out var fingerprintProfile))
            {
                throw new InvalidOperationException($"Incomplete ScreenScraper profile for {system.Id}.");
            }

            profiles.Add(new ScreenScraperSystemProfile(
                system.Id,
                providerSystemId,
                ScreenScraperSystemMap.Version,
                fingerprintProfile!));
        }
        return profiles;
    }
}
