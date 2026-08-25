namespace EmuShelf.Core.Launching;

/// <summary>
/// Which physical display a game should launch on, on a device that has more than one (the Thor's
/// built-in panel plus an external/second screen). Stored per system in
/// <see cref="EmulatorConfiguration.LaunchScreen"/>. Meaningless on single-display devices and on
/// desktop, where the window manager decides where an emulator opens — the whole feature is gated
/// behind a live external display, so the stored value is simply ignored there.
/// </summary>
public enum GameLaunchScreen
{
    /// <summary>No saved preference: ask each time a second screen is present (the default).</summary>
    Ask = 0,

    /// <summary>Always launch on the built-in (main) screen.</summary>
    BuiltIn = 1,

    /// <summary>Always launch on the external (second) screen.</summary>
    External = 2,
}
