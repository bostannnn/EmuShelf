using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public sealed class SuperNintendoRomReaderTests : TempAppDirectoryTestBase
{
    private readonly FileImportRules _rules = new();

    public SuperNintendoRomReaderTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    [Fact]
    public void LoRomSfc_UsesInternalHeaderAndExactHeaderlessSha1Evidence()
    {
        var headerless = CreateRomFixture("SUPER EMUSHELF");
        var path = Write("Example.sfc", headerless);
        var timestamp = new DateTime(2026, 7, 19, 15, 1, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);

        var header = SuperNintendoRomReader.TryRecognize(path);
        var evidence = SuperNintendoRomReader.TryRead(path);
        var analysis = _rules.AnalyzeFile(path);
        var metadata = _rules.ReadImportMetadata(path, System("snes"));

        Assert.NotNull(header);
        Assert.Equal("SUPER EMUSHELF", header.Title);
        Assert.Equal(SuperNintendoMapping.LoRom, header.Mapping);
        Assert.NotNull(evidence);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(headerless)), evidence.Sha1);
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("snes"));
        Assert.Equal(["snes"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.True(_rules.IsFolderCandidate(path, System("snes")));
        // The Shift-JIS-capable header title stays out of the display fields; only SHA-1 is evidence.
        Assert.Null(metadata.EmbeddedTitle);
        Assert.Collection(
            metadata.Identifiers,
            identifier =>
            {
                Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
                Assert.Equal(evidence.Sha1, identifier.Value);
                Assert.Equal("Super Nintendo ROM", identifier.Source);
                Assert.True(identifier.IsPrimary);
            });
        Assert.Equal(headerless, File.ReadAllBytes(path));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void HiRomSfc_IsRecognizedWithItsMapping()
    {
        var headerless = CreateRomFixture("HIROM GAME", SuperNintendoMapping.HiRom);
        var path = Write("HiRom.sfc", headerless);

        var header = SuperNintendoRomReader.TryRecognize(path);

        Assert.NotNull(header);
        Assert.Equal(SuperNintendoMapping.HiRom, header.Mapping);
        Assert.Equal("HIROM GAME", header.Title);
        Assert.True(_rules.IsFolderCandidate(path, System("snes")));
    }

    [Fact]
    public void SmcWithCopierHeader_NormalizesToTheSameHeaderlessSha1()
    {
        var headerless = CreateRomFixture("COPIER HEADER");
        var sfc = SuperNintendoRomReader.TryRead(Write("Raw.sfc", headerless));
        var smcPath = Write("Copier.smc", AddCopierHeader(headerless));

        var header = SuperNintendoRomReader.TryRecognize(smcPath);
        var smc = SuperNintendoRomReader.TryRead(smcPath);

        Assert.NotNull(header);
        Assert.NotNull(sfc);
        Assert.NotNull(smc);
        // The 512-byte copier header is normalized away, so a headered and headerless dump of the
        // same cartridge share one exact catalogue key.
        Assert.Equal(sfc.Sha1, smc.Sha1);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(headerless)), smc.Sha1);
        Assert.Equal(GameFileMatch.Compatible, _rules.AnalyzeFile(smcPath).MatchFor("snes"));
    }

    [Fact]
    public void Recognition_RejectsInconsistentChecksumComplement()
    {
        var bytes = CreateRomFixture("BAD CHECKSUM");
        bytes[0x7FDE] ^= 0x01; // Checksum no longer complements its pair.
        var path = Write("Bad.sfc", bytes);

        Assert.Null(SuperNintendoRomReader.TryRecognize(path));
        Assert.Null(SuperNintendoRomReader.TryRead(path));
        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(path).MatchFor("snes"));
        Assert.False(_rules.IsFolderCandidate(path, System("snes")));
        Assert.Same(GameImportMetadata.Empty, _rules.ReadImportMetadata(path, System("snes")));
    }

    [Fact]
    public void Recognition_RejectsResetVectorOutsideRom()
    {
        var bytes = CreateRomFixture("BAD RESET");
        bytes[0x7FFC] = 0x00;
        bytes[0x7FFD] = 0x00; // Emulation reset vector points at $0000, not into ROM.
        var path = Write("BadReset.sfc", bytes);

        Assert.Null(SuperNintendoRomReader.TryRecognize(path));
        Assert.False(_rules.IsFolderCandidate(path, System("snes")));
    }

    [Fact]
    public void Recognition_RejectsUndersizedAndOversizedImages()
    {
        var undersized = Write("Tiny.sfc", new byte[0x4000]);
        var oversized = Write("Huge.sfc", CreateRomFixture("OVERSIZE"));
        using (var stream = new FileStream(oversized, FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(SuperNintendoRomReader.MaximumNormalizedRomBytes + 1);

        Assert.Null(SuperNintendoRomReader.TryRecognize(undersized));
        Assert.Null(SuperNintendoRomReader.TryRecognize(oversized));
        Assert.False(_rules.IsFolderCandidate(undersized, System("snes")));
        Assert.False(_rules.IsFolderCandidate(oversized, System("snes")));
    }

    [Fact]
    public void Recognition_RejectsWrongExtension()
    {
        var path = Write("Example.zip", CreateRomFixture("ARCHIVE"));

        Assert.Null(SuperNintendoRomReader.TryRecognize(path));
        Assert.Equal(GameFileMatch.Unsupported, _rules.AnalyzeFile(path).MatchFor("snes"));
    }

    [Fact]
    public void AlteredPayloadWithTheSameHeaderGetsDifferentExactEvidence()
    {
        var original = SuperNintendoRomReader.TryRead(Write("A.sfc", CreateRomFixture("SAME TITLE")));
        var altered = SuperNintendoRomReader.TryRead(
            Write("B.sfc", CreateRomFixture("SAME TITLE", payloadValue: 0x99)));

        Assert.NotNull(original);
        Assert.NotNull(altered);
        Assert.NotEqual(original.Sha1, altered.Sha1);
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(BaseDirectory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Builds a headerless SNES ROM with a valid internal LoROM or HiROM header.</summary>
    internal static byte[] CreateRomFixture(
        string title = "SUPER EMUSHELF",
        SuperNintendoMapping mapping = SuperNintendoMapping.LoRom,
        byte payloadValue = 0)
    {
        var length = mapping == SuperNintendoMapping.HiRom ? 0x10000 : 0x8000;
        var headerOffset = mapping == SuperNintendoMapping.HiRom ? 0xFFC0 : 0x7FC0;
        var otherOffset = mapping == SuperNintendoMapping.HiRom ? 0x7FC0 : 0xFFC0;

        var bytes = new byte[length];
        for (var index = 0; index < bytes.Length; index++)
            bytes[index] = (byte)((index * 31 + 7) & 0xFF);
        bytes[0x10] = payloadValue;

        var title21 = Encoding.ASCII.GetBytes(title.Length > 21 ? title[..21] : title.PadRight(21));
        title21.CopyTo(bytes, headerOffset + 0x00);
        bytes[headerOffset + 0x15] = (byte)(mapping == SuperNintendoMapping.HiRom ? 0x21 : 0x20);

        // The complement is defined as checksum XOR 0xFFFF; the recognizer only checks consistency.
        const ushort checksum = 0xABCD;
        const ushort complement = checksum ^ 0xFFFF;
        bytes[headerOffset + 0x1C] = complement & 0xFF;
        bytes[headerOffset + 0x1D] = complement >> 8;
        bytes[headerOffset + 0x1E] = checksum & 0xFF;
        bytes[headerOffset + 0x1F] = checksum >> 8;

        // Emulation-mode reset vector must point into the $8000-$FFFF ROM window.
        bytes[headerOffset + 0x3C] = 0x00;
        bytes[headerOffset + 0x3D] = 0x80;

        // Make sure the unused mapping's header location cannot also validate.
        if (otherOffset + 0x40 <= length)
            bytes.AsSpan(otherOffset, 0x40).Clear();
        return bytes;
    }

    /// <summary>Prepends a 512-byte copier header, producing the classic <c>.smc</c> layout.</summary>
    internal static byte[] AddCopierHeader(byte[] headerless)
    {
        var smc = new byte[512 + headerless.Length];
        "SMC copier header"u8.CopyTo(smc);
        headerless.CopyTo(smc, 512);
        return smc;
    }

    private static Core.Systems.GameSystem System(string id) =>
        KnownSystems.All.Single(system => system.Id == id);
}
