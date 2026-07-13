using EmuShelf.Core.Importing;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Minimal extension-to-system recognition for M3's add/scan flow.
///
/// This is intentionally the simple version: a plain per-system extension set, so a
/// bare <c>.iso</c> suggests every system that uses ISOs and the user confirms. M4
/// replaces it with the authoritative format rules — .cue/.bin de-duplication, .m3u
/// playlist handling, and GameCube/Wii disc-header disambiguation — behind
/// <see cref="IGameImportRules"/>. PS3 is directory-based (M5) and has no file
/// extensions here, so file scanning never mis-attributes discs to it.
/// </summary>
public sealed class ExtensionImportRules : IGameImportRules
{
    // System id -> recognised file extensions (lower-case, with leading dot).
    private static readonly IReadOnlyDictionary<string, string[]> ExtensionsBySystem =
        new Dictionary<string, string[]>
        {
            ["playstation"] = [".cue", ".chd", ".m3u", ".pbp", ".iso"],
            ["playstation2"] = [".iso", ".chd", ".cso", ".m3u"],
            ["gamecube"] = [".iso", ".rvz", ".gcm", ".ciso"],
            ["wii"] = [".iso", ".rvz", ".wbfs", ".ciso"],
        };

    private readonly IReadOnlyList<GameSystem> _systems;

    public ExtensionImportRules() : this(KnownSystems.All)
    {
    }

    public ExtensionImportRules(IReadOnlyList<GameSystem> systems)
    {
        _systems = systems;
    }

    public IReadOnlyList<GameSystem> SuggestSystems(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension.Length == 0)
            return [];

        return _systems
            .Where(system =>
                ExtensionsBySystem.TryGetValue(system.Id, out var extensions) &&
                Array.IndexOf(extensions, extension) >= 0)
            .ToList();
    }

    public bool IsCandidate(string path, GameSystem system) =>
        ExtensionsBySystem.TryGetValue(system.Id, out var extensions) &&
        Array.IndexOf(extensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;
}
