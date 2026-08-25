using EmuShelf.Core.Library;

namespace EmuShelf.Core.Launching;

public interface IEmulatorLaunchService
{
    /// <summary>Launches <paramref name="game"/>. <paramref name="displayName"/> is the name used in
    /// the user-facing launch status (the App passes the normalized scraped title so the completion and
    /// failure messages match the library); it defaults to the game's own <see cref="Game.Title"/>.
    /// <paramref name="targetScreen"/> selects the physical display on a multi-screen device: the caller
    /// resolves the system's preference (and any one-time prompt) up front and passes a concrete
    /// <see cref="GameLaunchScreen.BuiltIn"/> or <see cref="GameLaunchScreen.External"/>. Only the Android
    /// head acts on it; every desktop launcher ignores it (the window manager places the emulator).</summary>
    Task<GameLaunchResult> LaunchAsync(
        Game game,
        string? displayName = null,
        Func<CancellationToken, Task>? beforeStart = null,
        GameLaunchScreen targetScreen = GameLaunchScreen.BuiltIn,
        CancellationToken cancellationToken = default);
}
