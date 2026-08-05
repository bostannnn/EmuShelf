using System.Globalization;
using System.Reflection;

namespace EmuShelf.App;

/// <summary>
/// Build identity surfaced in Settings → About and by <c>--version</c>. Everything here is stamped
/// into the entry assembly at build time by the StampGitVersion target in EmuShelf.App.csproj, so
/// this only reads reflection attributes — no file or process access at runtime.
/// </summary>
public static class AppBuildInfo
{
    private static readonly Assembly Assembly = typeof(AppBuildInfo).Assembly;

    /// <summary>Semantic version, e.g. "1.0.8" — the <c>vX.Y.Z</c> git tag the build came from.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>Short hash of the last commit included in the build, or empty when built without git
    /// (e.g. from a source tarball).</summary>
    public static string CommitHash { get; } = ReadMetadata("CommitHash");

    /// <summary>Date of that commit, or null when it was not stamped or could not be parsed.</summary>
    public static DateTimeOffset? CommitDate { get; } = ReadCommitDate();

    /// <summary>One-line "EmuShelf 1.0.8 (abc1234)" summary for the console and logs.</summary>
    public static string Summary =>
        string.IsNullOrEmpty(CommitHash) ? $"EmuShelf {Version}" : $"EmuShelf {Version} ({CommitHash})";

    private static string ReadVersion() => ParseVersion(
        Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        Assembly.GetName().Version);

    private static string ReadMetadata(string key) =>
        Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value ?? string.Empty;

    private static DateTimeOffset? ReadCommitDate() => ParseCommitDate(ReadMetadata("CommitDate"));

    /// <summary>Pull the clean semantic version out of an InformationalVersion, which the build
    /// stamps as "1.0.8+&lt;hash&gt;". Falls back to the numeric assembly version.</summary>
    internal static string ParseVersion(string? informational, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }
        return assemblyVersion?.ToString(3) ?? "0.0.0";
    }

    /// <summary>Parse the ISO 8601 commit date stamped into assembly metadata, or null.</summary>
    internal static DateTimeOffset? ParseCommitDate(string? raw) =>
        DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
