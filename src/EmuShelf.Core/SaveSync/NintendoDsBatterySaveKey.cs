namespace EmuShelf.Core.SaveSync;

/// <summary>
/// The cross-emulator cloud key for a Nintendo DS battery save.
/// </summary>
/// <remarks>
/// A DS battery save is a raw dump of the cartridge's save chip: standalone melonDS writes it as
/// <c>&lt;game&gt;.sav</c>, a RetroArch DS core writes the byte-identical payload as
/// <c>&lt;game&gt;.srm</c>. Keying by file name (as every other RetroArch system does) would file
/// those two as different cloud entries and let one machine's progress sit beside — never meet —
/// the other's. So DS battery saves key by game name alone, <c>nds/battery/&lt;game&gt;</c>, and each
/// provider resolves that key to whatever extension its own emulator reads. This mirrors the
/// PlayStation memory-card key DuckStation and RetroArch already share; see DECISIONS 2026-09-01.
///
/// <para>Only genuinely raw formats participate. DeSmuME's <c>.dsv</c> carries a footer and is not
/// interchangeable with a raw dump, so it keeps the plain file-name key it has always had.</para>
/// </remarks>
public static class NintendoDsBatterySaveKey
{
    /// <summary>The system whose saves this key belongs to.</summary>
    public const string SystemId = "nds";

    /// <summary>The sub-namespace, inside <c>nds/</c>, that holds canonical battery keys.</summary>
    public const string LocalIdPrefix = "battery/";

    /// <summary>
    /// The raw battery-dump extensions that share one key. Which one a given machine writes is the
    /// emulator's business, so each provider probes these in its own preferred order and picks its own
    /// extension for a save it is restoring for the first time.
    /// </summary>
    public static IReadOnlyList<string> Extensions { get; } = [".srm", ".sav"];

    /// <summary>Whether a save file name holds a raw battery dump that shares the canonical key.</summary>
    public static bool IsRawBatteryFile(string fileName) =>
        Extensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The canonical local id (the part after the <c>nds/</c> prefix) for a raw battery save file, or
    /// null when the file is not one — a <c>.dsv</c>, a core companion file, anything else.
    /// </summary>
    public static string? LocalIdFor(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !IsRawBatteryFile(fileName))
            return null;
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return IsSafeBaseName(baseName) ? LocalIdPrefix + baseName : null;
    }

    /// <summary>The game name inside a canonical local id, or null when the id is not one.</summary>
    public static string? BaseNameFrom(string localId)
    {
        if (string.IsNullOrEmpty(localId) || !localId.StartsWith(LocalIdPrefix, StringComparison.Ordinal))
            return null;
        var baseName = localId[LocalIdPrefix.Length..];
        return IsSafeBaseName(baseName) ? baseName : null;
    }

    /// <summary>
    /// The canonical unit id an old file-name-keyed DS battery unit maps to, or null when the id is
    /// not a re-keyable one (already canonical, a <c>.dsv</c>, a save state, another system).
    /// </summary>
    public static string? MapLegacyUnitId(string unitId)
    {
        const string prefix = SystemId + "/";
        if (string.IsNullOrEmpty(unitId) || !unitId.StartsWith(prefix, StringComparison.Ordinal))
            return null;

        var localId = unitId[prefix.Length..];
        // Only flat file-name ids are legacy; anything already carrying a sub-namespace (battery/,
        // states/, …) keeps its key.
        if (localId.Contains('/'))
            return null;
        return LocalIdFor(localId) is { } canonical ? prefix + canonical : null;
    }

    /// <summary>The local file name a canonical id takes under one extension.</summary>
    public static string FileNameFor(string baseName, string extension) => baseName + extension;

    /// <summary>
    /// The file in <paramref name="directory"/> this key means on this machine: the most recently
    /// written of the game's raw battery files, or <paramref name="preferredExtension"/> when the game
    /// has none here yet.
    /// </summary>
    /// <remarks>
    /// Newest wins rather than a fixed extension order, because one folder really can hold both
    /// spellings — a DS emulator that writes <c>.sav</c> beside an older <c>.srm</c> left there by a
    /// RetroArch core, or the reverse. Preferring a fixed extension would then sync the copy nobody is
    /// playing and quietly overwrite it on the next download; the file the emulator actually uses is
    /// the one it touched last. Ties break on the caller's own extension, the one its emulator reads.
    /// </remarks>
    public static string ResolveFileName(string directory, string baseName, string preferredExtension)
    {
        string? best = null;
        var bestWrite = DateTime.MinValue;
        foreach (var extension in Extensions)
        {
            var candidate = FileNameFor(baseName, extension);
            DateTime written;
            try
            {
                var path = Path.Combine(directory, candidate);
                if (!File.Exists(path))
                    continue;
                written = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                continue;
            }

            var isPreferred = string.Equals(extension, preferredExtension, StringComparison.OrdinalIgnoreCase);
            if (best is null || written > bestWrite || (written == bestWrite && isPreferred))
            {
                best = candidate;
                bestWrite = written;
            }
        }

        return best ?? FileNameFor(baseName, preferredExtension);
    }

    // A key becomes a file name, so it must be exactly one — no separator, no drive-relative form, no
    // leading dot — or a crafted cloud entry could resolve outside the save folder. The rule is the
    // host's own (Path.GetFileName plus its invalid characters), so a name that is legal here but not
    // on the other machine simply fails closed there. 250 leaves room for the extension under the
    // common 255-byte limit.
    private static bool IsSafeBaseName(string baseName) =>
        baseName.Length is > 0 and <= 250 &&
        !baseName.StartsWith('.') &&
        string.Equals(Path.GetFileName(baseName), baseName, StringComparison.Ordinal) &&
        baseName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
