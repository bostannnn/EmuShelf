using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Settings;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly ConcurrentDictionary<string, object> ProcessLocks =
        new(FilePathComparison.Comparer);
    private static readonly TimeSpan FileLockTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FileLockRetryDelay = TimeSpan.FromMilliseconds(15);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IAppPaths _appPaths;
    private readonly IAppLogger _logger;
    private readonly object _sync;
    private readonly string _lockFilePath;

    public JsonSettingsService(IAppPaths appPaths, IAppLogger? logger = null)
    {
        _appPaths = appPaths;
        _logger = logger ?? NullAppLogger.Instance;
        var settingsPath = Path.GetFullPath(_appPaths.SettingsFilePath);
        _sync = ProcessLocks.GetOrAdd(settingsPath, static _ => new object());
        _lockFilePath = settingsPath + ".lock";
    }

    public AppSettings Load()
    {
        lock (_sync)
        {
            using var fileLock = AcquireFileLock();
            return LoadCore();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_sync)
        {
            using var fileLock = AcquireFileLock();
            SaveCore(settings);
        }
    }

    public AppSettings Update(Func<AppSettings, AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_sync)
        {
            using var fileLock = AcquireFileLock();
            // An update must not turn a transient read failure or malformed file into a write of
            // defaults. Preserve the existing file and let the caller report the failure instead.
            var updated = update(LoadCore(fallbackToDefaultsOnError: false));
            SaveCore(updated);
            return updated;
        }
    }

    private FileStream AcquireFileLock()
    {
        var directory = Path.GetDirectoryName(_lockFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var started = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(
                    _lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException) when (started.Elapsed < FileLockTimeout)
            {
                Thread.Sleep(FileLockRetryDelay);
            }
        }
    }

    private AppSettings LoadCore(bool fallbackToDefaultsOnError = true)
    {
        if (!File.Exists(_appPaths.SettingsFilePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_appPaths.SettingsFilePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
            return WithScrapingDefaults(loaded);
        }
        catch (Exception ex) when (
            fallbackToDefaultsOnError &&
            ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // settings.json is a portable, hand-editable file: a syntax mistake, a transient
            // lock (AV/backup/second instance), or a permissions hiccup shouldn't block startup.
            _logger.Warning("Could not load Settings/settings.json; defaults were used.", ex);
            return new AppSettings();
        }
    }

    // settings.json is hand-editable, so tolerate an explicit "Scraping": null / "ScreenScraper": null
    // by substituting defaults instead of throwing an NRE out of the (otherwise robust) load path.
    // Media-kind and metadata-field lists are no longer serialized — they are code-owned defaults in
    // ScreenScraperMediaProfile — so nothing needs re-merging here anymore.
    private static AppSettings WithScrapingDefaults(AppSettings settings)
    {
        if (settings.Scraping is null)
            return settings with { Scraping = new ScrapingSettings() };
        if (settings.Scraping.ScreenScraper is null)
            return settings with { Scraping = settings.Scraping with { ScreenScraper = new ScreenScraperSettings() } };

        return settings;
    }

    private void SaveCore(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, SerializerOptions);

        // Write-then-rename so a crash or removed drive mid-write can't truncate the live
        // file into invalid JSON (which Load would silently discard back to defaults).
        AtomicFile.WriteAllText(_appPaths.SettingsFilePath, json);
    }
}
