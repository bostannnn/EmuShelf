using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Importing;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public sealed class GameBoyAdvanceRomReaderTests : TempAppDirectoryTestBase
{
    private const string NintendoLogoHex =
        "24FFAE51699AA2213D84820A84E409AD11248B98C0817F21A352BE199309CE2010464A4AF82731EC58C7E83382E3CEBF85F4DF94CE4B09C194568AC01372A7FC9F844D73A3CA9A615897A327FC039876231DC7610304AE56BF38840040A70EFDFF52FE036F9530F197FBC08560D68025A963BE03014E38E2F9A234FFBB3E0344780090CB88113A9465C07C6387F03CAFD625E48B380AAC7221D4F807";
    private readonly FileImportRules _rules = new();

    public GameBoyAdvanceRomReaderTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    [Fact]
    public void RawGbaRom_UsesBoundedHeaderAndExactReadOnlySha1Evidence()
    {
        var path = WriteRom("Example.gba", "Example GBA", "ABCE");
        var beforeBytes = File.ReadAllBytes(path);
        var beforeTimestamp = new DateTime(2026, 7, 19, 15, 1, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, beforeTimestamp);

        var header = GameBoyAdvanceRomReader.TryRecognize(path);
        var evidence = GameBoyAdvanceRomReader.TryRead(path);
        var analysis = _rules.AnalyzeFile(path);
        var metadata = _rules.ReadImportMetadata(path, System("gba"));

        Assert.NotNull(header);
        Assert.Equal("Example GBA", header.Title);
        Assert.Equal("ABCE", header.GameCode);
        Assert.False(header.IsHomebrew);
        Assert.NotNull(evidence);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(beforeBytes)), evidence.Sha1);
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("gba"));
        Assert.Equal(["gba"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.True(_rules.IsFolderCandidate(path, System("gba")));
        // Import is deferred: the whole-file SHA-1 identity is produced by
        // GameBoyAdvanceRomIdentifierExtractor during metadata enrichment (see IdentifierExtractorTests),
        // so ReadImportMetadata does no full read and reports no evidence here.
        Assert.Same(GameImportMetadata.Empty, metadata);
        Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        Assert.Equal(beforeTimestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void AlteredPayloadWithTheSameHeaderCodeGetsDifferentExactEvidence()
    {
        var original = WriteRom("Example.gba", "Example GBA", "ABCE");
        var altered = WriteRom("Example altered.gba", "Example GBA", "ABCE", payloadValue: 0x99);

        var originalEvidence = GameBoyAdvanceRomReader.TryRead(original);
        var alteredEvidence = GameBoyAdvanceRomReader.TryRead(altered);

        Assert.NotNull(originalEvidence);
        Assert.NotNull(alteredEvidence);
        Assert.Equal(originalEvidence.GameCode, alteredEvidence.GameCode);
        Assert.NotEqual(originalEvidence.Sha1, alteredEvidence.Sha1);
    }

    [Fact]
    public void Recognition_RejectsHeaderedImagesMalformedHeadersAndArchives()
    {
        var malformed = WriteRom("Malformed.gba", "Example GBA", "ABCE");
        var malformedBytes = File.ReadAllBytes(malformed);
        malformedBytes[0xBC] ^= 0x01; // Header complement is no longer valid.
        File.WriteAllBytes(malformed, malformedBytes);

        var archive = WriteRom("Example.zip", "Example GBA", "ABCE");
        var headered = Path.Combine(BaseDirectory, "Copier header.gba");
        File.WriteAllBytes(headered, [.. new byte[512], .. File.ReadAllBytes(WriteRom("Raw.gba", "Example GBA", "ABCE"))]);

        Assert.Null(GameBoyAdvanceRomReader.TryRecognize(malformed));
        Assert.Null(GameBoyAdvanceRomReader.TryRecognize(headered));
        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(malformed).MatchFor("gba"));
        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(headered).MatchFor("gba"));
        Assert.Equal(GameFileMatch.Unsupported, _rules.AnalyzeFile(archive).MatchFor("gba"));
        Assert.False(_rules.IsFolderCandidate(malformed, System("gba")));
        Assert.False(_rules.IsFolderCandidate(headered, System("gba")));
        Assert.False(_rules.IsFolderCandidate(archive, System("gba")));
        Assert.Same(GameImportMetadata.Empty, _rules.ReadImportMetadata(malformed, System("gba")));
    }

    [Fact]
    public void Recognition_RejectsAHeaderWithAChangedNintendoLogo()
    {
        var path = WriteRom("Forged logo.gba", "Example GBA", "ABCE");
        var bytes = File.ReadAllBytes(path);
        bytes[0x04] ^= 0x01;
        File.WriteAllBytes(path, bytes);

        Assert.Null(GameBoyAdvanceRomReader.TryRecognize(path));
        Assert.False(_rules.IsFolderCandidate(path, System("gba")));
    }

    [Fact]
    public void MalformedTitleDoesNotBecomePresentationEvidence()
    {
        var path = WriteRom("Filename fallback.gba", "Example GBA", "ABCE");
        var bytes = File.ReadAllBytes(path);
        bytes[0xA0] = 0x01;
        bytes[0xBD] = CalculateHeaderChecksum(bytes);
        File.WriteAllBytes(path, bytes);

        var header = GameBoyAdvanceRomReader.TryRecognize(path);
        var metadata = _rules.ReadImportMetadata(path, System("gba"));

        // A malformed header title is never surfaced as a display title, and import evidence is
        // deferred to enrichment regardless.
        Assert.NotNull(header);
        Assert.Null(header.Title);
        Assert.Same(GameImportMetadata.Empty, metadata);
    }

    [Fact]
    public void Recognition_RejectsOversizedRomBeforeHashing()
    {
        var path = WriteRom("Oversized.gba", "Example GBA", "ABCE");
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(GameBoyAdvanceRomReader.MaximumRomBytes + 1);

        Assert.Null(GameBoyAdvanceRomReader.TryRecognize(path));
        Assert.Null(GameBoyAdvanceRomReader.TryRead(path));
        Assert.False(_rules.IsFolderCandidate(path, System("gba")));
    }

    private string WriteRom(string name, string title, string gameCode, byte payloadValue = 0)
    {
        var bytes = CreateRomFixture(title, gameCode, payloadValue);
        var path = Path.Combine(BaseDirectory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    internal static byte[] CreateRomFixture(string title, string gameCode, byte payloadValue = 0)
    {
        var bytes = new byte[0x1000];
        bytes[3] = 0xEA;
        Convert.FromHexString(NintendoLogoHex).CopyTo(bytes, 0x04);
        Encoding.ASCII.GetBytes(title).CopyTo(bytes, 0xA0);
        Encoding.ASCII.GetBytes(gameCode).CopyTo(bytes, 0xAC);
        "01"u8.CopyTo(bytes.AsSpan(0xB0));
        bytes[0xB2] = 0x96;
        bytes[0xC0] = payloadValue;
        bytes[0xBD] = CalculateHeaderChecksum(bytes);
        return bytes;
    }

    private static byte CalculateHeaderChecksum(ReadOnlySpan<byte> bytes)
    {
        byte checksum = unchecked((byte)-0x19);
        foreach (var value in bytes.Slice(0xA0, 0x1D))
            checksum -= value;
        return checksum;
    }

    private static Core.Systems.GameSystem System(string id) =>
        KnownSystems.All.Single(system => system.Id == id);
}
