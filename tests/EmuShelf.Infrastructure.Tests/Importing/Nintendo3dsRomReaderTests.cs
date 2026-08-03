using System.Buffers.Binary;
using System.Text;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public sealed class Nintendo3dsRomReaderTests : TempAppDirectoryTestBase
{
    private readonly FileImportRules _rules = new();

    public Nintendo3dsRomReaderTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    [Fact]
    public void NcsdCartridge_ReadsProductCodeAndTitleIdFromPartitionZero_ReadOnly()
    {
        var path = Write("Ocarina Of Time 3D.3ds", BuildNcsd("CTR-P-AQNE", 0x0004000000033500));
        var beforeBytes = File.ReadAllBytes(path);
        var beforeTimestamp = new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, beforeTimestamp);

        var recognition = Nintendo3dsRomReader.TryRecognize(path);
        var evidence = Nintendo3dsRomReader.TryRead(path);
        var analysis = _rules.AnalyzeFile(path);
        var metadata = _rules.ReadImportMetadata(path, System("3ds"));

        Assert.Equal(Nintendo3dsFormat.NcsdCartridge, recognition?.Format);
        Assert.Equal(Nintendo3dsFormat.NcsdCartridge, evidence?.Format);
        Assert.Equal("CTR-P-AQNE", evidence!.ProductCode);
        Assert.Equal("0004000000033500", evidence.TitleId);
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("3ds"));
        Assert.Equal(["3ds"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.True(_rules.IsFolderCandidate(path, System("3ds")));
        Assert.Null(metadata.EmbeddedTitle);
        Assert.Collection(
            metadata.Identifiers,
            identifier =>
            {
                Assert.Equal(GameIdentifierKind.Serial, identifier.Kind);
                Assert.Equal("CTR-P-AQNE", identifier.Value);
                Assert.True(identifier.IsPrimary);
            },
            identifier =>
            {
                Assert.Equal(GameIdentifierKind.TitleId, identifier.Kind);
                Assert.Equal("0004000000033500", identifier.Value);
                Assert.False(identifier.IsPrimary);
            });
        Assert.Equal(beforeBytes, File.ReadAllBytes(path));
        Assert.Equal(beforeTimestamp, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void NcchTitle_ReadsProductCodeAndTitleId()
    {
        var path = Write("Homebrew Title.cxi", BuildNcch("CTR-P-AFAE", 0x0004000000030100));

        var evidence = Nintendo3dsRomReader.TryRead(path);
        var metadata = _rules.ReadImportMetadata(path, System("3ds"));

        Assert.Equal(Nintendo3dsFormat.Ncch, evidence?.Format);
        Assert.Equal("CTR-P-AFAE", evidence!.ProductCode);
        Assert.Equal("0004000000030100", evidence.TitleId);
        Assert.Equal(GameFileMatch.Compatible, _rules.AnalyzeFile(path).MatchFor("3ds"));
        Assert.Equal("CTR-P-AFAE", Assert.Single(metadata.Identifiers, id => id.IsPrimary).Value);
    }

    [Fact]
    public void CompressedCiaAndHomebrew_AreRecognizedAndLaunchableButCarryNoHeaderIdentity()
    {
        // Seekable-Zstandard compressed dumps (standard and skippable-metadata leading frames),
        // a CIA archive, and homebrew are all launchable but yield no exact identity here.
        var compressed = Write("Trimmed.z3ds", ZstandardFrame());
        var compressedSkippable = Write("Metadata First.zcci", ZstandardSkippableFrame());
        var cia = Write("eShop Title.cia", CiaHeader());
        var homebrew = Write("App.3dsx", Magic("3DSX"u8, 0x40));
        var elf = Write("App.elf", Magic([0x7F, (byte)'E', (byte)'L', (byte)'F'], 0x40));

        foreach (var (path, format) in new[]
                 {
                     (compressed, Nintendo3dsFormat.Compressed),
                     (compressedSkippable, Nintendo3dsFormat.Compressed),
                     (cia, Nintendo3dsFormat.Cia),
                     (homebrew, Nintendo3dsFormat.Homebrew),
                     (elf, Nintendo3dsFormat.Homebrew),
                 })
        {
            Assert.Equal(format, Nintendo3dsRomReader.TryRecognize(path)?.Format);
            var evidence = Nintendo3dsRomReader.TryRead(path);
            Assert.Equal(format, evidence?.Format);
            Assert.Null(evidence!.ProductCode);
            Assert.Null(evidence.TitleId);
            Assert.Equal(GameFileMatch.Compatible, _rules.AnalyzeFile(path).MatchFor("3ds"));
            Assert.True(_rules.IsFolderCandidate(path, System("3ds")));
            // No header identity, so the filename is the only cover evidence.
            Assert.Same(GameImportMetadata.Empty, _rules.ReadImportMetadata(path, System("3ds")));
        }
    }

    [Fact]
    public void HeaderTruncatedBeforeIdentityFields_IsRecognizedWithoutThrowingOrIdentity()
    {
        // A dump long enough to carry the magic at 0x100 but shorter than the identity fields must
        // not throw while reading: it is recognized and launchable, with the filename as the only
        // cover evidence.
        var ncch = new byte[0x110];
        "NCCH"u8.CopyTo(ncch.AsSpan(0x100));
        var ncsd = new byte[0x110];
        "NCSD"u8.CopyTo(ncsd.AsSpan(0x100));
        var ncchPath = Write("Truncated NCCH.cxi", ncch);
        var ncsdPath = Write("Truncated NCSD.3ds", ncsd);

        foreach (var (path, format) in new[]
                 {
                     (ncchPath, Nintendo3dsFormat.Ncch),
                     (ncsdPath, Nintendo3dsFormat.NcsdCartridge),
                 })
        {
            Assert.Equal(format, Nintendo3dsRomReader.TryRecognize(path)?.Format);
            var evidence = Nintendo3dsRomReader.TryRead(path);
            Assert.Equal(format, evidence?.Format);
            Assert.Null(evidence!.ProductCode);
            Assert.Null(evidence.TitleId);
            Assert.Equal(GameFileMatch.Compatible, _rules.AnalyzeFile(path).MatchFor("3ds"));
            Assert.Same(GameImportMetadata.Empty, _rules.ReadImportMetadata(path, System("3ds")));
        }
    }

    [Fact]
    public void Recognition_RejectsRenamedTruncatedAndUnsupportedFiles()
    {
        var renamedCartridge = Write("Not really.3ds", Enumerable.Repeat((byte)0x42, 0x8000).ToArray());
        var fakeCompressed = Write("Not zstd.zcci", Enumerable.Repeat((byte)0x42, 0x40).ToArray());
        var truncatedNcch = Write("Truncated.cxi", Magic("NCCH"u8, 0x40)); // magic lives at 0x100
        var archive = Write("Bundle.zip", ZstandardFrame());

        Assert.Null(Nintendo3dsRomReader.TryRecognize(renamedCartridge));
        Assert.Null(Nintendo3dsRomReader.TryRecognize(fakeCompressed));
        Assert.Null(Nintendo3dsRomReader.TryRecognize(truncatedNcch));
        Assert.Null(Nintendo3dsRomReader.TryRecognize(archive));

        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(renamedCartridge).MatchFor("3ds"));
        Assert.Equal(GameFileMatch.Incompatible, _rules.AnalyzeFile(fakeCompressed).MatchFor("3ds"));
        Assert.Equal(GameFileMatch.Unsupported, _rules.AnalyzeFile(archive).MatchFor("3ds"));
        Assert.False(_rules.IsFolderCandidate(renamedCartridge, System("3ds")));
        Assert.False(_rules.IsFolderCandidate(truncatedNcch, System("3ds")));
        Assert.False(_rules.IsFolderCandidate(archive, System("3ds")));
        Assert.Same(GameImportMetadata.Empty, _rules.ReadImportMetadata(renamedCartridge, System("3ds")));
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(BaseDirectory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    internal static byte[] BuildNcsd(string productCode, ulong titleId, long partitionOffset = 0x4000)
    {
        var bytes = new byte[partitionOffset + 0x200];
        "NCSD"u8.CopyTo(bytes.AsSpan(0x100));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x120, 4), (uint)(partitionOffset / 0x200));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x124, 4), 0x20); // partition 0 length units
        WriteNcch(bytes.AsSpan((int)partitionOffset), productCode, titleId);
        return bytes;
    }

    internal static byte[] BuildNcch(string productCode, ulong titleId)
    {
        var bytes = new byte[0x200];
        WriteNcch(bytes, productCode, titleId);
        return bytes;
    }

    private static void WriteNcch(Span<byte> ncch, string productCode, ulong titleId)
    {
        "NCCH"u8.CopyTo(ncch[0x100..]);
        BinaryPrimitives.WriteUInt64LittleEndian(ncch.Slice(0x118, 8), titleId);
        Encoding.ASCII.GetBytes(productCode).CopyTo(ncch[0x150..]);
    }

    private static byte[] ZstandardFrame()
    {
        var bytes = new byte[0x40];
        new byte[] { 0x28, 0xB5, 0x2F, 0xFD }.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] ZstandardSkippableFrame()
    {
        var bytes = new byte[0x40];
        new byte[] { 0x50, 0x2A, 0x4D, 0x18 }.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] CiaHeader()
    {
        var bytes = new byte[0x40];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 0x2020);
        return bytes; // type (0x04) and version (0x06) remain zero
    }

    private static byte[] Magic(ReadOnlySpan<byte> magic, int length)
    {
        var bytes = new byte[length];
        magic.CopyTo(bytes);
        return bytes;
    }

    private static Core.Systems.GameSystem System(string id) =>
        KnownSystems.All.Single(system => system.Id == id);
}
