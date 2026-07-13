using EmuShelf.Core.Systems;
using EmuShelf.Infrastructure.Importing;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public class FolderScannerTests : TempAppDirectoryTestBase
{
    private static readonly GameSystem Ps1 = KnownSystems.All.Single(s => s.Id == "playstation");
    private static readonly GameSystem Ps2 = KnownSystems.All.Single(s => s.Id == "playstation2");

    private readonly FolderScanner _scanner = new(new FileImportRules());

    private void Touch(params string[] relativeParts)
    {
        var full = Path.Combine([BaseDirectory, .. relativeParts]);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "x");
    }

    [Fact]
    public async Task ScanAsync_ReturnsOnlyCandidates_Recursively()
    {
        Touch("a.cue");
        Touch("sub", "b.chd");
        Touch("sub", "deeper", "c.pbp");
        Touch("notes.txt");          // not a game
        Touch("sub", "cover.png");   // not a game

        var found = await _scanner.ScanAsync(BaseDirectory, Ps1);

        Assert.Equal(3, found.EntryPaths.Count);
        Assert.All(found.EntryPaths, p =>
            Assert.Contains(Path.GetExtension(p), new[] { ".cue", ".chd", ".pbp" }));
    }

    [Fact]
    public async Task ScanAsync_DoesNotImportBinFiles()
    {
        Touch("Game.cue");
        File.WriteAllText(
            Path.Combine(BaseDirectory, "Game.cue"),
            "FILE \"Tracks\\Game (Track 01).bin\" BINARY\n" +
            "  TRACK 01 MODE2/2352\n" +
            "FILE \"Tracks/Game (Track 02).BIN\" BINARY\n");
        Touch("Tracks", "Game (Track 01).bin");
        Touch("Tracks", "Game (Track 02).BIN");
        Touch("Standalone.bin");

        var found = await _scanner.ScanAsync(BaseDirectory, Ps1);

        Assert.Equal(["Game.cue"], found.EntryPaths.Select(Path.GetFileName));
        Assert.Equal(
            ["Game (Track 01).bin", "Game (Track 02).BIN"],
            found.SuppressedPaths.Select(Path.GetFileName).OrderBy(name => name));
    }

    [Fact]
    public async Task ScanAsync_M3uIsEntryAndReferencedDiscsAreHidden()
    {
        Touch("Collection.m3u");
        File.WriteAllText(
            Path.Combine(BaseDirectory, "Collection.m3u"),
            "#EXTM3U\nDisc 1.cue\nSub\\Disc 2.chd\n");
        Touch("Disc 1.cue");
        Touch("Sub", "Disc 2.chd");
        Touch("Other.chd");

        var found = await _scanner.ScanAsync(BaseDirectory, Ps1);

        Assert.Equal(
            ["Collection.m3u", "Other.chd"],
            found.EntryPaths.Select(Path.GetFileName).OrderBy(name => name));
        Assert.Equal(
            ["Disc 1.cue", "Disc 2.chd"],
            found.SuppressedPaths.Select(Path.GetFileName).OrderBy(name => name));
    }

    [Fact]
    public async Task ScanAsync_M3uAlsoHidesPlayStation2Discs()
    {
        Touch("Collection.m3u");
        File.WriteAllText(Path.Combine(BaseDirectory, "Collection.m3u"), "Disc 1.iso\nDisc 2.cso\n");
        Touch("Disc 1.iso");
        Touch("Disc 2.cso");

        var found = await _scanner.ScanAsync(BaseDirectory, Ps2);

        Assert.Equal(["Collection.m3u"], found.EntryPaths.Select(Path.GetFileName));
    }

    [Fact]
    public async Task ScanAsync_ReportsProgress()
    {
        Touch("a.cue");
        Touch("b.chd");
        var reports = new List<int>();
        var progress = new Progress<Core.Importing.ScanProgress>(p => reports.Add(p.CandidatesFound));

        await _scanner.ScanAsync(BaseDirectory, Ps1, progress);

        // Progress is marshalled async; give the posts a moment to drain.
        await Task.Delay(50);
        Assert.NotEmpty(reports);
    }

    [Fact]
    public async Task ScanAsync_MissingFolder_ReturnsEmpty()
    {
        var found = await _scanner.ScanAsync(Path.Combine(BaseDirectory, "does-not-exist"), Ps1);
        Assert.Empty(found.EntryPaths);
        Assert.Empty(found.SuppressedPaths);
    }

    [Fact]
    public async Task ScanAsync_Cancelled_Throws()
    {
        Touch("a.cue");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _scanner.ScanAsync(BaseDirectory, Ps1, null, cts.Token));
    }
}
