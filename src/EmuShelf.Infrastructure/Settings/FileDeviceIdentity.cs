using System.Text.Json;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;

namespace EmuShelf.Infrastructure.Settings;

/// <summary>
/// The install's device id in portable <c>Settings/device.json</c>, created on first use. A missing or
/// damaged file gets a fresh id; if it cannot be written, the id still holds for this run and the next
/// start tries again. It can never prevent EmuShelf from starting or a game from launching.
/// </summary>
public sealed class FileDeviceIdentity(string filePath, IAppLogger? logger = null) : IDeviceIdentity
{
    private readonly Lazy<string> _deviceId = new(() => LoadOrCreate(filePath, logger ?? NullAppLogger.Instance));

    public string DeviceId => _deviceId.Value;

    private static string LoadOrCreate(string filePath, IAppLogger logger)
    {
        try
        {
            if (File.Exists(filePath) &&
                JsonSerializer.Deserialize<DeviceDocument>(File.ReadAllText(filePath)) is { DeviceId: { } stored } &&
                !string.IsNullOrWhiteSpace(stored))
                return stored;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.Warning($"Could not read the device id; making a new one: {ex.Message}");
        }

        var created = Guid.NewGuid().ToString("N");
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new DeviceDocument(created)));
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning($"Could not persist the device id: {ex.Message}");
        }
        return created;
    }

    private sealed record DeviceDocument(string? DeviceId);
}
