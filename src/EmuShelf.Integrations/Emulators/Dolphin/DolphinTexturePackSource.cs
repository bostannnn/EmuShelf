using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Integrations.Emulators.Dolphin;

/// <summary>Inventories Dolphin's direct-ID, region-free, marker-file, and shared pack layouts.</summary>
public sealed class DolphinTexturePackSource : TexturePackFileSystemSource
{
    public DolphinTexturePackSource(string installationId, string texturesDirectory)
        : base(DolphinDefinition.Instance.Id, installationId, texturesDirectory)
    {
    }

    protected override IReadOnlyList<TexturePackInventoryEntry> ScanEntries(CancellationToken cancellationToken) =>
        GetImmediateDirectories(RootDirectory)
            .Select(directory => ScanPack(directory, cancellationToken))
            .ToArray();

    private static TexturePackInventoryEntry ScanPack(string directory, CancellationToken cancellationToken)
    {
        var packKey = Path.GetFileName(directory);
        var matchKeys = new HashSet<TexturePackMatchKey>();
        if (IsUpperAsciiGameId(packKey, 6))
        {
            matchKeys.Add(new TexturePackMatchKey(
                TexturePackMatchRule.DolphinDirectoryExact,
                packKey));
        }
        else if (IsUpperAsciiGameId(packKey, 3))
        {
            matchKeys.Add(new TexturePackMatchKey(
                TexturePackMatchRule.DolphinDirectoryPrefix,
                packKey));
        }

        try
        {
            foreach (var file in EnumerateFilesRecursively(directory, "*.txt"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Path.GetExtension(file).Equals(".txt", StringComparison.Ordinal))
                    continue;
                var marker = Path.GetFileNameWithoutExtension(file);
                if (marker.Equals("all", StringComparison.Ordinal))
                {
                    matchKeys.Add(new TexturePackMatchKey(TexturePackMatchRule.DolphinShared, "all"));
                }
                else if (IsUpperAsciiGameId(marker, 6))
                {
                    matchKeys.Add(new TexturePackMatchKey(TexturePackMatchRule.DolphinMarkerExact, marker));
                }
                else if (IsUpperAsciiGameId(marker, 3))
                {
                    matchKeys.Add(new TexturePackMatchKey(TexturePackMatchRule.DolphinMarkerPrefix, marker));
                }
            }

            var hasReplacement = false;
            foreach (var file in EnumerateFilesRecursively(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (HasExtension(file, ".png", ".dds") &&
                    Path.GetFileName(file).StartsWith("tex1_", StringComparison.Ordinal))
                {
                    hasReplacement = true;
                    break;
                }
            }

            if (matchKeys.Count == 0)
            {
                return new TexturePackInventoryEntry(
                    packKey,
                    directory,
                    TexturePackContentStatus.UnrecognizedLayout,
                    [],
                    "No direct Dolphin game ID or supported game-ID marker was found.");
            }

            return new TexturePackInventoryEntry(
                packKey,
                directory,
                hasReplacement ? TexturePackContentStatus.Usable : TexturePackContentStatus.EmptyOrDumpsOnly,
                matchKeys.OrderBy(key => key.Rule).ThenBy(key => key.Value, StringComparer.Ordinal).ToArray(),
                hasReplacement ? null : "No tex1_ PNG or DDS replacements were found.");
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
                matchKeys.ToArray(),
                ex.Message);
        }
    }

    private static bool IsAsciiGameId(string value, int length) =>
        value.Length == length && value.All(char.IsAsciiLetterOrDigit);

    // Marker basenames are compared as strings by Dolphin, so lower-case IDs are not aliases for
    // the upper-case game ID even on a case-insensitive filesystem.
    private static bool IsUpperAsciiGameId(string value, int length) =>
        IsAsciiGameId(value, length) && value.Equals(value.ToUpperInvariant(), StringComparison.Ordinal);
}
