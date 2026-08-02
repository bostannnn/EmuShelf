using System.Security.Cryptography;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Achievements;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Metadata;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

// dreamcast_gd.chd is chdman 0.249's conversion of the GDI set DreamcastGdRomSetBuilder writes, so
// these run the production reader against a real container: real CHGD track metadata, a real
// Huffman-coded hunk map, and real cdlz/cdfl hunks. Its track 01 is 300 frames inside a 450-frame
// extent, which is what makes the high-density track's disc address (45000) differ from the frame
// that actually holds it (45004) — the case a naive reader gets wrong. The sync-stripped frames
// that chdman emits for ECC-valid sectors, and layouts with the boot executable behind 18 audio
// tracks, were verified during development against the real Dreamcast library by extracting each
// CHD back to a GDI set with chdman and requiring both packagings to hash identically.
public class DreamcastChdReaderTests : TempAppDirectoryTestBase
{
    private static readonly GameSystem Dreamcast =
        KnownSystems.All.Single(system => system.Id == "dreamcast");

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Chd", name);

    [Fact]
    public void ChdImage_IsRecognizedAndLeavesImportEvidenceToEnrichment()
    {
        var chd = Fixture("dreamcast_gd.chd");
        var rules = new FileImportRules();

        var analysis = rules.AnalyzeFile(chd);

        // The container is shared with the PlayStation systems, so the IP.BIN evidence has to both
        // put Dreamcast first and rule them out rather than leaving the user three candidates.
        Assert.Equal("dreamcast", analysis.SuggestedSystems[0].Id);
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("dreamcast"));
        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("playstation"));
        Assert.Equal(GameFileMatch.Incompatible, analysis.MatchFor("playstation2"));
        Assert.True(rules.IsFolderCandidate(chd, Dreamcast));
        Assert.Same(GameImportMetadata.Empty, rules.ReadImportMetadata(chd, Dreamcast));

        // A CHD names no other file, so nothing is suppressed as a component of the entry.
        var selection = rules.SelectGameEntries([chd], Dreamcast);
        Assert.Equal([chd], selection.EntryPaths);
        Assert.Empty(selection.SuppressedPaths);
    }

    [Fact]
    public void ChdImage_IsIdentifiedByItsHighDensityProductNumber()
    {
        var identifiers = new DreamcastIdentifierExtractor().Extract(NewGame(Fixture("dreamcast_gd.chd")));

        // MK-51099 is the high-density header; MK-00001 is the low-density copy that a reader
        // stopping at the first IP.BIN on the disc would have picked up instead.
        Assert.Equal(["MK51099", "51099"], identifiers.Select(identifier => identifier.Value));
        Assert.All(identifiers, identifier => Assert.Equal(GameIdentifierKind.Serial, identifier.Kind));
        Assert.All(
            identifiers,
            identifier => Assert.Equal("Dreamcast IP.BIN product number", identifier.Source));
    }

    [Fact]
    public void ChdAndGdiOfTheSameDisc_ProduceTheSameCanonicalHash()
    {
        var gdi = DreamcastGdRomSetBuilder.Write(BaseDirectory);
        var hasher = new RetroAchievementsGameHasher();

        var fromGdi = hasher.Identify(NewGame(gdi));
        var fromChd = hasher.Identify(NewGame(Fixture("dreamcast_gd.chd")));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, fromGdi.Status);
        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, fromChd.Status);
        Assert.Equal(fromGdi.CanonicalHash, fromChd.CanonicalHash);
        Assert.Equal(ExpectedRcheevosHash(), fromChd.CanonicalHash);

        // The algorithm is the packaging-independent one, so a hash stored for a GDI set stays
        // valid when the same disc is later imported as a CHD.
        Assert.Equal(fromGdi.HashAlgorithmVersion, fromChd.HashAlgorithmVersion);
    }

    [Fact]
    public void ChdImage_IsHashableWithoutAnyDescriptorDependency()
    {
        var chd = Fixture("dreamcast_gd.chd");

        var snapshot = new RetroAchievementsGameHasher().Inspect(NewGame(chd));

        Assert.True(snapshot.CanHash);
        Assert.Null(snapshot.Error);
    }

    [Fact]
    public void NonDreamcastChd_IsRejectedByEveryDreamcastEntryPoint()
    {
        // A DVD-geometry CHD has no CD track list at all, which is the shape a PS2 or PSP image
        // takes; it must not be filename-guessed onto Dreamcast by its shared extension.
        var chd = Fixture("game_dvd.chd");

        Assert.False(DreamcastDisc.TryRecognize(chd));
        Assert.False(new FileImportRules().IsFolderCandidate(chd, Dreamcast));
        Assert.Empty(DreamcastChdReader.ReadProductNumberAliases(chd));
        Assert.Empty(new DreamcastIdentifierExtractor().Extract(NewGame(chd)));

        var result = new RetroAchievementsGameHasher().Identify(NewGame(chd));
        Assert.Equal(RetroAchievementsIdentificationStatus.UnsupportedFormat, result.Status);
        Assert.Null(result.CanonicalHash);
    }

    [Fact]
    public void ChdThatIsNotAContainerAtAll_IsRejected()
    {
        Directory.CreateDirectory(BaseDirectory);
        var chd = Path.Combine(BaseDirectory, "broken.chd");
        File.WriteAllBytes(chd, new byte[4096]);

        Assert.False(DreamcastDisc.TryRecognize(chd));
        Assert.Equal(
            GameFileMatch.Incompatible,
            new FileImportRules().AnalyzeFile(chd).MatchFor("dreamcast"));
    }

    // rcheevos hashes the 256-byte IP.BIN header followed by the boot executable it names.
    private static string ExpectedRcheevosHash()
    {
        var ipBin = new byte[256];
        "SEGA SEGAKATANA "u8.CopyTo(ipBin);
        System.Text.Encoding.ASCII.GetBytes(DreamcastGdRomSetBuilder.ProductNumber)
            .CopyTo(ipBin.AsSpan(64));
        System.Text.Encoding.ASCII.GetBytes(DreamcastGdRomSetBuilder.BootFile)
            .CopyTo(ipBin.AsSpan(96));

        return Convert.ToHexString(
                MD5.HashData([.. ipBin, .. DreamcastGdRomSetBuilder.BootExecutable()]))
            .ToLowerInvariant();
    }

    private static Game NewGame(string path) => new()
    {
        Id = 1,
        Title = "Example",
        SystemId = "dreamcast",
        Path = path,
        DateAdded = DateTimeOffset.UtcNow,
    };
}
