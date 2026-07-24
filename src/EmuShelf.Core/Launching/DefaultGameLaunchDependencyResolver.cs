using EmuShelf.Core.Library;

namespace EmuShelf.Core.Launching;

/// <summary>Fallback resolver for hosts that do not provide descriptor-aware integration support.</summary>
public sealed class DefaultGameLaunchDependencyResolver : IGameLaunchDependencyResolver
{
    public GameLaunchDependencies Resolve(Game game) =>
        new(true, [game.Path]);
}
