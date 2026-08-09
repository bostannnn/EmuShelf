using System.Text.Json;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Emulators;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Emulators;

/// <summary>
/// Persists EmuShelf-managed emulator installs as an atomic, portable JSON file under <c>Settings</c>.
/// The manifest is the authority for "what is installed and at what version", so the install service can
/// read the version without probing an arbitrary binary and never overwrites an install it did not record.
/// </summary>
public sealed class JsonEmulatorInstallManifestStore : IEmulatorInstallManifestStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _manifestPath;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private Dictionary<string, EmulatorInstallRecord>? _records;

    public JsonEmulatorInstallManifestStore(IAppPaths appPaths, IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _manifestPath = Path.Combine(appPaths.SettingsDirectory, "emulator-installs.json");
        _logger = logger ?? NullAppLogger.Instance;
    }

    public EmulatorInstallRecord? Get(string emulatorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emulatorId);
        lock (_gate)
        {
            return Load().TryGetValue(emulatorId, out var record) ? record : null;
        }
    }

    public IReadOnlyList<EmulatorInstallRecord> GetAll()
    {
        lock (_gate)
        {
            return Load().Values
                .OrderBy(record => record.EmulatorId, StringComparer.Ordinal)
                .ToList();
        }
    }

    public void Save(EmulatorInstallRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.EmulatorId);
        lock (_gate)
        {
            var records = Load();
            records[record.EmulatorId] = record;
            Persist(records);
        }
    }

    public void Remove(string emulatorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emulatorId);
        lock (_gate)
        {
            var records = Load();
            if (records.Remove(emulatorId))
                Persist(records);
        }
    }

    private Dictionary<string, EmulatorInstallRecord> Load()
    {
        if (_records is not null)
            return _records;

        var loaded = new Dictionary<string, EmulatorInstallRecord>(StringComparer.Ordinal);
        if (File.Exists(_manifestPath))
        {
            try
            {
                var json = File.ReadAllText(_manifestPath);
                var document = JsonSerializer.Deserialize<ManifestDocument>(json, SerializerOptions);
                foreach (var record in document?.Installs ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(record.EmulatorId))
                        loaded[record.EmulatorId] = record;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // A corrupt or unreadable manifest must not brick the install manager: log and start empty.
                _logger.Warning($"Could not read the emulator install manifest at '{_manifestPath}'; starting empty.", ex);
                loaded.Clear();
            }
        }

        _records = loaded;
        return _records;
    }

    private void Persist(Dictionary<string, EmulatorInstallRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
        var document = new ManifestDocument(
            records.Values.OrderBy(record => record.EmulatorId, StringComparer.Ordinal).ToList());
        AtomicFile.WriteAllText(_manifestPath, JsonSerializer.Serialize(document, SerializerOptions));
    }

    private sealed record ManifestDocument(IReadOnlyList<EmulatorInstallRecord> Installs);
}
