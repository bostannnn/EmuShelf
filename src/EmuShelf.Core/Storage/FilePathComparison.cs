namespace EmuShelf.Core.Storage;

/// <summary>
/// The single source of truth for how absolute file paths are compared for identity across EmuShelf.
///
/// Windows (NTFS) and macOS (APFS/HFS+ in their default configuration) are case-insensitive, so
/// <c>Game.cue</c> and <c>GAME.CUE</c> name the same file and must share one identity — mirroring the
/// <c>Games.Path COLLATE NOCASE</c> database invariant that "a game is its absolute path." Linux
/// filesystems are case-sensitive, so paths there compare ordinally.
///
/// This lives in Core so App, Infrastructure, and Integrations share one rule instead of each call
/// site special-casing only Windows — which quietly treated macOS as case-sensitive and disagreed
/// with the case-insensitive database, splitting one on-disk file into two identities on macOS.
/// (The database's collation is case-insensitive on every platform, including Linux; reconciling that
/// last mismatch would mean a platform-dependent schema collation and is intentionally out of scope.)
/// </summary>
public static class FilePathComparison
{
    /// <summary>Whether EmuShelf treats this platform's filesystem as case-insensitive for path identity.</summary>
    public static bool IsCaseInsensitive { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>The comparison to use for absolute-path equality and under-root prefix tests.</summary>
    public static StringComparison Comparison =>
        IsCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>The comparer to key path-indexed dictionaries, sets, and ordering with.</summary>
    public static StringComparer Comparer =>
        IsCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
