using System.Globalization;

namespace EmuShelf.Integrations.Emulators;

/// <summary>
/// Copies an emulator config file aside before EmuShelf modifies it, and restores the most recent
/// copy on revert. Backups live under EmuShelf's own portable data (never beside the emulator's
/// files), one timestamped copy per apply, so a revert undoes the last apply and the originals are
/// always recoverable.
/// </summary>
public sealed class HotkeyConfigBackup
{
    private const string Suffix = ".bak";
    private readonly string _directory;

    public HotkeyConfigBackup(string backupRoot, string emulatorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(emulatorId);
        _directory = Path.Combine(backupRoot, emulatorId);
    }

    /// <summary>Copies a file to a fresh timestamped backup. A missing source is a no-op.</summary>
    public void Capture(string sourceFile)
    {
        if (!File.Exists(sourceFile))
            return;

        Directory.CreateDirectory(_directory);
        var name = Path.GetFileName(sourceFile);
        // yyyyMMddHHmmssfff sorts lexically the same as chronologically, so "newest" is a string sort;
        // the short random tail keeps two applies in the same millisecond from colliding.
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        var tail = Guid.NewGuid().ToString("N")[..6];
        File.Copy(sourceFile, Path.Combine(_directory, $"{name}.{stamp}.{tail}{Suffix}"), overwrite: false);
    }

    /// <summary>Whether any backup exists for any file this emulator manages.</summary>
    public bool HasAny() =>
        Directory.Exists(_directory) && Directory.EnumerateFiles(_directory, $"*{Suffix}").Any();

    /// <summary>The newest backup path for one file name, or null when none was ever taken.</summary>
    public string? NewestBackup(string fileName)
    {
        if (!Directory.Exists(_directory))
            return null;

        return Directory.EnumerateFiles(_directory, $"{fileName}.*{Suffix}")
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .LastOrDefault();
    }
}
