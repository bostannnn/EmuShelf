using System.Text.Json;
using System.Text.Json.Serialization;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IAppPaths _appPaths;

    public JsonSettingsService(IAppPaths appPaths)
    {
        _appPaths = appPaths;
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
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);

        // Write-then-rename so a crash or removed drive mid-write can't truncate the live
        // file into invalid JSON (which Load would silently discard back to defaults).
        var tempPath = _appPaths.SettingsFilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _appPaths.SettingsFilePath, overwrite: true);
    }
}
