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
        // 1:1 with the save folder (#5), and state compatibility resolves from the core binary's
        // architecture alone (the version is left unknown) when the core's .info file is absent — so a
        // state is not silently dropped (compatibility null -> zero units), which is common on a Steam
        // Deck Flatpak RetroArch or a bare core dropped in beside it.
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
        // start it with --version this would fail (compatibility null); instead the binary's
        // architecture keys it (version left unknown), so states resolve and no process is ever started.
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
    public void StandaloneState_FallsBackToHostArchitectureWhenTheEmulatorBinaryIsUnreadable()
    {
        // Regression for "PCSX2 states never upload from the Steam Deck": the emulator's architecture
        // could not be read (a Flatpak/wrapper with no parseable binary and no --show-arch), so
        // compatibility resolved to null and every state was silently dropped (compatibilityKey=none in
        // the Deck log). The emulator runs on THIS machine, so the host architecture is a sound fallback:
        // compatibility must resolve, and the key must carry a real architecture, not "(none)".
        Directory.CreateDirectory(_root);
        var stateDir = Path.Combine(_root, "pcsx2-states");
        Directory.CreateDirectory(stateDir);
        WriteState(stateDir, "game.p2s", 1);

        var descriptor = SaveProviderRegistry.Find("playstation2")!;
        var context = new SaveProviderContext(
            DirectoryOverride: null,
            EmulatorDirectory: _root,
            IsFlatpak: false,
            Paths: new AppPaths(_root),
            ExecutablePath: Path.Combine(_root, "unreadable-binary"), // no parseable architecture
            StateDirectoryOverride: stateDir);
        var provider = (AuxiliarySyncProvider)SaveProviderRegistry.WithOptionalContent(
            descriptor,
            descriptor.CreateProvider(context)!,
            context,
            includeSaveStates: true,
            includeBaseSaves: false);

        Assert.True(provider.HasStateCompatibility);
        Assert.StartsWith("st1|pcsx2|", provider.StateCompatibilityKey);
        Assert.DoesNotContain("(none)", provider.StateCompatibilityKey);
    }

    [Fact]
    public void RetroArchStateCompat_IgnoresFrontendVersionSoStatesRestoreCrossMachine()
    {
        // Regression for "states upload from the Deck but never restore on Windows": a libretro state
        // is produced by the core, so its compatibility key must depend on the core + architecture,
        // not the RetroArch frontend version. Two machines almost never run the same RetroArch build,
        // and keying on it marked every state "written by a different emulator version". Same core =>
        // same key regardless of which frontend produced it.
        Directory.CreateDirectory(_root);
        var core = WriteElfCore(Path.Combine(_root, "genesis_plus_gx_libretro.so"));
        var deckFrontend = Path.Combine(_root, "retroarch-deck");
        File.WriteAllBytes(deckFrontend, new byte[100]);
        var windowsFrontend = Path.Combine(_root, "retroarch-win.exe");
        File.WriteAllBytes(windowsFrontend, new byte[500]);
        var descriptor = SaveProviderRegistry.Find("megadrive")!;

        string? KeyFor(string frontend)
        {
            var context = new SaveProviderContext(
                DirectoryOverride: null,
                EmulatorDirectory: _root,
                IsFlatpak: false,
                Paths: new AppPaths(_root),
                CorePath: core,
                ExecutablePath: frontend,
                StateDirectoryOverride: _root);
            var provider = (AuxiliarySyncProvider)SaveProviderRegistry.WithOptionalContent(
                descriptor,
                descriptor.CreateProvider(context)!,
                context,
                includeSaveStates: true,
                includeBaseSaves: false);
            return provider.GetCompatibility("retroarch/megadrive/states/game.state");
        }

        var deckKey = KeyFor(deckFrontend);
        Assert.NotNull(deckKey);
        Assert.Equal(deckKey, KeyFor(windowsFrontend));
    }

    [Fact]
    public void RetroArchStateCompat_IgnoresCoreFileLengthSoDeckSoRestoresOnWindowsDll()
    {
        // The exact reported failure: a state uploaded from the Deck (a .so core, no info file) never
        // restored on Windows (a .dll core of the same id). The previous fix keyed the missing version
        // off the core file's byte length, which necessarily differs between a .so and a .dll, so the
        // two keys mismatched and every Deck state was skipped. The identity now keys on core id + CPU
        // architecture (both identical), leaving the version unknown, so the state restores cross-OS.
        Directory.CreateDirectory(_root);
        var deckCore = WriteElfCore(Path.Combine(_root, "genesis_plus_gx_libretro.so"));
        var windowsCore = WriteElfCore(Path.Combine(_root, "genesis_plus_gx_libretro.dll"), padTo: 4096);
        Assert.NotEqual(new FileInfo(deckCore).Length, new FileInfo(windowsCore).Length);
        var descriptor = SaveProviderRegistry.Find("megadrive")!;

        string? KeyFor(string corePath)
        {
            var context = new SaveProviderContext(
                DirectoryOverride: null,
                EmulatorDirectory: _root,
                IsFlatpak: false,
                Paths: new AppPaths(_root),
                CorePath: corePath,
                StateDirectoryOverride: _root);
            var provider = (AuxiliarySyncProvider)SaveProviderRegistry.WithOptionalContent(
                descriptor,
                descriptor.CreateProvider(context)!,
                context,
                includeSaveStates: true,
                includeBaseSaves: false);
            return provider.GetCompatibility("retroarch/megadrive/states/game.state");
        }

        var deckKey = KeyFor(deckCore);
        var windowsKey = KeyFor(windowsCore);
        Assert.NotNull(deckKey);
        Assert.NotNull(windowsKey);
        Assert.True(StateCompatibility.AreCompatible(windowsKey!, deckKey!));
    }

    [Fact]
    public void RetroArchStateCompat_AndroidCoreIdMatchesDesktop_SoSnes9xStateRestoresAcrossPlatforms()
    {
        // End-to-end for the arch-portable relaxation, driving the REAL core-id derivation (not a
        // hardcoded literal). Android RetroArch cores are named "<core>_libretro_android.so"; the
        // "_android" build tag must be stripped so the compat id is "retroarch:snes9x" on both platforms
        // — otherwise the id gate rejects a cross-platform state before architecture is even considered,
        // and the snes9x allowlist never gets a chance. See docs/android-save-sync-model.md.
        Directory.CreateDirectory(_root);
        var desktopCore = WriteElfCore(Path.Combine(_root, "snes9x_libretro.dll"), machine: ElfMachineX86_64);
        var androidCore = WriteElfCore(Path.Combine(_root, "snes9x_libretro_android.so"), machine: ElfMachineAArch64);
        var descriptor = SaveProviderRegistry.Find("snes")!;

        string? KeyFor(string corePath)
        {
            var context = new SaveProviderContext(
                DirectoryOverride: null,
                EmulatorDirectory: _root,
                IsFlatpak: false,
                Paths: new AppPaths(_root),
                CorePath: corePath,
                StateDirectoryOverride: _root);
            var provider = (AuxiliarySyncProvider)SaveProviderRegistry.WithOptionalContent(
                descriptor,
                descriptor.CreateProvider(context)!,
                context,
                includeSaveStates: true,
                includeBaseSaves: false);
            return provider.GetCompatibility("retroarch/snes/states/game.state");
        }

        var desktopKey = KeyFor(desktopCore);
        var androidKey = KeyFor(androidCore);
        Assert.NotNull(desktopKey);
        Assert.NotNull(androidKey);
        // Same core id on both platforms (the "_android" tag stripped), and different architectures.
        Assert.Contains("|retroarch_snes9x|", desktopKey);
        Assert.Contains("|retroarch_snes9x|", androidKey);
        Assert.Contains("|x64|", desktopKey);
        Assert.Contains("|arm64|", androidKey);
        // And because snes9x is arch-portable, the two reconcile despite the x64↔arm64 difference.
        Assert.True(StateCompatibility.AreCompatible(desktopKey!, androidKey!));
        Assert.True(StateCompatibility.AreCompatible(androidKey!, desktopKey!));
    }

    [Fact]
    public void StateCompat_UnknownVersionMatchesOnCoreAndArch_KnownVersionsAreGuarded()
    {
        var deck = StateCompatibility.Create("retroarch:genesis_plus_gx", null, "x64")!;
        var windows = StateCompatibility.Create("retroarch:genesis_plus_gx", null, "x64")!;
        // Unknown version on both -> compatible on same id + arch (the Deck<->Windows bare-core case).
        Assert.True(StateCompatibility.AreCompatible(windows.Key, deck.Key));

        // Asymmetric info-file availability: one side reads display_version, the other cannot. Still
        // restores — an unknown version never blocks a known one.
        var known = StateCompatibility.Create("retroarch:genesis_plus_gx", "1.7.4", "x64")!;
        Assert.True(StateCompatibility.AreCompatible(known.Key, deck.Key));
        Assert.True(StateCompatibility.AreCompatible(deck.Key, known.Key));

        // Two real, different versions keep the same-build guard; equal versions match.
        var v220 = StateCompatibility.Create("pcsx2", "2.2.0", "x64")!;
        var v240 = StateCompatibility.Create("pcsx2", "2.4.0", "x64")!;
        Assert.False(StateCompatibility.AreCompatible(v220.Key, v240.Key));
        Assert.True(StateCompatibility.AreCompatible(v220.Key, StateCompatibility.Create("pcsx2", "2.2.0", "x64")!.Key));

        // Architecture and core id are always hard gates, even when the version is unknown.
        Assert.False(StateCompatibility.AreCompatible(
            deck.Key, StateCompatibility.Create("retroarch:genesis_plus_gx", null, "arm64")!.Key));
        Assert.False(StateCompatibility.AreCompatible(
            deck.Key, StateCompatibility.Create("retroarch:snes9x", null, "x64")!.Key));

        // Legacy opaque keys (uploaded before this format) keep exact-match behaviour.
        Assert.True(StateCompatibility.AreCompatible("retroarch-1-0-x64-abc123", "retroarch-1-0-x64-abc123"));
        Assert.False(StateCompatibility.AreCompatible("retroarch-1-0-x64-abc123", "retroarch-1-0-x64-def456"));
        Assert.False(StateCompatibility.AreCompatible(deck.Key, "retroarch-1-0-x64-abc123"));
    }

    [Fact]
    public void StateCompat_ArchPortableCores_RestoreCrossArchitecture_OthersStillGate()
    {
        // snes9x save states are architecture-independent, so a Windows-x64 state restores on an
        // Android-arm64 machine and back. See docs/android-save-sync-model.md.
        var windows = StateCompatibility.Create("retroarch:snes9x", null, "x64")!;
        var android = StateCompatibility.Create("retroarch:snes9x", null, "arm64")!;
        Assert.True(StateCompatibility.AreCompatible(windows.Key, android.Key));
        Assert.True(StateCompatibility.AreCompatible(android.Key, windows.Key));

        // The arch-portable relaxation does not weaken the id gate or the known-version guard.
        Assert.False(StateCompatibility.AreCompatible(
            windows.Key, StateCompatibility.Create("retroarch:mgba", null, "arm64")!.Key));
        Assert.False(StateCompatibility.AreCompatible(
            StateCompatibility.Create("retroarch:snes9x", "1.62.3", "x64")!.Key,
            StateCompatibility.Create("retroarch:snes9x", "1.63.0", "arm64")!.Key));

        // A core NOT on the allowlist keeps the hard cross-architecture gate (mGBA is the counter-case:
        // its states are build/arch-sensitive and must not auto-restore across platforms).
        Assert.False(StateCompatibility.AreCompatible(
            StateCompatibility.Create("retroarch:mgba", null, "x64")!.Key,
            StateCompatibility.Create("retroarch:mgba", null, "arm64")!.Key));
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
    // padTo lets a test create two cores that read as the same architecture but differ in byte length.
    private const byte ElfMachineX86_64 = 0x3e; // EM_X86_64 (62)
    private const byte ElfMachineAArch64 = 0xb7; // EM_AARCH64 (183)

    private static string WriteElfCore(string path, int padTo = 20, byte machine = ElfMachineX86_64)
    {
        var bytes = new byte[Math.Max(20, padTo)];
        bytes[0] = 0x7f;
        bytes[1] = (byte)'E';
        bytes[2] = (byte)'L';
        bytes[3] = (byte)'F';
        bytes[4] = 0x02; // EI_CLASS = 64-bit
        bytes[5] = 0x01; // EI_DATA = little-endian
        bytes[18] = machine; // e_machine
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

    [Fact]
    public async Task DivergentPrefixes_BatteryIsSystemScoped_StatesStayEmulatorScoped()
    {
        // A provider whose battery namespace (playstation/) differs from its state namespace
        // (duckstation/) — the exact split this change introduces. The auxiliary wrapper must key states
        // off StateNamespacePrefix, keep owning the system-scoped battery unit, and NOT re-claim the old
        // emulator-scoped battery key left frozen by the migration.
        var states = Directory.CreateDirectory(Path.Combine(_root, "divergent-states")).FullName;
        WriteState(states, "GAME.state1", 1);
        var provider = new AuxiliarySyncProvider(
            new DivergentProvider(),
            [new("states", _ => states, path => path.Contains(".state", StringComparison.Ordinal))],
            new StateCompatibility("duckstation-1-0-x64", "1.0 · x64"));

        // Emitted state ids hang off the emulator-scoped StateNamespacePrefix, not the battery prefix.
        var units = await provider.GetSaveUnitsAsync(TestContext.Current.CancellationToken);
        Assert.Contains(units, unit => unit.UnitId.StartsWith("duckstation/states/", StringComparison.Ordinal));
        Assert.DoesNotContain(units, unit => unit.UnitId.StartsWith("playstation/states/", StringComparison.Ordinal));

        Assert.True(provider.OwnsUnit("playstation/shared/card1"));       // system-scoped battery
        Assert.True(provider.OwnsUnit("duckstation/states/GAME.state1")); // emulator-scoped state
        Assert.False(provider.OwnsUnit("duckstation/shared/card1"));      // frozen old battery key: inert

        var selected = provider.SelectRemoteUnits(new[]
        {
            new SaveUnitSnapshot("playstation/shared/card1", "h1", default, null),
            new SaveUnitSnapshot("duckstation/states/GAME.state1", "h2", default, "duckstation-1-0-x64"),
            new SaveUnitSnapshot("duckstation/shared/card1", "h3", default, null),
        }).Select(snapshot => snapshot.UnitId).ToArray();
        Assert.Contains("playstation/shared/card1", selected);
        Assert.Contains("duckstation/states/GAME.state1", selected);
        Assert.DoesNotContain("duckstation/shared/card1", selected);
    }

    private sealed class EmptyProvider : ISaveLocationProvider
    {
        public string SystemId => "test";
        public string UnitIdPrefix => "test/";
        public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SaveUnit>>([]);
        public SaveUnitLocation? ResolveUnit(string unitId) => null;
    }

    // Battery namespace (system-scoped) deliberately differs from the state namespace (emulator-scoped).
    private sealed class DivergentProvider : ISaveLocationProvider
    {
        public string SystemId => "playstation";
        public string UnitIdPrefix => "playstation/";
        public string StateNamespacePrefix => "duckstation/";
        public Task<IReadOnlyList<SaveUnit>> GetSaveUnitsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SaveUnit>>([new("playstation/shared/card1", "Card", SaveUnitKind.File)]);
        public SaveUnitLocation? ResolveUnit(string unitId) =>
            unitId == "playstation/shared/card1"
                ? new SaveUnitLocation(Path.Combine(Path.GetTempPath(), "card"), Path.GetTempPath(), SaveUnitKind.File)
                : null;
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
