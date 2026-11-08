namespace EmuShelf.Core.Handheld;

/// <summary>
/// What the Handheld Stage's status pill shows beside the time. Null means "not known here".
/// <paramref name="HoldsStill"/> (M45 H7c): the device asks for stillness, with battery saver on or the system's
/// reduced-motion setting, so the Stage's idle motion (the animated backdrop, the medium's sway) holds a still frame.
/// </summary>
public sealed record DeviceStatus(int? BatteryPercent, bool IsCharging, bool? IsWifiConnected, bool HoldsStill = false)
{
    public static DeviceStatus Unknown { get; } = new(null, false, null);
}

/// <summary>Battery and Wi-Fi for the status pill. The Android head reads the system; elsewhere it is unknown.</summary>
public interface IDeviceStatusService
{
    DeviceStatus GetStatus();

    /// <summary>
    /// Raised (on any thread) when the status changed in a way worth reading at once rather than at the next
    /// refresh: battery saver or reduced motion switched (<see cref="DeviceStatus.HoldsStill"/>).
    /// </summary>
    event Action? Changed;
}

public sealed class UnknownDeviceStatusService : IDeviceStatusService
{
    public static UnknownDeviceStatusService Instance { get; } = new();

    public DeviceStatus GetStatus() => DeviceStatus.Unknown;

    public event Action? Changed
    {
        add { }
        remove { }
    }
}
