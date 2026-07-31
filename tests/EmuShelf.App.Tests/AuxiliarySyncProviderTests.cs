using EmuShelf.App.Services;
using EmuShelf.Core.SaveSync;

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
}
