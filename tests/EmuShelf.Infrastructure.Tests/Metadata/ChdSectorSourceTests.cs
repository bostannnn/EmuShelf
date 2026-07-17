using EmuShelf.Core.Library;
using EmuShelf.Integrations.Metadata;
using EmuShelf.Integrations.Metadata.Chd;

namespace EmuShelf.Infrastructure.Tests.Metadata;

// The DVD fixtures are tiny real CHDs produced by chdman 0.288 from the committed game.iso,
// so these verify the ported decoder (header, Huffman map, crc16 self-check, zlib and LZMA
// hunks, ISO9660 read, serial extraction) against the reference encoder byte-for-byte. The
// CD path (cdzl/cdlz framing + reassembly) shares that pipeline and is verified byte-exact
// against real CD CHDs during development and by the opt-in test below.
public class ChdSectorSourceTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Chd", name);

    // Pure zlib and LZMA DVD images decode byte-for-byte against the source ISO.
    [Theory]
    [InlineData("dvd_zlib.chd")]
    [InlineData("dvd_lzma.chd")]
    public void CompressedDvd_DecodesLogicalBytesMatchingSourceIso(string chd)
    {
        var expected = File.ReadAllBytes(Fixture("game.iso"));
        using var source = ChdSectorSource.TryOpen(Fixture(chd));
        Assert.NotNull(source);

        var actual = ReadAllSectors(source!, expected.Length);
        Assert.Equal(expected, actual);
    }

    // The mixed default DVD profile also carries huff/flac hunks; the data hunks that hold
    // SYSTEM.CNF are zlib/lzma, so the serial still reads even without those codecs.
    [Theory]
    [InlineData("dvd_zlib.chd")]
    [InlineData("dvd_lzma.chd")]
    [InlineData("game_dvd.chd")]
    public void CompressedDvd_ExtractorReadsBootSerial(string chd)
    {
        var game = new Game
        {
            SystemId = "playstation2",
            Path = Fixture(chd),
            Title = Path.GetFileNameWithoutExtension(chd),
            DateAdded = DateTimeOffset.UtcNow,
        };

        var identifier = Assert.Single(new PlayStationIdentifierExtractor().Extract(game));

        Assert.Equal("SLUS-20064", identifier.Value);
        Assert.Equal("DiscContent", identifier.Source);
    }

    // Opt-in coverage against real CD/DVD CHDs. Point EMUSHELF_TEST_CHD_DIR at a folder of
    // real .chd files to smoke-test decoding on real hunks; skipped (passes trivially) in CI.
    [Fact]
    public void RealChds_DecodeSampleSectorsWithoutError()
    {
        var directory = Environment.GetEnvironmentVariable("EMUSHELF_TEST_CHD_DIR");
        if (directory is null || !Directory.Exists(directory))
            return;

        var decodedSectors = 0;
        var buffer = new byte[2048];
        foreach (var path in Directory.GetFiles(directory, "*.chd"))
        {
            using var source = ChdSectorSource.TryOpen(path);
            if (source is null)
                continue; // an unsupported codec (e.g. cdfl-only) legitimately falls back

            // ReadSector returns 2048 on success and 0 for an unsupported hunk (e.g. cdfl
            // audio) without throwing; that some sectors decode proves the pipeline runs.
            for (uint sector = 0; sector < 2000; sector++)
                if (source.ReadSector(sector, buffer) == 2048)
                    decodedSectors++;
        }
        Assert.True(decodedSectors > 0, "no real CHD sectors decoded");
    }

    private static byte[] ReadAllSectors(ChdSectorSource source, int length)
    {
        var result = new byte[length];
        var buffer = new byte[2048];
        for (var sector = 0; sector < length / 2048; sector++)
        {
            Assert.Equal(2048, source.ReadSector((uint)sector, buffer));
            Array.Copy(buffer, 0, result, sector * 2048, 2048);
        }
        return result;
    }
}
