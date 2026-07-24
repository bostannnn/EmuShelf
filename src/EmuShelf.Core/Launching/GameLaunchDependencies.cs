using EmuShelf.Core.Library;

namespace EmuShelf.Core.Launching;

/// <summary>All source paths an emulator must be able to read for one launch.</summary>
public sealed record GameLaunchDependencies(
    bool IsComplete,
    IReadOnlyList<string> Paths,
    string? FailureMessage = null);

public interface IGameLaunchDependencyResolver
{
    GameLaunchDependencies Resolve(Game game);
}
