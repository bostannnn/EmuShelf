using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>
/// Filesystem implementation for PCSX2 save units. It operates only below the configured
/// memory-card directory and writes conflict copies only below EmuShelf's portable <c>Saves</c>
/// directory.
/// </summary>
public sealed class FileSystemLocalSaveEndpoint : ILocalSaveEndpoint
{
    private const string Pcsx2Prefix = "pcsx2/";
    private static readonly DateTimeOffset ZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string _memoryCardsDirectory;
    private readonly string _conflictsDirectory;
    private readonly string _transfersDirectory;

    public FileSystemLocalSaveEndpoint(string memoryCardsDirectory, IAppPaths appPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryCardsDirectory);
        ArgumentNullException.ThrowIfNull(appPaths);
        _memoryCardsDirectory = Path.GetFullPath(memoryCardsDirectory);
        _conflictsDirectory = Path.Combine(appPaths.SavesDirectory, "conflicts");
        _transfersDirectory = Path.Combine(appPaths.SavesDirectory, "transfers");
    }

    public Task<SaveUnitSnapshot?> SnapshotAsync(string unitId, CancellationToken cancellationToken = default) =>
        Task.Run(() => Snapshot(unitId, cancellationToken), cancellationToken);

    public Task<Stream> ReadAsync(string unitId, CancellationToken cancellationToken = default) =>
        Task.Run<Stream>(() => Read(unitId, cancellationToken), cancellationToken);

    public Task WriteAsync(
        string unitId,
        Stream content,
        DateTimeOffset modifiedUtc,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Write(unitId, content, modifiedUtc, cancellationToken), cancellationToken);

    public Task BackupLocalAsync(string unitId, string reason, CancellationToken cancellationToken = default) =>
        Task.Run(() => BackupLocal(unitId, reason, cancellationToken), cancellationToken);

    public Task BackupIncomingAsync(
        string unitId,
        Stream content,
        string reason,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => BackupIncoming(unitId, content, reason, cancellationToken), cancellationToken);

    private SaveUnitSnapshot? Snapshot(string unitId, CancellationToken cancellationToken)
    {
        var location = Resolve(unitId);
        if (location.IsFolder)
        {
            if (!Directory.Exists(location.Path))
                return null;

            var files = EnumerateFolderFiles(location.Path, cancellationToken).ToList();
            return new SaveUnitSnapshot(unitId, HashFolder(files, location.Path, cancellationToken), MaxModifiedUtc(files));
        }

        if (!File.Exists(location.Path))
            return null;

        return new SaveUnitSnapshot(
            unitId,
            HashFile(location.Path, cancellationToken),
            File.GetLastWriteTimeUtc(location.Path));
    }

    private Stream Read(string unitId, CancellationToken cancellationToken)
    {
        var location = Resolve(unitId);
        if (!location.IsFolder)
        {
            return new FileStream(location.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }

        Directory.CreateDirectory(_transfersDirectory);
        var transferPath = Path.Combine(_transfersDirectory, Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var archiveStream = new FileStream(
                transferPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var file in EnumerateAllFolderFiles(location.Path, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativePath = ToRelativePath(location.Path, file);
                    var entry = archive.CreateEntry(relativePath, CompressionLevel.Optimal);
                    entry.LastWriteTime = ZipTimestamp;
                    using var entryStream = entry.Open();
                    using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    Copy(source, entryStream, cancellationToken);
                }
            }

            return new FileStream(
                transferPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.DeleteOnClose);
        }
        catch
        {
            if (File.Exists(transferPath))
                File.Delete(transferPath);
            throw;
        }
    }

    private void Write(string unitId, Stream content, DateTimeOffset modifiedUtc, CancellationToken cancellationToken)
    {
        var location = Resolve(unitId);
        Directory.CreateDirectory(_memoryCardsDirectory);
        if (!location.IsFolder)
        {
            var temporaryPath = location.Path + ".emushelf-tmp";
            try
            {
                using (var target = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    Copy(content, target, cancellationToken);
                File.Move(temporaryPath, location.Path, overwrite: true);
                File.SetLastWriteTimeUtc(location.Path, modifiedUtc.UtcDateTime);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }

            return;
        }

        var temporaryDirectory = location.Path + ".emushelf-tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            ExtractArchive(content, temporaryDirectory, cancellationToken);
            foreach (var file in Directory.EnumerateFiles(temporaryDirectory, "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(file, modifiedUtc.UtcDateTime);

            // SaveSyncService took a portable conflict copy before every live-folder overwrite.
            // A replacement (rather than a merge) is required to avoid resurrecting a file the
            // winning remote folder no longer contains.
            if (Directory.Exists(location.Path))
                Directory.Delete(location.Path, recursive: true);
            Directory.Move(temporaryDirectory, location.Path);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private void BackupLocal(string unitId, string reason, CancellationToken cancellationToken)
    {
        var location = Resolve(unitId);
        var backupDirectory = CreateBackupDirectory(unitId);
        WriteReason(backupDirectory, reason);
        if (location.IsFolder)
        {
            var destination = Path.Combine(backupDirectory, "local");
            CopyDirectory(location.Path, destination, cancellationToken);
        }
        else
        {
            File.Copy(location.Path, Path.Combine(backupDirectory, Path.GetFileName(location.Path)), overwrite: false);
        }
    }

    private void BackupIncoming(string unitId, Stream content, string reason, CancellationToken cancellationToken)
    {
        var location = Resolve(unitId);
        var backupDirectory = CreateBackupDirectory(unitId);
        WriteReason(backupDirectory, reason);
        var payloadName = location.IsFolder ? "incoming.zip" : Path.GetFileName(location.Path);
        using var target = new FileStream(
            Path.Combine(backupDirectory, payloadName),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        Copy(content, target, cancellationToken);
    }

    private ResolvedUnit Resolve(string unitId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unitId);
        if (!unitId.StartsWith(Pcsx2Prefix, StringComparison.Ordinal))
            throw new ArgumentException("Only PCSX2 save unit ids are supported.", nameof(unitId));

        var segments = unitId[Pcsx2Prefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length is < 1 or > 2 || segments.Any(segment => segment is "." or ".." || segment.Contains('\\')))
            throw new ArgumentException("The save unit id is not a safe PCSX2 relative path.", nameof(unitId));

        var isFolder = segments.Length == 2;
        if ((!isFolder && !segments[0].EndsWith(".ps2", StringComparison.OrdinalIgnoreCase)) ||
            (isFolder && !IsGameSerial(segments[1])))
        {
            throw new ArgumentException("The save unit id is not a recognized PCSX2 card unit.", nameof(unitId));
        }

        var path = Path.GetFullPath(Path.Combine(_memoryCardsDirectory, Path.Combine(segments)));
        if (!IsUnderRoot(path, _memoryCardsDirectory))
            throw new ArgumentException("The save unit resolves outside the PCSX2 memory-card directory.", nameof(unitId));
        return new ResolvedUnit(path, isFolder);
    }

    private string CreateBackupDirectory(string unitId)
    {
        var unitPath = Path.Combine(unitId.Split('/', StringSplitOptions.RemoveEmptyEntries));
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ");
        var path = Path.Combine(_conflictsDirectory, unitPath, timestamp);
        Directory.CreateDirectory(path);
        return path;
    }

    private static IEnumerable<string> EnumerateFolderFiles(string root, CancellationToken cancellationToken) =>
        EnumerateAllFolderFiles(root, cancellationToken);

    private static IEnumerable<string> EnumerateAllFolderFiles(string root, CancellationToken cancellationToken) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => ToRelativePath(root, path), StringComparer.Ordinal)
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return path;
            });

    private static string HashFolder(IEnumerable<string> files, string root, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Encoding.UTF8.GetBytes(ToRelativePath(root, file));
            hash.AppendData(BitConverter.GetBytes(relativePath.Length));
            hash.AppendData(relativePath);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            AppendStream(hash, stream, cancellationToken);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string HashFile(string path, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        AppendStream(hash, stream, cancellationToken);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendStream(IncrementalHash hash, Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
    }

    private static void Copy(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.Write(buffer, 0, read);
        }
    }

    private static DateTimeOffset MaxModifiedUtc(IEnumerable<string> files) =>
        files.Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.UnixEpoch).Max();

    private static void ExtractArchive(Stream source, string destination, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var path = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!IsUnderRoot(path, destination))
                throw new InvalidDataException("The folder-card archive contains a path outside its destination.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            Copy(input, output, cancellationToken);
        }
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, ToRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
            File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(file));
        }
    }

    private static void WriteReason(string directory, string reason) =>
        File.WriteAllText(Path.Combine(directory, "reason.txt"), reason);

    private static string ToRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsUnderRoot(string path, string root) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
        string.Equals(path, Path.TrimEndingDirectorySeparator(root), StringComparison.Ordinal);

    private static bool IsGameSerial(string value) =>
        value.Length is >= 4 and <= 32 && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private readonly record struct ResolvedUnit(string Path, bool IsFolder);
}
