using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
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

public class DreamcastGdiReaderTests : TempAppDirectoryTestBase
{
    // Every track change carries the standard 150-sector pregap that no track file stores, so a
    // track's file is exactly that much shorter than the gap between descriptor LBAs. Fixtures
    // reproduce it: without it they would only exercise layouts real dumps never produce.
    private const int PregapSectors = 150;

    private static readonly GameSystem Dreamcast =
        KnownSystems.All.Single(system => system.Id == "dreamcast");

    [Fact]
    public void GdiSet_IsRecognizedAndLeavesImportEvidenceToEnrichment()
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

        Assert.Equal(["dreamcast"], analysis.SuggestedSystems.Select(system => system.Id));
        Assert.Equal(GameFileMatch.Compatible, analysis.MatchFor("dreamcast"));
        Assert.True(rules.IsFolderCandidate(gdi, Dreamcast));

        // Import must not pay for a whole-track SHA-1; the extractor supplies it later.
        Assert.Same(GameImportMetadata.Empty, rules.ReadImportMetadata(gdi, Dreamcast));

        // The low-density track 01 is a data track too, so it contributes a secondary key; the
        // high-density track 03 is larger and stays primary.
        var identifiers = Extract(gdi);
        Assert.Equal(["Dreamcast track 03", "Dreamcast track 01"], identifiers.Select(id => id.Source));
        Assert.Equal([true, false], identifiers.Select(id => id.IsPrimary));
        Assert.All(identifiers, id => Assert.Equal(GameIdentifierKind.Sha1, id.Kind));
        Assert.Equal(
            Convert.ToHexString(SHA1.HashData(File.ReadAllBytes(dataTrack))),
            identifiers[0].Value);
        Assert.Equal(beforeDescriptor, File.ReadAllBytes(gdi));
        Assert.Equal(beforeTrack, SHA256.HashData(File.ReadAllBytes(dataTrack)));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(gdi));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(dataTrack));
    }

    // Redump hashes each track file and libretro's condensed catalogue keeps only the largest per
    // game — track 03 on a single-data-track disc, but a later high-density track when audio splits
    // the data. Both must be offered, largest first, or the audio-track games never match.
    [Fact]
    public void MultiTrackGdiSet_OffersEveryDataTrackSha1LargestFirst()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateMultiTrackGdiSet();

        var identifiers = Extract(gdi);

        Assert.Equal(
            ["Dreamcast track 05", "Dreamcast track 03", "Dreamcast track 01"],
            identifiers.Select(identifier => identifier.Source));
        Assert.Equal([true, false, false], identifiers.Select(identifier => identifier.IsPrimary));
        Assert.All(identifiers, identifier => Assert.Equal(GameIdentifierKind.Sha1, identifier.Kind));
        Assert.Equal(
            new[] { "track05.bin", "track03.bin", "track01.bin" }
                .Select(name => Convert.ToHexString(
                    SHA1.HashData(File.ReadAllBytes(Path.Combine(BaseDirectory, name))))),
            identifiers.Select(identifier => identifier.Value));
    }

    [Fact]
    public void GdiSet_ExposesNormalizedIpBinProductNumberAsFallbackEvidence()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateGdiSet(productNumber: "MK-51019");

        var serials = Extract(gdi).Where(item => item.Kind == GameIdentifierKind.Serial).ToArray();

        // Redump is inconsistent about Sega's MK- prefix that IP.BIN always keeps: US entries drop
        // it (51019), PAL entries such as MK-51053 retain it. Both spellings must be offered, the
        // disc's own first so a PAL disc prefers its exact PAL entry over the US one.
        Assert.Equal(["MK51019", "51019"], serials.Select(item => item.Value));
        Assert.All(serials, item =>
        {
            Assert.Equal("Dreamcast IP.BIN product number", item.Source);
            Assert.False(item.IsPrimary);
        });
    }

    // A sub-two-second audio track abuts the next track, leaving no extent to require. That is a
    // legal layout and must not reject the set; only a payload-less data track is invalid.
    [Fact]
    public void MultiTrackGdiSet_WithAudioTrackShorterThanAPregap_IsAccepted()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateMultiTrackGdiSet(trackFourSectors: 20, trackFourPregap: 0);

        Assert.True(DreamcastGdiReader.TryRecognize(gdi));
        Assert.Equal(
            GameFileMatch.Compatible,
            new FileImportRules().AnalyzeFile(gdi).MatchFor("dreamcast"));
    }

    [Fact]
    public void GdiSet_LabeledAsTranslation_DoesNotExposeRetailProductNumberFallback()
    {
        Directory.CreateDirectory(BaseDirectory);
        var original = CreateGdiSet(productNumber: "T7604M");
        var translated = Path.Combine(BaseDirectory, "Seven Mansions (English v1.4).gdi");
        File.Move(original, translated);

        Assert.DoesNotContain(Extract(translated), item => item.Kind == GameIdentifierKind.Serial);
    }

    [Fact]
    public void GdiSet_UnderAnUnrelatedPatchedLibraryRoot_StillExposesProductNumberFallback()
    {
        Directory.CreateDirectory(BaseDirectory);
        var original = CreateGdiSet(productNumber: "T7604M");
        var patchedRoot = Path.Combine(BaseDirectory, "Patches");
        Directory.CreateDirectory(patchedRoot);
        foreach (var path in Directory.GetFiles(BaseDirectory))
            File.Move(path, Path.Combine(patchedRoot, Path.GetFileName(path)));
        var moved = Path.Combine(patchedRoot, Path.GetFileName(original));

        Assert.Contains(Extract(moved), item => item.Kind == GameIdentifierKind.Serial);
    }

    // Real GDI sets place each following track 150 sectors beyond the previous track's own extent.
    // Treating the raw LBA delta as the required length rejects every set with audio tracks.
    [Fact]
    public void MultiTrackGdiSet_WithStandardPregaps_IsAccepted()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateMultiTrackGdiSet();
        var rules = new FileImportRules();

        Assert.True(DreamcastGdiReader.TryRecognize(gdi));
        Assert.Equal(GameFileMatch.Compatible, rules.AnalyzeFile(gdi).MatchFor("dreamcast"));
        Assert.True(rules.IsFolderCandidate(gdi, Dreamcast));
    }

    [Fact]
    public void GdiSet_WithNoPayloadBeforeTheFollowingPregap_IsRejected()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateMultiTrackGdiSet();
        var descriptor = File.ReadAllText(gdi)
            .Replace("4 45200", "4 45150", StringComparison.Ordinal)
            .Replace("5 45360", "5 45310", StringComparison.Ordinal);
        File.WriteAllText(gdi, descriptor);

        Assert.False(DreamcastGdiReader.TryRecognize(gdi));
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

        var result = new RetroAchievementsGameHasher().Identify(NewGame(gdi));

        var data = File.ReadAllBytes(dataTrack);
        var expectedInput = data.AsSpan(16, 256).ToArray()
            .Concat(data.AsSpan(21 * 2352 + 16, 300).ToArray())
            .ToArray();
        Assert.True(result.Status == RetroAchievementsIdentificationStatus.Hashed, result.Error);
        Assert.Equal(Convert.ToHexString(MD5.HashData(expectedInput)).ToLowerInvariant(), result.CanonicalHash);
        Assert.Equal("rcheevos-2ac45d3-dreamcast-v1", result.HashAlgorithmVersion);
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

        var result = new RetroAchievementsGameHasher().Identify(NewGame(gdi));

        var primary = File.ReadAllBytes(Path.Combine(BaseDirectory, "track03.bin"));
        var executable = File.ReadAllBytes(Path.Combine(BaseDirectory, "track05.bin"));
        var expected = primary.AsSpan(16, 256).ToArray()
            .Concat(executable.AsSpan(16, 300).ToArray())
            .ToArray();
        Assert.True(result.Status == RetroAchievementsIdentificationStatus.Hashed, result.Error);
        Assert.Equal(Convert.ToHexString(MD5.HashData(expected)).ToLowerInvariant(), result.CanonicalHash);
    }

    [Fact]
    public void GdiSet_PaddedPregapBytesAreNotReadAsLogicalSectors()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateMultiTrackGdiSet();
        var primaryPath = Path.Combine(BaseDirectory, "track03.bin");
        var primary = File.ReadAllBytes(primaryPath);
        Array.Resize(ref primary, 200 * 2352);

        // Point the ISO root at the sector immediately before track 04. A padded track file can
        // contain bytes there, but no valid GDI track owns that pregap sector.
        BinaryPrimitives.WriteUInt32LittleEndian(primary.AsSpan(16 * 2352 + 16 + 158), 45199);
        WriteDirectory(primary.AsSpan(199 * 2352 + 16, 2048), sector: 45199);
        File.WriteAllBytes(primaryPath, primary);

        var result = new RetroAchievementsGameHasher().Identify(NewGame(gdi));

        Assert.Equal(RetroAchievementsIdentificationStatus.InvalidMedia, result.Status);
    }

    // Real Dreamcast discs record absolute disc LBAs in their ISO9660 records, so the primary
    // layout must resolve without the relative-offset compatibility fallback.
    [Fact]
    public void GdiSet_WithAbsoluteIso9660Lbas_IsHashedFromTrackThree()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateGdiSet(absoluteLbas: true);

        var result = new RetroAchievementsGameHasher().Identify(NewGame(gdi));

        var data = File.ReadAllBytes(Path.Combine(BaseDirectory, "Example Track 03.bin"));
        var expected = data.AsSpan(16, 256).ToArray()
            .Concat(data.AsSpan(21 * 2352 + 16, 300).ToArray())
            .ToArray();
        Assert.True(result.Status == RetroAchievementsIdentificationStatus.Hashed, result.Error);
        Assert.Equal(Convert.ToHexString(MD5.HashData(expected)).ToLowerInvariant(), result.CanonicalHash);
    }

    // The reader accepts a cooked 2048-byte data track, so that layout needs its own coverage:
    // it has no raw sector header, which changes every user-data offset.
    [Fact]
    public void GdiSet_WithCookedDataTrack_IsRecognizedAndHashed()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateCookedGdiSet();
        var rules = new FileImportRules();

        Assert.Equal(GameFileMatch.Compatible, rules.AnalyzeFile(gdi).MatchFor("dreamcast"));
        var primary = Extract(gdi)[0];
        Assert.Equal("Dreamcast track 03", primary.Source);
        Assert.Equal(
            Convert.ToHexString(SHA1.HashData(
                File.ReadAllBytes(Path.Combine(BaseDirectory, "track03.bin")))),
            primary.Value);

        var result = new RetroAchievementsGameHasher().Identify(NewGame(gdi));

        var data = File.ReadAllBytes(Path.Combine(BaseDirectory, "track03.bin"));
        var expected = data.AsSpan(0, 256).ToArray()
            .Concat(data.AsSpan(21 * 2048, 300).ToArray())
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
        Assert.False(rules.IsFolderCandidate(gdi, Dreamcast));
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
        Assert.False(rules.IsFolderCandidate(gdi, Dreamcast));
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
        Assert.False(rules.IsFolderCandidate(gdi, Dreamcast));
        Assert.Empty(Extract(gdi));
        Assert.Same(GameImportMetadata.Empty, rules.ReadImportMetadata(gdi, Dreamcast));
    }

    [Fact]
    public void GdiWithMissingReferencedTrack_IsRejected()
    {
        Directory.CreateDirectory(BaseDirectory);
        var gdi = CreateGdiSet();
        File.Delete(Path.Combine(BaseDirectory, "track02.raw"));

        var rules = new FileImportRules();

        Assert.Equal(GameFileMatch.Incompatible, rules.AnalyzeFile(gdi).MatchFor("dreamcast"));
        Assert.False(rules.IsFolderCandidate(gdi, Dreamcast));
    }

    private static IReadOnlyList<GameIdentifier> Extract(string gdi) =>
        new DreamcastGdiIdentifierExtractor().Extract(NewGame(gdi));

    private static Game NewGame(string gdi) => new()
    {
        Id = 1,
        Title = "Example",
        SystemId = "dreamcast",
        Path = gdi,
        DateAdded = DateTimeOffset.UtcNow,
    };

    private string CreateGdiSet(bool absoluteLbas = false, string? productNumber = null)
    {
        const int trackThreeLba = 45150;
        var gdi = Path.Combine(BaseDirectory, "Example.gdi");
        File.WriteAllText(
            gdi,
            $"3\n1 0 4 2352 track01.bin 0\n2 45000 0 2352 track02.raw 0\n" +
            $"3 {trackThreeLba} 4 2352 \"Example Track 03.bin\" 0\n");
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track01.bin"), new byte[2352]);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track02.raw"), new byte[2352]);

        var origin = absoluteLbas ? (uint)trackThreeLba : 0;
        var data = new byte[24 * 2352];
        WriteRawModeOneHeader(data.AsSpan(0, 2352));
        var ip = data.AsSpan(16, 256);
        "SEGA SEGAKATANA "u8.CopyTo(ip);
        if (productNumber is not null)
            Encoding.ASCII.GetBytes(productNumber).CopyTo(ip[64..]);
        "1ST_READ.BIN"u8.CopyTo(ip[96..]);
        WritePvd(data.AsSpan(16 * 2352 + 16, 2048), rootDirectorySector: origin + 20);
        WriteDirectory(data.AsSpan(20 * 2352 + 16, 2048), sector: origin + 21);
        for (var index = 0; index < 300; index++)
            data[21 * 2352 + 16 + index] = (byte)((index * 17 + 3) & 0xFF);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "Example Track 03.bin"), data);
        return gdi;
    }

    private string CreateCookedGdiSet()
    {
        const int trackThreeLba = 45150;
        var gdi = Path.Combine(BaseDirectory, "Cooked.gdi");
        File.WriteAllText(
            gdi,
            $"3\n1 0 4 2352 track01.bin 0\n2 45000 0 2352 track02.raw 0\n" +
            $"3 {trackThreeLba} 4 2048 track03.bin 0\n");
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track01.bin"), new byte[2352]);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track02.raw"), new byte[2352]);

        // A cooked track has no raw sector header, so user data starts at each sector boundary.
        var data = new byte[24 * 2048];
        "SEGA SEGAKATANA "u8.CopyTo(data.AsSpan(0, 256));
        "1ST_READ.BIN"u8.CopyTo(data.AsSpan(96));
        WritePvd(data.AsSpan(16 * 2048, 2048), rootDirectorySector: trackThreeLba + 20);
        WriteDirectory(data.AsSpan(20 * 2048, 2048), sector: trackThreeLba + 21);
        for (var index = 0; index < 300; index++)
            data[21 * 2048 + index] = (byte)((index * 29 + 7) & 0xFF);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track03.bin"), data);
        return gdi;
    }

    // trackFourPregap defaults to the standard gap; pass 0 to abut track 05 against the audio
    // track, the layout a sub-two-second audio track produces.
    private string CreateMultiTrackGdiSet(int trackFourSectors = 10, int? trackFourPregap = null)
    {
        // Track 03 holds 50 sectors and track 04 starts 50 + 150 sectors later, exactly the shape
        // of a real dump: 102 Dalmatians' track 03 spans 221799 sectors with track 04 at +221949.
        const int trackThreeLba = 45000;
        const int trackThreeSectors = 50;
        const int trackFourLba = trackThreeLba + trackThreeSectors + PregapSectors;
        var trackFiveLba = trackFourLba + trackFourSectors + (trackFourPregap ?? PregapSectors);

        var gdi = Path.Combine(BaseDirectory, "Multi-track.gdi");
        File.WriteAllText(
            gdi,
            "5\n1 0 4 2352 track01.bin 0\n2 600 0 2352 track02.raw 0\n" +
            $"3 {trackThreeLba} 4 2352 track03.bin 0\n" +
            $"4 {trackFourLba} 0 2352 track04.raw 0\n" +
            $"5 {trackFiveLba} 4 2352 track05.bin 0\n");
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track01.bin"), new byte[2352]);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track02.raw"), new byte[2352]);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track04.raw"), new byte[trackFourSectors * 2352]);

        // Track 03 carries IP.BIN and the ISO9660 volume, but the boot executable lives in track 05
        // past the audio track, so the logical reader must span both.
        var primary = new byte[trackThreeSectors * 2352];
        WriteRawModeOneHeader(primary.AsSpan(0, 2352));
        "SEGA SEGAKATANA "u8.CopyTo(primary.AsSpan(16, 16));
        "1ST_READ.BIN"u8.CopyTo(primary.AsSpan(16 + 96));
        WritePvd(primary.AsSpan(16 * 2352 + 16, 2048), rootDirectorySector: trackThreeLba + 20);
        WriteDirectory(primary.AsSpan(20 * 2352 + 16, 2048), sector: (uint)trackFiveLba);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track03.bin"), primary);

        // Deliberately the largest data track, matching Sega Rally 2 and Tony Hawk's Pro Skater,
        // whose Redump entries are keyed on their final high-density track rather than track 03.
        var finalData = new byte[(trackThreeSectors + 10) * 2352];
        WriteRawModeOneHeader(finalData.AsSpan(0, 2352));
        for (var index = 0; index < 300; index++)
            finalData[16 + index] = (byte)((index * 23 + 5) & 0xFF);
        File.WriteAllBytes(Path.Combine(BaseDirectory, "track05.bin"), finalData);
        return gdi;
    }

    private static void WritePvd(Span<byte> pvd, uint rootDirectorySector)
    {
        pvd[0] = 1;
        "CD001"u8.CopyTo(pvd[1..]);
        BinaryPrimitives.WriteUInt16LittleEndian(pvd[128..], 2048);
        BinaryPrimitives.WriteUInt32LittleEndian(pvd[158..], rootDirectorySector);
        BinaryPrimitives.WriteUInt32LittleEndian(pvd[166..], 2048);
    }

    private static void WriteRawModeOneHeader(Span<byte> sector)
    {
        sector[0] = 0;
        sector[1..11].Fill(0xFF);
        sector[15] = 1;
    }

    private static void WriteDirectory(Span<byte> directory, uint sector)
    {
        var name = Encoding.ASCII.GetBytes("1ST_READ.BIN");
        var length = 33 + name.Length;
        directory[0] = (byte)length;
        BinaryPrimitives.WriteUInt32LittleEndian(directory[2..], sector);
        BinaryPrimitives.WriteUInt32LittleEndian(directory[10..], 300);
        directory[32] = (byte)name.Length;
        name.CopyTo(directory[33..]);
    }
}
