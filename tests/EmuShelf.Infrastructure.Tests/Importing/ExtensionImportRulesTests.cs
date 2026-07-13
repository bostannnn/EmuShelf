using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public class ExtensionImportRulesTests
{
    private readonly ExtensionImportRules _rules = new();

    [Fact]
    public void SuggestSystems_CueFile_SuggestsPlayStationOnly()
    {
        var systems = _rules.SuggestSystems("/games/Final Fantasy.cue");
        Assert.Equal(["playstation"], systems.Select(s => s.Id));
    }

    [Fact]
    public void SuggestSystems_IsoFile_SuggestsEverySystemThatUsesIso()
    {
        // .iso is used by PS1 ("where applicable"), PS2, GameCube, and Wii — but never PS3 (directory-based).
        var ids = _rules.SuggestSystems("/games/game.iso").Select(s => s.Id).ToHashSet();
        Assert.Equal(new HashSet<string> { "playstation", "playstation2", "gamecube", "wii" }, ids);
        Assert.DoesNotContain("playstation3", ids);
    }

    [Fact]
    public void SuggestSystems_IsCaseInsensitive()
    {
        Assert.Equal(["playstation"], _rules.SuggestSystems("/games/GAME.CUE").Select(s => s.Id));
    }

    [Fact]
    public void SuggestSystems_UnknownExtension_ReturnsEmpty()
    {
        Assert.Empty(_rules.SuggestSystems("/games/readme.txt"));
        Assert.Empty(_rules.SuggestSystems("/games/no-extension"));
    }

    [Fact]
    public void IsCandidate_MatchesSystemExtensions()
    {
        var wii = KnownSystems.All.Single(s => s.Id == "wii");
        Assert.True(_rules.IsCandidate("/g/x.wbfs", wii));
        Assert.False(_rules.IsCandidate("/g/x.cue", wii)); // .cue is PS1, not Wii
    }
}
