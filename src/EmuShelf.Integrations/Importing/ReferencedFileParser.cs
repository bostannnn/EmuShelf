namespace EmuShelf.Integrations.Importing;

internal static class ReferencedFileParser
{
    public static IReadOnlyList<string> ParseM3u(string playlistPath) =>
        ParseLines(playlistPath, ParseM3uLine);

    public static IReadOnlyList<string> ParseCue(string cuePath) =>
        ParseLines(cuePath, ParseCueLine);

    private static IReadOnlyList<string> ParseLines(
        string descriptorPath,
        Func<string, string?> parseReference)
    {
        var references = new List<string>();
        try
        {
            var baseDirectory = Path.GetDirectoryName(Path.GetFullPath(descriptorPath));
            if (baseDirectory is null)
                return references;

            foreach (var line in File.ReadLines(descriptorPath))
            {
                var reference = parseReference(line);
                if (reference is null)
                    continue;

                var resolved = ResolveReference(baseDirectory, reference);
                if (resolved is not null)
                    references.Add(resolved);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            // A malformed or temporarily unreadable descriptor remains importable;
            // its component files simply cannot be hidden during this pass.
        }

        return references;
    }

    private static string? ParseM3uLine(string line)
    {
        var value = line.Trim().TrimStart('\uFEFF');
        if (value.Length == 0 || value.StartsWith('#'))
            return null;

        return RemoveMatchingQuotes(value);
    }

    private static string? ParseCueLine(string line)
    {
        var value = line.TrimStart();
        if (!value.StartsWith("FILE", StringComparison.OrdinalIgnoreCase) ||
            value.Length == 4 || !char.IsWhiteSpace(value[4]))
        {
            return null;
        }

        value = value[4..].TrimStart();
        if (value.Length == 0)
            return null;

        if (value[0] is '"' or '\'')
        {
            var closingQuote = value.IndexOf(value[0], 1);
            return closingQuote > 1 ? value[1..closingQuote] : null;
        }

        var separator = value.IndexOfAny([' ', '\t']);
        return separator > 0 ? value[..separator] : value;
    }

    private static string RemoveMatchingQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == value[^1] && value[0] is '"' or '\'')
            return value[1..^1];
        return value;
    }

    private static string? ResolveReference(string baseDirectory, string reference)
    {
        if (Path.IsPathRooted(reference))
            return Path.GetFullPath(reference);

        if (Uri.TryCreate(reference, UriKind.Absolute, out var uri))
            return uri.IsFile ? Path.GetFullPath(uri.LocalPath) : null;

        // Playlists commonly outlive a move between Windows and macOS. Accept
        // either slash style for relative paths on every development platform.
        var localReference = reference
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(baseDirectory, localReference));
    }
}
