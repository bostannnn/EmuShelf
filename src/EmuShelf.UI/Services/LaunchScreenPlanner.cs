using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;

namespace EmuShelf.App.Services;

/// <summary>
/// The part of a launch that decides which physical screen a game runs on, before anything starts: the
/// system's saved preference, the dual-screen rule and whether a second screen is attached, resolved by
/// <see cref="LaunchScreenResolver"/>; and pinning a pick as the system's preference ("Always use this
/// screen"). Gamepad mode's launch core and Handheld mode's Title page both call it, so the two modes can
/// never disagree about when to ask.
/// </summary>
public sealed class LaunchScreenPlanner(
    IEmulatorConfigurationStore configurations,
    Func<IExternalDisplayProbe?> displays,
    IAppLogger logger)
{
    /// <summary>Whether a second screen is attached right now, so the question can come up at all.</summary>
    public bool HasSecondScreen => displays()?.HasExternalDisplay == true;

    /// <summary>
    /// Resolves the screen for one launch of a game on <paramref name="systemId"/>. With no second screen,
    /// or for a dual-screen console, the answer is the built-in screen without reading the preference.
    /// The read hits SQLite, so it runs off the UI thread.
    /// </summary>
    public async Task<LaunchScreenDecision> ResolveAsync(string systemId, bool isDualScreenSystem)
    {
        if (!HasSecondScreen || isDualScreenSystem)
            return LaunchScreenDecision.BuiltIn;
        var preference = await Task.Run(() => configurations.Get(systemId)?.LaunchScreen ?? GameLaunchScreen.Ask);
        return LaunchScreenResolver.Resolve(preference, externalDisplayAvailable: true, isDualScreenSystem);
    }

    /// <summary>
    /// Pins the system's launch-screen preference. <c>SetLaunchScreen</c> touches only that column, never
    /// the active emulator. Best-effort: a write failure must never block the launch the user just asked for.
    /// </summary>
    public void Remember(string systemId, GameLaunchScreen screen)
    {
        try
        {
            configurations.SetLaunchScreen(systemId, screen);
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not save the launch-screen preference for {systemId}.", ex);
        }
    }
}
