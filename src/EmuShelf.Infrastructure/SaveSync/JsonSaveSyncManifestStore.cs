using System.Text.Json;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>Persists the local sync baseline as an atomic, portable JSON file under <c>Saves</c>.</summary>
public sealed class JsonSaveSyncManifestStore : ISaveSyncManifestStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _manifestPath;

    public JsonSaveSyncManifestStore(IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        _manifestPath = Path.Combine(appPaths.SavesDirectory, "sync-manifest.json");
    }

    public async Task<SaveSyncManifest> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_manifestPath))
            return new SaveSyncManifest();

        await using var stream = new FileStream(
            _manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var document = await JsonSerializer.DeserializeAsync<ManifestDocument>(
            stream,
            SerializerOptions,
            cancellationToken);
        return new SaveSyncManifest(document?.Baselines ?? []);
    }

    public Task SaveAsync(SaveSyncManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
        return AtomicFile.WriteAsync(
            _manifestPath,
            (stream, token) => JsonSerializer.SerializeAsync(
                stream,
                new ManifestDocument(manifest.Baselines),
                SerializerOptions,
                token),
            cancellationToken);
    }

    private sealed record ManifestDocument(IReadOnlyList<SaveUnitBaseline> Baselines);
}
