namespace EmuShelf.Core.Library;

/// <summary>
/// A stable id for this EmuShelf install, written on every play session so two devices' logs can be merged
/// later without mistaking one device's rows for the other's. Portable: it lives in <c>Settings/</c>, so it
/// moves with the app folder.
/// </summary>
public interface IDeviceIdentity
{
    string DeviceId { get; }
}

/// <summary>A fixed id: tests, design time, and libraries built without an install behind them.</summary>
public sealed class FixedDeviceIdentity(string deviceId) : IDeviceIdentity
{
    public static FixedDeviceIdentity Unknown { get; } = new("unknown");

    public string DeviceId { get; } = string.IsNullOrWhiteSpace(deviceId)
        ? throw new ArgumentException("A device id cannot be empty.", nameof(deviceId))
        : deviceId;
}
