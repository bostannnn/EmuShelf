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
            Console.WriteLine(AppBuildInfo.Summary);
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
            // macOS only. Avalonia 12 defaults this list to [Metal, OpenGl, Software], and under
            // Metal the platform graphics is an IMetalDevice — so OpenGlControlBase asks for a GL
            // context, does not get one, and returns without initializing *or* throwing. The couch
            // shelf's 3D scene therefore never started a frame on macOS; only its watchdog noticed,
            // and the flat-cover fallback made that look like a working shelf. Avalonia ships no
            // Metal equivalent of OpenGlControlBase (Avalonia.Metal exports interop interfaces
            // only), so preferring OpenGl is what makes the scene possible here at all. Metal and
            // Software stay behind it, so a Mac whose GL fails degrades rather than breaking, and
            // no other platform is affected: Windows gets ANGLE, Linux is untouched.
            // See DECISIONS 2026-08-14.
            .With(new Avalonia.AvaloniaNativePlatformOptions
            {
                RenderingMode =
                [
                    Avalonia.AvaloniaNativeRenderingMode.OpenGl,
                    Avalonia.AvaloniaNativeRenderingMode.Metal,
                    Avalonia.AvaloniaNativeRenderingMode.Software,
                ],
            })
            .WithInterFont()
            .LogToTrace();
}
