using EmuShelf.Core.TexturePacks;
using EmuShelf.Infrastructure.TexturePacks;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class TexturePackInventoryCacheTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task Snapshot_RoundTripsPerInstallation()
    {
        var cache = new TexturePackInventoryCache(AppPaths);
        var snapshot = new TexturePackInventorySnapshot(
            "pcsx2",
            "portable:pcsx2/main",
            Path.Combine(BaseDirectory, "textures"),
            DateTimeOffset.Parse("2026-07-26T12:00:00Z"),
            TexturePackRootStatus.Ready,
            [
                new TexturePackInventoryEntry(
                    "SLUS-21291",
                    Path.Combine(BaseDirectory, "textures", "SLUS-21291"),
                    TexturePackContentStatus.Usable,
                    [new TexturePackMatchKey(TexturePackMatchRule.ExactSerial, "SLUS-21291")]),
            ]);

        await cache.SaveAsync(snapshot);

        var loaded = Assert.IsType<TexturePackInventorySnapshot>(
            await cache.LoadAsync(snapshot.InstallationId));
        Assert.Equal(snapshot.EmulatorId, loaded.EmulatorId);
        Assert.Equal(snapshot.InstallationId, loaded.InstallationId);
        Assert.Equal(snapshot.RootDirectory, loaded.RootDirectory);
        Assert.Equal(snapshot.ScannedAt, loaded.ScannedAt);
        Assert.Equal(snapshot.RootStatus, loaded.RootStatus);
        var loadedEntry = Assert.Single(loaded.Entries);
        Assert.Equal("SLUS-21291", loadedEntry.PackKey);
        Assert.Equal(TexturePackContentStatus.Usable, loadedEntry.ContentStatus);
        Assert.Equal(
            new TexturePackMatchKey(TexturePackMatchRule.ExactSerial, "SLUS-21291"),
            Assert.Single(loadedEntry.MatchKeys));
        Assert.Null(await cache.LoadAsync("another-installation"));
    }

    [Fact]
    public async Task CorruptSnapshot_IsIgnored()
    {
        var cache = new TexturePackInventoryCache(AppPaths);
        var snapshot = new TexturePackInventorySnapshot(
            "dolphin",
            "dolphin-main",
            Path.Combine(BaseDirectory, "textures"),
            DateTimeOffset.UtcNow,
            TexturePackRootStatus.Ready,
            []);
        await cache.SaveAsync(snapshot);
        var cacheFile = Assert.Single(Directory.EnumerateFiles(
            Path.Combine(AppPaths.CacheDirectory, "TexturePacks"),
            "*.json"));
        await File.WriteAllTextAsync(cacheFile, "not json");

        Assert.Null(await cache.LoadAsync(snapshot.InstallationId));
    }

    [Fact]
    public async Task StructurallyIncompleteSnapshot_IsIgnored()
    {
        var cache = new TexturePackInventoryCache(AppPaths);
        var snapshot = new TexturePackInventorySnapshot(
            "duckstation",
            "duckstation-main",
            Path.Combine(BaseDirectory, "textures"),
            DateTimeOffset.UtcNow,
            TexturePackRootStatus.Ready,
            []);
        await cache.SaveAsync(snapshot);
        var cacheFile = Assert.Single(Directory.EnumerateFiles(
            Path.Combine(AppPaths.CacheDirectory, "TexturePacks"),
            "*.json"));
        await File.WriteAllTextAsync(
            cacheFile,
            "{\"SchemaVersion\":1,\"Snapshot\":{\"InstallationId\":\"duckstation-main\"}}");

        Assert.Null(await cache.LoadAsync(snapshot.InstallationId));
    }
}
