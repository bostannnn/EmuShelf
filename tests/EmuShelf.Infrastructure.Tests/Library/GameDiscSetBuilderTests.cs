using EmuShelf.Core.Library;

namespace EmuShelf.Infrastructure.Tests.Library;

public sealed class GameDiscSetBuilderTests
{
    [Fact]
    public void Build_GroupsExplicitSameReleaseDiscsAndHonorsRememberedSelection()
    {
        var disc1 = Game(11, "Final Fantasy VII (USA) (Disc 1).chd");
        var disc2 = Game(12, "Final Fantasy VII (USA) (Disc 2).chd");
        var other = Game(13, "Final Fantasy VIII (USA) (Disc 1).chd");

        var set = Assert.Single(GameDiscSetBuilder.Build(
            [disc1, disc2, other],
            new Dictionary<string, long> { ["playstation\u001FFINAL FANTASY VII (USA)"] = disc2.Id }),
            candidate => candidate.IsMultiDisc);

        Assert.Equal("Final Fantasy VII (USA)", set.DisplayTitle);
        Assert.Equal([1, 2], set.Discs.Select(disc => disc.Number));
        Assert.Equal(disc2.Id, set.SelectedDisc.Game.Id);
        Assert.Contains(GameDiscSetBuilder.Build([disc1, disc2, other]),
            candidate => candidate.DisplayGame.Id == other.Id && !candidate.IsMultiDisc);
    }

    [Fact]
    public void Build_DoesNotGroupAmbiguousDemoOrUnnumberedFiles()
    {
        var demo1 = Game(21, "Demo Sampler (Disc 1).cue");
        var demo2 = Game(22, "Demo Sampler (Disc 2).cue");
        var unnumbered = Game(23, "Bonus Disc.cue");

        var sets = GameDiscSetBuilder.Build([demo1, demo2, unnumbered]);

        Assert.Equal(3, sets.Count);
        Assert.All(sets, set => Assert.False(set.IsMultiDisc));
    }

    [Fact]
    public void Build_DoesNotGroupRevisionVariantsOrDuplicateDiscSources()
    {
        var revisionDisc1 = Game(31, "Example (Rev 1) (Disc 1).chd");
        var revisionDisc2 = Game(32, "Example (Rev 1) (Disc 2).chd");
        var disc1Cue = Game(33, "Archive (Disc 1).cue");
        var disc1Chd = Game(34, "Archive (Disc 1).chd");
        var disc2 = Game(35, "Archive (Disc 2).chd");

        var sets = GameDiscSetBuilder.Build(
            [revisionDisc1, revisionDisc2, disc1Cue, disc1Chd, disc2]);

        Assert.Equal(5, sets.Count);
        Assert.All(sets, set => Assert.False(set.IsMultiDisc));
    }

    private static Game Game(long id, string fileName) => new()
    {
        Id = id,
        SystemId = "playstation",
        Path = Path.Combine("C:\\games", fileName),
        Title = Path.GetFileNameWithoutExtension(fileName),
        DateAdded = DateTimeOffset.UtcNow,
    };
}
