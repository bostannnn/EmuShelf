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

    /// <summary>
    /// "nobatch": the probe on, and Big Type's batching off, for an A/B run against "1" (M47 B6). Either way the backdrops' picks are
    /// seeded, so two runs of the same presses show the same backdrops.
    /// </summary>
    public static bool NoBatch { get; private set; }

    /// <summary>"ab": batching on and off every two frames within one run, GPU times filed under "+b" and "+u" (M47 B6).</summary>
    public static bool Alternate { get; private set; }

    /// <summary>
    /// "rest": as "1", and the frames drawn only for a resting Stage's backdrop steps are logged too, as runs tagged "rest" (M47 B13,
    /// to measure a backdrop's cost when nothing else moves).
    /// </summary>
    public static bool Rest { get; private set; }

    /// <summary>"ring": the probe on, and the native Launcher logs where its Big Type ring and its target are every frame (M47 B11).</summary>
    public static bool Ring { get; private set; }

    private static bool Read()
    {
        try
        {
            var buffer = new byte[92]; // PROP_VALUE_MAX
            var length = SystemPropertyGet(Property, buffer);
            var value = length > 0 ? Encoding.UTF8.GetString(buffer, 0, length).Trim().ToLowerInvariant() : string.Empty;
            NoBatch = value == "nobatch";
            Alternate = value == "ab";
            Rest = value == "rest";
            Ring = value == "ring";
            var on = value is "1" or "on" or "true" or "nobatch" or "ab" or "rest" or "ring";
            if (on)
            {
                global::Android.Util.Log.Info("EmuShelfBigType", $"{Property}: native frame times are logged for this run");
                EmuShelf.App.Views.Handheld.HandheldProbeLog.Write = line => global::Android.Util.Log.Info("EmuShelfBigType", line);
            }
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
