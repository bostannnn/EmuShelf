namespace EmuShelf.Infrastructure.Emulators;

/// <summary>One downloadable file attached to an emulator's GitHub release.</summary>
public sealed record GitHubEmulatorReleaseAsset(string Name, string DownloadUrl, long SizeBytes);

/// <summary>
/// A parsed emulator GitHub release. Unlike the app self-updater, the tag is treated as an opaque string:
/// emulator release tags are frequently not semantic versions (DuckStation's rolling <c>latest</c>, RPCS3's
/// commit-hash tags), so "is there a newer build" is decided by tag inequality, not version comparison.
/// </summary>
public sealed record GitHubEmulatorRelease(
    string Tag,
    string? Name,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<GitHubEmulatorReleaseAsset> Assets);
