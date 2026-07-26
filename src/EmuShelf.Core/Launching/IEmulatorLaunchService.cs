using EmuShelf.Core.Library;

namespace EmuShelf.Core.Launching;

public interface IEmulatorLaunchService
{
    Task<GameLaunchResult> LaunchAsync(
        Game game,
        Func<CancellationToken, Task>? beforeStart = null,
        CancellationToken cancellationToken = default);
}
