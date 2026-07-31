using System.IO.Compression;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.TexturePacks;
using EmuShelf.Integrations.Emulators.Dolphin;
using EmuShelf.Integrations.Emulators.DuckStation;
using EmuShelf.Integrations.Emulators.Pcsx2;
using EmuShelf.Integrations.Emulators.Ppsspp;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public sealed class TexturePackSourceTests : TempAppDirectoryTestBase
{
    [Fact]
    public async Task MissingRoot_IsReportedWithoutInventingEntries()
    {
        var root = Path.Combine(BaseDirectory, "missing");
        var source = new Pcsx2TexturePackSource("pcsx2-main", root);

        var snapshot = await source.ScanAsync();

        Assert.Equal(TexturePackRootStatus.Missing, snapshot.RootStatus);
        Assert.Empty(snapshot.Entries);
        Assert.Equal(Path.GetFullPath(root), snapshot.RootDirectory);
    }

    [Fact]
    public async Task Pcsx2_RequiresExactReplacementsDirectoryAndLoaderShapedFilename()
    {
        var root = Path.Combine(BaseDirectory, "pcsx2-textures");
        await WriteFileAsync(
            Path.Combine(root, "SLUS-21291", "replacements", "100553481f443474-1bb259fa92e8f8d7-000059d4.dds"),
            "replacement");
        await WriteFileAsync(
            Path.Combine(root, "SLUS-21292", "replacements", "1003626f782b0fd6-20902241-r250x31-00001614.png"),
            "region replacement");
        await WriteFileAsync(
            Path.Combine(root, "SCUS-99999", "dumps", "1234567890abcdef-00000000.png"),
            "dump");
        await WriteFileAsync(
            Path.Combine(root, "SLES-00001", "Replacements", "1234567890abcdef-00000000.png"),
            "wrong-case");
        await WriteFileAsync(
            Path.Combine(root, "SLES-00002", "replacements", "cover.png"),
            "not-a-hash");
        await WriteFileAsync(
            Path.Combine(root, "slus-21293", "replacements", "1003626f782b0fd6-20902241.png"),
            "wrong serial case");

        var snapshot = await new Pcsx2TexturePackSource("pcsx2-main", root).ScanAsync();

        Assert.Equal(TexturePackRootStatus.Ready, snapshot.RootStatus);
        Assert.Equal(TexturePackContentStatus.Usable, Entry(snapshot, "SLUS-21291").ContentStatus);
        Assert.Equal(TexturePackContentStatus.EmptyOrDumpsOnly, Entry(snapshot, "SCUS-99999").ContentStatus);
        Assert.Equal(TexturePackContentStatus.UnrecognizedLayout, Entry(snapshot, "SLES-00001").ContentStatus);
        Assert.Equal(TexturePackContentStatus.EmptyOrDumpsOnly, Entry(snapshot, "SLES-00002").ContentStatus);
        Assert.Equal(TexturePackContentStatus.UnrecognizedLayout, Entry(snapshot, "slus-21293").ContentStatus);

        var matches = TexturePackMatcher.Match(
            snapshot.Entries,
            [new GameIdentifier(GameIdentifierKind.Serial, "SLUS-21291", "fixture")]);
        Assert.Equal("SLUS-21291", Assert.Single(matches).PackKey);
    }

    [Fact]
    public async Task DuckStation_RecognizesCurrentLegacyAndAliasedContent_ButNotDumps()
    {
        var root = Path.Combine(BaseDirectory, "duckstation-textures");
        await WriteFileAsync(
            Path.Combine(root, "SLUS-00001", "replacements", $"vram-write-{new string('A', 32)}.png"),
            "current");
        await WriteFileAsync(
            Path.Combine(root, "SLUS-00002", "texpage-C16-0123456789ABCDEF-64x64-0-0-64x64.webp"),
            "legacy");
        await WriteFileAsync(
            Path.Combine(root, "SLUS-00003", "dumps", $"vram-write-{new string('B', 32)}.png"),
            "dump");
        await WriteFileAsync(
            Path.Combine(root, "SLUS-00004", "replacements", "custom", "sky.jpg"),
            "alias-target");
        await WriteFileAsync(
            Path.Combine(root, "SLUS-00004", "config.yaml"),
            $"Aliases:\n  vram-write-{new string('C', 32)}: custom/sky.jpg\n");
        await WriteFileAsync(
            Path.Combine(root, "SLUS-00005", "replacements", "custom", "sky.jpg"),
            "invalid-alias-target");
        await WriteFileAsync(
            Path.Combine(root, "SLUS-00005", "config.yaml"),
            "Aliases:\n  replacement: custom/sky.jpg\n");
        await WriteFileAsync(
            Path.Combine(root, "SLUS-00006", "replacements", "TEXPAGE-P4-001BF4FA62B223BC-4B314B4E8613BF8F-64x256-96-96-32x32-P1-15.png"),
            "wrong structural case");

        var snapshot = await new DuckStationTexturePackSource("duckstation-main", root).ScanAsync();

        Assert.Equal(TexturePackContentStatus.Usable, Entry(snapshot, "SLUS-00001").ContentStatus);
        Assert.Equal(TexturePackContentStatus.Usable, Entry(snapshot, "SLUS-00002").ContentStatus);
        Assert.Equal(TexturePackContentStatus.EmptyOrDumpsOnly, Entry(snapshot, "SLUS-00003").ContentStatus);
        Assert.Equal(TexturePackContentStatus.Usable, Entry(snapshot, "SLUS-00004").ContentStatus);
        Assert.Equal(TexturePackContentStatus.EmptyOrDumpsOnly, Entry(snapshot, "SLUS-00005").ContentStatus);
        Assert.Equal(TexturePackContentStatus.EmptyOrDumpsOnly, Entry(snapshot, "SLUS-00006").ContentStatus);
    }

    [Fact]
    public async Task Dolphin_UsesDirectIdsMarkersAndSharedPacks_WithExactDirectoryPrecedence()
    {
        var root = Path.Combine(BaseDirectory, "dolphin-textures");
        Directory.CreateDirectory(Path.Combine(root, "GZLE01"));
        await WriteFileAsync(Path.Combine(root, "GZL", "tex1_prefix_0123.png"), "prefix");
        await WriteFileAsync(Path.Combine(root, "RMG", "tex1_region_0123.dds"), "region");
        await WriteFileAsync(Path.Combine(root, "Wind Waker Pack", "gameids", "GZLE01.txt"), "");
        await WriteFileAsync(Path.Combine(root, "Wind Waker Pack", "tex1_marker_0123.png"), "marker");
        await WriteFileAsync(Path.Combine(root, "Shared Pack", "gameids", "all.txt"), "");
        await WriteFileAsync(Path.Combine(root, "Shared Pack", "tex1_shared_0123.png"), "shared");
        await WriteFileAsync(Path.Combine(root, "gm4e01", "tex1_wrong_case_0123.png"), "wrong-case");

        var snapshot = await new DolphinTexturePackSource("dolphin-main", root).ScanAsync();
        var windWakerMatches = TexturePackMatcher.Match(
            snapshot.Entries,
            [new GameIdentifier(GameIdentifierKind.DiscId, "GZLE01", "fixture")]);
        var marioMatches = TexturePackMatcher.Match(
            snapshot.Entries,
            [new GameIdentifier(GameIdentifierKind.DiscId, "RMGE01", "fixture")]);

        Assert.Equal(TexturePackContentStatus.EmptyOrDumpsOnly, Entry(snapshot, "GZLE01").ContentStatus);
        Assert.DoesNotContain(windWakerMatches, entry => entry.PackKey == "GZL");
        Assert.Contains(windWakerMatches, entry => entry.PackKey == "Wind Waker Pack");
        Assert.Contains(windWakerMatches, entry => entry.PackKey == "Shared Pack");
        Assert.Contains(marioMatches, entry => entry.PackKey == "RMG");
        Assert.Contains(marioMatches, entry => entry.PackKey == "Shared Pack");
        Assert.Equal(TexturePackContentStatus.UnrecognizedLayout, Entry(snapshot, "gm4e01").ContentStatus);
    }

    [Fact]
    public async Task Ppsspp_RecognizesDirectoryAndZipPacks_ButExcludesNewDumpDirectory()
    {
        var root = Path.Combine(BaseDirectory, "ppsspp-textures");
        await WriteFileAsync(Path.Combine(root, "ULUS10041", "textures.ini"), "[options]\nversion = 1\n");
        await WriteFileAsync(Path.Combine(root, "ULUS10041", "ui", "menu.png"), "replacement");
        await WriteFileAsync(Path.Combine(root, "NPJH50505", "new", "0123456789abcdef.png"), "dump");
        await WriteFileAsync(Path.Combine(root, "NPJH50505", "textures.ini"), "[options]\nversion = 1\n");
        var zippedPack = Path.Combine(root, "ULES01234");
        Directory.CreateDirectory(zippedPack);
        var zipPath = Path.Combine(zippedPack, "textures.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("textures.ini");
            archive.CreateEntry("environment/0123456789abcdef.ktx2");
        }

        var snapshot = await new PpssppTexturePackSource("ppsspp-main", root).ScanAsync();

        Assert.Equal(TexturePackContentStatus.Usable, Entry(snapshot, "ULUS10041").ContentStatus);
        Assert.Equal(TexturePackContentStatus.EmptyOrDumpsOnly, Entry(snapshot, "NPJH50505").ContentStatus);
        Assert.Equal(TexturePackContentStatus.Usable, Entry(snapshot, "ULES01234").ContentStatus);

        var matches = TexturePackMatcher.Match(
            snapshot.Entries,
            [new GameIdentifier(GameIdentifierKind.Serial, "ULUS-10041", "fixture")]);
        Assert.Equal("ULUS10041", Assert.Single(matches).PackKey);
    }

    [Fact]
    public async Task Cancellation_IsPropagatedInsteadOfReportedAsAnUnreadableRoot()
    {
        var root = Path.Combine(BaseDirectory, "textures");
        Directory.CreateDirectory(root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DolphinTexturePackSource("dolphin-main", root).ScanAsync(cancellation.Token));
    }

    private static TexturePackInventoryEntry Entry(TexturePackInventorySnapshot snapshot, string packKey) =>
        Assert.Single(snapshot.Entries, entry => entry.PackKey == packKey);

    private static async Task WriteFileAsync(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }
}
