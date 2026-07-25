using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>
/// Filesystem implementation for provider-resolved save units. It operates only at locations an
/// <see cref="ISaveLocationProvider"/> explicitly allow-lists and writes conflict copies only
/// below EmuShelf's portable <c>Saves</c> directory.
/// </summary>
public sealed class FileSystemLocalSaveEndpoint : ILocalSaveEndpoint
{
    private static readonly DateTimeOffset ZipTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ISaveLocationProvider _provider;
    private readonly string _conflictsDirectory;
    private readonly string _transfersDirectory;

    public FileSystemLocalSaveEndpoint(ISaveLocationProvider provider, IAppPaths appPaths)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(appPaths);
        _provider = provider;
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

            var files = EnumerateAllFolderFiles(location.Path, cancellationToken).ToList();
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
        Directory.CreateDirectory(Path.GetDirectoryName(location.Path)!);
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

        var parentDirectory = Path.GetDirectoryName(location.Path)!;
        var temporaryDirectory = Path.Combine(parentDirectory, "_emushelf-incoming-" + Guid.NewGuid().ToString("N"));
        var displacedDirectory = Path.Combine(parentDirectory, "_emushelf-previous-" + Guid.NewGuid().ToString("N"));
        var displaced = false;
        var installed = false;
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            ExtractArchive(content, temporaryDirectory, cancellationToken);
            foreach (var file in Directory.EnumerateFiles(temporaryDirectory, "*", SearchOption.AllDirectories))
                File.SetLastWriteTimeUtc(file, modifiedUtc.UtcDateTime);

            // Replace rather than merge so a file absent from the winning remote folder is not
            // resurrected. Keep the live folder beside the incoming one until the final move
            // succeeds, allowing an immediate rollback if installation fails.
            if (Directory.Exists(location.Path))
            {
                Directory.Move(location.Path, displacedDirectory);
                displaced = true;
            }
            Directory.Move(temporaryDirectory, location.Path);
            installed = true;
            if (displaced)
                Directory.Delete(displacedDirectory, recursive: true);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
                Directory.Delete(temporaryDirectory, recursive: true);
            if (!installed && displaced && Directory.Exists(displacedDirectory) && !Directory.Exists(location.Path))
                Directory.Move(displacedDirectory, location.Path);
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
        var approved = _provider.ResolveUnit(unitId) ??
            throw new ArgumentException(
                $"The save provider cannot safely materialize unit '{unitId}' in its active configuration.",
                nameof(unitId));
        if (approved.Kind is not (SaveUnitKind.File or SaveUnitKind.Folder))
            throw new ArgumentException("The save unit kind is not supported by the filesystem endpoint.", nameof(unitId));

        var root = Path.GetFullPath(approved.RootPath);
        var path = Path.GetFullPath(approved.Path);
        if (!IsUnderRoot(path, root))
            throw new ArgumentException("The provider resolved the save unit outside its approved root.", nameof(unitId));
        EnsureNoLinkedPathBelowRoot(path, root);

        return new ResolvedUnit(path, approved.Kind == SaveUnitKind.Folder);
    }

    private string CreateBackupDirectory(string unitId)
    {
        var unitPath = Path.Combine(unitId.Split('/', StringSplitOptions.RemoveEmptyEntries));
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ");
        var path = Path.Combine(_conflictsDirectory, unitPath, timestamp);
        Directory.CreateDirectory(path);
        return path;
    }

    private static IEnumerable<string> EnumerateAllFolderFiles(string root, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("A save folder contains a symbolic link or reparse point.");
                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
                else
                    files.Add(entry);
            }
        }

        return files.OrderBy(path => ToRelativePath(root, path), StringComparer.Ordinal);
    }

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
        foreach (var file in EnumerateAllFolderFiles(source, cancellationToken))
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

    private static bool IsUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var trimmedRoot = Path.TrimEndingDirectorySeparator(root);
        return path.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static void EnsureNoLinkedPathBelowRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        for (var current = path;
             !string.Equals(Path.TrimEndingDirectorySeparator(current), Path.TrimEndingDirectorySeparator(root), comparison);
             current = Path.GetDirectoryName(current) ?? root)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("A save unit resolves through a symbolic link or reparse point.");
            }
        }
    }

    private readonly record struct ResolvedUnit(string Path, bool IsFolder);
}
