using System.Buffers.Binary;
using System.Text;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;
using EmuShelf.Infrastructure.Tests.Metadata;

namespace EmuShelf.Infrastructure.Tests.Importing;

public class FileImportRulesTests : TempAppDirectoryTestBase
{
    private readonly FileImportRules _rules = new();

    public FileImportRulesTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    [Fact]
    public void AnalyzeFile_CueFile_SuggestsBothPlayStationSystems()
    {
        var analysis = _rules.AnalyzeFile("/games/Final Fantasy.cue");

        Assert.Equal(
            ["playstation", "playstation2"],
            analysis.SuggestedSystems.Select(system => system.Id));
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("playstation"));
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("playstation2"));
    }

    [Fact]
    public void AnalyzeFile_ArcadeZip_SuggestsArcade()
    {
        var analysis = _rules.AnalyzeFile("/games/mslug.zip");

        Assert.Equal(["arcade"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("arcade"));
        Assert.True(_rules.IsFolderCandidate(analysis.Path, System("arcade")));
    }

    [Fact]
    public void AnalyzeFile_ArcadeBiosZip_IsHiddenFromTheLibrary()
    {
        var analysis = _rules.AnalyzeFile("/games/neogeo.zip");

        Assert.Empty(analysis.SuggestedSystems);
        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("arcade"));
        Assert.False(_rules.IsFolderCandidate(analysis.Path, System("arcade")));
    }

    [Theory]
    [InlineData("neogeo")]
    [InlineData("pgm")]
    [InlineData("decocass")]
    public void IsFolderCandidate_ArcadeBiosArchives_AreRejected(string setName)
    {
        Assert.False(_rules.IsFolderCandidate($"/games/{setName}.zip", System("arcade")));
    }

    [Fact]
    public void ReadImportMetadata_ArcadeZip_UsesTheSetNameAsIdentifier()
    {
        var metadata = _rules.ReadImportMetadata("/games/sfa3.zip", System("arcade"));

        var identifier = Assert.Single(metadata.Identifiers);
        Assert.Equal(GameIdentifierKind.ArcadeSetName, identifier.Kind);
        Assert.Equal("sfa3", identifier.Value);
        Assert.True(identifier.IsPrimary);
        Assert.Null(metadata.EmbeddedTitle);
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
            ["gamecube", "wii", "playstation", "playstation2"],
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
            ["playstation", "playstation2"],
            _rules.AnalyzeFile("/games/GAME.CUE").SuggestedSystems.Select(system => system.Id));
    }

    [Fact]
    public void AnalyzeFile_RawBin_IsExplicitOnly()
    {
        var analysis = _rules.AnalyzeFile("/games/track.bin");

        Assert.Equal(
            ["playstation", "playstation2"],
            analysis.SuggestedSystems.Select(system => system.Id));
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("playstation"));
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("playstation2"));
        Assert.False(_rules.IsFolderCandidate(analysis.Path, System("playstation")));
        Assert.False(_rules.IsFolderCandidate(analysis.Path, System("playstation2")));
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
    [InlineData("playstation2", ".cue")]
    [InlineData("playstation2", ".chd")]
    [InlineData("playstation2", ".cso")]
    [InlineData("playstation2", ".m3u")]
    public void AnalyzeFile_MatchesPlayStationExtensionMaps(string systemId, string extension)
    {
        var analysis = _rules.AnalyzeFile($"/games/game{extension}");

        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor(systemId));
    }

    [Theory]
    [InlineData(".iso")]
    [InlineData(".cso")]
    [InlineData(".CSO")]
    [InlineData(".chd")]
    [InlineData(".CHD")]
    public void PspImage_RecognizesReadOnlyParamSfoAndExposesTrustedEvidence(string extension)
    {
        var path = Path.Combine(BaseDirectory, $"Lumines{extension}");
        var iso = PspIsoBuilder.Build("ULUS10002", "Lumines");
        File.WriteAllBytes(path, WrapPspImage(iso, extension));
        var beforeBytes = File.ReadAllBytes(path);
        var beforeTimestamp = File.GetLastWriteTimeUtc(path);

        var analysis = _rules.AnalyzeFile(path);
        var system = System("psp");
        var metadata = _rules.ReadImportMetadata(path, system);

        Assert.Equal("psp", analysis.SuggestedSystems[0].Id);
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("psp"));
        // PS1 lists ISO and CHD but not CSO; PS2 lists all three. Wherever the container is
        // shared, the validated PARAM.SFO has to veto the PlayStation match.
        Assert.Equal(
            extension.Equals(".cso", StringComparison.OrdinalIgnoreCase)
                ? GameFileMatch.Unsupported
                : GameFileMatch.Incompatible,
            analysis.MatchFor("playstation"));
        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("playstation2"));
        Assert.Equal(
            extension.Equals(".iso", StringComparison.OrdinalIgnoreCase)
                ? GameFileMatch.Incompatible
                : GameFileMatch.Unsupported,
            analysis.MatchFor("gamecube"));
        Assert.Equal(
            extension.Equals(".iso", StringComparison.OrdinalIgnoreCase)
                ? GameFileMatch.Incompatible
                : GameFileMatch.Unsupported,
            analysis.MatchFor("wii"));
        Assert.True(_rules.IsFolderCandidate(path, system));
        Assert.False(_rules.IsFolderCandidate(path, System("playstation")));
        Assert.False(_rules.IsFolderCandidate(path, System("playstation2")));
        Assert.False(_rules.IsFolderCandidate(path, System("gamecube")));
        Assert.False(_rules.IsFolderCandidate(path, System("wii")));
        Assert.Equal("Lumines", metadata.EmbeddedTitle);
        var identifier = Assert.Single(metadata.Identifiers);
        Assert.Equal(GameIdentifierKind.Serial, identifier.Kind);
        Assert.Equal("ULUS10002", identifier.Value);
        Assert.Equal("PSP PARAM.SFO", identifier.Source);
        Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        Assert.Equal(beforeTimestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void PspImage_MissingOrMalformedSfoIsNotRecognizedAndCannotBeFolderImported()
    {
        var path = Path.Combine(BaseDirectory, "Not a PSP.iso");
        File.WriteAllBytes(path, new byte[24 * 2048]);

        var analysis = _rules.AnalyzeFile(path);

        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("psp"));
        Assert.False(_rules.IsFolderCandidate(path, System("psp")));
        Assert.Same(GameImportMetadata.Empty, _rules.ReadImportMetadata(path, System("psp")));
    }

    [Fact]
    public void PspImage_MalformedSfoDataRangeIsRejectedWithoutChangingTheImage()
    {
        var path = Path.Combine(BaseDirectory, "Malformed PARAM.SFO.iso");
        var image = PspIsoBuilder.Build();
        // PARAM.SFO begins at sector 22. Its first index entry's data-max field is at 0x1C;
        // making it smaller than data-len must fail the bounds check instead of guessing.
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(22 * 2048 + 0x1C, 4), 0);
        File.WriteAllBytes(path, image);
        var beforeBytes = File.ReadAllBytes(path);
        var beforeTimestamp = File.GetLastWriteTimeUtc(path);

        var analysis = _rules.AnalyzeFile(path);

        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("psp"));
        Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        Assert.Equal(beforeTimestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void PspImage_MalformedCsoIsRejectedWithoutThrowing()
    {
        var path = Path.Combine(BaseDirectory, "Malformed image.cso");
        var iso = PspIsoBuilder.Build();
        // Point PARAM.SFO at a logical sector beyond the CSO's uncompressed image. The reader
        // must turn this malformed descriptor into an incompatible PSP candidate, never an
        // inspection failure that interrupts the import flow.
        iso.AsSpan(21 * 2048 + 2, 3).Fill(0xFF);
        File.WriteAllBytes(path, CompressedIsoBuilder.BuildCso(iso));

        var exception = Record.Exception(() => _rules.AnalyzeFile(path));
        var analysis = _rules.AnalyzeFile(path);

        Assert.Null(exception);
        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("psp"));
    }

    [Theory]
    // A descriptor pointing past the end of the decoded image, and a container whose own header
    // is corrupt. Neither may throw out of an import inspection; both are simply not PSP games.
    [InlineData(true)]
    [InlineData(false)]
    public void PspImage_MalformedChdIsRejectedWithoutThrowing(bool corruptTheContainer)
    {
        var path = Path.Combine(BaseDirectory, $"Malformed {corruptTheContainer}.chd");
        var iso = PspIsoBuilder.Build();
        if (!corruptTheContainer)
            iso.AsSpan(21 * 2048 + 2, 3).Fill(0xFF);
        var chd = ChdImageBuilder.BuildDvdChd(iso);
        if (corruptTheContainer)
            chd.AsSpan(56, 4).Fill(0xFF); // an absurd hunk size fails the header's own checks
        File.WriteAllBytes(path, chd);

        var exception = Record.Exception(() => _rules.AnalyzeFile(path));
        var analysis = _rules.AnalyzeFile(path);

        Assert.Null(exception);
        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("psp"));
        Assert.False(_rules.IsFolderCandidate(path, System("psp")));
    }

    [Fact]
    public void PspImage_UsesFilenameWhenSfoEvidenceIsUnavailable()
    {
        var path = Path.Combine(BaseDirectory, "Homebrew.iso");
        File.WriteAllBytes(path, PspIsoBuilder.Build(discId: "not-an-id", title: "bad\u0001title"));

        var metadata = _rules.ReadImportMetadata(path, System("psp"));

        Assert.Null(metadata.EmbeddedTitle);
        Assert.Empty(metadata.Identifiers);
        Assert.True(_rules.IsFolderCandidate(path, System("psp")));
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

    [Fact]
    public void SelectGameEntries_PlayStation2CueHidesReferencedBinButKeepsExplicitOrphan()
    {
        var cue = Path.Combine(BaseDirectory, "Game.cue");
        var referenced = Path.Combine(BaseDirectory, "Game.bin");
        var orphan = Path.Combine(BaseDirectory, "Orphan.bin");
        File.WriteAllText(cue, "FILE \"Game.bin\" BINARY\n");
        File.WriteAllText(referenced, "x");
        File.WriteAllText(orphan, "x");

        var selection = _rules.SelectGameEntries([cue, referenced, orphan], System("playstation2"));

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

    /// <summary>Packs a PSP ISO into the container named by <paramref name="extension"/>.</summary>
    private static byte[] WrapPspImage(byte[] iso, string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".cso" => CompressedIsoBuilder.BuildCso(iso),
            ".chd" => ChdImageBuilder.BuildDvdChd(iso),
            _ => iso,
        };

    private static Core.Systems.GameSystem System(string id) =>
        KnownSystems.All.Single(system => system.Id == id);
}
