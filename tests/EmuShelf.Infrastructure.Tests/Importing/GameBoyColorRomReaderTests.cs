using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public sealed class GameBoyColorRomReaderTests : TempAppDirectoryTestBase
{
    // The 48-byte Game Boy boot logo (0x104..0x133), present in every licensed cartridge.
    private const string GameBoyLogoHex =
        "CEED6666CC0D000B03730083000C000D" +
        "0008111F8889000EDCCC6EE6DDDDD999" +
        "BBBB67636E0EECCCDDDC999FBBB9333E";

    private readonly FileImportRules _rules = new();

    public GameBoyColorRomReaderTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    [Fact]
    public void RawGbcRom_UsesBoundedHeaderAndExactReadOnlySha1Evidence()
    {
        var path = WriteRom("Example.gbc", "EXAMPLE");
        var beforeBytes = File.ReadAllBytes(path);
        var beforeTimestamp = new DateTime(2026, 7, 31, 15, 1, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, beforeTimestamp);

        var header = GameBoyColorRomReader.TryRecognize(path);
        var evidence = GameBoyColorRomReader.TryRead(path);
        var analysis = _rules.AnalyzeFile(path);
        var metadata = _rules.ReadImportMetadata(path, System("gbc"));

        Assert.NotNull(header);
        Assert.Equal("EXAMPLE", header.Title);
        Assert.False(header.IsColorOnly);
        Assert.NotNull(evidence);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(beforeBytes)), evidence.Sha1);
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("gbc"));
        Assert.Equal(["gbc"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.True(_rules.IsFolderCandidate(path, System("gbc")));
        Assert.Null(metadata.EmbeddedTitle);
        // Game Boy Color has no game code: SHA-1 is the sole, primary identifier.
        var identifier = Assert.Single(metadata.Identifiers);
        Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
        Assert.Equal(evidence.Sha1, identifier.Value);
        Assert.Equal("Game Boy Color ROM", identifier.Source);
        Assert.True(identifier.IsPrimary);
        Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        Assert.Equal(beforeTimestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void ColorOnlyFlag_IsReadFromTheCgbByte()
    {
        var enhanced = GameBoyColorRomReader.TryRecognize(WriteRom("Enhanced.gbc", "ENH", 0x80));
        var colorOnly = GameBoyColorRomReader.TryRecognize(WriteRom("ColorOnly.gbc", "COL", 0xC0));

        Assert.NotNull(enhanced);
        Assert.False(enhanced.IsColorOnly);
        Assert.NotNull(colorOnly);
        Assert.True(colorOnly.IsColorOnly);
    }

    [Fact]
    public void GbcContentPackagedAsDotGb_IsStillAccepted()
    {
        var analysis = _rules.AnalyzeFile(WriteRom("Example.gb", "EXAMPLE"));

        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("gbc"));
        Assert.Equal(["gbc"], analysis.SuggestedSystems.Select(system => system.Id));
    }

    [Fact]
    public void PlainGameBoyRomWithoutTheCgbFlag_IsNotClassifiedAsColor()
    {
        // A valid Game Boy cartridge whose CGB flag is a title character (0x00 here) is an original
        // Game Boy title, which this platform deliberately does not claim.
        var path = WriteRom("Original.gb", "TETRIS", cgbFlag: 0x00);

        Assert.Null(GameBoyColorRomReader.TryRecognize(path));
        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(path).MatchFor("gbc"));
        Assert.False(_rules.IsFolderCandidate(path, System("gbc")));
        Assert.Same(GameImportMetadata.Empty, _rules.ReadImportMetadata(path, System("gbc")));
    }

    [Fact]
    public void Recognition_RejectsAForgedLogoAndAnInvalidHeaderChecksum()
    {
        var forgedLogo = WriteRom("Forged.gbc", "EXAMPLE");
        var logoBytes = File.ReadAllBytes(forgedLogo);
        logoBytes[0x104] ^= 0x01;
        File.WriteAllBytes(forgedLogo, logoBytes);

        var badChecksum = WriteRom("BadChecksum.gbc", "EXAMPLE");
        var checksumBytes = File.ReadAllBytes(badChecksum);
        checksumBytes[0x14D] ^= 0x01; // header checksum no longer matches the header.
        File.WriteAllBytes(badChecksum, checksumBytes);

        Assert.Null(GameBoyColorRomReader.TryRecognize(forgedLogo));
        Assert.Null(GameBoyColorRomReader.TryRecognize(badChecksum));
        Assert.False(_rules.IsFolderCandidate(forgedLogo, System("gbc")));
        Assert.False(_rules.IsFolderCandidate(badChecksum, System("gbc")));
    }

    [Fact]
    public void AlteredPayloadWithTheSameHeaderGetsDifferentExactEvidence()
    {
        var original = GameBoyColorRomReader.TryRead(WriteRom("A.gbc", "EXAMPLE"));
        var altered = GameBoyColorRomReader.TryRead(WriteRom("B.gbc", "EXAMPLE", payloadValue: 0x99));

        Assert.NotNull(original);
        Assert.NotNull(altered);
        Assert.NotEqual(original.Sha1, altered.Sha1);
    }

    [Fact]
    public void Recognition_RejectsOversizedRomBeforeHashing()
    {
        var path = WriteRom("Oversized.gbc", "EXAMPLE");
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(GameBoyColorRomReader.MaximumRomBytes + 1);

        Assert.Null(GameBoyColorRomReader.TryRecognize(path));
        Assert.Null(GameBoyColorRomReader.TryRead(path));
    }

    private string WriteRom(string name, string title, byte cgbFlag = 0x80, byte payloadValue = 0)
    {
        var bytes = CreateRomFixture(title, cgbFlag, payloadValue);
        var path = Path.Combine(BaseDirectory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    internal static byte[] CreateRomFixture(string title, byte cgbFlag = 0x80, byte payloadValue = 0)
    {
        var bytes = new byte[0x8000];
        Convert.FromHexString(GameBoyLogoHex).CopyTo(bytes, 0x104);
        Encoding.ASCII.GetBytes(title).CopyTo(bytes, 0x134);
        bytes[0x143] = cgbFlag;
        bytes[0x7FFF] = payloadValue;
        bytes[0x14D] = CalculateHeaderChecksum(bytes);
        return bytes;
    }

    private static byte CalculateHeaderChecksum(ReadOnlySpan<byte> bytes)
    {
        byte checksum = 0;
        for (var offset = 0x134; offset <= 0x14C; offset++)
            checksum = unchecked((byte)(checksum - bytes[offset] - 1));
        return checksum;
    }

    private static Core.Systems.GameSystem System(string id) =>
        KnownSystems.All.Single(system => system.Id == id);
}
