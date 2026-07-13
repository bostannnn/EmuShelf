using System.Text;

namespace EmuShelf.Core.Launching;

/// <summary>
/// Expands the four documented placeholders into an argument array. This is deliberately
/// a small template language, not a command line or shell interpreter.
/// </summary>
public static class ArgumentTemplate
{
    public static IReadOnlyList<string> Expand(
        string template,
        string gamePath,
        string emulatorPath)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(emulatorPath);

        var emulatorDirectory = Path.GetDirectoryName(emulatorPath)
            ?? throw new FormatException("The emulator executable has no parent directory.");
        var normalizedGamePath = gamePath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var gameDirectory = Directory.Exists(gamePath)
            ? gamePath
            : Path.GetDirectoryName(gamePath) ?? string.Empty;
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GamePath"] = gamePath,
            ["GameDirectory"] = gameDirectory,
            ["GameFileName"] = Path.GetFileName(normalizedGamePath),
            ["EmulatorDirectory"] = emulatorDirectory,
        };

        return Tokenize(template)
            .Select(token => ExpandToken(token, replacements))
            .ToArray();
    }

    private static IReadOnlyList<string> Tokenize(string template)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var tokenStarted = false;

        foreach (var character in template)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                tokenStarted = true;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (tokenStarted)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    tokenStarted = false;
                }
                continue;
            }

            current.Append(character);
            tokenStarted = true;
        }

        if (inQuotes)
            throw new FormatException("Launch arguments contain an unmatched double quote.");

        if (tokenStarted)
            arguments.Add(current.ToString());

        return arguments;
    }

    private static string ExpandToken(
        string token,
        IReadOnlyDictionary<string, string> replacements)
    {
        var expanded = new StringBuilder(token.Length);
        for (var index = 0; index < token.Length; index++)
        {
            if (token[index] == '}')
                throw new FormatException("Launch arguments contain an unmatched closing brace.");

            if (token[index] != '{')
            {
                expanded.Append(token[index]);
                continue;
            }

            var closingBrace = token.IndexOf('}', index + 1);
            if (closingBrace < 0)
                throw new FormatException("Launch arguments contain an unmatched opening brace.");

            var placeholder = token[(index + 1)..closingBrace];
            if (!replacements.TryGetValue(placeholder, out var replacement))
                throw new FormatException($"Unknown launch placeholder '{{{placeholder}}}'.");

            expanded.Append(replacement);
            index = closingBrace;
        }

        return expanded.ToString();
    }
}
