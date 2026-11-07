namespace EmuShelf.Core.Handheld;

/// <summary>What the Handheld Stage's status pill shows beside the time. Null means "not known here".</summary>
public sealed record DeviceStatus(int? BatteryPercent, bool IsCharging, bool? IsWifiConnected)
{
    public static DeviceStatus Unknown { get; } = new(null, false, null);
}

/// <summary>Battery and Wi-Fi for the status pill. The Android head reads the system; elsewhere it is unknown.</summary>
public interface IDeviceStatusService
{
    DeviceStatus GetStatus();
}

public sealed class UnknownDeviceStatusService : IDeviceStatusService
{
    public static UnknownDeviceStatusService Instance { get; } = new();

    public DeviceStatus GetStatus() => DeviceStatus.Unknown;
}
