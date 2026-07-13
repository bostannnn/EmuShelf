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
    public string? CoverPath { get; init; }
    public bool IsAvailable { get; init; } = true;
    public DateTimeOffset DateAdded { get; init; }
}
