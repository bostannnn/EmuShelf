using System.Text.RegularExpressions;
using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Integrations.Emulators.Pcsx2;

/// <summary>Inventories PCSX2's serial/replacements layout without opening image contents.</summary>
public sealed class Pcsx2TexturePackSource : TexturePackFileSystemSource
{
    private static readonly Regex ReplacementName = new(
        "^[0-9a-f]+(?:-[0-9a-f]+)?(?:-r(?:[0-9]+x[0-9]+|[0-9a-f]+))?-[0-9a-f]{8}\\.(?:png|dds)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public Pcsx2TexturePackSource(string installationId, string texturesDirectory)
        : base(Pcsx2Definition.Instance.Id, installationId, texturesDirectory)
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

            if (matchKeys.Length == 0)
            {
                return new TexturePackInventoryEntry(
                    packKey,
                    directory,
                    TexturePackContentStatus.UnrecognizedLayout,
                    [],
                    "The directory name is not an uppercase PCSX2 serial.");
            }

            if (replacements is null)
            {
                return new TexturePackInventoryEntry(
                    packKey,
                    directory,
                    TexturePackContentStatus.EmptyOrDumpsOnly,
                    matchKeys,
                    "No replacements directory was found.");
            }

            var hasReplacement = false;
            foreach (var file in EnumerateFilesRecursively(replacements))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ReplacementName.IsMatch(Path.GetFileName(file)))
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
                hasReplacement ? null : "No PCSX2-compatible replacement filenames were found.");
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

    private static bool IsPotentialSerial(string value) =>
        value.Length is >= 4 and <= 32 &&
        value.IndexOfAny(['-', '_']) >= 0 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static bool IsUppercase(string value) =>
        value.Equals(value.ToUpperInvariant(), StringComparison.Ordinal);
}
