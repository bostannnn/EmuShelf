namespace EmuShelf.Core.Launching;

/// <summary>What the launch path should do about screen selection for one launch.</summary>
public enum LaunchScreenDecision
{
    /// <summary>Launch on the built-in (main) screen.</summary>
    BuiltIn,

    /// <summary>Launch on the external (second) screen.</summary>
    External,

    /// <summary>Ask the user which screen to use before launching.</summary>
    Prompt,
}

/// <summary>
/// Turns a system's stored <see cref="GameLaunchScreen"/> preference plus the current hardware state
/// (is a second screen actually attached?) into a single <see cref="LaunchScreenDecision"/>. Pure and
/// side-effect free so the whole policy is unit-tested on the desktop suite; the Android head only has
/// to obey the decision.
/// </summary>
public static class LaunchScreenResolver
{
    /// <summary>
    /// Resolves what to do for one launch. With no external display attached the answer is always
    /// <see cref="LaunchScreenDecision.BuiltIn"/> — even for a system pinned to <see
    /// cref="GameLaunchScreen.External"/> — so unplugging the second screen degrades gracefully to the
    /// built-in panel instead of failing. When a second screen is present, an unset (<see
    /// cref="GameLaunchScreen.Ask"/>) preference prompts, and a pinned preference is obeyed.
    /// </summary>
    public static LaunchScreenDecision Resolve(GameLaunchScreen preference, bool externalDisplayAvailable)
    {
        if (!externalDisplayAvailable)
            return LaunchScreenDecision.BuiltIn;

        return preference switch
        {
            GameLaunchScreen.BuiltIn => LaunchScreenDecision.BuiltIn,
            GameLaunchScreen.External => LaunchScreenDecision.External,
            _ => LaunchScreenDecision.Prompt,
        };
    }
}
