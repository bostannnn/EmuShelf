namespace EmuShelf.Core.Settings;

/// <summary>One platform's texture-root override. The inventory itself lives in the portable cache.</summary>
/// <param name="DirectoryOverride">An explicit texture folder, used instead of the detected one.</param>
public sealed record TextureLocationSettings(string? DirectoryOverride = null);

/// <summary>
/// Texture-pack inventory configuration. This holds only what the user chose — overrides and
/// whether scanning is enabled. Scan results are cache, not settings, so they never land here.
/// </summary>
public sealed record TexturePackSettings
{
    private static readonly IReadOnlyDictionary<string, TextureLocationSettings> Empty =
        new Dictionary<string, TextureLocationSettings>(StringComparer.Ordinal);

    /// <summary>Whether EmuShelf inventories installed texture packs at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Per-system texture-root overrides, keyed by system id.</summary>
    public IReadOnlyDictionary<string, TextureLocationSettings> Locations { get; init; } =
        new Dictionary<string, TextureLocationSettings>(StringComparer.Ordinal);

    /// <summary>The explicit texture-root override for one system, or null when none is set.</summary>
    public string? GetOverride(string systemId) =>
        SafeLocations.TryGetValue(systemId, out var location) &&
        location is not null &&
        !string.IsNullOrWhiteSpace(location.DirectoryOverride)
            ? location.DirectoryOverride
            : null;

    /// <summary>Replaces one system's override.</summary>
    public TexturePackSettings WithOverride(string systemId, string? directory)
    {
        var trimmed = string.IsNullOrWhiteSpace(directory) ? null : directory.Trim();
        var locations = new Dictionary<string, TextureLocationSettings>(SafeLocations, StringComparer.Ordinal)
        {
            [systemId] = new TextureLocationSettings(trimmed),
        };
        return this with { Locations = locations };
    }

    // A hand-edited or older settings.json can deserialize this as null; treat that as "none set"
    // rather than letting every read throw.
    private IReadOnlyDictionary<string, TextureLocationSettings> SafeLocations => Locations ?? Empty;
}
