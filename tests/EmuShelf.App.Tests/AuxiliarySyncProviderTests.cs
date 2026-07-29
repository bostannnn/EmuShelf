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
    public async Task EnumeratesPerFileCheatsAndOnlyNewestManualStates()
    {
        var cheats = Directory.CreateDirectory(Path.Combine(_root, "cheats")).FullName;
        var states = Directory.CreateDirectory(Path.Combine(_root, "states")).FullName;
        File.WriteAllText(Path.Combine(cheats, "GAME.cht"), "cheat");
        WriteState(states, "GAME.state1", 1);
        WriteState(states, "GAME.state2", 2);
        WriteState(states, "GAME.state3", 3);
        WriteState(states, "GAME.state.auto", 4);

        var provider = new AuxiliarySyncProvider(
            new EmptyProvider(),
            [
                new(AuxiliaryContentKind.Cheats, "cheats", _ => cheats, path => path.EndsWith(".cht")),
                new(
                    AuxiliaryContentKind.SaveStates,
                    "states",
                    _ => states,
                    path => AuxiliarySyncProvider.IsManualState(path) && path.Contains(".state", StringComparison.Ordinal),
                    StateGroup: AuxiliarySyncProvider.DefaultStateGroup),
            ],
            new StateCompatibility("retroarch-1-0-x64", "1.0 · x64"),
            stateRetention: 2);

        var units = await provider.GetSaveUnitsAsync(TestContext.Current.CancellationToken);

        Assert.Contains(units, unit => unit.UnitId == "test/cheats/GAME.cht");
        Assert.Contains(units, unit => unit.UnitId.EndsWith("/GAME.state3", StringComparison.Ordinal));
        Assert.Contains(units, unit => unit.UnitId.EndsWith("/GAME.state2", StringComparison.Ordinal));
        Assert.DoesNotContain(units, unit => unit.UnitId.EndsWith("/GAME.state1", StringComparison.Ordinal));
        Assert.DoesNotContain(units, unit => unit.UnitId.Contains(".auto", StringComparison.Ordinal));
    }

    [Fact]
    public void IncompatibleStateIsReportedFromCloudMetadata()
    {
        var states = Directory.CreateDirectory(Path.Combine(_root, "states")).FullName;
        var provider = new AuxiliarySyncProvider(
            new EmptyProvider(),
            [new(
                AuxiliaryContentKind.SaveStates,
                "states",
                _ => states,
                _ => true,
                StateGroup: AuxiliarySyncProvider.DefaultStateGroup)],
            new StateCompatibility("current", "current"),
            stateRetention: 3);

        var reason = provider.GetRemoteIncompatibilityReason(new SaveUnitSnapshot(
            "test/states/GAME.state",
            "hash",
            DateTimeOffset.UtcNow,
            Compatibility: "other"));

        Assert.Contains("different emulator version", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoteRetentionSelectsNewestStatesPerGameWithoutDeletingOlderEntries()
    {
        var provider = new AuxiliarySyncProvider(
            new EmptyProvider(),
            [new(
                AuxiliaryContentKind.SaveStates,
                "states",
                _ => _root,
                _ => true,
                StateGroup: AuxiliarySyncProvider.DefaultStateGroup)],
            new StateCompatibility("current", "current"),
            stateRetention: 2);
        var snapshots = Enumerable.Range(1, 4)
            .Select(slot => new SaveUnitSnapshot(
                $"test/states/GAME.state{slot}",
                $"hash{slot}",
                new DateTimeOffset(2026, 1, slot, 0, 0, 0, TimeSpan.Zero)))
            .ToArray();

        var selected = provider.SelectRemoteUnits(snapshots);

        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, snapshot => snapshot.UnitId.EndsWith("state4", StringComparison.Ordinal));
        Assert.Contains(selected, snapshot => snapshot.UnitId.EndsWith("state3", StringComparison.Ordinal));
    }

    [Fact]
    public void RemoteRetentionCountsNewerLocalStatesTowardTheLimit()
    {
        var states = Directory.CreateDirectory(Path.Combine(_root, "states-combined")).FullName;
        WriteState(states, "GAME.state4", 4);
        var provider = new AuxiliarySyncProvider(
            new EmptyProvider(),
            [new(
                AuxiliaryContentKind.SaveStates,
                "states",
                _ => states,
                _ => true,
                StateGroup: AuxiliarySyncProvider.DefaultStateGroup)],
            new StateCompatibility("current", "current"),
            stateRetention: 2);
        var snapshots = Enumerable.Range(1, 3)
            .Select(slot => new SaveUnitSnapshot(
                $"test/states/GAME.state{slot}",
                $"hash{slot}",
                new DateTimeOffset(2026, 1, slot, 0, 0, 0, TimeSpan.Zero)))
            .ToArray();

        var selected = provider.SelectRemoteUnits(snapshots);

        Assert.Single(selected);
        Assert.EndsWith("state3", selected[0].UnitId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentLocations_ReportExactRootsAndKeepOneBrokenSourceAdvisory()
    {
        var cheats = Directory.CreateDirectory(Path.Combine(_root, "cheats-inspected")).FullName;
        var states = Directory.CreateDirectory(Path.Combine(_root, "states-inspected")).FullName;
        File.WriteAllText(Path.Combine(cheats, "GAME.cht"), "cheat");
        WriteState(states, "GAME.state1", 1);
        WriteState(states, "GAME.state2", 2);

        var provider = new AuxiliarySyncProvider(
            new EmptyProvider(),
            [
                new(AuxiliaryContentKind.Cheats, "cheats", _ => cheats, path => path.EndsWith(".cht")),
                new(AuxiliaryContentKind.Patches, "patches", _ => throw new IOException("patch config unreadable"), _ => true),
                new(
                    AuxiliaryContentKind.SaveStates,
                    "states",
                    _ => states,
                    path => path.Contains(".state", StringComparison.Ordinal),
                    StateGroup: AuxiliarySyncProvider.DefaultStateGroup),
            ],
            new StateCompatibility("current", "1.0 · x64"),
            stateRetention: 1);

        var locations = await provider.GetContentLocationsAsync(TestContext.Current.CancellationToken);

        var cheatLocation = Assert.Single(locations, location => location.Kind == AuxiliaryContentKind.Cheats);
        Assert.Equal(Path.GetFullPath(cheats), cheatLocation.Directory);
        Assert.Equal(1, cheatLocation.EligibleFileCount);
        var stateLocation = Assert.Single(locations, location => location.Kind == AuxiliaryContentKind.SaveStates);
        Assert.Equal(1, stateLocation.EligibleFileCount);
        Assert.Equal(2, stateLocation.TotalFileCount);
        Assert.Equal("1.0 · x64", stateLocation.Compatibility);
        var broken = Assert.Single(locations, location => location.Kind == AuxiliaryContentKind.Patches);
        Assert.Null(broken.Directory);
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
