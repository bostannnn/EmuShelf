using System.Text.Json;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Storage;

/// <summary>
/// File-backed <see cref="IDataLocationStore"/>: a single small JSON file at a fixed, pointer-independent
/// path. On Android the head places it in app-private <c>FilesDir</c> — the one location that is writable
/// before (and regardless of) the data folder the pointer names. Writes are atomic so a crash mid-write
/// cannot strand the app between "no folder chosen" and "folder chosen" with a truncated file.
///
/// An optional <b>mirror</b> path keeps a second copy somewhere that outlives the app itself — on Android,
/// a dotfile on shared storage. App-private storage is wiped by an uninstall while the data folder on
/// shared storage is not, so without the mirror every reinstall re-ran first-run onboarding on top of a
/// library that was still there. The mirror is strictly best-effort: it is only readable/writable once the
/// all-files grant is held, so every mirror operation swallows I/O failures, and the primary copy is
/// always tried first. A primary hit whose mirror is missing re-creates the mirror, so installs that
/// pre-date the mirror pick it up on their next resolve.
/// </summary>
public sealed class JsonDataLocationStore : IDataLocationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly string? _mirrorFilePath;
    private readonly IAppLogger _logger;

    public JsonDataLocationStore(string filePath, IAppLogger? logger = null)
        : this(filePath, mirrorFilePath: null, logger)
    {
    }

    /// <param name="mirrorFilePath">
    /// A second, best-effort copy of the pointer at a location that survives an uninstall (see the class
    /// remarks). Null keeps the single-file behaviour.
    /// </param>
    public JsonDataLocationStore(string filePath, string? mirrorFilePath, IAppLogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _mirrorFilePath = string.IsNullOrWhiteSpace(mirrorFilePath) ? null : mirrorFilePath;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public DataLocation? Read()
    {
        var primary = ReadFile(_filePath);
        if (primary is not null)
        {
            // Heal a missing mirror (an install that pre-dates it, or one whose mirror was deleted). Cheap:
            // one Exists check per read, and the write only happens when it is absent.
            if (_mirrorFilePath is not null && !SafeExists(_mirrorFilePath))
                TryWriteMirror(primary);
            return primary;
        }

        if (_mirrorFilePath is null)
            return null;

        var mirror = ReadFile(_mirrorFilePath);
        if (mirror is not null)
            _logger.Information($"Data-location pointer restored from its mirror at '{_mirrorFilePath}'.");
        return mirror;
    }

    public void Write(DataLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        WriteFile(_filePath, location);
        TryWriteMirror(location);
    }

    public void Clear()
    {
        TryDelete(_filePath);
        if (_mirrorFilePath is not null)
            TryDelete(_mirrorFilePath);
    }

    private DataLocation? ReadFile(string path)
    {
        if (!SafeExists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var location = JsonSerializer.Deserialize<DataLocation>(json, SerializerOptions);
            if (location is null || string.IsNullOrWhiteSpace(location.BaseDirectory))
                return null;
            return location;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable pointer must not crash startup — treat it as "not chosen" so the
            // resolver falls back to onboarding (or the mirror), where the file is rewritten cleanly.
            _logger.Warning($"Could not read the data-location pointer at '{path}'; treating as unset.", ex);
            return null;
        }
    }

    private static void WriteFile(string path, DataLocation location)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(location, SerializerOptions);
        AtomicFile.WriteAllText(path, json);
    }

    private void TryWriteMirror(DataLocation location)
    {
        if (_mirrorFilePath is null)
            return;

        try
        {
            WriteFile(_mirrorFilePath, location);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Expected before the all-files grant is held; the next Read/Write retries.
            _logger.Information($"Could not write the data-location mirror at '{_mirrorFilePath}': {ex.Message}");
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not clear the data-location pointer at '{path}'.", ex);
        }
    }

    // File.Exists never throws, but a shared-storage probe without the grant can still be slow or odd on
    // some FUSE firmware; keep every mirror touch inside one guarded helper.
    private static bool SafeExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
