using System.Text;

namespace EmuShelf.Integrations.Emulators;

/// <summary>
/// A line-preserving editor for the flat, sectioned config files these emulators keep their hotkeys
/// in. Unlike <see cref="EmulatorIniFile"/> — which parses into a dictionary and throws structure
/// away — this keeps every original line verbatim and edits only the specific key lines it is asked
/// to, so comments, ordering, unknown keys, blank lines, and the file's newline style survive a write
/// untouched. That is what makes writing into an emulator's own config defensible: EmuShelf changes
/// exactly the bindings it manages and nothing else.
/// </summary>
/// <remarks>
/// A <c>null</c> section addresses the whole file, for section-less configs such as RetroArch's
/// <c>retroarch.cfg</c>. Keys are matched case-insensitively (matching the emulators' own tolerant
/// readers); the value that follows the first <c>=</c> is the only thing a replace changes.
/// </remarks>
public sealed class EmulatorConfigDocument
{
    private readonly List<string> _lines;
    private readonly string _newline;
    private readonly bool _trailingNewline;

    public EmulatorConfigDocument(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        _trailingNewline = text.Length > 0 && text[^1] == '\n';
        _lines = SplitLines(text);
    }

    /// <summary>True once any operation actually altered the document.</summary>
    public bool Changed { get; private set; }

    /// <summary>The first value for a key, or null when the key is absent.</summary>
    public string? GetValue(string? section, string key)
    {
        foreach (var index in KeyLines(section, key))
            return ValueOf(_lines[index]);
        return null;
    }

    /// <summary>The keys within a section whose value equals <paramref name="value"/> exactly.</summary>
    public IReadOnlyList<string> KeysWithValue(string? section, string value)
    {
        var (start, end) = SectionRange(section);
        if (start < 0)
            return [];

        var keys = new List<string>();
        for (var i = start; i < end; i++)
        {
            if (TryParseKey(_lines[i], out var key) &&
                string.Equals(ValueOf(_lines[i]), value, StringComparison.Ordinal))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    /// <summary>
    /// Sets a key to a value, replacing the first existing occurrence in place (preserving that
    /// line's key text and spacing around <c>=</c>) or inserting a new line at the end of the section.
    /// A missing section is created when <paramref name="createSection"/> is set. Returns true when the
    /// document changed.
    /// </summary>
    public bool SetValue(string? section, string key, string value, bool createSection = true)
    {
        var lineIndex = KeyLines(section, key).Cast<int?>().FirstOrDefault();
        if (lineIndex is { } existing)
        {
            var replacement = ReplaceValue(_lines[existing], value);
            if (string.Equals(replacement, _lines[existing], StringComparison.Ordinal))
                return false;
            _lines[existing] = replacement;
            Changed = true;
            return true;
        }

        var (start, end) = SectionRange(section);
        if (start < 0)
        {
            if (section is null || !createSection)
                return false;
            AppendSection(section, key, value);
            Changed = true;
            return true;
        }

        // Insert after the last non-blank line of the section, so a new binding sits with the others
        // rather than after a trailing gap before the next section.
        var insertAt = end;
        while (insertAt > start && _lines[insertAt - 1].Trim().Length == 0)
            insertAt--;
        _lines.Insert(insertAt, $"{key} = {value}");
        Changed = true;
        return true;
    }

    /// <summary>Removes every line for a key within a section. Returns true when something was removed.</summary>
    public bool RemoveKey(string? section, string key)
    {
        var indices = KeyLines(section, key).ToList();
        if (indices.Count == 0)
            return false;

        for (var i = indices.Count - 1; i >= 0; i--)
            _lines.RemoveAt(indices[i]);
        Changed = true;
        return true;
    }

    /// <summary>Whether a section header exists.</summary>
    public bool HasSection(string section) => SectionHeaderIndex(section) >= 0;

    /// <summary>The document as text, in its original newline style and trailing-newline state.</summary>
    public string ToText()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < _lines.Count; i++)
        {
            builder.Append(_lines[i]);
            if (i < _lines.Count - 1 || _trailingNewline)
                builder.Append(_newline);
        }

        return builder.ToString();
    }

    private void AppendSection(string section, string key, string value)
    {
        if (_lines.Count > 0 && _lines[^1].Trim().Length != 0)
            _lines.Add(string.Empty);
        _lines.Add($"[{section}]");
        _lines.Add($"{key} = {value}");
    }

    private IEnumerable<int> KeyLines(string? section, string key)
    {
        var (start, end) = SectionRange(section);
        if (start < 0)
            yield break;

        for (var i = start; i < end; i++)
        {
            if (TryParseKey(_lines[i], out var found) &&
                string.Equals(found, key, StringComparison.OrdinalIgnoreCase))
            {
                yield return i;
            }
        }
    }

    /// <summary>
    /// The half-open line range holding a section's body. For the whole file (<paramref name="section"/>
    /// null) that is every line; for a named section it is the lines after its header up to the next
    /// header or end of file. Returns (-1, -1) when a named section is absent.
    /// </summary>
    private (int Start, int End) SectionRange(string? section)
    {
        if (section is null)
            return (0, _lines.Count);

        var header = SectionHeaderIndex(section);
        if (header < 0)
            return (-1, -1);

        var end = header + 1;
        while (end < _lines.Count && !IsSectionHeader(_lines[end], out _))
            end++;
        return (header + 1, end);
    }

    private int SectionHeaderIndex(string section)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            if (IsSectionHeader(_lines[i], out var name) &&
                string.Equals(name, section, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSectionHeader(string line, out string name)
    {
        name = string.Empty;
        var trimmed = line.Trim();
        if (trimmed.Length <= 2 || trimmed[0] != '[' || trimmed[^1] != ']')
            return false;
        name = trimmed[1..^1];
        return true;
    }

    private static bool TryParseKey(string line, out string key)
    {
        key = string.Empty;
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed[0] is ';' or '#' || IsSectionHeader(trimmed, out _))
            return false;
        var equals = line.IndexOf('=');
        if (equals <= 0)
            return false;
        key = line[..equals].Trim();
        return key.Length != 0;
    }

    private static string ValueOf(string line)
    {
        var equals = line.IndexOf('=');
        return equals < 0 ? string.Empty : line[(equals + 1)..].Trim();
    }

    /// <summary>
    /// Rewrites a line's value while keeping its key text and the exact whitespace around <c>=</c>, so
    /// a replace produces a minimal diff against the emulator's own serializer.
    /// </summary>
    private static string ReplaceValue(string line, string value)
    {
        var equals = line.IndexOf('=');
        var before = line[..(equals + 1)];
        var after = line[(equals + 1)..];
        var lead = 0;
        while (lead < after.Length && (after[lead] == ' ' || after[lead] == '\t'))
            lead++;
        return before + after[..lead] + value;
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;
            lines.Add(TrimCarriageReturn(text, start, i));
            start = i + 1;
        }

        if (start < text.Length)
            lines.Add(TrimCarriageReturn(text, start, text.Length));
        else if (text.Length == 0)
            return lines;

        return lines;
    }

    private static string TrimCarriageReturn(string text, int start, int endExclusive)
    {
        var length = endExclusive - start;
        if (length > 0 && text[endExclusive - 1] == '\r')
            length--;
        return text.Substring(start, length);
    }
}
