using Android.Content;
using Android.Database;
using Android.Net;
using Android.OS;
using Android.Provider;
using EmuShelf.Core.Handheld;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// Battery level, charging and Wi-Fi for Handheld mode's status pill, read from the system; and whether the device asks
/// for stillness (M45 H7c): battery saver on, or animations removed in the system's accessibility settings (the
/// animator duration scale at 0, Android's reduced motion). Both are watched, so <see cref="Changed"/> fires the moment
/// either switches rather than at the pill's next refresh.
/// </summary>
internal sealed class AndroidDeviceStatusService : IDeviceStatusService
{
    private readonly Context _context;
    private readonly PowerManager? _power;

    public AndroidDeviceStatusService(Context context)
    {
        _context = context;
        _power = context.GetSystemService(Context.PowerService) as PowerManager;
        try
        {
            var filter = new IntentFilter(PowerManager.ActionPowerSaveModeChanged);
            var receiver = new ChangeReceiver(this);
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                context.RegisterReceiver(receiver, filter, ReceiverFlags.NotExported);
            else
                context.RegisterReceiver(receiver, filter);
            if (Settings.Global.GetUriFor(Settings.Global.AnimatorDurationScale) is { } uri && context.ContentResolver is { } resolver)
                resolver.RegisterContentObserver(uri, false, new SettingObserver(this));
        }
        catch (Exception ex)
        {
            // Without the watchers the pill's 15-second refresh still picks the change up.
            global::Android.Util.Log.Warn("EmuShelfStage", $"Battery saver and reduced motion are not watched: {ex.Message}");
        }
    }

    public event Action? Changed;

    public DeviceStatus GetStatus()
    {
        int? battery = null;
        var charging = false;
        if (_context.GetSystemService(Context.BatteryService) is BatteryManager batteryManager)
        {
            var capacity = batteryManager.GetIntProperty((int)BatteryProperty.Capacity);
            battery = capacity is >= 0 and <= 100 ? capacity : null;
            charging = batteryManager.IsCharging;
        }

        bool? wifi = null;
        if (_context.GetSystemService(Context.ConnectivityService) is ConnectivityManager connectivity)
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

        return new DeviceStatus(battery, charging, wifi, HoldsStill());
    }

    private bool HoldsStill()
    {
        try
        {
            if (_power?.IsPowerSaveMode == true)
                return true;
            // "Remove animations" (accessibility) and the developer option both set the animator scale to 0.
            return _context.ContentResolver is { } resolver
                && Settings.Global.GetFloat(resolver, Settings.Global.AnimatorDurationScale, 1f) == 0f;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed class ChangeReceiver(AndroidDeviceStatusService owner) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent) => owner.Changed?.Invoke();
    }

    private sealed class SettingObserver(AndroidDeviceStatusService owner) : ContentObserver(new Handler(Looper.MainLooper!))
    {
        public override void OnChange(bool selfChange) => owner.Changed?.Invoke();
    }
}
