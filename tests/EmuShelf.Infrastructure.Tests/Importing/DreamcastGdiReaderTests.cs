using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Importing;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Integrations.Achievements;
using EmuShelf.Integrations.Importing;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.Infrastructure.Tests.Importing;

public class DreamcastGdiReaderTests : TempAppDirectoryTestBase
{
    [Fact]
    public void GdiSet_IsRecognizedAndExposesExactDataTrackEvidenceWithoutWriting()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateGdiSet();
        var dataTrack = Path.Combine(BaseDirectory, "Example Track 03.bin");
        var timestamp = new DateTime(2026, 7, 25, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(gdi, timestamp);
        File.SetLastWriteTimeUtc(dataTrack, timestamp);
        var beforeDescriptor = File.ReadAllBytes(gdi);
        var beforeTrack = SHA256.HashData(File.ReadAllBytes(dataTrack));
        var rules = new FileImportRules();

        var analysis = rules.AnalyzeFile(gdi);
        var metadata = rules.ReadImportMetadata(
            gdi,
            KnownSystems.All.Single(system => system.Id == "dreamcast"));

        Assert.Equal(["dreamcast"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("dreamcast"));
        Assert.True(rules.IsFolderCandidate(gdi, KnownSystems.All.Single(system => system.Id == "dreamcast")));
        var identifier = Assert.Single(metadata.Identifiers);
        Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind);
        Assert.Equal(Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(dataTrack))), identifier.Value);
        Assert.Equal(beforeDescriptor, File.ReadAllBytes(gdi));
        Assert.Equal(beforeTrack, SHA256.HashData(File.ReadAllBytes(dataTrack)));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(gdi));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(dataTrack));
    }

    [Fact]
    public void GdiSet_HashesLikeRcheevosWithoutWriting()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateGdiSet();
        var dataTrack = Path.Combine(BaseDirectory, "Example Track 03.bin");
        var timestamp = new DateTime(2026, 7, 25, 12, 1, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(gdi, timestamp);
        File.SetLastWriteTimeUtc(dataTrack, timestamp);
        var beforeDescriptor = File.ReadAllBytes(gdi);
        var beforeTrack = SHA256.HashData(File.ReadAllBytes(dataTrack));

        var hasher = new RetroAchievementsGameHasher();
        var result = hasher.Identify(new Game
        {
            Id = 1,
            Title = "Example",
            SystemId = "dreamcast",
            Path = gdi,
            DateAdded = DateTimeOffset.UtcNow,
        });

        var data = File.ReadAllBytes(dataTrack);
        var expectedInput = data.AsSpan(16, 256).ToArray()
            .Concat(data.AsSpan(21 * 2352 + 16, 300).ToArray())
            .ToArray();
        Assert.True(result.Status == RetroAchievementsIdentificationStatus.Hashed, result.Error);
        Assert.Equal(Convert.ToHexString(MD5.HashData(expectedInput)).ToLowerInvariant(), result.CanonicalHash);
        Assert.Equal("rcheevos-2ac45d3-dreamcast-gdi-v1", result.HashAlgorithmVersion);
        Assert.Equal(40, RetroAchievementsConsoles.ForSystem("dreamcast"));
        Assert.Equal(beforeDescriptor, File.ReadAllBytes(gdi));
        Assert.Equal(beforeTrack, SHA256.HashData(File.ReadAllBytes(dataTrack)));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(gdi));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(dataTrack));
    }

    [Fact]
    public void GdiSet_WithAudioTracks_HashesBootExecutableFromLaterDataTrack()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateMultiTrackGdiSet();
        var hasher = new RetroAchievementsGameHasher();

        var result = hasher.Identify(new Game
        {
            Id = 1,
            Title = "Multi-track example",
            SystemId = "dreamcast",
            Path = gdi,
            DateAdded = DateTimeOffset.UtcNow,
        });

        var primary = File.ReadAllBytes(Path.Combine(BaseDirectory, "track03.bin"));
        var executable = File.ReadAllBytes(Path.Combine(BaseDirectory, "track05.bin"));
        var expected = primary.AsSpan(16, 256).ToArray()
            .Concat(executable.AsSpan(16, 300).ToArray())
            .ToArray();
        Assert.True(result.Status == RetroAchievementsIdentificationStatus.Hashed, result.Error);
        Assert.Equal(Convert.ToHexString(MD5.HashData(expected)).ToLowerInvariant(), result.CanonicalHash);
    }

    [Fact]
    public void GdiSet_WithTruncatedHighDensityTrack_IsRejected()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateMultiTrackGdiSet();
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track03.bin"), new byte[23 * 2352]);

        var rules = new FileImportRules();

        Assert.Equal(GameFileMatch.Incompatible, rules.AnalyzeFile(gdi).MatchFor("dreamcast"));
        Assert.False(rules.IsFolderCandidate(gdi, KnownSystems.All.Single(system => system.Id == "dreamcast")));
    }

    [Fact]
    public void GdiSet_WithUnsupportedTrackType_IsRejected()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateGdiSet();
        var descriptor = File.ReadAllText(gdi).Replace("2 45000 0 2352", "2 45000 1 2352");
        File.WriteAllText(gdi, descriptor);

        var rules = new FileImportRules();

        Assert.Equal(GameFileMatch.Incompatible, rules.AnalyzeFile(gdi).MatchFor("dreamcast"));
        Assert.False(rules.IsFolderCandidate(gdi, KnownSystems.All.Single(system => system.Id == "dreamcast")));
    }

    [Fact]
    public void GdiWithoutTrackThreeIpBin_IsRejected()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = Path.Combine(BaseDirectory, "broken.gdi");
        File.WriteAllText(gdi, "3\n1 0 4 2352 track01.bin 0\n2 45000 0 2352 track02.raw 0\n3 45150 4 2352 track03.bin 0\n");
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track01.bin"), new byte[2352]);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track02.raw"), new byte[2352]);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track03.bin"), new byte[2352]);

        var rules = new FileImportRules();

        Assert.Equal(GameFileMatch.Incompatible, rules.AnalyzeFile(gdi).MatchFor("dreamcast"));
        Assert.False(rules.IsFolderCandidate(gdi, KnownSystems.All.Single(system => system.Id == "dreamcast")));
        Assert.Same(GameImportMetadata.Empty, rules.ReadImportMetadata(
            gdi,
            KnownSystems.All.Single(system => system.Id == "dreamcast")));
    }

    [Fact]
    public void GdiWithMissingReferencedTrack_IsRejected()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateGdiSet();
        File.Delete(Path.Combine(BaseDirectory, "track02.raw"));

        var rules = new FileImportRules();

        Assert.Equal(GameFileMatch.Incompatible, rules.AnalyzeFile(gdi).MatchFor("dreamcast"));
        Assert.False(rules.IsFolderCandidate(gdi, KnownSystems.All.Single(system => system.Id == "dreamcast")));
    }

    private string CreateGdiSet()
    {
        var gdi = Path.Combine(BaseDirectory, "Example.gdi");
        File.WriteAllText(gdi, "3\n1 0 4 2352 track01.bin 0\n2 45000 0 2352 track02.raw 0\n3 45150 4 2352 \"Example Track 03.bin\" 0\n");
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track01.bin"), new byte[2352]);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track02.raw"), new byte[2352]);

        var data = new byte[24 * 2352];
        WriteRawModeOneHeader(data.AsSpan(0, 2352));
        var ip = data.AsSpan(16, 256);
        "SEGA SEGAKATANA "u8.CopyTo(ip);
        "1ST_READ.BIN"u8.CopyTo(ip[96..]);
        WritePvd(data.AsSpan(16 * 2352 + 16, 2048));
        WriteDirectory(data.AsSpan(20 * 2352 + 16, 2048));
        for (var index = 0; index < 300; index++)
            data[21 * 2352 + 16 + index] = (byte)((index * 17 + 3) & 0xFF);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "Example Track 03.bin"), data);
        return gdi;
    }

    private string CreateMultiTrackGdiSet()
    {
        var gdi = Path.Combine(BaseDirectory, "Multi-track.gdi");
        File.WriteAllText(gdi, "5\n1 0 4 2352 track01.bin 0\n2 600 0 2352 track02.raw 0\n3 45000 4 2352 track03.bin 0\n4 45024 0 2352 track04.raw 0\n5 45025 4 2352 track05.bin 0\n");
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track01.bin"), new byte[2352]);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track02.raw"), new byte[2352]);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track04.raw"), new byte[2352]);

        // Two padding sectors intentionally extend track 03 into track 05's LBA. The logical
        // reader must honor the descriptor boundary and read the executable from track 05.
        var primary = new byte[26 * 2352];
        WriteRawModeOneHeader(primary.AsSpan(0, 2352));
        "SEGA SEGAKATANA "u8.CopyTo(primary.AsSpan(16, 16));
        "1ST_READ.BIN"u8.CopyTo(primary.AsSpan(16 + 96));
        WritePvd(primary.AsSpan(16 * 2352 + 16, 2048));
        WriteDirectory(primary.AsSpan(20 * 2352 + 16, 2048), sector: 45025);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track03.bin"), primary);

        var finalData = new byte[2352];
        WriteRawModeOneHeader(finalData);
        for (var index = 0; index < 300; index++)
            finalData[16 + index] = (byte)((index * 23 + 5) & 0xFF);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track05.bin"), finalData);
        return gdi;
    }

    private static void WritePvd(Span<byte> pvd)
    {
        pvd[0] = 1;
        "CD001"u8.CopyTo(pvd[1..]);
        BinaryPrimitives.WriteUInt16LittleEndian(pvd[128..], 2048);
        pvd[158] = 20;
        BinaryPrimitives.WriteUInt32LittleEndian(pvd[166..], 2048);
    }

    private static void WriteRawModeOneHeader(Span<byte> sector)
    {
        sector[0] = 0;
        sector[1..11].Fill(0xFF);
        sector[15] = 1;
    }

    private static void WriteDirectory(Span<byte> directory, uint sector = 21)
    {
        var name = Encoding.ASCII.GetBytes("1ST_READ.BIN");
        var length = 33 + name.Length;
        directory[0] = (byte)length;
        directory[2] = (byte)sector;
        directory[3] = (byte)(sector >> 8);
        directory[4] = (byte)(sector >> 16);
        BinaryPrimitives.WriteUInt32LittleEndian(directory[10..], 300);
        directory[32] = (byte)name.Length;
        name.CopyTo(directory[33..]);
    }
}
