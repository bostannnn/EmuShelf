using System.Runtime.InteropServices;
using System.Text;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Whether the native screens log their frame times (M47 B6, R1's probe kept for the owner's measurement on the Thor): the system
/// property <c>debug.emushelf.probe</c>, which adb's shell can set without root (<c>adb shell setprop debug.emushelf.probe 1</c>; an
/// empty value clears it), read once at start-up. While it is set each native screen logs, under logcat tag <c>EmuShelfBigType</c>, one
/// line per run of frames with the same motions ("letters", "wipe", "letters+wipe", "flood", "other"): intervals and render-thread CPU
/// time (<see cref="EmuShelf.Rendering.Stage.NativeFrameProbe"/>), then GPU time per frame (<see cref="GpuFrameTimer"/>). A measurement
/// switch, not a look: nothing draws differently. Unset, nothing is measured.
/// </summary>
internal static class FrameProbeProperty
{
    public const string Property = "debug.emushelf.probe";

    private static bool? _on;

    public static bool IsOn => _on ??= Read();

    private static bool Read()
    {
        try
        {
            var buffer = new byte[92]; // PROP_VALUE_MAX
            var length = SystemPropertyGet(Property, buffer);
            var value = length > 0 ? Encoding.UTF8.GetString(buffer, 0, length).Trim().ToLowerInvariant() : string.Empty;
            var on = value is "1" or "on" or "true";
            if (on)
                global::Android.Util.Log.Info("EmuShelfBigType", $"{Property}: native frame times are logged for this run");
            return on;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    [DllImport("libc", EntryPoint = "__system_property_get")]
    private static extern int SystemPropertyGet([MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte[] value);
}
