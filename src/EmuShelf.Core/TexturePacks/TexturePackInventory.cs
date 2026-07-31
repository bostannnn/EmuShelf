using EmuShelf.Core.Metadata;

namespace EmuShelf.Core.TexturePacks;

/// <summary>The state of the external texture root at the time of a read-only scan.</summary>
public enum TexturePackRootStatus
{
    Unknown,
    Ready,
    Missing,
    Unreadable,
}

/// <summary>Whether a discovered pack contains content its emulator can actually load.</summary>
public enum TexturePackContentStatus
{
    Unknown,
    Usable,
    EmptyOrDumpsOnly,
    UnrecognizedLayout,
    Unreadable,
}

/// <summary>
/// The explicit emulator rule used to compare a pack key with cached game identifiers. These are
/// deliberately not fuzzy matching modes: each one mirrors a path or marker rule used at runtime.
/// </summary>
public enum TexturePackMatchRule
{
    Unknown,
    ExactSerial,
    PspGameId,
    DolphinDirectoryExact,
    DolphinDirectoryPrefix,
    DolphinMarkerExact,
    DolphinMarkerPrefix,
    DolphinShared,
}

/// <summary>One identifier or marker declared by a texture pack.</summary>
public sealed record TexturePackMatchKey(TexturePackMatchRule Rule, string Value);

/// <summary>One external texture pack discovered without changing it.</summary>
public sealed record TexturePackInventoryEntry(
    string PackKey,
    string SourcePath,
    TexturePackContentStatus ContentStatus,
    IReadOnlyList<TexturePackMatchKey> MatchKeys,
    string? Diagnostic = null)
{
    public bool IsUsable => ContentStatus == TexturePackContentStatus.Usable;
}

/// <summary>A complete, installation-scoped result from one provider scan.</summary>
public sealed record TexturePackInventorySnapshot(
    string EmulatorId,
    string InstallationId,
    string RootDirectory,
    DateTimeOffset ScannedAt,
    TexturePackRootStatus RootStatus,
    IReadOnlyList<TexturePackInventoryEntry> Entries,
    string? Diagnostic = null);

/// <summary>Read-only source of replacement-texture inventory for one emulator installation.</summary>
public interface ITexturePackSource
{
    string EmulatorId { get; }

    string InstallationId { get; }

    string RootDirectory { get; }

    Task<TexturePackInventorySnapshot> ScanAsync(CancellationToken cancellationToken = default);
}

/// <summary>Outcome of resolving one emulator installation's effective texture directory.</summary>
public enum TexturePackRootResolutionStatus
{
    Unknown,
    Resolved,
    ConfigurationMissing,
    ConfigurationUnsupported,
}

/// <summary>A proven texture root, or a visible reason why no root was selected.</summary>
public sealed record TexturePackRootResolution(
    TexturePackRootResolutionStatus Status,
    string? RootDirectory,
    string? Diagnostic = null)
{
    public bool IsResolved => Status == TexturePackRootResolutionStatus.Resolved;
}

/// <summary>Read-only, version-aware texture-root discovery for one emulator installation.</summary>
public interface ITexturePackRootResolver
{
    string EmulatorId { get; }

    string InstallationId { get; }

    Task<TexturePackRootResolution> ResolveAsync(CancellationToken cancellationToken = default);
}

/// <summary>Portable cache of the last completed inventory for each emulator installation.</summary>
public interface ITexturePackInventoryStore
{
    Task<TexturePackInventorySnapshot?> LoadAsync(
        string installationId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        TexturePackInventorySnapshot snapshot,
        CancellationToken cancellationToken = default);
}

/// <summary>Applies emulator-exact pack keys to identifiers already cached by EmuShelf.</summary>
public static class TexturePackMatcher
{
    public static IReadOnlyList<TexturePackInventoryEntry> Match(
        IEnumerable<TexturePackInventoryEntry> entries,
        IEnumerable<GameIdentifier> identifiers)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(identifiers);

        var entryList = entries.ToArray();
        var identifierList = identifiers.ToArray();
        var serials = identifierList
            .Where(identifier => identifier.Kind == GameIdentifierKind.Serial)
            .Select(identifier => identifier.Value.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var pspIds = identifierList
            .Where(identifier => identifier.Kind == GameIdentifierKind.Serial)
            .Select(identifier => NormalizePspGameId(identifier.Value))
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var discIds = identifierList
            .Where(identifier => identifier.Kind == GameIdentifierKind.DiscId)
            .Select(identifier => identifier.Value.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        // Dolphin checks for an exact six-character directory before considering a region-free
        // three-character directory. The exact directory blocks fallback even when it is empty.
        var exactDolphinDirectories = entryList
            .SelectMany(entry => entry.MatchKeys)
            .Where(key => key.Rule == TexturePackMatchRule.DolphinDirectoryExact)
            .Select(key => key.Value)
            .ToHashSet(StringComparer.Ordinal);

        return entryList
            .Where(entry => entry.IsUsable && entry.MatchKeys.Any(key => Matches(
                key,
                serials,
                pspIds,
                discIds,
                exactDolphinDirectories)))
            .ToArray();
    }

    private static bool Matches(
        TexturePackMatchKey key,
        IReadOnlySet<string> serials,
        IReadOnlySet<string> pspIds,
        IReadOnlySet<string> discIds,
        IReadOnlySet<string> exactDolphinDirectories) =>
        key.Rule switch
        {
            TexturePackMatchRule.ExactSerial => serials.Contains(key.Value),
            TexturePackMatchRule.PspGameId => pspIds.Contains(key.Value),
            TexturePackMatchRule.DolphinDirectoryExact or TexturePackMatchRule.DolphinMarkerExact =>
                discIds.Contains(key.Value),
            TexturePackMatchRule.DolphinDirectoryPrefix => discIds.Any(discId =>
                discId.StartsWith(key.Value, StringComparison.Ordinal) &&
                !exactDolphinDirectories.Contains(discId)),
            TexturePackMatchRule.DolphinMarkerPrefix =>
                discIds.Any(discId => discId.StartsWith(key.Value, StringComparison.Ordinal)),
            TexturePackMatchRule.DolphinShared => discIds.Count > 0,
            _ => false,
        };

    private static string NormalizePspGameId(string value) =>
        string.Concat(value.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
}
