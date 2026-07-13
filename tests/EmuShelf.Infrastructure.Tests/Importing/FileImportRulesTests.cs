using System.Buffers.Binary;
using System.Text;
using EmuShelf.Core.Importing;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public class FileImportRulesTests : TempAppDirectoryTestBase
{
    private readonly FileImportRules _rules = new();

    public FileImportRulesTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    [Fact]
    public void AnalyzeFile_CueFile_SuggestsPlayStationOnly()
    {
        var analysis = _rules.AnalyzeFile("/games/Final Fantasy.cue");

        Assert.Equal(["playstation"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("playstation"));
        Assert.Equal(GameFileMatch.Unsupported, analysis.MatchFor("playstation2"));
    }

    [Fact]
    public void AnalyzeFile_SharedPlayStationExtensions_SuggestBothSystems()
    {
        Assert.Equal(
            ["playstation", "playstation2"],
            _rules.AnalyzeFile("/games/game.chd").SuggestedSystems.Select(system => system.Id));
        Assert.Equal(
            ["playstation", "playstation2"],
            _rules.AnalyzeFile("/games/game.m3u").SuggestedSystems.Select(system => system.Id));
    }

    [Fact]
    public void AnalyzeFile_IsoWithoutNintendoHeader_SuggestsEveryPlausibleSystem()
    {
        var path = Path.Combine(BaseDirectory, "game.iso");
        File.WriteAllText(path, "not a Nintendo disc");

        var analysis = _rules.AnalyzeFile(path);

        Assert.Equal(
            ["playstation", "playstation2", "gamecube", "wii"],
            analysis.SuggestedSystems.Select(system => system.Id));
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("playstation"));
        Assert.Equal(GameFileMatch.Unrecognized, analysis.MatchFor("gamecube"));
        Assert.Equal(GameFileMatch.Unrecognized, analysis.MatchFor("wii"));
    }

    [Theory]
    [InlineData(".iso")]
    [InlineData(".gcm")]
    [InlineData(".ciso")]
    [InlineData(".wbfs")]
    [InlineData(".rvz")]
    public void NintendoFormats_GameCubeHeader_SelectsGameCube(string extension)
    {
        var analysis = _rules.AnalyzeFile(WriteNintendoImage(extension, isWii: false));

        Assert.Equal("gamecube", analysis.SuggestedSystems[0].Id);
        Assert.DoesNotContain(analysis.SuggestedSystems, system => system.Id == "wii");
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("gamecube"));
        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("wii"));
        Assert.True(_rules.IsFolderCandidate(analysis.Path, System("gamecube")));
        Assert.False(_rules.IsFolderCandidate(analysis.Path, System("wii")));
    }

    [Theory]
    [InlineData(".iso")]
    [InlineData(".gcm")]
    [InlineData(".ciso")]
    [InlineData(".wbfs")]
    [InlineData(".rvz")]
    public void NintendoFormats_WiiHeader_SelectsWii(string extension)
    {
        var analysis = _rules.AnalyzeFile(WriteNintendoImage(extension, isWii: true));

        Assert.Equal("wii", analysis.SuggestedSystems[0].Id);
        Assert.DoesNotContain(analysis.SuggestedSystems, system => system.Id == "gamecube");
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("wii"));
        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("gamecube"));
        Assert.True(_rules.IsFolderCandidate(analysis.Path, System("wii")));
        Assert.False(_rules.IsFolderCandidate(analysis.Path, System("gamecube")));
    }

    [Fact]
    public void AnalyzeFile_NintendoIsoHeader_RulesOutPlayStationSystems()
    {
        var analysis = _rules.AnalyzeFile(WriteNintendoImage(".iso", isWii: true));

        Assert.Equal(["wii"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("playstation"));
        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("playstation2"));
    }

    [Fact]
    public void AnalyzeFile_NintendoContainerWithInvalidHeader_IsUnrecognized()
    {
        var path = Path.Combine(BaseDirectory, "broken.rvz");
        File.WriteAllText(path, "not an RVZ image");

        var analysis = _rules.AnalyzeFile(path);

        Assert.Equal(
            ["gamecube", "wii"],
            analysis.SuggestedSystems.Select(system => system.Id));
        Assert.Equal(GameFileMatch.Unrecognized, analysis.MatchFor("gamecube"));
        Assert.Equal(GameFileMatch.Unrecognized, analysis.MatchFor("wii"));
        Assert.False(_rules.IsFolderCandidate(analysis.Path, System("gamecube")));
        Assert.False(_rules.IsFolderCandidate(analysis.Path, System("wii")));
    }

    [Fact]
    public void AnalyzeFile_IsCaseInsensitive()
    {
        Assert.Equal(
            ["playstation"],
            _rules.AnalyzeFile("/games/GAME.CUE").SuggestedSystems.Select(system => system.Id));
    }

    [Fact]
    public void AnalyzeFile_RawBin_IsExplicitOnly()
    {
        var analysis = _rules.AnalyzeFile("/games/track.bin");

        Assert.Equal(["playstation"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("playstation"));
        Assert.False(_rules.IsFolderCandidate(analysis.Path, System("playstation")));
    }

    [Fact]
    public void AnalyzeFile_UnknownExtension_IsUnsupported()
    {
        var analysis = _rules.AnalyzeFile("/games/readme.txt");

        Assert.Empty(analysis.SuggestedSystems);
        Assert.Equal(GameFileMatch.Unsupported, analysis.MatchFor("playstation"));
    }

    [Theory]
    [InlineData("playstation", ".cue")]
    [InlineData("playstation", ".chd")]
    [InlineData("playstation", ".m3u")]
    [InlineData("playstation", ".pbp")]
    [InlineData("playstation", ".iso")]
    [InlineData("playstation2", ".iso")]
    [InlineData("playstation2", ".chd")]
    [InlineData("playstation2", ".cso")]
    [InlineData("playstation2", ".m3u")]
    public void AnalyzeFile_MatchesPlayStationExtensionMaps(string systemId, string extension)
    {
        var analysis = _rules.AnalyzeFile($"/games/game{extension}");

        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor(systemId));
    }

    [Fact]
    public void SelectGameEntries_CueHidesReferencedBinButKeepsExplicitOrphan()
    {
        var cue = Path.Combine(BaseDirectory, "Game.cue");
        var referenced = Path.Combine(BaseDirectory, "Game.bin");
        var orphan = Path.Combine(BaseDirectory, "Orphan.bin");
        File.WriteAllText(cue, "FILE \"Game.bin\" BINARY\n");
        File.WriteAllText(referenced, "x");
        File.WriteAllText(orphan, "x");

        var selection = _rules.SelectGameEntries([cue, referenced, orphan], System("playstation"));

        Assert.Equal(
            ["Game.cue", "Orphan.bin"],
            selection.EntryPaths.Select(Path.GetFileName).OrderBy(name => name));
        Assert.Equal(["Game.bin"], selection.SuppressedPaths.Select(Path.GetFileName));
    }

    private string WriteNintendoImage(string extension, bool isWii)
    {
        var discHeaderOffset = extension switch
        {
            ".ciso" => 0x8000,
            ".wbfs" => 0x200,
            ".rvz" => 0x58,
            _ => 0,
        };

        var bytes = new byte[discHeaderOffset + 0x20];
        switch (extension)
        {
            case ".ciso":
                Encoding.ASCII.GetBytes("CISO").CopyTo(bytes, 0);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 0x200000);
                bytes[8] = 1;
                break;
            case ".wbfs":
                Encoding.ASCII.GetBytes("WBFS").CopyTo(bytes, 0);
                bytes[8] = 9;
                bytes[12] = 1;
                break;
            case ".rvz":
                Encoding.ASCII.GetBytes("RVZ\x01").CopyTo(bytes, 0);
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0x0C, 4), 0xDC);
                break;
        }

        var magicOffset = discHeaderOffset + (isWii ? 0x18 : 0x1C);
        BinaryPrimitives.WriteUInt32BigEndian(
            bytes.AsSpan(magicOffset, 4),
            isWii ? 0x5D1C9EA3u : 0xC2339F3Du);

        var path = Path.Combine(BaseDirectory, $"game{extension}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static Core.Systems.GameSystem System(string id) =>
        KnownSystems.All.Single(system => system.Id == id);
}
