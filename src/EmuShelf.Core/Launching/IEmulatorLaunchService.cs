using EmuShelf.Core.Library;

namespace EmuShelf.Core.Launching;

public interface IEmulatorLaunchService
{
    Task<GameLaunchResult> LaunchAsync(Game game, CancellationToken cancellationToken = default);
}
