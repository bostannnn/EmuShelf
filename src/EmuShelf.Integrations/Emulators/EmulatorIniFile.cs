using System.Text;

namespace EmuShelf.Integrations.Emulators;

/// <summary>
/// A strict, read-only reader for the flat INI files these emulators keep their settings in.
/// Strict on purpose: a repeated section, a repeated key, or a line that is neither a section nor a
/// <c>key=value</c> pair means the file is not the format this adapter was written against, and
/// guessing at it is how a reader silently reports the wrong setting.
/// </summary>
public sealed class EmulatorIniFile
{
    private readonly Dictionary<string, Dictionary<string, string>> _values;

    private EmulatorIniFile(Dictionary<string, Dictionary<string, string>> values) => _values = values;

    /// <summary>Reads and parses one INI file, or returns null with a reason.</summary>
    public static EmulatorIniFile? TryRead(
        string path,
        out string? diagnostic,
        CancellationToken cancellationToken = default)
    {
        diagnostic = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return new EmulatorIniFile(Parse(reader, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or InvalidDataException)
        {
            diagnostic = ex.Message;
            return null;
        }
    }

    public bool TryGet(string section, string key, out string value)
    {
        value = string.Empty;
        if (!_values.TryGetValue(section, out var sectionValues) ||
            !sectionValues.TryGetValue(key, out var found))
        {
            return false;
        }

        value = found;
        return true;
    }

    /// <summary>
    /// Reads a boolean the way these emulators write them (<c>true</c>, <c>True</c>, <c>1</c>).
    /// An unrecognized spelling is not coerced: it returns false so the caller reports Unknown.
    /// </summary>
    public bool TryGetBoolean(string section, string key, out bool value)
    {
        value = false;
        if (!TryGet(section, key, out var raw))
            return false;

        var text = raw.Trim();
        if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1")
        {
            value = true;
            return true;
        }

        if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0")
        {
            value = false;
            return true;
        }

        return false;
    }

    public bool HasVersion(string section, string key, string supportedVersion) =>
        TryGet(section, key, out var version) && version == supportedVersion;

    internal static Dictionary<string, Dictionary<string, string>> Parse(
        TextReader reader,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        string? section = null;
        var lineNumber = 0;
        while (reader.ReadLine() is { } rawLine)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']') && line.Length > 2)
            {
                section = line[1..^1];
                if (!values.TryAdd(section, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
                    throw new InvalidDataException($"The settings file repeats section [{section}] at line {lineNumber}.");
                continue;
            }

            var equals = line.IndexOf('=');
            if (section is null || equals <= 0)
                throw new InvalidDataException($"The settings file has an unsupported line at {lineNumber}.");
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Length == 0 || !values[section].TryAdd(key, value))
                throw new InvalidDataException($"The settings file has an empty or repeated key at line {lineNumber}.");
        }

        return values;
    }
}
