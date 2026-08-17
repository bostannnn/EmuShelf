namespace EmuShelf.App.Startup;

using EmuShelf.Core.Settings;

internal static class AppLaunchOptions
{
    /// <summary>
    /// A shortcut-specific, one-run interface choice. Steam Gaming Mode uses Gamepad while a
    /// desktop launcher can explicitly request Desktop without changing the user's stored default.
    /// </summary>
    public static InterfaceMode? InterfaceModeOverride { get; set; }
}
