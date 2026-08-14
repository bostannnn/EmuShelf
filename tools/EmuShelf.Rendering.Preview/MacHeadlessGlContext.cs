using System.Runtime.InteropServices;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// A current, windowless OpenGL 3.2 core context on macOS, via CGL.
/// </summary>
/// <remarks>
/// The EGL path this tool was written against is Mesa-only, so on macOS the preview could not run at
/// all — which meant every question about how a shell actually looks had to go to a human with the
/// app open, including "which way up is this cartridge". CGL gives a pixel-format-only context with
/// no drawable, which is all the renderer needs: it draws into its own framebuffer object and blits
/// into whichever one it is handed.
///
/// Apple deprecated OpenGL, so this warns at build time on some SDKs and will eventually stop
/// working. It is a development tool, not shipped code, and the same argument that put the app on
/// OpenGL applies more strongly here.
/// </remarks>
internal static class MacHeadlessGlContext
{
    private const string OpenGlFramework =
        "/System/Library/Frameworks/OpenGL.framework/Versions/Current/OpenGL";

    // CGLPixelFormatAttribute values.
    private const int AttributeAccelerated = 73;
    private const int AttributeOpenGlProfile = 99;
    private const int ProfileVersion3_2Core = 0x3200;
    private const int AttributeEnd = 0;

    private static IntPtr _openGl;

    public static Func<string, IntPtr> CreateCurrent()
    {
        int[] attributes =
        [
            AttributeOpenGlProfile, ProfileVersion3_2Core,
            AttributeAccelerated,
            AttributeEnd,
        ];

        var error = CGLChoosePixelFormat(attributes, out var pixelFormat, out _);
        if (error != 0 || pixelFormat == IntPtr.Zero)
        {
            // Retry without requiring hardware acceleration, so this still runs under a software
            // renderer the way the Mesa path falls back to llvmpipe.
            int[] fallback = [AttributeOpenGlProfile, ProfileVersion3_2Core, AttributeEnd];
            error = CGLChoosePixelFormat(fallback, out pixelFormat, out _);
            if (error != 0 || pixelFormat == IntPtr.Zero)
            {
                throw new InvalidOperationException($"CGLChoosePixelFormat failed ({error}).");
            }
        }

        error = CGLCreateContext(pixelFormat, IntPtr.Zero, out var context);
        CGLDestroyPixelFormat(pixelFormat);
        if (error != 0 || context == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CGLCreateContext failed ({error}).");
        }

        error = CGLSetCurrentContext(context);
        if (error != 0)
        {
            throw new InvalidOperationException($"CGLSetCurrentContext failed ({error}).");
        }

        _openGl = NativeLibrary.Load(OpenGlFramework);
        return name => NativeLibrary.TryGetExport(_openGl, name, out var address)
            ? address
            : IntPtr.Zero;
    }

    [DllImport(OpenGlFramework)]
    private static extern int CGLChoosePixelFormat(int[] attributes, out IntPtr pixelFormat, out int count);

    [DllImport(OpenGlFramework)]
    private static extern int CGLCreateContext(IntPtr pixelFormat, IntPtr share, out IntPtr context);

    [DllImport(OpenGlFramework)]
    private static extern int CGLDestroyPixelFormat(IntPtr pixelFormat);

    [DllImport(OpenGlFramework)]
    private static extern int CGLSetCurrentContext(IntPtr context);
}
