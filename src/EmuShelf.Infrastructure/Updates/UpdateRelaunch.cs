namespace EmuShelf.Infrastructure.Updates;

/// <summary>
/// Picks what a platform update helper should relaunch after swapping the app's files.
///
/// The documented Gamepad-mode setup adds EmuShelf to Steam as a non-Steam game, and Steam Input
/// attaches only to the process Steam launches. Relaunching the app binary directly (a bare <c>.exe</c>,
/// or <c>open</c>-ing the <c>.app</c> bundle) therefore escapes Steam Input, leaving the controller dead
/// until the user quits and restarts from Steam. Steam exports <c>SteamGameId</c> in the environment of
/// every game it starts — Steam app or non-Steam shortcut — and its value is exactly the token
/// <c>steam://rungameid</c> expects, so feeding it back re-enters Steam's launch path: Steam Input
/// reattaches and the shortcut's launch options (e.g. <c>--gamepad-ui</c>) apply again. This is the
/// Windows/macOS counterpart to the AppImage applier's same-PID <c>execv</c>, which keeps the Steam
/// session alive on SteamOS without a second launch.
/// </summary>
internal static class UpdateRelaunch
{
    /// <summary>
    /// The <c>steam://rungameid</c> target when Steam launched this run, otherwise
    /// <paramref name="appTarget"/> — the executable or <c>.app</c> bundle to relaunch directly.
    /// </summary>
    public static string ResolveTarget(string appTarget) =>
        ResolveTarget(appTarget, Environment.GetEnvironmentVariable("SteamGameId"));

    // Overload taking the raw SteamGameId value so the selection is unit-testable without the process
    // environment. A missing, empty, or zero id means Steam did not launch us (a plain shortcut or a
    // dev run), so the app target is relaunched directly.
    public static string ResolveTarget(string appTarget, string? steamGameId) =>
        ulong.TryParse(steamGameId, out var gameId) && gameId != 0
            ? $"steam://rungameid/{gameId}"
            : appTarget;
}
