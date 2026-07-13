using System.Text;
using System.Text.RegularExpressions;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Finds PlayStation product codes in the early disc data where SYSTEM.CNF and
/// its boot executable live. Descriptor and playlist files are followed read-only.
/// Compressed containers fall back to an explicit serial in their filename.
/// </summary>
public sealed partial class PlayStationIdentifierExtractor : IGameIdentifierExtractor
{
    private const int MaximumBytesToInspect = 32 * 1024 * 1024;
    private const int ChunkSize = 128 * 1024;

    public IReadOnlyList<GameIdentifier> Extract(Game game)
    {
        var paths = ResolveContentPaths(game.Path);
        var results = new List<GameIdentifier>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            foreach (var serial in FindSerialsInContent(path))
            {
                if (seen.Add(serial))
                    results.Add(new GameIdentifier(
                        GameIdentifierKind.Serial,
                        serial,
                        "DiscContent",
                        IsPrimary: results.Count == 0));
            }
        }

        if (results.Count == 0)
        {
            foreach (var path in paths.Prepend(game.Path))
            {
                foreach (Match match in ProductCodeRegex().Matches(Path.GetFileNameWithoutExtension(path)))
                {
                    var serial = Normalize(match);
                    if (seen.Add(serial))
                        results.Add(new GameIdentifier(
                            GameIdentifierKind.Serial,
                            serial,
                            "Filename",
                            IsPrimary: results.Count == 0));
                }
            }
        }

        return results;
    }

    internal static string NormalizeProductCode(string value)
    {
        var match = ProductCodeRegex().Match(value);
        return match.Success ? Normalize(match) : value.Trim().ToUpperInvariant();
    }

    private static IReadOnlyList<string> ResolveContentPaths(string entryPath)
    {
        var extension = Path.GetExtension(entryPath).ToLowerInvariant();
        var paths = extension switch
        {
            ".m3u" => ReferencedFileParser.ParseM3u(entryPath)
                .SelectMany(ResolveContentPaths)
                .ToArray(),
            ".cue" => ReferencedFileParser.ParseCue(entryPath),
            _ => [entryPath],
        };
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> FindSerialsInContent(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".chd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cso", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                ChunkSize,
                FileOptions.SequentialScan);
            var buffer = new byte[ChunkSize + 64];
            var overlap = 0;
            var inspected = 0L;
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (inspected < MaximumBytesToInspect)
            {
                var requested = (int)Math.Min(ChunkSize, MaximumBytesToInspect - inspected);
                var read = stream.Read(buffer, overlap, requested);
                if (read == 0)
                    break;

                var text = Encoding.ASCII.GetString(buffer, 0, overlap + read);
                foreach (Match match in ProductCodeRegex().Matches(text))
                {
                    var serial = Normalize(match);
                    if (seen.Add(serial))
                        found.Add(serial);
                }

                var total = overlap + read;
                overlap = Math.Min(64, total);
                Buffer.BlockCopy(buffer, total - overlap, buffer, 0, overlap);
                inspected += read;
            }
            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    private static string Normalize(Match match) =>
        $"{match.Groups[1].Value.ToUpperInvariant()}-" +
        $"{match.Groups[2].Value}{match.Groups[3].Value}";

    [GeneratedRegex(
        @"(?<![A-Z0-9])([A-Z]{4})[\s_-]*([0-9]{3})[.\s_-]*([0-9]{2})(?![0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ProductCodeRegex();
}
