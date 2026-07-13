using EmuShelf.Core.Systems;
using EmuShelf.Infrastructure.Importing;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public class FolderScannerTests : TempAppDirectoryTestBase
{
    private static readonly GameSystem Ps1 = KnownSystems.All.Single(s => s.Id == "playstation");

    private readonly FolderScanner _scanner = new(new ExtensionImportRules());

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

        Assert.Equal(3, found.Count);
        Assert.All(found, p => Assert.Contains(Path.GetExtension(p), new[] { ".cue", ".chd", ".pbp" }));
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
        Assert.Empty(found);
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
