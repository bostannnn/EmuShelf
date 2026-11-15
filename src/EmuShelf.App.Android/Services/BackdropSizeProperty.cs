using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using EmuShelf.Rendering.Stage.Backdrops;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// A measurement switch for the Thor (M47 B13): the system property <c>debug.emushelf.bdsize</c>, read once at start-up, draws every
/// sharp Big Type backdrop at that width (its height at 16:9) instead of the table's size, or at its console's own size with 0, so the
/// GPU cost of each size can be measured with the frame probe without a rebuild (<c>adb shell setprop debug.emushelf.bdsize 960</c>;
/// an empty value clears it). Unset, the table's sizes are drawn. Not a look the app ever chooses.
/// </summary>
internal static class BackdropSizeProperty
{
    public const string Property = "debug.emushelf.bdsize";

    public static void Apply()
    {
        try
        {
            var buffer = new byte[92]; // PROP_VALUE_MAX
            var length = SystemPropertyGet(Property, buffer);
            var value = length > 0 ? Encoding.UTF8.GetString(buffer, 0, length).Trim() : string.Empty;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width) && width is >= 0 and <= 3840)
            {
                BigTypeBackdrops.SharpWidthOverride = width;
                global::Android.Util.Log.Info("EmuShelfBigType", $"{Property}: sharp backdrops drawn {(width == 0 ? "at their console's size" : $"{width} wide")} for this run");
            }
        }
        catch (System.Exception)
        {
            // No property: the table's sizes.
        }
    }

    [DllImport("libc", EntryPoint = "__system_property_get")]
    private static extern int SystemPropertyGet([MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte[] value);
}
