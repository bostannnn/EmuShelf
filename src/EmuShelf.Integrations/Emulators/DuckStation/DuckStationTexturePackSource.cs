using System.Text.RegularExpressions;
using EmuShelf.Core.TexturePacks;
using YamlDotNet.RepresentationModel;

namespace EmuShelf.Integrations.Emulators.DuckStation;

/// <summary>Inventories DuckStation's serial-scoped current and legacy replacement layouts.</summary>
public sealed class DuckStationTexturePackSource : TexturePackFileSystemSource
{
    private static readonly Regex VramWriteName = new(
        "^vram-write-[0-9A-Fa-f]{32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PalettedTextureName = new(
        "^tex(?:upload|page)-(?:P4|P8|STP4|STP8)-[0-9A-Fa-f]{16}-[0-9A-Fa-f]{16}-[0-9]+x[0-9]+-[0-9]+-[0-9]+-[0-9]+x[0-9]+-P[0-9]+-[0-9]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DirectTextureName = new(
        "^tex(?:upload|page)-(?:C16|STC16)-[0-9A-Fa-f]{16}-[0-9]+x[0-9]+-[0-9]+-[0-9]+-[0-9]+x[0-9]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public DuckStationTexturePackSource(string installationId, string texturesDirectory)
        : base(DuckStationDefinition.Instance.Id, installationId, texturesDirectory)
    {
    }

    protected override IReadOnlyList<TexturePackInventoryEntry> ScanEntries(CancellationToken cancellationToken) =>
        GetImmediateDirectories(RootDirectory)
            .Select(directory => ScanPack(directory, cancellationToken))
            .ToArray();

    private static TexturePackInventoryEntry ScanPack(string directory, CancellationToken cancellationToken)
    {
        var packKey = Path.GetFileName(directory);
        var matchKeys = IsPotentialSerial(packKey) && IsUppercase(packKey)
            ? new[] { new TexturePackMatchKey(TexturePackMatchRule.ExactSerial, packKey) }
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
                    "The directory name is not an uppercase DuckStation serial.");
            }

            var replacements = FindImmediateDirectory(directory, "replacements", out var wrongCase);
            if (wrongCase)
            {
                return new TexturePackInventoryEntry(
                    packKey,
                    directory,
                    TexturePackContentStatus.UnrecognizedLayout,
                    matchKeys,
                    "The replacements directory has the wrong case for a case-sensitive filesystem.");
            }

            // DuckStation retains a deprecated layout where replacement images are direct children
            // of the serial directory. Dumps must never make that legacy layout look installed.
            var replacementRoot = replacements ?? directory;
            var hasReplacement = false;
            foreach (var file in EnumerateFilesRecursively(replacementRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (replacements is null && IsUnderNamedDirectory(directory, file, "dumps"))
                    continue;
                if (IsRecognizedReplacement(file))
                {
                    hasReplacement = true;
                    break;
                }
            }

            hasReplacement |= HasUsableAlias(directory, replacementRoot, cancellationToken);

            return new TexturePackInventoryEntry(
                packKey,
                directory,
                hasReplacement ? TexturePackContentStatus.Usable : TexturePackContentStatus.EmptyOrDumpsOnly,
                matchKeys,
                hasReplacement ? null : "No DuckStation-compatible replacement filenames or aliases were found.");
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

    private static bool HasUsableAlias(
        string packDirectory,
        string replacementRoot,
        CancellationToken cancellationToken)
    {
        var configurationPath = Path.Combine(packDirectory, "config.yaml");
        if (!File.Exists(configurationPath))
            return false;

        try
        {
            using var stream = new FileStream(configurationPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var yaml = new YamlStream();
            yaml.Load(reader);
            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root ||
                !root.Children.TryGetValue(new YamlScalarNode("Aliases"), out var aliases) ||
                aliases is not YamlMappingNode aliasMapping)
            {
                return false;
            }

            foreach (var pair in aliasMapping.Children)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pair.Key is not YamlScalarNode key || pair.Value is not YamlScalarNode value ||
                    !IsRecognizedReplacementTitle(key.Value) || string.IsNullOrWhiteSpace(value.Value))
                    continue;
                var normalized = value.Value
                    .Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                var candidate = Path.GetFullPath(Path.Combine(replacementRoot, normalized));
                var relative = Path.GetRelativePath(replacementRoot, candidate);
                if (Path.IsPathFullyQualified(relative) || relative == ".." ||
                    relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    !File.Exists(candidate) || !HasExtension(candidate, ".png", ".jpg", ".webp"))
                {
                    continue;
                }

                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsRecognizedReplacement(string file)
    {
        if (!HasExtension(file, ".png", ".jpg", ".webp"))
            return false;
        return IsRecognizedReplacementTitle(Path.GetFileNameWithoutExtension(file));
    }

    private static bool IsRecognizedReplacementTitle(string? title) =>
        title is not null &&
        (VramWriteName.IsMatch(title) || PalettedTextureName.IsMatch(title) || DirectTextureName.IsMatch(title));

    private static bool IsUnderNamedDirectory(string root, string path, string directoryName)
    {
        var relative = Path.GetRelativePath(root, path);
        var firstSeparator = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var firstSegment = firstSeparator < 0 ? relative : relative[..firstSeparator];
        return firstSegment.Equals(directoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPotentialSerial(string value) =>
        value.Length is >= 4 and <= 32 &&
        value.IndexOfAny(['-', '_']) >= 0 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool IsUppercase(string value) =>
        value.Equals(value.ToUpperInvariant(), StringComparison.Ordinal);
}
