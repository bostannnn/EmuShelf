using Android.Content;
using Android.Net;
using Android.OS;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.Android.Services;

/// <summary>Battery level, charging and Wi-Fi for Handheld mode's status pill, read from the system.</summary>
internal sealed class AndroidDeviceStatusService(Context context) : IDeviceStatusService
{
    public DeviceStatus GetStatus()
    {
        int? battery = null;
        var charging = false;
        if (context.GetSystemService(Context.BatteryService) is BatteryManager batteryManager)
        {
            var capacity = batteryManager.GetIntProperty((int)BatteryProperty.Capacity);
            battery = capacity is >= 0 and <= 100 ? capacity : null;
            charging = batteryManager.IsCharging;
        }

        bool? wifi = null;
        if (context.GetSystemService(Context.ConnectivityService) is ConnectivityManager connectivity)
        {
            try
            {
                var capabilities = connectivity.GetNetworkCapabilities(connectivity.ActiveNetwork);
                wifi = capabilities?.HasTransport(TransportType.Wifi) == true;
            }
            catch (Java.Lang.SecurityException)
            {
                // No network-state permission (an older install's manifest): leave Wi-Fi unknown.
            }
        }

        return new DeviceStatus(battery, charging, wifi);
    }
}
