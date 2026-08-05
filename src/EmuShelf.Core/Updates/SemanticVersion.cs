using System.Globalization;

namespace EmuShelf.Core.Updates;

/// <summary>
/// A minimal three-part version (major.minor.patch) parsed from a release tag such as
/// <c>v1.2.3</c>. EmuShelf releases are plain <c>vX.Y.Z</c> git tags (see the StampGitVersion target
/// in EmuShelf.App.csproj), so a tolerant parse — dropping a leading <c>v</c> and any build or
/// pre-release suffix — is all that is needed to answer "is the release newer than what is running?".
/// </summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch)
    : IComparable<SemanticVersion>
{
    /// <summary>The lowest possible version; used as the current version when a build was produced
    /// without git and its version could not be parsed, so any real release counts as newer.</summary>
    public static readonly SemanticVersion Zero = new(0, 0, 0);

    /// <summary>
    /// Parses <c>v1.2.3</c>, <c>1.2</c>, <c>1.2.3+abc123</c>, <c>1.2.3-rc1</c> and similar. Missing
    /// minor/patch default to zero. Returns false for anything without a numeric leading component.
    /// </summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var span = text.Trim();
        if (span[0] is 'v' or 'V')
            span = span[1..];

        // Anything from the first build/pre-release/whitespace separator onward is metadata we ignore.
        var cut = span.IndexOfAny(['+', '-', ' ']);
        if (cut == 0)
            return false;
        if (cut > 0)
            span = span[..cut];

        var parts = span.Split('.');
        if (parts.Length is 0 or > 3)
            return false;

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                return false;
            numbers[i] = value;
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    /// <summary>Parses <paramref name="text"/>, falling back to <see cref="Zero"/> when it cannot.</summary>
    public static SemanticVersion ParseOrZero(string? text) =>
        TryParse(text, out var version) ? version : Zero;

    public int CompareTo(SemanticVersion other)
    {
        var byMajor = Major.CompareTo(other.Major);
        if (byMajor != 0)
            return byMajor;
        var byMinor = Minor.CompareTo(other.Minor);
        return byMinor != 0 ? byMinor : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
