using EmuShelf.App.Services;
using EmuShelf.Core.SaveSync;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.App.Tests;

public sealed class AuxiliarySyncProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "emushelf-optional-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("v1.20.4", "1.20.4")]
    [InlineData("PPSSPP 1.20.4.0", "1.20.4")]
    [InlineData("RPCS3 v0.0.37-17890", "0.0.37-17890")]
    public void VersionIdentity_IsNormalizedAcrossPackagingFormats(string input, string expected) =>
        Assert.Equal(expected, SaveProviderRegistry.NormalizeVersion(input));

    [Fact]
    public void StateArchitecture_ComesFromTheEmulatorBinaryRatherThanTheFrontendProcess()
    {
        var executable = Path.Combine(_root, "fake-arm64-elf");
        Directory.CreateDirectory(_root);
        var header = new byte[64];
        header[0] = 0x7f;
        header[1] = (byte)'E';
        header[2] = (byte)'L';
        header[3] = (byte)'F';
        header[5] = 1; // little endian
        header[18] = 183; // EM_AARCH64
        File.WriteAllBytes(executable, header);

        Assert.Equal("arm64", SaveProviderRegistry.ReadBinaryArchitecture(executable));
    }

    [Fact]
    public async Task EnumeratesEveryManualState()
    {
        var states = Directory.CreateDirectory(Path.Combine(_root, "states")).FullName;
        WriteState(states, "GAME.state1", 1);
        WriteState(states, "GAME.state2", 2);
        WriteState(states, "GAME.state3", 3);
        WriteState(states, "GAME.state.auto", 4);

        var provider = new AuxiliarySyncProvider(
            new EmptyProvider(),
            [new(
                "states",
                _ => states,
                path => AuxiliarySyncProvider.IsManualState(path) && path.Contains(".state", StringComparison.Ordinal))],
            new StateCompatibility("retroarch-1-0-x64", "1.0 · x64"));

        var units = await provider.GetSaveUnitsAsync(TestContext.Current.CancellationToken);

        Assert.Contains(units, unit => unit.UnitId.EndsWith("/GAME.state3", StringComparison.Ordinal));
        Assert.Contains(units, unit => unit.UnitId.EndsWith("/GAME.state2", StringComparison.Ordinal));
        Assert.Contains(units, unit => unit.UnitId.EndsWith("/GAME.state1", StringComparison.Ordinal));
        Assert.DoesNotContain(units, unit => unit.UnitId.Contains(".auto", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StateOnlyProviderDoesNotClaimOrEnumerateBaseSaves()
    {
        var states = Directory.CreateDirectory(Path.Combine(_root, "states-only")).FullName;
        WriteState(states, "GAME.state1", 1);
        var provider = new AuxiliarySyncProvider(
            new OneSaveProvider(),
            [new("states", _ => states, _ => true)],
            new StateCompatibility("current", "current"),
            includeBaseSaves: false);

        var units = await provider.GetSaveUnitsAsync(TestContext.Current.CancellationToken);
        var selected = provider.SelectRemoteUnits([
            new SaveUnitSnapshot("test/card", "card", DateTimeOffset.UtcNow),
            new SaveUnitSnapshot("test/states/GAME.state1", "state", DateTimeOffset.UtcNow, "current"),
        ]);

        Assert.Single(units);
        Assert.StartsWith("test/states/", units[0].UnitId);
        Assert.False(provider.OwnsUnit("test/card"));
        Assert.Single(selected);
        Assert.StartsWith("test/states/", selected[0].UnitId);
        Assert.Null(provider.ResolveUnit("test/card"));
    }

    // The cheats and patches namespaces were removed because they pointed at each emulator's whole
    // cheats folder, which on DuckStation and PCSX2 is the shipped community database. A remote
    // still holding those payloads from an older build must not be claimed by the state provider:
    // it owns "states" and nothing else.
    [Fact]
    public void RetiredCheatAndPatchNamespacesAreNotClaimed()
    {
        var provider = new AuxiliarySyncProvider(
            new EmptyProvider(),
            [new("states", _ => _root, _ => true)],
            new StateCompatibility("current", "current"));

        Assert.False(provider.OwnsUnit("test/cheats/GAME.cht"));
        Assert.False(provider.OwnsUnit("test/patches/GAME.pnach"));
        Assert.True(provider.OwnsUnit("test/states/GAME.state1"));
    }

    [Fact]
    public void IncompatibleStateIsReportedFromCloudMetadata()
    {
        var states = Directory.CreateDirectory(Path.Combine(_root, "states")).FullName;
        var provider = new AuxiliarySyncProvider(
            new EmptyProvider(),
            [new("states", _ => states, _ => true)],
            new StateCompatibility("current", "current"));

        var reason = provider.GetRemoteIncompatibilityReason(new SaveUnitSnapshot(
            "test/states/GAME.state",
            "hash",
            DateTimeOffset.UtcNow,
            Compatibility: "other"));

        Assert.Contains("different emulator version", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoteSelectionIncludesEveryStateWithoutDeletingOlderEntries()
    {
        var provider = new AuxiliarySyncProvider(
            new EmptyProvider(),
            [new("states", _ => _root, _ => true)],
            new StateCompatibility("current", "current"));
        var snapshots = Enumerable.Range(1, 4)
            .Select(slot => new SaveUnitSnapshot(
                $"test/states/GAME.state{slot}",
                $"hash{slot}",
                new DateTimeOffset(2026, 1, slot, 0, 0, 0, TimeSpan.Zero)))
            .ToArray();

        var selected = provider.SelectRemoteUnits(snapshots);

        Assert.Equal(4, selected.Count);
        Assert.All(snapshots, snapshot => Assert.Contains(snapshot, selected));
    }

    [Fact]
    public async Task ContentLocations_ReportExactRootsAndKeepOneBrokenSourceAdvisory()
    {
        var states = Directory.CreateDirectory(Path.Combine(_root, "states-inspected")).FullName;
        WriteState(states, "GAME.state1", 1);
        WriteState(states, "GAME.state2", 2);

        var provider = new AuxiliarySyncProvider(
            new EmptyProvider(),
            [
                new("states", _ => states, path => path.Contains(".state", StringComparison.Ordinal)),
                new("states", _ => throw new IOException("state config unreadable"), _ => true),
            ],
            new StateCompatibility("current", "1.0 · x64"));

        var locations = await provider.GetContentLocationsAsync(TestContext.Current.CancellationToken);

        var stateLocation = Assert.Single(locations, location => location.Directory is not null);
        Assert.Equal(Path.GetFullPath(states), stateLocation.Directory);
        Assert.Equal(2, stateLocation.EligibleFileCount);
        Assert.Equal(2, stateLocation.TotalFileCount);
        Assert.Equal("1.0 · x64", stateLocation.Compatibility);
        var broken = Assert.Single(locations, location => location.Directory is null);
        Assert.Contains("unreadable", broken.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RetroArchStates_ResolveViaOverrideAndCoreBinaryWhenNoInfoFileExists()
    {
        // Regression for "arcade save state not synced": the RetroArch state folder is overridable
        // 1:1 with the save folder (#5), and state compatibility resolves from the core binary alone
        // — its architecture plus a length token standing in for a missing info-file version — so a
        // state is not silently dropped (compatibility null -> zero units) when the core's .info is
        // absent, which is common on a Steam Deck Flatpak RetroArch or a core dropped in beside it.
        Directory.CreateDirectory(_root);
        var stateDir = Path.Combine(_root, "chosen-states");
        Directory.CreateDirectory(stateDir);
        WriteState(stateDir, "spiderman.state", 1);
        var corePath = WriteElfCore(Path.Combine(_root, "fbneo_libretro.so"));

        var descriptor = SaveProviderRegistry.Find("arcade")!;
        var context = new SaveProviderContext(
            DirectoryOverride: null,
            EmulatorDirectory: _root,
            IsFlatpak: false,
            Paths: new AppPaths(_root),
            CorePath: corePath,
            StateDirectoryOverride: stateDir);
        var saves = descriptor.CreateProvider(context)!;
        var provider = (AuxiliarySyncProvider)SaveProviderRegistry.WithOptionalContent(
            descriptor,
            saves,
            context,
            includeSaveStates: true,
            includeBaseSaves: false);

        Assert.True(provider.HasStateCompatibility);
        var units = await provider.GetSaveUnitsAsync(TestContext.Current.CancellationToken);
        Assert.Contains(units, unit => unit.UnitId.EndsWith("/spiderman.state", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DirectExecutableStates_ResolveWithoutLaunchingTheEmulator()
    {
        // Regression for the "Unknown parameter: --version" dialog: a GUI emulator with no embedded
        // version resource (a Linux binary) must still resolve state compatibility without being run.
        // Here the fake executable is not a launchable program, so if version resolution tried to
        // start it with --version this would fail (compatibility null); instead the binary's length
        // and architecture key it, so states resolve and no process is ever started.
        Directory.CreateDirectory(_root);
        var stateDir = Path.Combine(_root, "pcsx2-states");
        Directory.CreateDirectory(stateDir);
        WriteState(stateDir, "game.p2s", 1);
        var executable = WriteElfCore(Path.Combine(_root, "pcsx2"));

        var descriptor = SaveProviderRegistry.Find("playstation2")!;
        var context = new SaveProviderContext(
            DirectoryOverride: null,
            EmulatorDirectory: _root,
            IsFlatpak: false,
            Paths: new AppPaths(_root),
            ExecutablePath: executable,
            StateDirectoryOverride: stateDir);
        var saves = descriptor.CreateProvider(context)!;
        var provider = (AuxiliarySyncProvider)SaveProviderRegistry.WithOptionalContent(
            descriptor,
            saves,
            context,
            includeSaveStates: true,
            includeBaseSaves: false);

        Assert.True(provider.HasStateCompatibility);
        var units = await provider.GetSaveUnitsAsync(TestContext.Current.CancellationToken);
        Assert.Contains(units, unit => unit.UnitId.EndsWith("/game.p2s", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StateScoping_LimitsLaunchSyncToTheLaunchedGamesStates()
    {
        // Regression for "launching Bully syncs every game's states": on a launch/exit pass the state
        // phase is scoped to the launched game's keys (its file stem + serials). RetroArch names
        // states after the ROM (Bully.state), PCSX2 after the serial (SLUS-21269 (...).p2s); both
        // match, another game's state does not. A manual Sync all passes no keys and takes everything.
        var states = Directory.CreateDirectory(Path.Combine(_root, "scoped-states")).FullName;
        WriteState(states, "Bully.state", 1);
        WriteState(states, "Bully.state1", 2);
        WriteState(states, "SLUS-21269 (ABCD1234).00.p2s", 3);
        WriteState(states, "OtherGame.state", 4);

        AuxiliarySyncProvider Build(IReadOnlyCollection<string>? keys) => new(
            new EmptyProvider(),
            [new("states", _ => states, path =>
                path.Contains(".state", StringComparison.Ordinal) ||
                path.EndsWith(".p2s", StringComparison.Ordinal))],
            new StateCompatibility("current", "1.0 · x64"),
            includeBaseSaves: false,
            stateGameKeys: keys);

        var scoped = (await Build(["Bully", "SLUS-21269"])
            .GetSaveUnitsAsync(TestContext.Current.CancellationToken))
            .Select(unit => unit.UnitId)
            .ToArray();
        Assert.Contains(scoped, id => id.EndsWith("/Bully.state", StringComparison.Ordinal));
        Assert.Contains(scoped, id => id.EndsWith("/Bully.state1", StringComparison.Ordinal));
        Assert.Contains(scoped, id => id.Contains("SLUS-21269", StringComparison.Ordinal));
        Assert.DoesNotContain(scoped, id => id.Contains("OtherGame", StringComparison.OrdinalIgnoreCase));

        // Manual Sync all (no keys) still takes every state.
        var unscoped = await Build(null).GetSaveUnitsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(4, unscoped.Count);
    }

    // A minimal little-endian x86-64 ELF header, enough for the architecture reader to identify it.
    private static string WriteElfCore(string path)
    {
        var bytes = new byte[20];
        bytes[0] = 0x7f;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        bytes[4] = 0x02; // EI_CLASS = 64-bit
        bytes[5] = 0x01; // EI_DATA = little-endian
        bytes[18] = 0x3e; // e_machine = EM_X86_64 (62)
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void WriteState(string directory, string name, int day)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class EmptyProvider : ISaveLocationProvider
    {
        public string SystemId => "test";
        public string UnitIdPrefix => "test/";
        public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SaveUnit>>([]);
        public SaveUnitLocation? ResolveUnit(string unitId) => null;
    }

    private sealed class OneSaveProvider : ISaveLocationProvider
    {
        public string SystemId => "test";
        public string UnitIdPrefix => "test/";
        public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SaveUnit>>([new("test/card", "Card", SaveUnitKind.File)]);
        public SaveUnitLocation? ResolveUnit(string unitId) =>
            unitId == "test/card" ? new SaveUnitLocation(Path.Combine(Path.GetTempPath(), "card"), Path.GetTempPath(), SaveUnitKind.File) : null;
        public bool OwnsUnit(string unitId) => unitId == "test/card";
        public IReadOnlyList<SaveUnitSnapshot> SelectRemoteUnits(IReadOnlyList<SaveUnitSnapshot> snapshots) =>
            snapshots.Where(snapshot => OwnsUnit(snapshot.UnitId)).ToArray();
    }
}
