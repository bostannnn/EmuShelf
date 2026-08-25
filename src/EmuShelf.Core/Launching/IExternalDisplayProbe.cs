namespace EmuShelf.Core.Launching;

/// <summary>
/// Reports whether the device currently has a usable external (second) screen a game could launch on.
/// Implemented only by the Android head (the Thor's <c>FLAG_PRESENTATION</c> display); desktop heads
/// leave it null, which disables the whole launch-screen feature — the window manager, not EmuShelf,
/// decides where a desktop emulator opens. Read on the UI thread just before a launch.
/// </summary>
public interface IExternalDisplayProbe
{
    /// <summary>True when a second screen is attached and available to receive a launch right now.</summary>
    bool HasExternalDisplay { get; }
}
