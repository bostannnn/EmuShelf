namespace EmuShelf.Core.Library;

/// <summary>A single availability change used by the library's transactional batch update.</summary>
public sealed record GameAvailabilityUpdate(long GameId, bool IsAvailable);
