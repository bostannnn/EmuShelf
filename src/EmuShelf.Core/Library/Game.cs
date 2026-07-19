namespace EmuShelf.Core.Library;

/// <summary>
/// A single library entry. <see cref="Path"/> is the absolute, resolved path
/// (the DB stores it relative-when-portable; the repository resolves it on read).
/// Identity is the path — see DECISIONS.md "Game identity is the absolute file path".
/// </summary>
public sealed record Game
{
    public long Id { get; init; }
    public required string SystemId { get; init; }
    public required string Path { get; init; }
    public required string Title { get; init; }
    public GameTitleOrigin TitleOrigin { get; init; } = GameTitleOrigin.LegacyUnknown;
    public string? CoverPath { get; init; }
    public GameCoverOrigin CoverOrigin { get; init; } = GameCoverOrigin.None;
    /// <summary>External emulator library that authoritatively discovered this entry, if any.</summary>
    public string? ExternalSourceId { get; init; }
    /// <summary>Stable entry id inside <see cref="ExternalSourceId"/>, if any.</summary>
    public string? ExternalSourceEntryId { get; init; }
    /// <summary>
    /// Whether the external source listed this entry during its latest successful sync. This is
    /// null for local entries so a missing source record remains distinct from an unavailable
    /// listed path.
    /// </summary>
    public bool? IsPresentInExternalSource { get; init; }
    public bool IsAvailable { get; init; } = true;
    public DateTimeOffset DateAdded { get; init; }
}
