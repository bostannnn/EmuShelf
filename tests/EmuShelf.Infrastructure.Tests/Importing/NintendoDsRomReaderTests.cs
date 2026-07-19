using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public sealed class NintendoDsRomReaderTests : TempAppDirectoryTestBase
{
    private readonly FileImportRules _rules = new();

    public NintendoDsRomReaderTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    [Fact]
    public void RawDsRom_UsesValidatedHeaderAndExactReadOnlySha1Evidence()
    {
        var path = WriteRom("Example.nds", "Example DS", "ABCE");
        var beforeBytes = File.ReadAllBytes(path);
        var beforeTimestamp = new DateTime(2026, 7, 19, 15, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, beforeTimestamp);

        var header = NintendoDsRomReader.TryRecognize(path);
        var evidence = NintendoDsRomReader.TryRead(path);
        var analysis = _rules.AnalyzeFile(path);
        var metadata = _rules.ReadImportMetadata(path, System("nds"));

        Assert.NotNull(header);
        Assert.Equal("Example DS", header.Title);
        Assert.Equal("ABCE", header.GameCode);
        Assert.False(header.IsHomebrew);
        Assert.NotNull(evidence);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(beforeBytes)), evidence.Sha1);
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("nds"));
        Assert.Equal(["nds"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.True(_rules.IsFolderCandidate(path, System("nds")));
        Assert.Equal("Example DS", metadata.EmbeddedTitle);
        Assert.Collection(
            metadata.Identifiers,
            identifier =>
            {
                Assert.Equal(GameIdentifierKind.TitleId, identifier.Kind);
                Assert.Equal("ABCE", identifier.Value);
                Assert.Equal("Nintendo DS header", identifier.Source);
                Assert.False(identifier.IsPrimary);
            },
            identifier =>
            {
                Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
                Assert.Equal(evidence.Sha1, identifier.Value);
                Assert.Equal("Nintendo DS ROM", identifier.Source);
                Assert.True(identifier.IsPrimary);
            });
        Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        Assert.Equal(beforeTimestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void ValidHomebrew_RemainsLocalEvidenceAndCannotUseASharedGameCodeForMetadata()
    {
        var path = WriteRom("Homebrew.nds", "Homebrew", "####", homebrew: true);

        var header = NintendoDsRomReader.TryRecognize(path);
        var metadata = _rules.ReadImportMetadata(path, System("nds"));

        Assert.NotNull(header);
        Assert.True(header.IsHomebrew);
        Assert.Null(header.GameCode);
        Assert.Equal("Homebrew", metadata.EmbeddedTitle);
        var identifier = Assert.Single(metadata.Identifiers);
        Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
        Assert.True(identifier.IsPrimary);
    }

    [Fact]
    public void Recognition_RejectsMalformedHeadersDsiExclusiveAndUnsupportedContainers()
    {
        var malformed = WriteRom("Malformed.nds", "Example DS", "ABCE");
        var malformedBytes = File.ReadAllBytes(malformed);
        malformedBytes[0x40] ^= 0x01; // Header CRC is no longer valid.
        File.WriteAllBytes(malformed, malformedBytes);

        var dsiExclusive = WriteRom("DSi only.nds", "Example DS", "ABCE", unitCode: 0x03);
        var archive = WriteRom("Example.zip", "Example DS", "ABCE");
        var headered = Path.Combine(BaseDirectory, "Copier header.nds");
        File.WriteAllBytes(headered, [.. new byte[512], .. File.ReadAllBytes(WriteRom("Raw.nds", "Example DS", "ABCE"))]);

        Assert.Null(NintendoDsRomReader.TryRecognize(malformed));
        Assert.Null(NintendoDsRomReader.TryRecognize(dsiExclusive));
        Assert.Null(NintendoDsRomReader.TryRecognize(headered));
        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(malformed).MatchFor("nds"));
        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(dsiExclusive).MatchFor("nds"));
        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(headered).MatchFor("nds"));
        Assert.Equal(GameFileMatch.Unsupported, _rules.AnalyzeFile(archive).MatchFor("nds"));
        Assert.False(_rules.IsFolderCandidate(malformed, System("nds")));
        Assert.False(_rules.IsFolderCandidate(dsiExclusive, System("nds")));
        Assert.False(_rules.IsFolderCandidate(headered, System("nds")));
        Assert.False(_rules.IsFolderCandidate(archive, System("nds")));
        Assert.Same(GameImportMetadata.Empty, _rules.ReadImportMetadata(malformed, System("nds")));
    }

    [Fact]
    public void Recognition_RejectsOversizedRomBeforeHashing()
    {
        var path = WriteRom("Oversized.nds", "Example DS", "ABCE");
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
            stream.SetLength(NintendoDsRomReader.MaximumRomBytes + 1);

        Assert.Null(NintendoDsRomReader.TryRecognize(path));
        Assert.Null(NintendoDsRomReader.TryRead(path));
        Assert.False(_rules.IsFolderCandidate(path, System("nds")));
    }

    private string WriteRom(
        string name,
        string title,
        string gameCode,
        bool homebrew = false,
        byte unitCode = 0)
    {
        var bytes = CreateRomFixture(title, gameCode, homebrew, unitCode);
        var path = Path.Combine(BaseDirectory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    internal static byte[] CreateRomFixture(
        string title,
        string gameCode,
        bool homebrew = false,
        byte unitCode = 0)
    {
        var bytes = new byte[0x10000];
        Encoding.ASCII.GetBytes(title).CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes(gameCode).CopyTo(bytes, 0x0C);
        "01"u8.CopyTo(bytes.AsSpan(0x10));
        bytes[0x12] = unitCode;
        bytes[0x14] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x20, 4), homebrew ? 0x200u : 0x4000u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x2C, 4), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x30, 4), homebrew ? 0x300u : 0x5000u);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x3C, 4), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x80, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x84, 4), 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x15C, 2), CalculateCrc16(bytes.AsSpan(0xC0, 156)));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x15E, 2), CalculateCrc16(bytes.AsSpan(0, 0x15E)));
        return bytes;
    }

    private static ushort CalculateCrc16(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
        }
        return crc;
    }

    private static Core.Systems.GameSystem System(string id) =>
        KnownSystems.All.Single(system => system.Id == id);
}
