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
            // Linux/X11 only (inert elsewhere, like the macOS block above). The Steam Deck's default
            // X11 path is GLX-first, which the device log confirms hands OpenGlControlBase a *desktop*
            // GL context (Mesa 4.6 Core, GLSL 4.60). That is the render path this app's shaders are
            // documented to get wrong — it renders darker than Windows, whose ANGLE backend gives a
            // GLES context instead (see MediaShellRenderer's EMUSHELF_SHADING_DEBUG probe). Prefer EGL
            // with a GLES profile so Linux takes the same GLES/Es300 path Windows already ships, and
            // keep GLX + desktop-GL profiles behind it so a host without EGL/GLES still renders.
            // See DECISIONS 2026-08-16.
            .With(new Avalonia.X11PlatformOptions
            {
                RenderingMode =
                [
                    Avalonia.X11RenderingMode.Egl,
                    Avalonia.X11RenderingMode.Glx,
                    Avalonia.X11RenderingMode.Software,
                ],
                GlProfiles =
                [
                    new Avalonia.OpenGL.GlVersion(Avalonia.OpenGL.GlProfileType.OpenGLES, 3, 0),
                    new Avalonia.OpenGL.GlVersion(Avalonia.OpenGL.GlProfileType.OpenGLES, 2, 0),
                    new Avalonia.OpenGL.GlVersion(Avalonia.OpenGL.GlProfileType.OpenGL, 4, 0),
                    new Avalonia.OpenGL.GlVersion(Avalonia.OpenGL.GlProfileType.OpenGL, 3, 2),
                ],
            })
            .WithInterFont()
            .LogToTrace();
}
