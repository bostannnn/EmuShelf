using System.Runtime.InteropServices;

namespace EmuShelf.Rendering.Preview;

/// <summary>
/// A current, windowless desktop-GL context obtained through Mesa's surfaceless EGL platform.
/// </summary>
/// <remarks>
/// Lets the preview render on a machine with no display server and no GPU — Mesa falls back to
/// llvmpipe — so the shell renderer can be looked at from a headless checkout or from CI, rather
/// than only on a developer's desktop.
/// </remarks>
internal static class HeadlessGlContext
{
    private const int EglPlatformSurfacelessMesa = 0x31DD;
    private const int EglOpenGlApi = 0x30A2;
    private const int EglContextMajorVersion = 0x3098;
    private const int EglContextMinorVersion = 0x30FB;
    private const int EglContextOpenGlProfileMask = 0x30FD;
    private const int EglContextOpenGlCoreProfileBit = 0x00000001;
    private const int EglNone = 0x3038;

    private static IntPtr _openGl;

    /// <summary>Creates the context, makes it current, and returns a GL entry-point resolver.</summary>
    public static Func<string, IntPtr> CreateCurrent()
    {
        var getPlatformDisplay = Marshal.GetDelegateForFunctionPointer<GetPlatformDisplayExt>(
            eglGetProcAddress("eglGetPlatformDisplayEXT") is var fn && fn != IntPtr.Zero
                ? fn
                : throw new InvalidOperationException(
                    "eglGetPlatformDisplayEXT is unavailable; this tool needs Mesa's EGL."));

        var display = getPlatformDisplay(EglPlatformSurfacelessMesa, IntPtr.Zero, IntPtr.Zero);
        if (display == IntPtr.Zero || !eglInitialize(display, out _, out _))
        {
            throw new InvalidOperationException(
                $"eglInitialize on the surfaceless platform failed (0x{eglGetError():X}). "
                + "Install libegl1 and libgl1-mesa-dri.");
        }

        if (!eglBindAPI(EglOpenGlApi))
        {
            throw new InvalidOperationException($"eglBindAPI(EGL_OPENGL_API) failed (0x{eglGetError():X}).");
        }

        int[] contextAttributes =
        [
            EglContextMajorVersion, 3,
            EglContextMinorVersion, 3,
            EglContextOpenGlProfileMask, EglContextOpenGlCoreProfileBit,
            EglNone,
        ];

        // Mesa advertises EGL_KHR_no_config_context, so a null config is legitimate here — and
        // necessary, because a surfaceless display offers no window-compatible configs to choose.
        var context = eglCreateContext(display, IntPtr.Zero, IntPtr.Zero, contextAttributes);
        if (context == IntPtr.Zero)
        {
            throw new InvalidOperationException($"eglCreateContext failed (0x{eglGetError():X}).");
        }

        if (!eglMakeCurrent(display, IntPtr.Zero, IntPtr.Zero, context))
        {
            throw new InvalidOperationException($"eglMakeCurrent failed (0x{eglGetError():X}).");
        }

        _openGl = NativeLibrary.Load("libGL.so.1");
        return Resolve;
    }

    // eglGetProcAddress covers extensions everywhere and core entry points only where
    // EGL_KHR_get_all_proc_addresses is present, so fall through to the library's own symbols.
    private static IntPtr Resolve(string name)
    {
        var address = eglGetProcAddress(name);
        if (address != IntPtr.Zero)
        {
            return address;
        }

        return NativeLibrary.TryGetExport(_openGl, name, out var exported) ? exported : IntPtr.Zero;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetPlatformDisplayExt(int platform, IntPtr nativeDisplay, IntPtr attributes);

    [DllImport("libEGL.so.1")]
    private static extern IntPtr eglGetProcAddress([MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport("libEGL.so.1")]
    private static extern bool eglInitialize(IntPtr display, out int major, out int minor);

    [DllImport("libEGL.so.1")]
    private static extern bool eglBindAPI(int api);

    [DllImport("libEGL.so.1")]
    private static extern IntPtr eglCreateContext(
        IntPtr display, IntPtr config, IntPtr shareContext, int[] attributes);

    [DllImport("libEGL.so.1")]
    private static extern bool eglMakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context);

    [DllImport("libEGL.so.1")]
    private static extern int eglGetError();
}
