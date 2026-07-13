using EmuShelf.Core.Systems;

namespace EmuShelf.Core.Importing;

/// <summary>How confidently an explicitly selected file belongs to a system.</summary>
public enum GameFileMatch
{
    Unsupported,
    Compatible,
    Incompatible,
    Unrecognized,
}

/// <summary>
/// Reusable result of inspecting one file. Callers can keep it after the user
/// confirms a system, avoiding a second read of the same disc header.
/// </summary>
public sealed record GameFileAnalysis(
    string Path,
    IReadOnlyList<GameSystem> SuggestedSystems,
    IReadOnlyDictionary<string, GameFileMatch> MatchesBySystem)
{
    public GameFileMatch MatchFor(string systemId) =>
        MatchesBySystem.TryGetValue(systemId, out var match)
            ? match
            : GameFileMatch.Unsupported;
}
