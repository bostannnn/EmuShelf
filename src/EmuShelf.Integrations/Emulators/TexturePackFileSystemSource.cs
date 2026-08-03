using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Integrations.Emulators;

/// <summary>Shared error boundary for read-only, installation-scoped texture directory scans.</summary>
public abstract class TexturePackFileSystemSource : ITexturePackSource
{
    private static readonly EnumerationOptions RecursiveFiles = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    protected TexturePackFileSystemSource(
        string emulatorId,
        string installationId,
        string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emulatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        EmulatorId = emulatorId;
        InstallationId = installationId;
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string EmulatorId { get; }

    public string InstallationId { get; }

    public string RootDirectory { get; }

    public Task<TexturePackInventorySnapshot> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    protected abstract IReadOnlyList<TexturePackInventoryEntry> ScanEntries(CancellationToken cancellationToken);

    protected static IEnumerable<string> EnumerateFilesRecursively(
        string directory,
        string searchPattern = "*") =>
        Directory.EnumerateFiles(directory, searchPattern, RecursiveFiles);

    protected static IReadOnlyList<string> GetImmediateDirectories(string directory) =>
        Directory.EnumerateDirectories(directory)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    protected static string? FindImmediateDirectory(string parent, string name, out bool wrongCase)
    {
        var candidates = Directory.EnumerateDirectories(parent).ToArray();
        var exact = candidates.FirstOrDefault(candidate =>
            Path.GetFileName(candidate).Equals(name, StringComparison.Ordinal));
        if (exact is not null)
        {
            wrongCase = false;
            return exact;
        }

        var insensitive = candidates.FirstOrDefault(candidate =>
            Path.GetFileName(candidate).Equals(name, StringComparison.OrdinalIgnoreCase));
        wrongCase = insensitive is not null;
        return insensitive;
    }

    protected static bool HasExtension(string path, params string[] extensions)
    {
        var extension = Path.GetExtension(path);
        return extensions.Any(candidate => extension.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private TexturePackInventorySnapshot Scan(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if ((File.GetAttributes(RootDirectory) & FileAttributes.Directory) == 0)
            {
                return new TexturePackInventorySnapshot(
                    EmulatorId,
                    InstallationId,
                    RootDirectory,
                    DateTimeOffset.UtcNow,
                    TexturePackRootStatus.Missing,
                    [],
                    "The configured texture root is not a directory.");
            }
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            return new TexturePackInventorySnapshot(
                EmulatorId,
                InstallationId,
                RootDirectory,
                DateTimeOffset.UtcNow,
                TexturePackRootStatus.Missing,
                []);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new TexturePackInventorySnapshot(
                EmulatorId,
                InstallationId,
                RootDirectory,
                DateTimeOffset.UtcNow,
                TexturePackRootStatus.Unreadable,
                [],
                ex.Message);
        }

        try
        {
            return new TexturePackInventorySnapshot(
                EmulatorId,
                InstallationId,
                RootDirectory,
                DateTimeOffset.UtcNow,
                TexturePackRootStatus.Ready,
                ScanEntries(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new TexturePackInventorySnapshot(
                EmulatorId,
                InstallationId,
                RootDirectory,
                DateTimeOffset.UtcNow,
                TexturePackRootStatus.Unreadable,
                [],
                ex.Message);
        }
    }
}
