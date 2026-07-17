using System.Text;
using System.Text.RegularExpressions;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Integrations.Metadata;

/// <summary>
/// Finds PlayStation product codes by reading the disc's SYSTEM.CNF boot record directly.
/// Descriptor and playlist files are followed read-only. When a disc has no readable
/// layout, a bounded early-exit scan of the early data is used, then a filename fallback.
/// Compressed containers fall back to an explicit serial in their filename.
/// </summary>
public sealed partial class PlayStationIdentifierExtractor : IGameIdentifierExtractor
{
    // A disc with no readable ISO9660 layout is scanned only up to this bound, and the
    // scan stops at the first product code. Real discs are read via SYSTEM.CNF instead.
    private const int MaximumFallbackBytes = 16 * 1024 * 1024;
    private const int ChunkSize = 128 * 1024;

    private static readonly string[] CompressedExtensions =
        [".chd", ".cso", ".zso", ".pbp"];

    public IReadOnlyList<GameIdentifier> Extract(Game game)
    {
        var results = new List<GameIdentifier>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var disc in ResolveDiscEntries(game.Path))
        {
            foreach (var serial in ReadSerialsForDisc(disc))
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
            foreach (var path in ResolveDiscEntries(game.Path).Prepend(game.Path))
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

    /// <summary>A single library entry resolves to one or more disc images (via M3U).</summary>
    private static IReadOnlyList<string> ResolveDiscEntries(string entryPath) =>
        Path.GetExtension(entryPath).Equals(".m3u", StringComparison.OrdinalIgnoreCase)
            ? ReferencedFileParser.ParseM3u(entryPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [entryPath];

    /// <summary>The raw data files backing a disc image, used only for the bounded fallback.</summary>
    private static IReadOnlyList<string> ResolveDataFiles(string discPath) =>
        Path.GetExtension(discPath).Equals(".cue", StringComparison.OrdinalIgnoreCase)
            ? ReferencedFileParser.ParseCue(discPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [discPath];

    private static IReadOnlyList<string> ReadSerialsForDisc(string discPath)
    {
        var targeted = PlayStationDiscSerialReader.TryReadSerial(discPath)
            ?? PbpSerialReader.TryReadSerial(discPath);
        if (targeted is not null)
            return [targeted];

        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataPath in ResolveDataFiles(discPath))
        {
            foreach (var serial in BoundedScanForSerials(dataPath))
            {
                if (seen.Add(serial))
                    found.Add(serial);
            }
        }
        return found;
    }

    private static IReadOnlyList<string> BoundedScanForSerials(string path)
    {
        var extension = Path.GetExtension(path);
        foreach (var compressed in CompressedExtensions)
        {
            if (extension.Equals(compressed, StringComparison.OrdinalIgnoreCase))
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

            while (inspected < MaximumFallbackBytes)
            {
                var requested = (int)Math.Min(ChunkSize, MaximumFallbackBytes - inspected);
                var read = stream.Read(buffer, overlap, requested);
                if (read == 0)
                    break;

                var text = Encoding.ASCII.GetString(buffer, 0, overlap + read);
                var match = ProductCodeRegex().Match(text);
                if (match.Success)
                    return [Normalize(match)];

                var total = overlap + read;
                overlap = Math.Min(64, total);
                Buffer.BlockCopy(buffer, total - overlap, buffer, 0, overlap);
                inspected += read;
            }
            return [];
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
