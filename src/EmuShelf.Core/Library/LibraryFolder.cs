namespace EmuShelf.Core.Library;

/// <summary>
/// A folder the user added for a system. Remembered so it can be re-walked by a
/// rescan. <see cref="Path"/> is absolute (resolved on read from portable storage).
/// </summary>
public sealed record LibraryFolder
{
    public long Id { get; init; }
    public required string SystemId { get; init; }
    public required string Path { get; init; }
}
