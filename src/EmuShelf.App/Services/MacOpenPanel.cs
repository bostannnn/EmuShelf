using System.Runtime.InteropServices;

namespace EmuShelf.App.Services;

/// <summary>
/// A native macOS <c>NSOpenPanel</c> for choosing an emulator, used because Avalonia's cross-platform
/// picker cannot select a <c>.app</c> bundle on macOS — its open panel treats the bundle as a package
/// and navigates into it, so a file picker returns nothing and a folder picker cannot select it either.
/// This panel sets <c>treatsFilePackagesAsDirectories = NO</c> with <c>canChooseFiles = YES</c>, which
/// makes a <c>.app</c> selectable as a single item (a bare Unix binary stays selectable too). The
/// launch layer then resolves a chosen bundle to its inner Mach-O binary.
///
/// Runs the panel modally on the calling thread, which must be the UI (NSApplication main) thread —
/// EmuShelf's dialog service is always invoked there. Objective-C is reached through the stable
/// libobjc message-send ABI; every selector used here is a long-standing AppKit API.
/// </summary>
internal static class MacOpenPanel
{
    private const string LibObjc = "/usr/lib/libobjc.dylib";

    [DllImport(LibObjc)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjc)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, nuint index);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern long SendLong(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
    private static extern void SendSetBool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);

    private static IntPtr Sel(string name) => sel_registerName(name);

    // NSModalResponseOK.
    private const long ModalResponseOk = 1;

    /// <summary>
    /// Shows the panel and returns the selected path, or null if the user cancelled or the panel could
    /// not be created. Throws only on a genuine interop failure (missing symbol), which the caller
    /// treats as "fall back to the Avalonia picker."
    /// </summary>
    public static string? ChooseEmulator()
    {
        var panelClass = objc_getClass("NSOpenPanel");
        if (panelClass == IntPtr.Zero)
            return null;

        var panel = Send(panelClass, Sel("openPanel"));
        if (panel == IntPtr.Zero)
            return null;

        SendSetBool(panel, Sel("setCanChooseFiles:"), true);
        SendSetBool(panel, Sel("setCanChooseDirectories:"), false);
        SendSetBool(panel, Sel("setAllowsMultipleSelection:"), false);
        // The key line: keep `.app` bundles as selectable single items rather than folders to enter.
        SendSetBool(panel, Sel("setTreatsFilePackagesAsDirectories:"), false);

        if (SendLong(panel, Sel("runModal")) != ModalResponseOk)
            return null;

        var urls = Send(panel, Sel("URLs"));
        if (urls == IntPtr.Zero || SendLong(urls, Sel("count")) < 1)
            return null;

        var url = SendIndex(urls, Sel("objectAtIndex:"), 0);
        if (url == IntPtr.Zero)
            return null;

        var nsPath = Send(url, Sel("path"));
        if (nsPath == IntPtr.Zero)
            return null;

        var utf8 = Send(nsPath, Sel("UTF8String"));
        return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
    }
}
