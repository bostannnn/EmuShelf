using EmuShelf.Core.TexturePacks;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class TexturePackInventoryServiceTests
{
    [Fact]
    public async Task ReadyRefresh_IsCachedWithoutRequiringAStartupScan()
    {
        var store = new MemoryStore();
        var snapshot = Snapshot(TexturePackRootStatus.Ready);
        var source = new StubSource(snapshot);
        var service = new TexturePackInventoryService(store);

        Assert.Null(await service.LoadCachedAsync(snapshot.InstallationId));
        Assert.Equal(0, source.ScanCount);

        var state = await service.RefreshAsync(source);

        Assert.False(state.IsStale);
        Assert.Equal(TexturePackRootStatus.Ready, state.ObservedRootStatus);
        Assert.Same(snapshot, await service.LoadCachedAsync(snapshot.InstallationId));
        Assert.Equal(1, source.ScanCount);
    }

    [Theory]
    [InlineData(TexturePackRootStatus.Missing)]
    [InlineData(TexturePackRootStatus.Unreadable)]
    public async Task UnavailableRefresh_PreservesLastGoodInventoryAsStale(TexturePackRootStatus unavailable)
    {
        var store = new MemoryStore();
        var cached = Snapshot(TexturePackRootStatus.Ready);
        await store.SaveAsync(cached);
        var current = Snapshot(unavailable) with { Entries = [], Diagnostic = "Drive unavailable" };

        var state = await new TexturePackInventoryService(store).RefreshAsync(new StubSource(current));

        Assert.True(state.IsStale);
        Assert.Same(cached, state.Snapshot);
        Assert.Equal(unavailable, state.ObservedRootStatus);
        Assert.Equal("Drive unavailable", state.AvailabilityDiagnostic);
    }

    private static TexturePackInventorySnapshot Snapshot(TexturePackRootStatus status) =>
        new(
            "pcsx2",
            "pcsx2-main",
            Path.GetFullPath("textures"),
            DateTimeOffset.Parse("2026-07-26T12:00:00Z"),
            status,
            []);

    private sealed class StubSource(TexturePackInventorySnapshot snapshot) : ITexturePackSource
    {
        public int ScanCount { get; private set; }

        public string EmulatorId => snapshot.EmulatorId;

        public string InstallationId => snapshot.InstallationId;

        public string RootDirectory => snapshot.RootDirectory;

        public Task<TexturePackInventorySnapshot> ScanAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanCount++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class MemoryStore : ITexturePackInventoryStore
    {
        private readonly Dictionary<string, TexturePackInventorySnapshot> _snapshots = new(StringComparer.Ordinal);

        public Task<TexturePackInventorySnapshot?> LoadAsync(
            string installationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshots.TryGetValue(installationId, out var snapshot);
            return Task.FromResult(snapshot);
        }

        public Task SaveAsync(
            TexturePackInventorySnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _snapshots[snapshot.InstallationId] = snapshot;
            return Task.CompletedTask;
        }
    }
}
