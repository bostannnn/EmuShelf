using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Achievements;

namespace EmuShelf.Infrastructure.Tests.Achievements;

public class RetroAchievementsGameHasherTests : TempAppDirectoryTestBase
{
    private readonly RetroAchievementsGameHasher _hasher = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Identify_PlayStationCue_MatchesOfficialRcheevosVector_WithoutWriting(
        bool raw2352)
    {
        Directory.CreateDirectory(BaseDirectory);
        var binPath = Path.Combine(BaseDirectory, "game.bin");
        var cuePath = Path.Combine(BaseDirectory, "game.cue");
        var image = CreatePlayStationImage("SLUS_007.45", 0x07D800, isPs2: false);
        if (raw2352)
            image = ConvertTo2352(image, firstSector: 0);
        File.WriteAllBytes(binPath, image);
        File.WriteAllText(
            cuePath,
            $"FILE \"game.bin\" BINARY\n  TRACK 01 MODE1/{(raw2352 ? 2352 : 2048)}\n    INDEX 01 00:00:00\n");
        var timestamp = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(binPath, timestamp);
        File.SetLastWriteTimeUtc(cuePath, timestamp);
        var bytesBefore = SHA256.HashData(File.ReadAllBytes(binPath));
        var binTimeBefore = File.GetLastWriteTimeUtc(binPath);
        var cueTimeBefore = File.GetLastWriteTimeUtc(cuePath);

        var result = _hasher.Identify(Game("playstation", cuePath));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("db433fb038cde4fb15c144e8c7dea6e3", result.CanonicalHash);
        Assert.Equal(bytesBefore, SHA256.HashData(File.ReadAllBytes(binPath)));
        Assert.Equal(binTimeBefore, File.GetLastWriteTimeUtc(binPath));
        Assert.Equal(cueTimeBefore, File.GetLastWriteTimeUtc(cuePath));
    }

    [Fact]
    public void Identify_PlayStation2Iso_MatchesOfficialRcheevosVector()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game.iso");
        File.WriteAllBytes(path, CreatePlayStationImage("SLUS_200.64", 0x07D800, isPs2: true));

        var result = _hasher.Identify(Game("playstation2", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("01a517e4ad72c6c2654d1b839be7579d", result.CanonicalHash);
    }

    [Fact]
    public void Identify_GameCubeIso_MatchesOfficialRcheevosVector()
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game.gcm");
        File.WriteAllBytes(path, CreateGameCubeImage(32));

        var result = _hasher.Identify(Game("gamecube", path));

        Assert.Equal(RetroAchievementsIdentificationStatus.Hashed, result.Status);
        Assert.Equal("c7803b704fa43d22d8f6e55f4789cb45", result.CanonicalHash);
    }

    [Fact]
    public void Inspect_M3uAndCue_FingerprintChangesWithSelectedPayloadTimestamp()
    {
        Directory.CreateDirectory(BaseDirectory);
        var binPath = Path.Combine(BaseDirectory, "disc.bin");
        var cuePath = Path.Combine(BaseDirectory, "disc.cue");
        var m3uPath = Path.Combine(BaseDirectory, "game.m3u");
        File.WriteAllBytes(binPath, new byte[2048]);
        File.WriteAllText(cuePath, "FILE \"disc.bin\" BINARY\n  TRACK 01 MODE1/2048\n    INDEX 01 00:00:00\n");
        File.WriteAllText(m3uPath, "# first disc\ndisc.cue\n");
        var beforeTime = new DateTime(2026, 7, 13, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(binPath, beforeTime);

        var before = _hasher.Inspect(Game("playstation", m3uPath));
        File.SetLastWriteTimeUtc(binPath, beforeTime.AddMinutes(1));
        var after = _hasher.Inspect(Game("playstation", m3uPath));

        Assert.True(before.CanHash);
        Assert.True(after.CanHash);
        Assert.NotEqual(before.Fingerprint, after.Fingerprint);
    }

    [Theory]
    [InlineData("playstation", ".chd")]
    [InlineData("playstation2", ".cso")]
    [InlineData("gamecube", ".rvz")]
    [InlineData("wii", ".iso")]
    [InlineData("playstation3", "")]
    public void Identify_UnverifiedFormatsRemainUnsupported(
        string systemId,
        string extension)
    {
        Directory.CreateDirectory(BaseDirectory);
        var path = Path.Combine(BaseDirectory, "game" + extension);
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        var result = _hasher.Identify(Game(systemId, path));

        Assert.Equal(RetroAchievementsIdentificationStatus.UnsupportedFormat, result.Status);
        Assert.Null(result.CanonicalHash);
    }

    private static Game Game(string systemId, string path) => new()
    {
        Id = 1,
        SystemId = systemId,
        Path = path,
        Title = "Test",
        DateAdded = DateTimeOffset.UtcNow,
    };

    private static byte[] CreatePlayStationImage(
        string executableName,
        int executableSize,
        bool isPs2)
    {
        var sectors = ((executableSize + 2047) / 2048) + 20;
        var image = CreateIso9660(sectors, "TEST");
        var systemCnf = isPs2
            ? $"BOOT2 = cdrom0:\\{executableName};1\nVER = 1.0\nVMODE = NTSC\n"
            : $"BOOT=cdrom:\\{executableName};1\nTCB=4\nEVENT=10\nSTACK=801FFFF0\n";
        AddIsoFile(image, "SYSTEM.CNF", Encoding.ASCII.GetBytes(systemCnf));
        var executable = AddIsoFile(image, executableName, contents: null, executableSize);
        if (isPs2)
        {
            "\u007fELF"u8.CopyTo(executable);
        }
        else
        {
            "PS-X EXE"u8.CopyTo(executable);
            var bodySize = executableSize - 2048;
            BitConverter.GetBytes(bodySize).CopyTo(executable[28..32]);
        }
        return image;
    }

    private static byte[] CreateIso9660(int sectors, string label)
    {
        var image = new byte[sectors * 2048];
        var descriptor = image.AsSpan(16 * 2048, 2048);
        new byte[] { 0x01, (byte)'C', (byte)'D', (byte)'0', (byte)'0', (byte)'1', 0x01, 0x00 }
            .CopyTo(descriptor);
        Encoding.ASCII.GetBytes(label).CopyTo(descriptor[40..]);
        descriptor[128] = 0x00;
        descriptor[129] = 0x08;
        descriptor[158] = 17;
        image[17 * 2048 - 4] = 18;
        return image;
    }

    private static Span<byte> AddIsoFile(
        byte[] image,
        string filename,
        byte[]? contents,
        int contentsSize = -1)
    {
        if (contentsSize < 0)
            contentsSize = contents?.Length ?? 0;
        const int rootOffset = 17 * 2048;
        var entryOffset = rootOffset;
        while (image[entryOffset] != 0)
            entryOffset += image[entryOffset];

        var nextSector = image[rootOffset - 4] |
                         (image[rootOffset - 3] << 8) |
                         (image[rootOffset - 2] << 16);
        var nameBytes = Encoding.ASCII.GetBytes(filename);
        image[entryOffset] = (byte)(nameBytes.Length + 48);
        image[entryOffset + 2] = (byte)nextSector;
        image[entryOffset + 3] = (byte)(nextSector >> 8);
        image[entryOffset + 10] = (byte)contentsSize;
        image[entryOffset + 11] = (byte)(contentsSize >> 8);
        image[entryOffset + 12] = (byte)(contentsSize >> 16);
        image[entryOffset + 32] = (byte)(nameBytes.Length + 2);
        nameBytes.CopyTo(image.AsSpan(entryOffset + 33));
        image[entryOffset + 33 + nameBytes.Length] = (byte)';';
        image[entryOffset + 34 + nameBytes.Length] = (byte)'1';

        var file = image.AsSpan(nextSector * 2048, contentsSize);
        if (contents is not null)
            contents.CopyTo(file);
        else
            FillImage(file);

        nextSector += (contentsSize + 2047) / 2048;
        image[rootOffset - 4] = (byte)nextSector;
        image[rootOffset - 3] = (byte)(nextSector >> 8);
        image[rootOffset - 2] = (byte)(nextSector >> 16);
        return file;
    }

    private static byte[] CreateGameCubeImage(int megabytes)
    {
        var image = new byte[megabytes * 1024 * 1024];
        FillImage(image);
        image[0x1C] = 0xC2;
        image[0x1D] = 0x33;
        image[0x1E] = 0x9F;
        image[0x1F] = 0x3D;

        for (var index = 0; index < 8; index++)
            image[0x2440 + 0x14 + index] = (byte)(index % 4 == 3 ? 0xFF : 0);
        for (var index = 0; index < 4; index++)
            image[0x420 + index] = (byte)(index % 4 == 2 ? 0x30 : 0);
        for (var index = 0; index < 18 * 4; index++)
        {
            image[0x3000 + index] = (byte)(index % 4 == 2 ? 0x30 + 1 + index / 4 : 0);
            image[0x3000 + 0x90 + index] = (byte)(index % 8 == 3 ? 0xFF : 0);
        }
        return image;
    }

    private static void FillImage(Span<byte> image)
    {
        var size = image.Length;
        var seed = unchecked((int)(
            (uint)size ^ ((uint)size >> 8) ^ ((uint)(size - 1) * 25387U)));
        var offset = 0;
        while (size > 0)
        {
            int count;
            byte value;
            switch (seed & 0xFF)
            {
                case 0:
                    count = ((seed >> 8) & 0x3F) & ~(size & 0x0F);
                    if (count == 0)
                        count = 1;
                    value = 0;
                    break;
                case 1:
                    count = ((seed >> 8) & 0x07) + 1;
                    value = (byte)(seed >> 16);
                    break;
                case 2:
                    count = ((seed >> 8) & 0x03) + 1;
                    value = (byte)((seed >> 16) ^ 0xFF);
                    break;
                case 3:
                    count = ((seed >> 8) & 0x03) + 1;
                    value = (byte)((seed >> 16) ^ 0xA5);
                    break;
                case 4:
                    count = ((seed >> 8) & 0x03) + 1;
                    value = (byte)((seed >> 16) ^ 0xC3);
                    break;
                case 5:
                    count = ((seed >> 8) & 0x03) + 1;
                    value = (byte)((seed >> 16) ^ 0x96);
                    break;
                case 6:
                case 7:
                    count = ((seed >> 8) & 0x03) + 1;
                    value = (byte)((seed >> 16) ^ 0x78);
                    break;
                default:
                    count = 1;
                    value = (byte)((seed >> 8) ^ (seed >> 16));
                    break;
            }

            do
            {
                image[offset++] = value;
                size--;
            } while (size > 0 && --count > 0);

            seed = unchecked((seed * 0x41C64E6D + 12345) & 0x7FFFFFFF);
        }
    }

    private static byte[] ConvertTo2352(byte[] input, int firstSector)
    {
        var sectors = (input.Length + 2047) / 2048;
        var output = new byte[sectors * 2352];
        ReadOnlySpan<byte> sync = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];
        firstSector += 150;
        var frames = firstSector % 75;
        firstSector /= 75;
        var seconds = firstSector % 60;
        var minutes = firstSector / 60;

        for (var index = 0; index < sectors; index++)
        {
            var destination = output.AsSpan(index * 2352, 2352);
            sync.CopyTo(destination);
            destination[12] = ToBcd(minutes);
            destination[13] = ToBcd(seconds);
            destination[14] = ToBcd(frames);
            destination[15] = 2;
            input.AsSpan(index * 2048, 2048).CopyTo(destination[16..]);
            if (++frames == 75)
            {
                frames = 0;
                if (++seconds == 60)
                {
                    seconds = 0;
                    minutes++;
                }
            }
        }
        return output;
    }

    private static byte ToBcd(int value) =>
        (byte)(((value / 10) << 4) | (value % 10));
}
