namespace EmuShelf.Core.Settings;

/// <summary>
/// Which couch mode an Android start comes up in (Android has no Desktop mode). A saved Handheld choice needs the second
/// screen to be there; without it the start falls back to Gamepad. A fresh install on a dual-screen handheld starts in
/// Handheld mode (M45 H6d), so its first run, setup wizard included, happens there: "fresh" is a mode never chosen on this
/// device (Android never saves Desktop) and a setup wizard never finished, so an existing install that has used Gamepad
/// mode is never moved.
/// </summary>
public static class AndroidStartMode
{
    public static InterfaceMode Resolve(AppSettings settings, bool hasPresentationDisplay)
    {
        if (!hasPresentationDisplay)
            return InterfaceMode.Gamepad;
        return settings.InterfaceMode == InterfaceMode.Handheld || IsFreshInstall(settings)
            ? InterfaceMode.Handheld
            : InterfaceMode.Gamepad;
    }

    /// <summary>Nothing has been chosen on this device yet: no mode switch, no setup wizard finished.</summary>
    public static bool IsFreshInstall(AppSettings settings) =>
        settings.InterfaceMode == InterfaceMode.Desktop && settings.SetupCompletedVersion == 0;
}
