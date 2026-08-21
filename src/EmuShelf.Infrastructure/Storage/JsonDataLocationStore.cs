using System.Text.Json;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Storage;

/// <summary>
/// File-backed <see cref="IDataLocationStore"/>: a single small JSON file at a fixed, pointer-independent
/// path. On Android the head places it in app-private <c>FilesDir</c> — the one location that is writable
/// before (and regardless of) the data folder the pointer names. Writes are atomic so a crash mid-write
/// cannot strand the app between "no folder chosen" and "folder chosen" with a truncated file.
/// </summary>
public sealed class JsonDataLocationStore : IDataLocationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly IAppLogger _logger;

    public JsonDataLocationStore(string filePath, IAppLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public DataLocation? Read()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var json = File.ReadAllText(_filePath);
            var location = JsonSerializer.Deserialize<DataLocation>(json, SerializerOptions);
            if (location is null || string.IsNullOrWhiteSpace(location.BaseDirectory))
                return null;
            return location;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable pointer must not crash startup — treat it as "not chosen" so the
            // resolver falls back to onboarding, where the user re-picks and the file is rewritten cleanly.
            _logger.Warning($"Could not read the data-location pointer at '{_filePath}'; treating as unset.", ex);
            return null;
        }
    }

    public void Write(DataLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(location, SerializerOptions);
        AtomicFile.WriteAllText(_filePath, json);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_filePath))
                File.Delete(_filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not clear the data-location pointer at '{_filePath}'.", ex);
        }
    }
}
