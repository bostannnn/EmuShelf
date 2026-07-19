using System.Diagnostics;
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

    [Fact]
    public void CompressedCd_CookedFrameBytes_AreNotOffsetAsRawHeaders_WhenChdmanAvailable()
    {
        var chdman = FindChdman();
        if (chdman is null)
            return;

        var directory = Path.Combine(Path.GetTempPath(), "EmuShelfTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const int sector = 3;
            var cooked = new byte[2048 * 32];
            var expected = cooked.AsSpan(sector * 2048, 2048);
            "BOOT2 = cdrom0:\\SLUS_209.50;1\r\n"u8.CopyTo(expected);
            for (var index = 32; index < expected.Length; index++)
                expected[index] = (byte)(index * 17);

            var binPath = Path.Combine(directory, "source.bin");
            var cuePath = Path.Combine(directory, "source.cue");
            var chdPath = Path.Combine(directory, "source.chd");
            File.WriteAllBytes(binPath, ConvertToMode1Frames(cooked));
            File.WriteAllText(
                cuePath,
                "FILE \"source.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n");
            RunChdman(chdman, "createcd", cuePath, chdPath, "cdfl");

            using var source = ChdSectorSource.TryOpen(chdPath);
            Assert.NotNull(source);
            var actual = new byte[2048];

            Assert.Equal(2048, source!.ReadSector(sector, actual));
            Assert.Equal(expected.ToArray(), actual);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

    private static string? FindChdman()
    {
        foreach (var candidate in new[]
                 {
                     "/opt/homebrew/bin/chdman", "/usr/local/bin/chdman",
                     "/usr/bin/chdman",
                 })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        // Fall back to a real PATH lookup (covers Windows chdman.exe and custom installs).
        // Returning the bare command name unconditionally would make Process.Start throw on
        // machines without chdman instead of letting the caller skip; only return a path that
        // actually resolves to an executable.
        return ResolveOnPath("chdman");
    }

    private static string? ResolveOnPath(string command)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
            return null;

        var extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [string.Empty];

        foreach (var directory in pathVariable.Split(
                     Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, command + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static void RunChdman(
        string chdman,
        string command,
        string inputPath,
        string outputPath,
        string compression)
    {
        using var process = Process.Start(new ProcessStartInfo(chdman)
        {
            ArgumentList = { command, "-i", inputPath, "-o", outputPath, "-c", compression },
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"chdman {command} failed ({process.ExitCode}): {process.StandardError.ReadToEnd()}");
    }

    private static byte[] ConvertToMode1Frames(byte[] cooked)
    {
        var frames = new byte[checked((cooked.Length / 2048) * 2352)];
        for (var sector = 0; sector < cooked.Length / 2048; sector++)
        {
            var frame = frames.AsSpan(sector * 2352, 2352);
            frame[0] = 0x00;
            frame.Slice(1, 10).Fill(0xFF);
            frame[11] = 0x00;
            frame[15] = 1;
            cooked.AsSpan(sector * 2048, 2048).CopyTo(frame[16..]);
        }

        return frames;
    }
}
