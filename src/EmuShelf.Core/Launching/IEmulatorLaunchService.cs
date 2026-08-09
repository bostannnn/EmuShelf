using EmuShelf.Core.Library;

namespace EmuShelf.Core.Launching;

public interface IEmulatorLaunchService
{
    /// <summary>Launches <paramref name="game"/>. <paramref name="displayName"/> is the name used in
    /// the user-facing launch status (the App passes the normalized scraped title so the completion and
    /// failure messages match the library); it defaults to the game's own <see cref="Game.Title"/>.</summary>
    Task<GameLaunchResult> LaunchAsync(
        Game game,
        string? displayName = null,
        Func<CancellationToken, Task>? beforeStart = null,
        CancellationToken cancellationToken = default);
}
