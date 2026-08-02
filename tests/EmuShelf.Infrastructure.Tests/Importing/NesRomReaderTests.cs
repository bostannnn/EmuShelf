using System.Security.Cryptography;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public sealed class NesRomReaderTests : TempAppDirectoryTestBase
{
    private readonly FileImportRules _rules = new();

    public NesRomReaderTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    [Fact]
    public void RawNesRom_UsesTheWholeHeaderedFileAsExactReadOnlySha1Evidence()
    {
        var path = WriteRom("Example.nes");
        var beforeBytes = File.ReadAllBytes(path);
        var beforeTimestamp = new DateTime(2026, 8, 1, 15, 1, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, beforeTimestamp);

        var header = NesRomReader.TryRecognize(path);
        var evidence = NesRomReader.TryRead(path);
        var analysis = _rules.AnalyzeFile(path);
        var metadata = _rules.ReadImportMetadata(path, System("nes"));

        Assert.NotNull(header);
        Assert.Equal(1, header.PrgBankCount);
        Assert.NotNull(evidence);
        // The No-Intro NES set keeps the 16-byte iNES header, so the catalogue key is the SHA-1 of
        // the whole file — not a header-stripped stream like the RetroAchievements hash.
        Assert.Equal(Convert.ToHexString(SHA1.HashData(beforeBytes)), evidence.Sha1);
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("nes"));
        Assert.Equal(["nes"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.True(_rules.IsFolderCandidate(path, System("nes")));
        Assert.Null(metadata.EmbeddedTitle);
        var identifier = Assert.Single(metadata.Identifiers);
        Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
        Assert.Equal(evidence.Sha1, identifier.Value);
        Assert.Equal("NES ROM", identifier.Source);
        Assert.True(identifier.IsPrimary);
        Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        Assert.Equal(beforeTimestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Recognition_RejectsAFileWithoutTheInesMagic()
    {
        var bytes = CreateRomFixture();
        bytes[0] = 0x4D; // corrupt "NES\x1A" -> "MES\x1A"
        var path = Path.Combine(BaseDirectory, "Forged.nes");
        File.WriteAllBytes(path, bytes);

        Assert.Null(NesRomReader.TryRecognize(path));
        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(path).MatchFor("nes"));
        Assert.False(_rules.IsFolderCandidate(path, System("nes")));
        Assert.Same(GameImportMetadata.Empty, _rules.ReadImportMetadata(path, System("nes")));
    }

    [Fact]
    public void Recognition_RejectsAFileSmallerThanTheHeaderDeclares()
    {
        // The header claims two PRG banks but only one bank of data is present.
        var bytes = CreateRomFixture(prgBanks: 1, chrBanks: 0);
        bytes[4] = 2;
        var path = Path.Combine(BaseDirectory, "Truncated.nes");
        File.WriteAllBytes(path, bytes);

        Assert.Null(NesRomReader.TryRecognize(path));
        Assert.Null(NesRomReader.TryRead(path));
    }

    [Fact]
    public void Recognition_RejectsAnEmptyHeaderThatOnlyCarriesTheMagic()
    {
        var bytes = new byte[16];
        bytes[0] = 0x4E; bytes[1] = 0x45; bytes[2] = 0x53; bytes[3] = 0x1A;
        var path = Path.Combine(BaseDirectory, "HeaderOnly.nes");
        File.WriteAllBytes(path, bytes);

        Assert.Null(NesRomReader.TryRecognize(path));
    }

    [Fact]
    public void Recognition_RejectsOversizedRomBeforeHashing()
    {
        var path = WriteRom("Oversized.nes");
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(NesRomReader.MaximumRomBytes + 1);

        Assert.Null(NesRomReader.TryRecognize(path));
        Assert.Null(NesRomReader.TryRead(path));
    }

    private string WriteRom(string name)
    {
        var path = Path.Combine(BaseDirectory, name);
        File.WriteAllBytes(path, CreateRomFixture());
        return path;
    }

    /// <summary>
    /// Builds an iNES image: a 16-byte header (magic, PRG/CHR bank counts, flags) followed by
    /// <paramref name="prgBanks"/> * 16 KiB of PRG and <paramref name="chrBanks"/> * 8 KiB of CHR,
    /// plus optional trailing padding used to prove the RetroAchievements hash trims an over-dump.
    /// </summary>
    internal static byte[] CreateRomFixture(
        int prgBanks = 1,
        int chrBanks = 1,
        byte prgFill = 0xA5,
        byte chrFill = 0x5A,
        byte flags6 = 0,
        int trailingPadding = 0)
    {
        var prg = prgBanks * 16 * 1024;
        var chr = chrBanks * 8 * 1024;
        var bytes = new byte[16 + prg + chr + trailingPadding];
        bytes[0] = 0x4E; bytes[1] = 0x45; bytes[2] = 0x53; bytes[3] = 0x1A; // "NES\x1A"
        bytes[4] = (byte)prgBanks;
        bytes[5] = (byte)chrBanks;
        bytes[6] = flags6;
        Array.Fill(bytes, prgFill, 16, prg);
        Array.Fill(bytes, chrFill, 16 + prg, chr);
        return bytes;
    }

    private static Core.Systems.GameSystem System(string id) =>
        KnownSystems.All.Single(system => system.Id == id);
}
