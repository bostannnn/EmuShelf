using EmuShelf.Core.SaveSync;

namespace EmuShelf.Integrations.Emulators.Azahar;

/// <summary>
/// Locates the in-game save data on Azahar's emulated SD card without modifying it. Each title's
/// save archive (<c>sdmc/Nintendo 3DS/&lt;ID0&gt;/&lt;ID1&gt;/title/&lt;hi&gt;/&lt;lo&gt;/data</c>)
/// and each extdata archive (<c>…/extdata/00000000/&lt;id&gt;</c>) is one save unit. Installed
/// updates/DLC (the sibling <c>content</c> folder) and build-fragile save states are never synced.
///
/// The <c>&lt;ID0&gt;/&lt;ID1&gt;</c> pair is console-unique and differs between installs, so a unit
/// is keyed by the machine-independent title/extdata id and resolved under whichever console folder
/// exists locally. That makes a save portable across machines even though its on-disk path is not.
/// A machine that has never run Azahar has no SD card yet, so nothing resolves there until it does.
/// </summary>
public sealed class AzaharSaveLocationProvider : ISaveLocationProvider
{
    private const string FlatpakApplicationId = "org.azahar_emu.Azahar";
    private readonly string _installationDirectory;
    private readonly string? _userDirectoryOverride;
    private readonly string _homeDirectory;
    private readonly string _appDataDirectory;
    private readonly bool _isWindows;
    private readonly bool _isMacOS;
    private readonly bool _isFlatpak;

    public AzaharSaveLocationProvider(
        string installationDirectory,
        string? userDirectoryOverride = null,
        string? homeDirectory = null,
        string? appDataDirectory = null,
        bool? isWindows = null,
        bool isFlatpak = false,
        bool? isMacOS = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);
        _installationDirectory = Path.GetFullPath(installationDirectory);
        _userDirectoryOverride = string.IsNullOrWhiteSpace(userDirectoryOverride)
            ? null
            : Path.GetFullPath(userDirectoryOverride);
        _homeDirectory = string.IsNullOrWhiteSpace(homeDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetFullPath(homeDirectory);
        _appDataDirectory = string.IsNullOrWhiteSpace(appDataDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Path.GetFullPath(appDataDirectory);
        _isWindows = isWindows ?? OperatingSystem.IsWindows();
        _isMacOS = isMacOS ?? OperatingSystem.IsMacOS();
        _isFlatpak = isFlatpak;
    }

    public string SystemId => "3ds";

    // Battery saves key by the system ("3ds/"). Azahar does not sync save states, so it keeps no
    // separate emulator-scoped state namespace.
    public string UnitIdPrefix => SystemId + "/";

    /// <summary>The resolved local console folder, or the SD-card root when no console exists yet.</summary>
    public Task<string> GetSaveDataDirectoryAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => GetConsoleRoot(cancellationToken) ?? GetNintendo3dsDirectory(), cancellationToken);

    public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SaveUnit>>(() => GetSaveUnits(cancellationToken), cancellationToken);

    public SaveUnitLocation? ResolveUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || !unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal))
            return null;

        var consoleRoot = GetConsoleRoot(CancellationToken.None);
        if (consoleRoot is null)
            return null;

        var root = GetNintendo3dsDirectory();
        var segments = unitId[UnitIdPrefix.Length..].Split('/');

        if (segments is ["title", var hi, var lo] && IsHex8(hi) && IsHex8(lo))
        {
            return new SaveUnitLocation(
                Path.Combine(consoleRoot, "title", hi, lo, "data"),
                root,
                SaveUnitKind.Folder);
        }

        if (segments is ["extdata", var extId] && IsHex8(extId))
        {
            return new SaveUnitLocation(
                Path.Combine(consoleRoot, "extdata", "00000000", extId),
                root,
                SaveUnitKind.Folder);
        }

        return null;
    }

    private IReadOnlyList<SaveUnit> GetSaveUnits(CancellationToken cancellationToken)
    {
        var consoleRoot = GetConsoleRoot(cancellationToken);
        if (consoleRoot is null)
            return [];

        var units = new List<SaveUnit>();
        CollectTitleUnits(consoleRoot, units, cancellationToken);
        CollectExtdataUnits(consoleRoot, units, cancellationToken);
        return units.OrderBy(unit => unit.UnitId, StringComparer.Ordinal).ToArray();
    }

    private void CollectTitleUnits(string consoleRoot, List<SaveUnit> units, CancellationToken cancellationToken)
    {
        var titleRoot = Path.Combine(consoleRoot, "title");
        if (!Directory.Exists(titleRoot))
            return;

        foreach (var highDirectory in Directory.EnumerateDirectories(titleRoot))
        {
            var high = Path.GetFileName(highDirectory);
            if (!IsHex8(high))
                continue;

            foreach (var lowDirectory in Directory.EnumerateDirectories(highDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var low = Path.GetFileName(lowDirectory);
                // Only a title that actually has a save archive (the `data` folder) is a unit; the
                // sibling `content` folder holds installed updates/DLC and is never synced.
                if (!IsHex8(low) || !Directory.Exists(Path.Combine(lowDirectory, "data")))
                    continue;

                units.Add(new SaveUnit(
                    $"{UnitIdPrefix}title/{high}/{low}",
                    $"3DS {(high + low).ToUpperInvariant()}",
                    SaveUnitKind.Folder));
            }
        }
    }

    private void CollectExtdataUnits(string consoleRoot, List<SaveUnit> units, CancellationToken cancellationToken)
    {
        var extdataRoot = Path.Combine(consoleRoot, "extdata", "00000000");
        if (!Directory.Exists(extdataRoot))
            return;

        foreach (var extdataDirectory in Directory.EnumerateDirectories(extdataRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extId = Path.GetFileName(extdataDirectory);
            if (!IsHex8(extId))
                continue;

            units.Add(new SaveUnit(
                $"{UnitIdPrefix}extdata/{extId}",
                $"3DS extdata {extId.ToUpperInvariant()}",
                SaveUnitKind.Folder));
        }
    }

    // The SD card can, rarely, hold more than one console (a user imported several NANDs). The
    // most-populated pair is the active one, matching how the Dolphin provider prefers the populated
    // user directory; the Settings override is the escape hatch when that guess is wrong.
    private string? GetConsoleRoot(CancellationToken cancellationToken)
    {
        var nintendo3ds = GetNintendo3dsDirectory();
        if (!Directory.Exists(nintendo3ds))
            return null;

        string? best = null;
        var bestScore = -1;
        foreach (var id0 in Directory.EnumerateDirectories(nintendo3ds))
        {
            if (!IsConsoleId(Path.GetFileName(id0)))
                continue;

            foreach (var id1 in Directory.EnumerateDirectories(id0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsConsoleId(Path.GetFileName(id1)))
                    continue;

                var score = CountSaveArchives(id1);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = id1;
                }
            }
        }

        return best;
    }

    private static int CountSaveArchives(string consoleRoot)
    {
        var count = 0;
        var title = Path.Combine(consoleRoot, "title");
        if (Directory.Exists(title))
            count += Directory.EnumerateDirectories(title).Count();
        var extdata = Path.Combine(consoleRoot, "extdata", "00000000");
        if (Directory.Exists(extdata))
            count += Directory.EnumerateDirectories(extdata).Count();
        return count;
    }

    private string GetNintendo3dsDirectory() =>
        Path.Combine(GetUserDirectory(), "sdmc", "Nintendo 3DS");

    internal string GetUserDirectory()
    {
        if (_userDirectoryOverride is not null)
            return _userDirectoryOverride;

        // Azahar is portable when a "user" directory sits beside the executable (its USERDATA_DIR).
        var portable = Path.Combine(_installationDirectory, "user");
        if (Directory.Exists(portable))
            return portable;

        return GetDefaultUserDirectory(
            _homeDirectory, _appDataDirectory, _isWindows, _isMacOS, _isFlatpak);
    }

    internal static string GetDefaultUserDirectory(
        string homeDirectory,
        string appDataDirectory,
        bool isWindows,
        bool isMacOS,
        bool isFlatpak)
    {
        if (isFlatpak)
            return Path.Combine(homeDirectory, ".var", "app", FlatpakApplicationId, "data", "azahar-emu");
        if (isWindows)
            return Path.Combine(appDataDirectory, "Azahar");
        if (isMacOS)
            return Path.Combine(homeDirectory, "Library", "Application Support", "Azahar");
        return Path.Combine(homeDirectory, ".local", "share", "azahar-emu");
    }

    private static bool IsHex8(string value) => value.Length == 8 && IsHex(value);

    // Azahar's ID0/ID1 are 32-hex console ids; require a hex string of at least 16 characters so a
    // real id is accepted while `title`/`extdata`/junk folders are not.
    private static bool IsConsoleId(string value) => value.Length >= 16 && IsHex(value);

    private static bool IsHex(string value)
    {
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character))
                return false;
        }
        return value.Length > 0;
    }
}
