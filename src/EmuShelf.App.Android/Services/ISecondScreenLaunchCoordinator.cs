using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// The launch path's view of the second screen: where it is, and a hook fired when a game has started so
/// the companion can react. Implemented by <see cref="SecondScreenController"/>; the launch service holds
/// only this contract so the two stay decoupled and the service needs no Android display types.
/// </summary>
internal interface ISecondScreenLaunchCoordinator
{
    /// <summary>The display id of the attached second screen, or null when none is present right now.</summary>
    int? ExternalDisplayId { get; }

    /// <summary>
    /// A game has started. <paramref name="screen"/> is the display it was actually launched on: on
    /// <see cref="GameLaunchScreen.External"/> the companion swaps onto the built-in panel and hides the
    /// Screen-2 Presentation so the game is visible; on <see cref="GameLaunchScreen.BuiltIn"/> the
    /// companion stays on Screen-2 with its spotlight, as before.
    /// </summary>
    void GameStarted(Game game, string title, GameLaunchScreen screen);
}
