using System.Text.Json;
using System.Text.Json.Serialization;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IAppPaths _appPaths;
    private readonly IAppLogger _logger;

    public JsonSettingsService(IAppPaths appPaths, IAppLogger? logger = null)
    {
        _appPaths = appPaths;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_appPaths.SettingsFilePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_appPaths.SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // settings.json is a portable, hand-editable file: a syntax mistake, a transient
            // lock (AV/backup/second instance), or a permissions hiccup shouldn't block startup.
            _logger.Warning("Could not load Settings/settings.json; defaults were used.", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);

        // Write-then-rename so a crash or removed drive mid-write can't truncate the live
        // file into invalid JSON (which Load would silently discard back to defaults).
        AtomicFile.WriteAllText(_appPaths.SettingsFilePath, json);
    }
}
