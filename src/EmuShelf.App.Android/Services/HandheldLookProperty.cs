using System.Runtime.InteropServices;
using System.Text;
using EmuShelf.App.ViewModels;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// A developer override of Handheld mode's look for one run (M47 B2): the system property <c>debug.emushelf.look</c>, which
/// adb's shell can set without root (<c>adb shell setprop debug.emushelf.look bigtype</c>, or "classic"; set it to an
/// empty value to clear it). Read once at start-up into <see cref="HandheldLookChoice.Override"/>. Debug properties do
/// not survive a reboot, and the saved settings are never touched. Unset, nothing changes.
/// </summary>
internal static class HandheldLookProperty
{
    public const string Property = "debug.emushelf.look";

    private static bool _read;

    public static void Read()
    {
        if (_read)
            return;
        _read = true;
        try
        {
            var buffer = new byte[92]; // PROP_VALUE_MAX
            var length = SystemPropertyGet(Property, buffer);
            HandheldLookChoice.Override = HandheldLookChoice.Parse(length > 0 ? Encoding.UTF8.GetString(buffer, 0, length) : null);
        }
        catch (System.Exception)
        {
            HandheldLookChoice.Override = null;
        }
        if (HandheldLookChoice.Override is { } look)
            global::Android.Util.Log.Info("EmuShelf", $"{Property}: Handheld look {look} for this run");
    }

    [DllImport("libc", EntryPoint = "__system_property_get")]
    private static extern int SystemPropertyGet([MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte[] value);
}
