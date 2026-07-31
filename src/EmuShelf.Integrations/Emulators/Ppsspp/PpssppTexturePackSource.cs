using System.IO.Compression;
using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Integrations.Emulators.Ppsspp;

/// <summary>Inventories PPSSPP's hyphenless game-ID directories and supported textures.zip packs.</summary>
public sealed class PpssppTexturePackSource : TexturePackFileSystemSource
{
    public PpssppTexturePackSource(string installationId, string texturesDirectory)
        : base(PpssppDefinition.Instance.Id, installationId, texturesDirectory)
    {
    }

    public static PpssppTexturePackSource FromMemoryStick(
        string installationId,
        string memoryStickDirectory) =>
        new(installationId, Path.Combine(memoryStickDirectory, "PSP", "TEXTURES"));

    protected override IReadOnlyList<TexturePackInventoryEntry> ScanEntries(CancellationToken cancellationToken) =>
        GetImmediateDirectories(RootDirectory)
            .Select(directory => ScanPack(directory, cancellationToken))
            .ToArray();

    private static TexturePackInventoryEntry ScanPack(string directory, CancellationToken cancellationToken)
    {
        var packKey = Path.GetFileName(directory);
        var matchKeys = IsPotentialGameId(packKey) && IsUppercase(packKey)
            ? new[] { new TexturePackMatchKey(TexturePackMatchRule.PspGameId, packKey) }
            : [];

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (matchKeys.Length == 0)
            {
                return new TexturePackInventoryEntry(
                    packKey,
                    directory,
                    TexturePackContentStatus.UnrecognizedLayout,
                    [],
                    "The directory name is not an uppercase PPSSPP game ID.");
            }

            var hasLooseReplacement = false;
            foreach (var file in EnumerateFilesRecursively(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsUnderNewDirectory(directory, file) ||
                    !HasExtension(file, ".png", ".dds", ".ktx2"))
                {
                    continue;
                }

                hasLooseReplacement = true;
                break;
            }

            var zipPath = Path.Combine(directory, "textures.zip");
            var hasUsableZip = false;
            var invalidZip = false;
            if (File.Exists(zipPath))
            {
                try
                {
                    using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
                    var hasRootConfiguration = archive.Entries.Any(entry =>
                        !entry.FullName.Contains('/') && !entry.FullName.Contains('\\') &&
                        entry.Name.Equals("textures.ini", StringComparison.Ordinal));
                    hasUsableZip = hasRootConfiguration && archive.Entries.Any(entry =>
                        !IsArchiveEntryUnderNew(entry.FullName) &&
                        HasExtension(entry.Name, ".png", ".dds", ".ktx2"));
                }
                catch (InvalidDataException)
                {
                    invalidZip = true;
                }
            }

            var usable = hasLooseReplacement || hasUsableZip;
            return new TexturePackInventoryEntry(
                packKey,
                directory,
                usable
                    ? TexturePackContentStatus.Usable
                    : invalidZip
                        ? TexturePackContentStatus.UnrecognizedLayout
                        : TexturePackContentStatus.EmptyOrDumpsOnly,
                matchKeys,
                usable
                    ? null
                    : invalidZip
                        ? "textures.zip is not a readable PPSSPP texture archive."
                        : "Only dumps or no PPSSPP replacement content was found.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new TexturePackInventoryEntry(
                packKey,
                directory,
                TexturePackContentStatus.Unreadable,
                matchKeys,
                ex.Message);
        }
    }

    private static bool IsPotentialGameId(string value) =>
        value.Length is >= 4 and <= 16 && value.All(char.IsAsciiLetterOrDigit);

    private static bool IsUppercase(string value) =>
        value.Equals(value.ToUpperInvariant(), StringComparison.Ordinal);

    private static bool IsUnderNewDirectory(string root, string path) =>
        IsArchiveEntryUnderNew(Path.GetRelativePath(root, path));

    private static bool IsArchiveEntryUnderNew(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var firstSegment = normalized.Split('/', 2)[0];
        return firstSegment.Equals("new", StringComparison.OrdinalIgnoreCase);
    }
}
