using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;

namespace EmuShelf.Integrations.Emulators.Dolphin;

/// <summary>
/// Resolves Dolphin's effective user directory and configured save paths without modifying its
/// configuration. GameCube raw cards, individual GCI files, and Wii disc-title data are exposed as
/// separate allow-listed units; the rest of the NAND and all save states remain outside the model.
/// </summary>
public sealed class DolphinSaveLocationProvider : ISaveLocationProvider
{
    private const int RawMemoryCardDevice = 1;
    private const int GciFolderDevice = 8;
    private const int NoDevice = 0xff;
    private const int GciHeaderSize = 0x40;
    private const int GciBlockSize = 0x2000;
    private static readonly string[] Regions = ["USA", "JPN", "EUR", "DEV"];

    private readonly string _systemId;
    private readonly string _installationDirectory;
    private readonly string? _userDirectoryOverride;
    private readonly string? _launchArguments;
    private readonly string _homeDirectory;
    private readonly string _documentsDirectory;
    private readonly string? _xdgDataHome;
    private readonly bool _isWindows;
    private readonly bool _isMacOS;
    private readonly bool _isFlatpak;

    public DolphinSaveLocationProvider(
        string systemId,
        string installationDirectory,
        string? userDirectoryOverride = null,
        string? launchArguments = null,
        bool isFlatpak = false,
        string? homeDirectory = null,
        string? documentsDirectory = null,
        string? xdgDataHome = null,
        bool? isWindows = null,
        bool? isMacOS = null)
    {
        if (systemId is not ("gamecube" or "wii"))
            throw new ArgumentException("Dolphin save sync supports GameCube or Wii.", nameof(systemId));
        ArgumentException.ThrowIfNullOrWhiteSpace(installationDirectory);

        _systemId = systemId;
        _installationDirectory = Path.GetFullPath(installationDirectory);
        _userDirectoryOverride = string.IsNullOrWhiteSpace(userDirectoryOverride)
            ? null
            : Path.GetFullPath(userDirectoryOverride);
        _launchArguments = launchArguments;
        _homeDirectory = string.IsNullOrWhiteSpace(homeDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Path.GetFullPath(homeDirectory);
        _documentsDirectory = string.IsNullOrWhiteSpace(documentsDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : Path.GetFullPath(documentsDirectory);
        _xdgDataHome = string.IsNullOrWhiteSpace(xdgDataHome) ? null : Path.GetFullPath(xdgDataHome);
        _isWindows = isWindows ?? OperatingSystem.IsWindows();
        _isMacOS = isMacOS ?? OperatingSystem.IsMacOS();
        _isFlatpak = isFlatpak;
    }

    public string SystemId => _systemId;

    public string UnitIdPrefix => _systemId == "gamecube" ? "dolphin/gc/" : "dolphin/wii/";

    public Task<string> GetUserDirectoryAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => GetUserDirectory(cancellationToken), cancellationToken);

    /// <summary>
    /// Resolves and validates Dolphin's configuration, then reports the physical save locations
    /// represented by the units currently present on this machine.
    /// </summary>
    public Task<DolphinSaveLocationInfo> GetSaveLocationInfoAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run(() => GetSaveLocationInfo(cancellationToken), cancellationToken);

    public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<SaveUnit>>(
            () => _systemId == "gamecube"
                ? GetGameCubeUnits(cancellationToken)
                : GetWiiUnits(cancellationToken),
            cancellationToken);

    public SaveUnitLocation? ResolveUnit(string unitId)
    {
        if (string.IsNullOrWhiteSpace(unitId) || !unitId.StartsWith(UnitIdPrefix, StringComparison.Ordinal))
            return null;

        return _systemId == "gamecube"
            ? ResolveGameCubeUnit(unitId)
            : ResolveWiiUnit(unitId);
    }

    private IReadOnlyList<SaveUnit> GetGameCubeUnits(CancellationToken cancellationToken)
    {
        var state = ReadState(cancellationToken);
        return GetGameCubeUnits(state, cancellationToken);
    }

    private IReadOnlyList<SaveUnit> GetGameCubeUnits(
        DolphinState state,
        CancellationToken cancellationToken)
    {
        ValidateSlotLocationsDoNotAlias(state);
        var units = new List<SaveUnit>();
        foreach (var slot in new[] { 'a', 'b' })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = GetSlotDevice(state.Configuration, slot);
            if (device == RawMemoryCardDevice)
            {
                foreach (var region in Regions)
                {
                    foreach (var card in GetExistingRawCards(state, slot, region))
                    {
                        units.Add(new SaveUnit(
                            RawUnitId(slot, region, card.Variant),
                            card.Variant is null
                                ? $"Memory Card {char.ToUpperInvariant(slot)} — {region}"
                                : $"Memory Card {char.ToUpperInvariant(slot)} — {region} ({card.Variant} blocks)",
                            SaveUnitKind.File));
                    }
                }
            }
            else if (device == GciFolderDevice)
            {
                AddGciUnits(state, slot, units, cancellationToken);
            }
            else if (device != NoDevice)
            {
                // Other EXI devices are controllers/adapters, not save locations.
                continue;
            }
        }

        return units.OrderBy(unit => unit.UnitId, StringComparer.Ordinal).ToList();
    }

    private void AddGciUnits(
        DolphinState state,
        char slot,
        List<SaveUnit> units,
        CancellationToken cancellationToken)
    {
        var saves = new Dictionary<string, (string Folder, IReadOnlyList<GciFile> Files)>(StringComparer.Ordinal);
        var folders = Regions.Select(region => ResolveGciFolder(state, slot, region))
            .Concat(state.PerGameGciOverrides
                .Where(pair => pair.Key.Slot == slot)
                .Select(pair => pair.Value))
            .Distinct(PathComparer);

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var group in GetGciFiles(folder, cancellationToken).GroupBy(file => file.GameId))
            {
                if (saves.TryGetValue(group.Key, out var other) && !PathComparer.Equals(other.Folder, folder))
                {
                    throw new DolphinConfigurationFormatException(
                        $"Dolphin resolves {group.Key} to more than one GCI folder for slot {char.ToUpperInvariant(slot)}.");
                }

                saves[group.Key] = (folder, group.OrderBy(file => file.Identity, StringComparer.Ordinal).ToList());
            }
        }

        foreach (var (gameId, save) in saves.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (save.Files.Select(file => file.Identity).Distinct(StringComparer.Ordinal).Count() != save.Files.Count)
            {
                throw new DolphinConfigurationFormatException(
                    $"Dolphin has duplicate GCI identities for {gameId} in slot {char.ToUpperInvariant(slot)}.");
            }

            // Keep one deterministic file on the original per-game id at every cardinality. This
            // lets a one-file cloud entry remain valid when the game later creates sibling saves.
            var primary = save.Files[0];
            units.Add(new SaveUnit(
                GciUnitId(slot, gameId),
                save.Files.Count == 1
                    ? $"Card {char.ToUpperInvariant(slot)} — {gameId}"
                    : $"Card {char.ToUpperInvariant(slot)} — {gameId} — {Path.GetFileName(primary.Path)}",
                SaveUnitKind.File));

            foreach (var file in save.Files.Skip(1))
            {
                units.Add(new SaveUnit(
                    GciUnitId(slot, gameId, file.Identity),
                    $"Card {char.ToUpperInvariant(slot)} — {gameId} — {Path.GetFileName(file.Path)}",
                    SaveUnitKind.File));
            }
        }
    }

    private SaveUnitLocation? ResolveGameCubeUnit(string unitId)
    {
        var state = ReadState(CancellationToken.None);
        ValidateSlotLocationsDoNotAlias(state);
        return ResolveGameCubeUnit(state, unitId);
    }

    private SaveUnitLocation? ResolveGameCubeUnit(DolphinState state, string unitId)
    {
        var relative = unitId[UnitIdPrefix.Length..];
        var parts = relative.Split('/', StringSplitOptions.None);
        if (TryParseRawUnit(parts, out var slot, out var region, out var variant) &&
            GetSlotDevice(state.Configuration, slot) == RawMemoryCardDevice)
        {
            var path = ResolveRawCardPath(
                state,
                slot,
                region,
                variant,
                requireExisting: false);
            if (path is null)
                return null;
            return new SaveUnitLocation(path, Path.GetDirectoryName(path)!, SaveUnitKind.File);
        }

        if (TryParseGciUnit(parts, out var gciSlot, out var gameId, out var identity) &&
            GetSlotDevice(state.Configuration, gciSlot) == GciFolderDevice)
        {
            var folder = ResolveGciFolderForGame(state, gciSlot, gameId);
            if (folder is null)
                return null;
            var files = GetGciFiles(folder, CancellationToken.None)
                .Where(file => file.GameId == gameId)
                .OrderBy(file => file.Identity, StringComparer.Ordinal)
                .ToList();

            GciFile? selected;
            if (identity is null)
            {
                selected = files.FirstOrDefault();
            }
            else
            {
                // The first local file belongs to the base unit. A remote sibling must never
                // resolve to that same path: the base may be replaced earlier in the pre-planned
                // pass when another machine has added an identity that sorts before it.
                selected = files.Skip(1).SingleOrDefault(file => file.Identity == identity);
            }

            var path = selected?.Path ?? Path.Combine(
                folder,
                identity is null ? $"{gameId}.gci" : $"{gameId}-{identity}.gci");
            return new SaveUnitLocation(path, folder, SaveUnitKind.File);
        }

        return null;
    }

    private IReadOnlyList<SaveUnit> GetWiiUnits(CancellationToken cancellationToken)
    {
        var state = ReadState(cancellationToken);
        return GetWiiUnits(state, cancellationToken);
    }

    private IReadOnlyList<SaveUnit> GetWiiUnits(
        DolphinState state,
        CancellationToken cancellationToken)
    {
        var titleRoot = Path.Combine(GetNandRoot(state), "title", "00010000");
        if (!Directory.Exists(titleRoot))
            return [];

        var units = new List<SaveUnit>();
        foreach (var titleDirectory in Directory.EnumerateDirectories(titleRoot).OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var titleId = Path.GetFileName(titleDirectory).ToLowerInvariant();
            var dataDirectory = Path.Combine(titleDirectory, "data");
            if (!IsHexTitleId(titleId) || !Directory.Exists(dataDirectory) ||
                !Directory.EnumerateFiles(dataDirectory, "*", SearchOption.AllDirectories).Any())
                continue;
            units.Add(new SaveUnit(WiiUnitId(titleId), titleId, SaveUnitKind.Folder));
        }

        return units;
    }

    private SaveUnitLocation? ResolveWiiUnit(string unitId)
    {
        var state = ReadState(CancellationToken.None);
        return ResolveWiiUnit(state, unitId);
    }

    private SaveUnitLocation? ResolveWiiUnit(DolphinState state, string unitId)
    {
        var relative = unitId[UnitIdPrefix.Length..];
        var parts = relative.Split('/', StringSplitOptions.None);
        if (parts is not ["title", "00010000", var titleId] || !IsHexTitleId(titleId))
            return null;

        var titleRoot = Path.Combine(GetNandRoot(state), "title", "00010000");
        return new SaveUnitLocation(
            Path.Combine(titleRoot, titleId.ToLowerInvariant(), "data"),
            titleRoot,
            SaveUnitKind.Folder);
    }

    private DolphinSaveLocationInfo GetSaveLocationInfo(CancellationToken cancellationToken)
    {
        var state = ReadState(cancellationToken);
        var units = _systemId == "gamecube"
            ? GetGameCubeUnits(state, cancellationToken)
            : GetWiiUnits(state, cancellationToken);
        List<string> locations = _systemId == "wii"
            ? [Path.Combine(GetNandRoot(state), "title", "00010000")]
            : units
                .Select(unit => (Unit: unit, Location: ResolveGameCubeUnit(state, unit.UnitId)))
                .Where(item => item.Location is not null)
                .Select(item => item.Unit.UnitId.StartsWith("dolphin/gc/gci/", StringComparison.Ordinal)
                    ? item.Location!.RootPath
                    : item.Location!.Path)
                .Distinct(PathComparer)
                .OrderBy(path => path, PathComparer)
                .ToList();

        if (locations.Count == 0)
        {
            locations.AddRange(GetConfiguredGameCubeRoots(state));
        }

        return new DolphinSaveLocationInfo(state.UserDirectory, locations);
    }

    private IReadOnlyList<string> GetConfiguredGameCubeRoots(DolphinState state)
    {
        var roots = new List<string>();
        foreach (var slot in new[] { 'a', 'b' })
        {
            var device = GetSlotDevice(state.Configuration, slot);
            if (device == RawMemoryCardDevice)
            {
                roots.AddRange(Regions
                    .Select(region => Path.GetDirectoryName(GetExpectedRawCardPath(state, slot, region))!));
            }
            else if (device == GciFolderDevice)
            {
                roots.AddRange(Regions.Select(region => ResolveGciFolder(state, slot, region)));
                roots.AddRange(state.PerGameGciOverrides
                    .Where(pair => pair.Key.Slot == slot)
                    .Select(pair => pair.Value));
            }
        }
        return roots.Distinct(PathComparer).OrderBy(path => path, PathComparer).ToList();
    }

    private DolphinState ReadState(CancellationToken cancellationToken)
    {
        var userDirectory = GetUserDirectory(cancellationToken);
        var configuration = IniDocument.Read(Path.Combine(userDirectory, "Config", "Dolphin.ini"));
        var overrides = ReadPerGameOverrides(userDirectory, configuration, cancellationToken);
        return new DolphinState(userDirectory, configuration, overrides);
    }

    private Dictionary<(char Slot, string GameId), string> ReadPerGameOverrides(
        string userDirectory,
        IniDocument globalConfiguration,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<(char Slot, string GameId), string>();
        var directory = Path.Combine(userDirectory, "GameSettings");
        if (!Directory.Exists(directory))
            return result;

        foreach (var path in Directory.EnumerateFiles(directory, "*.ini", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileNameWithoutExtension(path);
            var document = IniDocument.Read(path);
            if (fileName.Length < 6 || !IsGameId(fileName[..6].ToUpperInvariant()))
            {
                if (HasSaveLayoutSetting(document))
                {
                    throw new DolphinConfigurationFormatException(
                        $"{Path.GetFileName(path)} changes Dolphin's save layout but does not identify one exact game.");
                }
                continue;
            }
            var gameId = fileName[..6].ToUpperInvariant();
            if (_systemId == "wii")
            {
                if (!string.IsNullOrWhiteSpace(document.Get("General", "NANDRootPath")))
                {
                    throw new DolphinConfigurationFormatException(
                        $"{Path.GetFileName(path)} selects a per-game NAND root, which cannot be synced safely.");
                }
                continue;
            }

            foreach (var slot in new[] { 'a', 'b' })
            {
                var suffix = char.ToUpperInvariant(slot);
                if (!string.IsNullOrWhiteSpace(document.Get("Core", $"Slot{suffix}")))
                {
                    throw new DolphinConfigurationFormatException(
                        $"{Path.GetFileName(path)} changes the per-game device in slot {suffix}, " +
                        "so its save layout cannot be synced safely.");
                }
                if (!string.IsNullOrWhiteSpace(document.Get("Core", $"Memcard{suffix}Path")))
                {
                    throw new DolphinConfigurationFormatException(
                        $"{Path.GetFileName(path)} selects a per-game raw memory-card path, which cannot be synced safely.");
                }

                var configured = document.Get("Core", $"GCIFolder{suffix}PathOverride") ??
                    document.Get("Core", $"GCIFolder{suffix}Path");
                if (string.IsNullOrWhiteSpace(configured))
                    continue;
                var resolved = ResolveConfiguredPath(configured, userDirectory);
                var key = (slot, gameId);
                if (result.TryGetValue(key, out var previous) && !PathComparer.Equals(previous, resolved))
                {
                    throw new DolphinConfigurationFormatException(
                        $"Dolphin has conflicting GCI-folder overrides for {gameId}.");
                }
                result[key] = resolved;
            }
        }

        return result;
    }

    private bool HasSaveLayoutSetting(IniDocument document)
    {
        if (_systemId == "wii")
            return !string.IsNullOrWhiteSpace(document.Get("General", "NANDRootPath"));

        foreach (var suffix in new[] { 'A', 'B' })
        {
            if (!string.IsNullOrWhiteSpace(document.Get("Core", $"Slot{suffix}")) ||
                !string.IsNullOrWhiteSpace(document.Get("Core", $"Memcard{suffix}Path")) ||
                !string.IsNullOrWhiteSpace(document.Get("Core", $"GCIFolder{suffix}Path")) ||
                !string.IsNullOrWhiteSpace(document.Get("Core", $"GCIFolder{suffix}PathOverride")))
            {
                return true;
            }
        }
        return false;
    }

    private string? ResolveRawCardPath(
        DolphinState state,
        char slot,
        string region,
        string? variant,
        bool requireExisting)
    {
        var expected = GetExpectedRawCardPath(state, slot, region);
        var candidates = GetExistingRawCards(state, slot, region)
            .Where(card => string.Equals(card.Variant, variant, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count > 1)
        {
            throw new DolphinConfigurationFormatException(
                $"Dolphin has more than one matching raw card for slot {char.ToUpperInvariant(slot)} and region {region}.");
        }
        if (candidates.Count == 1)
            return candidates[0].Path;
        return requireExisting ? null : AddRawCardVariant(expected, variant);
    }

    private string GetExpectedRawCardPath(DolphinState state, char slot, string region)
    {
        var suffix = char.ToUpperInvariant(slot);
        var configured = state.Configuration.Get("Core", $"Memcard{suffix}Path");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(state.UserDirectory, "GC", $"MemoryCard{suffix}.{region}.raw")
            : SubstituteRawRegion(ResolveConfiguredPath(configured, state.UserDirectory), region);
    }

    private IReadOnlyList<RawCard> GetExistingRawCards(DolphinState state, char slot, string region)
    {
        var expected = GetExpectedRawCardPath(state, slot, region);
        var candidates = ExistingRawCards(expected).ToList();
        if (region == "JPN" && candidates.Count == 0)
            candidates.AddRange(ExistingRawCards(ReplaceTerminalRegion(expected, "JPN", "JAP")));
        return candidates;
    }

    private static IEnumerable<RawCard> ExistingRawCards(string expected)
    {
        var directory = Path.GetDirectoryName(expected)!;
        if (!Directory.Exists(directory))
            yield break;
        var extension = Path.GetExtension(expected);
        var stem = Path.GetFileNameWithoutExtension(expected);
        foreach (var path in Directory.EnumerateFiles(directory, "*" + extension, SearchOption.TopDirectoryOnly))
        {
            var candidateStem = Path.GetFileNameWithoutExtension(path);
            if (candidateStem.Equals(stem, StringComparison.OrdinalIgnoreCase))
            {
                yield return new RawCard(Path.GetFullPath(path), null);
                continue;
            }
            if (!candidateStem.StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase))
                continue;
            var variant = candidateStem[(stem.Length + 1)..];
            if (IsRawCardVariant(variant))
                yield return new RawCard(Path.GetFullPath(path), variant);
        }
    }

    private void ValidateSlotLocationsDoNotAlias(DolphinState state)
    {
        var targets = new List<(char Slot, string Path)>();
        foreach (var slot in new[] { 'a', 'b' })
        {
            var device = GetSlotDevice(state.Configuration, slot);
            if (device == RawMemoryCardDevice)
            {
                targets.AddRange(Regions.Select(region =>
                    (slot, GetExpectedRawCardPath(state, slot, region))));
            }
            else if (device == GciFolderDevice)
            {
                targets.AddRange(Regions.Select(region =>
                    (slot, ResolveGciFolder(state, slot, region))));
                targets.AddRange(state.PerGameGciOverrides
                    .Where(pair => pair.Key.Slot == slot)
                    .Select(pair => (slot, pair.Value)));
            }
        }

        var slotA = targets.Where(target => target.Slot == 'a')
            .Select(target => Path.GetFullPath(target.Path))
            .Distinct(PathComparer);
        var slotB = targets.Where(target => target.Slot == 'b')
            .Select(target => Path.GetFullPath(target.Path))
            .ToHashSet(PathComparer);
        var aliased = slotA.FirstOrDefault(slotB.Contains);
        if (aliased is not null)
        {
            throw new DolphinConfigurationFormatException(
                $"Dolphin slots A and B use the same save location ({aliased}). " +
                "Choose separate card files or folders before enabling save sync.");
        }
    }

    private string? ResolveGciFolderForGame(DolphinState state, char slot, string gameId)
    {
        if (state.PerGameGciOverrides.TryGetValue((slot, gameId), out var overridden))
            return overridden;

        var containing = Regions.Select(region => ResolveGciFolder(state, slot, region))
            .Where(folder => GetGciFiles(folder, CancellationToken.None).Any(file => file.GameId == gameId))
            .Distinct(PathComparer)
            .ToList();
        if (containing.Count > 1)
        {
            throw new DolphinConfigurationFormatException(
                $"Dolphin has {gameId} in more than one region folder for slot {char.ToUpperInvariant(slot)}.");
        }
        if (containing.Count == 1)
            return containing[0];

        var region = RegionForGameId(gameId);
        return region is null ? null : ResolveGciFolder(state, slot, region);
    }

    private string ResolveGciFolder(DolphinState state, char slot, string region)
    {
        var suffix = char.ToUpperInvariant(slot);
        var configured = state.Configuration.Get("Core", $"GCIFolder{suffix}Path");
        if (string.IsNullOrWhiteSpace(configured))
        {
            var modern = Path.Combine(state.UserDirectory, "GC", region, $"Card {suffix}");
            if (region == "JPN" && !Directory.Exists(modern))
            {
                var legacy = Path.Combine(state.UserDirectory, "GC", "JAP", $"Card {suffix}");
                if (Directory.Exists(legacy))
                    return legacy;
            }
            return modern;
        }

        var path = ResolveConfiguredPath(configured, state.UserDirectory);
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var finalSegment = Path.GetFileName(trimmed);
        if (finalSegment is "USA" or "JPN" or "EUR" or "DEV")
        {
            trimmed = Path.GetDirectoryName(trimmed)!;
        }
        var resolved = Path.Combine(trimmed, region);
        if (region == "JPN" && !Directory.Exists(resolved))
        {
            var legacy = Path.Combine(trimmed, "JAP");
            if (Directory.Exists(legacy))
                return legacy;
        }
        return resolved;
    }

    private static IReadOnlyList<GciFile> GetGciFiles(string folder, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
            return [];
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new DolphinConfigurationFormatException(
                    "A Dolphin GCI folder contains a symbolic link or reparse point and cannot be synced safely.");
            }
            if ((attributes & FileAttributes.Directory) != 0)
            {
                throw new DolphinConfigurationFormatException(
                    "A Dolphin GCI folder contains a nested directory and cannot be mapped to individual save files safely.");
            }
        }
        var result = new List<GciFile>();
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => Path.GetExtension(path).Equals(".gci", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryReadGci(path, out var gameId, out var identity))
                result.Add(new GciFile(Path.GetFullPath(path), gameId, identity));
        }
        return result;
    }

    private static bool TryReadGci(string path, out string gameId, out string identity)
    {
        gameId = string.Empty;
        identity = string.Empty;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length < GciHeaderSize)
                return false;
            Span<byte> header = stackalloc byte[GciHeaderSize];
            if (stream.Read(header) != header.Length)
                return false;
            var blocks = BinaryPrimitives.ReadUInt16BigEndian(header.Slice(0x38, 2));
            if (blocks is 0 or > 2043 || stream.Length != GciHeaderSize + (long)blocks * GciBlockSize)
                return false;
            var id = Encoding.ASCII.GetString(header[..6]).ToUpperInvariant();
            if (!IsGameId(id))
                return false;
            gameId = id;
            // The internal save name is stable even when Dolphin chooses a different physical
            // filename on another machine. It distinguishes the uncommon games that own several
            // sibling GCI files without turning the whole shared card folder into one unit.
            identity = Convert.ToHexString(SHA256.HashData(header[0x08..0x28]))[..16];
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string GetNandRoot(DolphinState state)
    {
        var configured = state.Configuration.Get("General", "NANDRootPath");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(state.UserDirectory, "Wii")
            : ResolveConfiguredPath(configured, state.UserDirectory);
    }

    private int GetSlotDevice(IniDocument configuration, char slot)
    {
        var text = configuration.Get("Core", $"Slot{char.ToUpperInvariant(slot)}");
        if (string.IsNullOrWhiteSpace(text))
            return slot == 'a' ? GciFolderDevice : NoDevice;
        if (!int.TryParse(text, out var value) || value is < 0 or > 0xff)
            throw new DolphinConfigurationFormatException("Dolphin.ini contains an unsupported memory-card slot value.");
        return value;
    }

    private string GetUserDirectory(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_userDirectoryOverride is not null)
            return NormalizeSelectedUserDirectory(_userDirectoryOverride);

        if (TryGetUserArgument(_launchArguments, out var userArgument))
            return ResolveConfiguredPath(userArgument, _installationDirectory);

        if (File.Exists(Path.Combine(_installationDirectory, "portable.txt")))
        {
            var name = _isWindows || _isMacOS ? "User" : "user";
            return Path.Combine(_installationDirectory, name);
        }

        if (_isFlatpak)
        {
            return Path.Combine(
                _homeDirectory,
                ".var",
                "app",
                "org.DolphinEmu.dolphin-emu",
                "data",
                "dolphin-emu");
        }
        if (_isWindows)
            return Path.Combine(_documentsDirectory, "Dolphin Emulator");
        if (_isMacOS)
            return Path.Combine(_homeDirectory, "Library", "Application Support", "Dolphin");

        var modern = Path.Combine(_xdgDataHome ?? Path.Combine(_homeDirectory, ".local", "share"), "dolphin-emu");
        var legacy = Path.Combine(_homeDirectory, ".dolphin-emu");
        return Directory.Exists(modern) || !Directory.Exists(legacy) ? modern : legacy;
    }

    private static string NormalizeSelectedUserDirectory(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var name = Path.GetFileName(trimmed);
        return name.Equals("Config", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GC", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Wii", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(trimmed) ?? trimmed
            : trimmed;
    }

    private static bool TryGetUserArgument(string? arguments, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(arguments))
            return false;
        var tokens = Tokenize(arguments);
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.Equals("-u", StringComparison.Ordinal) || token.Equals("--user", StringComparison.Ordinal))
            {
                if (++index >= tokens.Count || string.IsNullOrWhiteSpace(tokens[index]))
                    throw new DolphinConfigurationFormatException("Dolphin's user-directory argument has no path.");
                value = tokens[index];
                return true;
            }
            if (token.StartsWith("--user=", StringComparison.Ordinal))
            {
                value = token[7..];
                if (string.IsNullOrWhiteSpace(value))
                    throw new DolphinConfigurationFormatException("Dolphin's user-directory argument has no path.");
                return true;
            }
        }
        return false;
    }

    private static List<string> Tokenize(string arguments)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        foreach (var character in arguments)
        {
            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
                else
                    current.Append(character);
                continue;
            }
            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(character);
            }
        }
        if (quote != '\0')
            throw new DolphinConfigurationFormatException("Dolphin's launch arguments contain an unterminated quote.");
        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens;
    }

    private static string ResolveConfiguredPath(string configured, string baseDirectory)
    {
        var value = configured.Trim().Trim('"');
        if (value.Length == 0 || value.Contains('\0') || value.Contains('\r') || value.Contains('\n'))
            throw new DolphinConfigurationFormatException("Dolphin contains an unsupported save path.");
        try
        {
            var normalized = value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            var path = Path.IsPathFullyQualified(normalized) ? normalized : Path.Combine(baseDirectory, normalized);
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new DolphinConfigurationFormatException("Dolphin contains an unsupported save path.", ex);
        }
    }

    private static string SubstituteRawRegion(string path, string region)
    {
        var directory = Path.GetDirectoryName(path)!;
        var extension = Path.GetExtension(path);
        var name = Path.GetFileNameWithoutExtension(path);
        foreach (var marker in new[] { "USA", "JAP", "EUR", "DEV" })
        {
            if (name.EndsWith("." + marker, StringComparison.Ordinal))
            {
                name = name[..^(marker.Length + 1)];
                break;
            }
        }
        return Path.Combine(directory, $"{name}.{region}{extension}");
    }

    private static string ReplaceTerminalRegion(string path, string from, string to)
    {
        var directory = Path.GetDirectoryName(path)!;
        var extension = Path.GetExtension(path);
        var name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("." + from, StringComparison.Ordinal)
            ? Path.Combine(directory, $"{name[..^from.Length]}{to}{extension}")
            : path;
    }

    private static string AddRawCardVariant(string path, string? variant)
    {
        if (variant is null)
            return path;
        return Path.Combine(
            Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}.{variant}{Path.GetExtension(path)}");
    }

    private static string? RegionForGameId(string gameId) => gameId[3] switch
    {
        'E' or 'N' => "USA",
        'J' or 'K' or 'W' => "JPN",
        'P' or 'D' or 'F' or 'H' or 'I' or 'S' or 'X' or 'Y' or 'Z' => "EUR",
        _ => null,
    };

    private static bool TrySlot(string value, out char slot)
    {
        slot = value.Length == 1 ? char.ToLowerInvariant(value[0]) : '\0';
        return slot is 'a' or 'b';
    }

    private static bool TryParseRawUnit(
        string[] parts,
        out char slot,
        out string region,
        out string? variant)
    {
        slot = '\0';
        region = string.Empty;
        variant = null;
        if (parts is ["raw", var slotText, var parsedRegion] &&
            TrySlot(slotText, out slot) && Regions.Contains(parsedRegion, StringComparer.Ordinal))
        {
            region = parsedRegion;
            return true;
        }
        if (parts is ["raw", var variantSlotText, var variantRegion, var parsedVariant] &&
            TrySlot(variantSlotText, out slot) && Regions.Contains(variantRegion, StringComparer.Ordinal) &&
            IsRawCardVariant(parsedVariant))
        {
            region = variantRegion;
            variant = parsedVariant;
            return true;
        }
        return false;
    }

    private static bool TryParseGciUnit(
        string[] parts,
        out char slot,
        out string gameId,
        out string? identity)
    {
        slot = '\0';
        gameId = string.Empty;
        identity = null;
        if (parts is ["gci", var slotText, var parsedGameId] &&
            TrySlot(slotText, out slot) && IsGameId(parsedGameId))
        {
            gameId = parsedGameId;
            return true;
        }
        if (parts is ["gci", var identitySlotText, var identityGameId, var parsedIdentity] &&
            TrySlot(identitySlotText, out slot) && IsGameId(identityGameId) && IsGciIdentity(parsedIdentity))
        {
            gameId = identityGameId;
            identity = parsedIdentity;
            return true;
        }
        return false;
    }

    private static bool IsGameId(string value) =>
        value.Length == 6 && value.All(char.IsAsciiLetterOrDigit);

    private static bool IsHexTitleId(string value) =>
        value.Length == 8 && value.All(Uri.IsHexDigit);

    private static bool IsRawCardVariant(string value) =>
        value.Length > 0 && value.Length <= 10 && value.All(char.IsAsciiDigit);

    private static bool IsGciIdentity(string value) =>
        value.Length == 16 && value.All(char.IsAsciiHexDigit) && value.All(character => !char.IsAsciiLetter(character) || char.IsUpper(character));

    private static string RawUnitId(char slot, string region, string? variant) =>
        variant is null
            ? $"dolphin/gc/raw/{slot}/{region}"
            : $"dolphin/gc/raw/{slot}/{region}/{variant}";
    private static string GciUnitId(char slot, string gameId) => $"dolphin/gc/gci/{slot}/{gameId}";
    private static string GciUnitId(char slot, string gameId, string identity) =>
        $"{GciUnitId(slot, gameId)}/{identity}";
    private static string WiiUnitId(string titleId) => $"dolphin/wii/title/00010000/{titleId}";

    private static StringComparer PathComparer => FilePathComparison.Comparer;

    private sealed record DolphinState(
        string UserDirectory,
        IniDocument Configuration,
        IReadOnlyDictionary<(char Slot, string GameId), string> PerGameGciOverrides);

    private sealed record GciFile(string Path, string GameId, string Identity);

    private sealed record RawCard(string Path, string? Variant);

    private sealed class IniDocument
    {
        private readonly Dictionary<string, Dictionary<string, string>> _sections =
            new(StringComparer.OrdinalIgnoreCase);

        public string? Get(string section, string key) =>
            _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value)
                ? value
                : null;

        public static IniDocument Read(string path)
        {
            var document = new IniDocument();
            if (!File.Exists(path))
                return document;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                Dictionary<string, string>? section = null;
                while (reader.ReadLine() is { } raw)
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] is '#' or ';')
                        continue;
                    if (line[0] == '[' && line[^1] == ']')
                    {
                        var name = line[1..^1].Trim();
                        if (name.Length == 0)
                            throw new DolphinConfigurationFormatException("Dolphin contains an empty INI section.");
                        if (!document._sections.TryGetValue(name, out section))
                        {
                            section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            document._sections[name] = section;
                        }
                        continue;
                    }
                    var separator = line.IndexOf('=');
                    if (separator <= 0 || section is null)
                        continue;
                    section[line[..separator].Trim()] = line[(separator + 1)..].Trim();
                }
                return document;
            }
            catch (DolphinConfigurationFormatException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new DolphinConfigurationFormatException($"{Path.GetFileName(path)} could not be read.", ex);
            }
        }
    }
}

public sealed class DolphinConfigurationFormatException : SaveProviderConfigurationException
{
    public DolphinConfigurationFormatException(string message) : base(message)
    {
    }

    public DolphinConfigurationFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed record DolphinSaveLocationInfo(
    string UserDirectory,
    IReadOnlyList<string> SaveLocations);
