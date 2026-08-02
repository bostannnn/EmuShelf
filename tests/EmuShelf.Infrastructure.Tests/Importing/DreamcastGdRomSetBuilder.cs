using System.Buffers.Binary;
using System.Text;

namespace EmuShelf.Infrastructure.Tests.Importing;

/// <summary>
/// Writes a complete GD-ROM shaped GDI set: a short low-density data track, an audio track, and
/// the high-density data track at LBA 45000 that carries the game's IP.BIN, ISO9660 volume, and
/// boot executable. The committed <c>dreamcast_gd.chd</c> fixture is chdman's conversion of
/// exactly this set, so a test can build the set and assert that both packagings of the same disc
/// read identically.
///
/// Two details make it a real GD-ROM rather than a generic three-track disc: track 01 is 300
/// frames but the descriptor puts track 02 at LBA 450, so chdman stores it padded to a four-frame
/// boundary and every later track's physical position stops matching its disc address; and track
/// 01 carries a low-density IP.BIN copy of its own, with a product number that differs from the
/// game's so a reader that mistakes it for the boot header is caught.
/// </summary>
internal static class DreamcastGdRomSetBuilder
{
    public const int LowDensityFrames = 300;
    public const int AudioTrackLba = 450;
    public const int AudioFrames = 913;
    public const int HighDensityLba = 45000;
    public const int HighDensityFrames = 32;

    public const string ProductNumber = "MK-51099";
    public const string LowDensityProductNumber = "MK-00001";
    public const string BootFile = "1ST_READ.BIN";
    public const int BootSize = 300;

    private const int FrameBytes = 2352;
    private const int UserDataOffset = 16;
    private const int BootSector = HighDensityLba + 21;

    public static string Write(string directory)
    {
        Directory.CreateDirectory(directory);
        var gdi = Path.Combine(directory, "Example.gdi");
        File.WriteAllText(
            gdi,
            "3\n" +
            "1 0 4 2352 track01.bin 0\n" +
            $"2 {AudioTrackLba} 0 2352 track02.raw 0\n" +
            $"3 {HighDensityLba} 4 2352 track03.bin 0\n");

        File.WriteAllBytes(Path.Combine(directory, "track01.bin"), BuildLowDensityTrack());
        File.WriteAllBytes(Path.Combine(directory, "track02.raw"), new byte[AudioFrames * FrameBytes]);
        File.WriteAllBytes(Path.Combine(directory, "track03.bin"), BuildHighDensityTrack());
        return gdi;
    }

    /// <summary>The boot executable's bytes, which the rcheevos hash covers after IP.BIN.</summary>
    public static byte[] BootExecutable()
    {
        var executable = new byte[BootSize];
        for (var index = 0; index < executable.Length; index++)
            executable[index] = (byte)((index * 17 + 3) & 0xFF);
        return executable;
    }

    private static byte[] BuildLowDensityTrack()
    {
        var track = new byte[LowDensityFrames * FrameBytes];
        for (var frame = 0; frame < LowDensityFrames; frame++)
            WriteRawModeOneHeader(track.AsSpan(frame * FrameBytes, FrameBytes), frame);

        WriteIpBin(UserData(track, 0), LowDensityProductNumber);
        return track;
    }

    private static byte[] BuildHighDensityTrack()
    {
        var track = new byte[HighDensityFrames * FrameBytes];
        for (var frame = 0; frame < HighDensityFrames; frame++)
            WriteRawModeOneHeader(track.AsSpan(frame * FrameBytes, FrameBytes), HighDensityLba + frame);

        WriteIpBin(UserData(track, 0), ProductNumber);
        WritePvd(UserData(track, 16), rootDirectorySector: HighDensityLba + 20);
        WriteDirectory(UserData(track, 20), BootSector);
        BootExecutable().CopyTo(UserData(track, 21));
        return track;
    }

    private static Span<byte> UserData(byte[] track, int frame) =>
        track.AsSpan(frame * FrameBytes + UserDataOffset, 2048);

    private static void WriteIpBin(Span<byte> userData, string productNumber)
    {
        "SEGA SEGAKATANA "u8.CopyTo(userData);
        Encoding.ASCII.GetBytes(productNumber).CopyTo(userData[64..]);
        Encoding.ASCII.GetBytes(BootFile).CopyTo(userData[96..]);
    }

    private static void WriteRawModeOneHeader(Span<byte> frame, int lba)
    {
        frame[0] = 0;
        frame[1..11].Fill(0xFF);
        var address = lba + 150;
        frame[12] = ToBcd(address / (75 * 60));
        frame[13] = ToBcd(address / 75 % 60);
        frame[14] = ToBcd(address % 75);
        frame[15] = 1;
    }

    private static byte ToBcd(int value) => (byte)(((value / 10) << 4) | (value % 10));

    private static void WritePvd(Span<byte> pvd, uint rootDirectorySector)
    {
        pvd[0] = 1;
        "CD001"u8.CopyTo(pvd[1..]);
        BinaryPrimitives.WriteUInt16LittleEndian(pvd[128..], 2048);
        BinaryPrimitives.WriteUInt32LittleEndian(pvd[158..], rootDirectorySector);
        BinaryPrimitives.WriteUInt32LittleEndian(pvd[166..], 2048);
    }

    private static void WriteDirectory(Span<byte> directory, uint sector)
    {
        var name = Encoding.ASCII.GetBytes(BootFile);
        directory[0] = (byte)(33 + name.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(directory[2..], sector);
        BinaryPrimitives.WriteUInt32LittleEndian(directory[10..], BootSize);
        directory[32] = (byte)name.Length;
        name.CopyTo(directory[33..]);
    }
}
