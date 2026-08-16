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
    /// <summary>When the game was last launched, or null if it has never been played. Stamped at
    /// launch start (not exit) so it survives an app kill mid-session. Drives the Recently Played
    /// collection.</summary>
    public DateTimeOffset? LastPlayedAt { get; init; }
    /// <summary>Total accumulated play time across all completed sessions (whole seconds), or zero if
    /// never played. Accrues when a tracked emulator process exits (M43), so a session lost to an app
    /// kill contributes nothing even though it still counts toward <see cref="PlayCount"/>.</summary>
    public TimeSpan Playtime { get; init; }
    /// <summary>How many times the game has been launched. Incremented at launch start alongside
    /// <see cref="LastPlayedAt"/>, so it counts every real launch — including one killed mid-session.</summary>
    public int PlayCount { get; init; }
}
