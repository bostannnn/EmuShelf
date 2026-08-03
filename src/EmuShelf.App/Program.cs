using Avalonia;
using System;
using System.Linq;
using EmuShelf.App.Startup;
using EmuShelf.Core.Settings;

namespace EmuShelf.App;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--version", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("EmuShelf");
            return;
        }
        AppLaunchOptions.InterfaceModeOverride =
            args.Contains("--gamepad-ui", StringComparer.OrdinalIgnoreCase)
                ? InterfaceMode.Gamepad
                : args.Contains("--desktop-ui", StringComparer.OrdinalIgnoreCase)
                    ? InterfaceMode.Desktop
                    : null;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
