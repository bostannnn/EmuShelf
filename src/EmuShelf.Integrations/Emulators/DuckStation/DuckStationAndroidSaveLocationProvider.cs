using EmuShelf.Core.SaveSync;

namespace EmuShelf.Integrations.Emulators.DuckStation;

/// <summary>
/// The Android counterpart of <see cref="DuckStationSaveLocationProvider"/>. DuckStation for Android keeps
/// no readable <c>settings.ini</c> (its configuration lives in app-private internal storage, unreadable
/// without root), so this provider cannot read the configured card type the desktop provider relies on.
/// Instead it works directly off DuckStation Android's fixed on-device memory-card folder
/// (<c>Android/data/&lt;pkg&gt;/files/memcards</c>, reachable on the Thor under all-files access — see
/// DECISIONS 2026-08-20) and classifies each card by its on-disk name using DuckStation's own defaults.
///
/// The unit ids it emits are byte-for-byte the ones <see cref="DuckStationSaveLocationProvider"/> emits, so
/// a card syncs 1:1 between a desktop DuckStation and an Android DuckStation — provided both use the same
/// per-game card type, which is DuckStation's default (<c>PerGameTitle</c>) on each. A card named after a
/// PlayStation serial is classified <c>serial</c>; every other per-game card is classified <c>title</c>,
/// matching that default and the names observed on the Thor (e.g. <c>Metal Gear Solid (USA)_1.mcd</c>).
///
/// Scope of this first slice: per-game cards only. DuckStation Android's single global card
/// (<c>memorycard.mcd</c>) and explicit <c>shared_card_N.mcd</c> cards are not yet mapped — the shared-card
/// slot/number cannot be recovered from the on-disk name without the configuration, so they are deliberately
/// skipped rather than guessed at (a mis-mapped shared card would overwrite the wrong slot). Tracked as a
/// follow-up.
/// </summary>
public sealed class DuckStationAndroidSaveLocationProvider : ISaveLocationProvider
{
    private readonly string _memoryCardsDirectory;

    // Battery cards key by the system (UnitIdPrefix defaults to "playstation/"), byte-for-byte matching
    // what the desktop DuckStation provider emits so a card syncs 1:1 between the two.
    private string PerGamePrefix => UnitIdPrefix + "per-game/";

    /// <param name="memoryCardsDirectory">
    /// DuckStation Android's memory-card folder — normally
    /// <c>/storage/…/Android/data/com.github.stenzek.duckstation/files/memcards</c>, or the user's chosen
    /// folder when they have relocated it.
    /// </param>
    public DuckStationAndroidSaveLocationProvider(string memoryCardsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memoryCardsDirectory);
        _memoryCardsDirectory = Path.GetFullPath(memoryCardsDirectory);
    }

    public string SystemId => "playstation";

    public string UnitIdPrefix => SystemId + "/";

    // Kept in lock-step with the desktop DuckStation provider so that if Android DuckStation ever gains
    // save-state sync, its states land in the same emulator-scoped namespace rather than the system one.
    public string StateNamespacePrefix => "duckstation/";

    /// <summary>The resolved memory-card folder, whether or not it exists yet.</summary>
    public string MemoryCardsDirectory => _memoryCardsDirectory;

    public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SaveUnit>>(() => GetSaveUnits(cancellationToken), cancellationToken);

    public SaveUnitLocation? ResolveUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || !unitId.StartsWith(PerGamePrefix, StringComparison.Ordinal))
            return null;

        var segments = unitId[PerGamePrefix.Length..].Split('/', 2, StringSplitOptions.None);
        if (segments.Length != 2 || !IsKnownScheme(segments[0]))
            return null;

        var fileName = segments[1];
        // The scheme in the id must be the one this card's name actually classifies to; otherwise a
        // remote id could name a real file under the wrong scheme and resolve it into place.
        if (!IsSafeCardFileName(fileName) ||
            !TryClassifyPerGame(fileName, out var scheme) ||
            !string.Equals(scheme, segments[0], StringComparison.Ordinal))
        {
            return null;
        }

        return new SaveUnitLocation(
            Path.Combine(_memoryCardsDirectory, fileName),
            _memoryCardsDirectory,
            SaveUnitKind.File);
    }

    private IReadOnlyList<SaveUnit> GetSaveUnits(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_memoryCardsDirectory))
            return [];

        var units = new List<SaveUnit>();
        foreach (var path in Directory.EnumerateFiles(_memoryCardsDirectory, "*.mcd")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(path);
            if (IsSafeCardFileName(fileName) && TryClassifyPerGame(fileName, out var scheme))
            {
                units.Add(new SaveUnit(
                    PerGamePrefix + scheme + "/" + fileName,
                    fileName,
                    SaveUnitKind.File));
            }
        }

        return units;
    }

    // Classifies a per-game card file name into DuckStation's scheme token. A card is
    // "<identity>_<slot>.mcd"; the slot is 1 or 2. An identity that is a PlayStation serial is the
    // 'serial' scheme; any other identity is 'title' — DuckStation's default per-game card type, and the
    // scheme the desktop provider emits for the same names. Returns false for a name that is not a
    // per-game card (e.g. the global 'memorycard.mcd' or a 'shared_card_N.mcd'), which this slice skips.
    private static bool TryClassifyPerGame(string fileName, out string scheme)
    {
        scheme = string.Empty;
        if (!fileName.EndsWith(".mcd", StringComparison.OrdinalIgnoreCase))
            return false;

        var stem = fileName[..^".mcd".Length];
        var underscore = stem.LastIndexOf('_');
        if (underscore <= 0 || underscore != stem.Length - 2)
            return false; // must end in "_<slot>" with a single-character slot

        var slot = stem[^1];
        if (slot is not ('1' or '2'))
            return false;

        var identity = stem[..underscore];
        if (identity.Length == 0 || identity.Equals("shared_card", StringComparison.OrdinalIgnoreCase))
            return false;

        scheme = IsPlayStationSerial(identity) ? "serial" : "title";
        return true;
    }

    private static bool IsKnownScheme(string value) => value is "serial" or "title" or "file-title";

    // Mirrors DuckStationSaveLocationProvider.IsSafeCardFileName — keep the two in sync; a card id that
    // one accepts and the other rejects would break cross-machine sync of that card.
    private static bool IsSafeCardFileName(string value) =>
        value.Length is >= 6 and <= 255 &&
        value.EndsWith(".mcd", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !value.Contains('/') &&
        !value.Contains('\\') &&
        !value.Contains('\0');

    // Mirrors DuckStationSaveLocationProvider.IsPlayStationSerial — a 10-character "AAAA-#####" id.
    private static bool IsPlayStationSerial(string value) =>
        value.Length == 10 &&
        value[..4].All(char.IsAsciiLetter) &&
        value[4] == '-' &&
        value[5..].All(char.IsAsciiDigit);
}
