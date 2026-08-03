using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Integrations.Emulators.Azahar;

/// <summary>
/// Inventories Azahar's <c>load/textures/&lt;title id&gt;</c> custom-texture folders. Each immediate
/// subdirectory named by a 16-hex title id is one pack keyed on that title id; a folder holding at
/// least one replacement image (<c>.png</c>/<c>.dds</c>/<c>.ktx2</c>) is usable. Dumps live under a
/// separate <c>dump/textures</c> tree and are never scanned here.
/// </summary>
public sealed class AzaharTexturePackSource : TexturePackFileSystemSource
{
    public AzaharTexturePackSource(string installationId, string texturesDirectory)
        : base(AzaharDefinition.Instance.Id, installationId, texturesDirectory)
    {
    }

    public static AzaharTexturePackSource FromUserDirectory(string installationId, string userDirectory) =>
        new(installationId, Path.Combine(userDirectory, "load", "textures"));

    protected override IReadOnlyList<TexturePackInventoryEntry> ScanEntries(CancellationToken cancellationToken) =>
        GetImmediateDirectories(RootDirectory)
            .Select(directory => ScanPack(directory, cancellationToken))
            .ToArray();

    private static TexturePackInventoryEntry ScanPack(string directory, CancellationToken cancellationToken)
    {
        var packKey = Path.GetFileName(directory);
        var matchKeys = IsTitleId(packKey)
            ? new[] { new TexturePackMatchKey(TexturePackMatchRule.Nintendo3dsTitleId, packKey.ToUpperInvariant()) }
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
                    "The directory name is not a 16-character 3DS title id.");
            }

            var hasReplacement = false;
            foreach (var file in EnumerateFilesRecursively(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (HasExtension(file, ".png", ".dds", ".ktx2"))
                {
                    hasReplacement = true;
                    break;
                }
            }

            return new TexturePackInventoryEntry(
                packKey,
                directory,
                hasReplacement ? TexturePackContentStatus.Usable : TexturePackContentStatus.EmptyOrDumpsOnly,
                matchKeys,
                hasReplacement ? null : "No custom texture images were found.");
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

    private static bool IsTitleId(string value)
    {
        if (value.Length != 16)
            return false;
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }
        return true;
    }
}
