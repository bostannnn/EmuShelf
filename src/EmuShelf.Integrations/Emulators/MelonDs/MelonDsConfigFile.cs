using System.Text;

namespace EmuShelf.Integrations.Emulators.MelonDs;

/// <summary>
/// The two path settings EmuShelf reads out of melonDS's configuration, and where they came from.
/// </summary>
/// <param name="Path">The configuration file that was read.</param>
/// <param name="SaveFilePath">
/// melonDS's <c>SaveFilePath</c> — the folder it writes <c>&lt;game&gt;.sav</c> into — or null when it
/// is unset, which is melonDS's default and means "beside the ROM".
/// </param>
/// <param name="SavestatePath">Its <c>SavestatePath</c> (<c>&lt;game&gt;.ml0</c>…<c>.ml9</c>), or null when unset.</param>
public sealed record MelonDsConfiguration(string Path, string? SaveFilePath, string? SavestatePath);

/// <summary>
/// A tolerant, read-only reader for melonDS's configuration file. melonDS 1.0 keeps it in
/// <c>melonDS.toml</c>, where the per-instance settings — <c>SaveFilePath</c> and
/// <c>SavestatePath</c> among them — live under <c>[Instance0]</c>; older builds keep the same keys
/// flat in <c>melonDS.ini</c>, which the newer builds still import.
/// </summary>
/// <remarks>
/// Deliberately tolerant, unlike <see cref="EmulatorIniFile"/>: melonDS's TOML holds arrays, nested
/// tables, and per-instance duplicates that a strict flat-INI parse would reject, and rejecting the
/// whole file would silently turn save sync off for a correctly configured emulator. Only the two
/// keys below are extracted, and anything unparseable is reported as unset — the same state as a
/// melonDS that was never given a save folder.
/// </remarks>
public static class MelonDsConfigFile
{
    /// <summary>melonDS 1.0's configuration file name.</summary>
    public const string FileName = "melonDS.toml";

    /// <summary>The pre-1.0 configuration file name, still imported by newer builds.</summary>
    public const string LegacyFileName = "melonDS.ini";

    private const string SaveFilePathKey = "SaveFilePath";
    private const string SavestatePathKey = "SavestatePath";

    // Per-instance settings live under [Instance0] for the first (and, for a normal single-window
    // session, only) instance. A key at the file root is honored too: the legacy INI is flat, and
    // melonDS's own -1 "no instance" table is the root.
    private const string InstanceTable = "Instance0";

    /// <summary>
    /// Reads the first configuration file that exists in <paramref name="configDirectory"/>, or null
    /// when neither is there or it cannot be read.
    /// </summary>
    public static MelonDsConfiguration? TryRead(
        string configDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configDirectory))
            return null;

        foreach (var fileName in new[] { FileName, LegacyFileName })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(configDirectory, fileName);
            if (!File.Exists(path))
                continue;
            if (TryReadFile(path, cancellationToken) is { } configuration)
                return configuration;
        }

        return null;
    }

    /// <summary>Reads one configuration file, or null when it cannot be read.</summary>
    public static MelonDsConfiguration? TryReadFile(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return Parse(path, reader, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static MelonDsConfiguration Parse(string path, TextReader reader, CancellationToken cancellationToken)
    {
        string? saveFilePath = null;
        string? savestatePath = null;
        var table = string.Empty;
        // Only TOML has comments. melonDS's own legacy INI reader takes everything after "=" verbatim,
        // so a folder called "Rock #1" must survive here rather than being cut at the hash.
        var isToml = path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase);

        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] is '#' or ';')
                continue;

            if (trimmed[0] == '[')
            {
                var close = trimmed.IndexOf(']');
                table = close > 1 ? trimmed[1..close].Trim() : string.Empty;
                continue;
            }

            // Only the root and the first instance's table are read; a second instance's overrides
            // ([Instance1] …) belong to a window EmuShelf never launches.
            if (table.Length != 0 && !string.Equals(table, InstanceTable, StringComparison.OrdinalIgnoreCase))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;
            var key = trimmed[..separator].Trim();
            if (!key.Equals(SaveFilePathKey, StringComparison.OrdinalIgnoreCase) &&
                !key.Equals(SavestatePathKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = ReadStringValue(trimmed[(separator + 1)..], isToml);
            if (value is null)
                continue;

            // [Instance0] wins over a root-level key of the same name: melonDS writes the live value
            // there and only leaves a root one behind from an imported legacy config.
            var fromInstance = table.Length != 0;
            if (key.Equals(SaveFilePathKey, StringComparison.OrdinalIgnoreCase))
            {
                if (fromInstance || saveFilePath is null)
                    saveFilePath = value;
            }
            else if (fromInstance || savestatePath is null)
            {
                savestatePath = value;
            }
        }

        return new MelonDsConfiguration(path, NullIfEmpty(saveFilePath), NullIfEmpty(savestatePath));
    }

    // A TOML basic string ("…", with backslash escapes), a TOML literal string ('…', verbatim), or
    // the bare unquoted value the legacy INI holds. Returns null for a value shape this reader does
    // not understand (an array, an inline table), which is reported as unset.
    private static string? ReadStringValue(string raw, bool isToml)
    {
        var value = isToml ? StripComment(raw.Trim()) : raw.Trim();
        if (value.Length == 0)
            return string.Empty;

        if (value[0] == '\'')
        {
            var end = value.IndexOf('\'', 1);
            return end < 0 ? null : value[1..end];
        }

        if (value[0] != '"')
            return value[0] is '[' or '{' ? null : value;

        var builder = new StringBuilder();
        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '"')
                return builder.ToString();
            if (character != '\\')
            {
                builder.Append(character);
                continue;
            }

            if (++index >= value.Length)
                return null;
            // The escapes a Windows path and a normal folder name can produce. An unknown escape is
            // kept verbatim rather than dropped, so an unusual path still points somewhere real.
            builder.Append(value[index] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '"' => '"',
                '\\' => '\\',
                var other => other,
            });
        }

        return null;
    }

    // Trims a trailing TOML comment from an unquoted value. Only a hash that follows whitespace starts
    // one here: melonDS never writes an unquoted path, so a bare hash is far more likely to be part of
    // a folder name ("Rock #1") than a comment marker, and cutting there would resolve a real folder
    // to a wrong one.
    private static string StripComment(string value)
    {
        if (value.Length == 0 || value[0] is '"' or '\'')
            return value;
        var hash = value.IndexOf(" #", StringComparison.Ordinal);
        if (hash < 0)
            hash = value.IndexOf("\t#", StringComparison.Ordinal);
        return hash < 0 ? value : value[..hash].TrimEnd();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
