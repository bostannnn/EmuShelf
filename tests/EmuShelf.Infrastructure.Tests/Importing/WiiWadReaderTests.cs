using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EmuShelf.Integrations.Importing;

namespace EmuShelf.Infrastructure.Tests.Importing;

public class WiiWadReaderTests : TempAppDirectoryTestBase
{
    public WiiWadReaderTests()
    {
        Directory.CreateDirectory(BaseDirectory);
    }

    [Fact]
    public void TryRead_InstallableWad_ReturnsGameCodeAndTitleId()
    {
        var path = Path.Combine(BaseDirectory, "Misleading Channel Name.wad");
        File.WriteAllBytes(path, BuildWad("WB4E"));

        var evidence = WiiWadReader.TryRead(path);

        Assert.NotNull(evidence);
        // "WB4E" is the GameTDB cover key; the full title id keeps the WiiWare title type high word.
        Assert.Equal("WB4E", evidence!.GameCode);
        Assert.Equal("0001000157423445", evidence.TitleId);
    }

    [Fact]
    public void TryRecognize_InstallableWad_ReturnsTrue()
    {
        var path = Path.Combine(BaseDirectory, "channel.wad");
        File.WriteAllBytes(path, BuildWad("WMLE"));

        Assert.True(WiiWadReader.TryRecognize(path));
    }

    [Fact]
    public void TryRead_SystemTitleWithoutPrintableCode_ReturnsTitleIdOnly()
    {
        // An IOS/system title (title id low word 0x00000002) is a recognized WAD but carries no
        // four-character game code, so it yields the title id alone and the library uses the filename.
        var path = Path.Combine(BaseDirectory, "IOS.wad");
        File.WriteAllBytes(path, BuildWad(0x0000000100000002UL));

        var evidence = WiiWadReader.TryRead(path);

        Assert.NotNull(evidence);
        Assert.Null(evidence!.GameCode);
        Assert.Equal("0000000100000002", evidence.TitleId);
    }

    [Fact]
    public void TryRecognize_DoomWad_IsRejected()
    {
        // A Doom IWAD borrows the extension but is not a Wii package: its first bytes are "IWAD",
        // not a 0x20 header size, so the structural check rejects it.
        var path = Path.Combine(BaseDirectory, "doom.wad");
        var bytes = new byte[0x100];
        Encoding.ASCII.GetBytes("IWAD").CopyTo(bytes, 0);
        File.WriteAllBytes(path, bytes);

        Assert.False(WiiWadReader.TryRecognize(path));
        Assert.Null(WiiWadReader.TryRead(path));
    }

    [Fact]
    public void TryRecognize_TruncatedBeforeTmd_IsRejected()
    {
        // A WAD whose declared sections run past the end of the file cannot be a valid package.
        var path = Path.Combine(BaseDirectory, "truncated.wad");
        var full = BuildWad("WB4E");
        File.WriteAllBytes(path, full[..0x100]);

        Assert.False(WiiWadReader.TryRecognize(path));
    }

    [Fact]
    public void TryRecognize_TooShortForHeader_IsRejected()
    {
        var path = Path.Combine(BaseDirectory, "tiny.wad");
        File.WriteAllBytes(path, new byte[0x10]);

        Assert.False(WiiWadReader.TryRecognize(path));
    }

    [Fact]
    public void TryRead_NonWadExtension_ReturnsNull()
    {
        var path = Path.Combine(BaseDirectory, "disc.iso");
        File.WriteAllBytes(path, BuildWad("WB4E"));

        Assert.Null(WiiWadReader.TryRead(path));
        Assert.False(WiiWadReader.TryRecognize(path));
    }

    [Fact]
    public void TryRead_DoesNotModifyTheFile()
    {
        var path = Path.Combine(BaseDirectory, "channel.wad");
        File.WriteAllBytes(path, BuildWad("WB4E"));
        var timestamp = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);
        var before = SHA256.HashData(File.ReadAllBytes(path));

        _ = WiiWadReader.TryRead(path);

        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
    }

    /// <summary>
    /// Builds a minimal but structurally valid installable ("Is") WAD whose TMD carries the given
    /// four-character game code under the standard WiiWare/channel title type. Sections are laid out
    /// exactly as a real WAD — header, cert, ticket, TMD, and data, each padded to a 0x40 boundary.
    /// </summary>
    public static byte[] BuildWad(string gameCode, uint titleTypeHigh = 0x00010001)
    {
        ArgumentNullException.ThrowIfNull(gameCode);
        if (gameCode.Length != 4)
            throw new ArgumentException("A WAD game code is four characters.", nameof(gameCode));

        var low = ((uint)gameCode[0] << 24) | ((uint)gameCode[1] << 16) |
                  ((uint)gameCode[2] << 8) | gameCode[3];
        return BuildWad(((ulong)titleTypeHigh << 32) | low);
    }

    /// <summary>Builds a minimal installable WAD whose TMD carries the given 64-bit title id.</summary>
    public static byte[] BuildWad(ulong titleId)
    {
        const int headerSize = 0x20;
        const int certSize = 0x40;
        const int ticketSize = 0x2A4;
        const int tmdSize = 0x208;
        const int dataSize = 0x40;

        var tmd = new byte[tmdSize];
        // RSA-2048 signature type, so the TMD header (and its title id at +0x4C) begins at 0x140.
        BinaryPrimitives.WriteUInt32BigEndian(tmd.AsSpan(0, 4), 0x00010001u);
        BinaryPrimitives.WriteUInt64BigEndian(tmd.AsSpan(0x140 + 0x4C, 8), titleId);

        var header = new byte[headerSize];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x00, 4), headerSize);
        header[0x04] = (byte)'I';
        header[0x05] = (byte)'s';
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x08, 4), certSize);
        // crl size (0x0C) and footer size (0x1C) stay zero.
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x10, 4), ticketSize);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x14, 4), tmdSize);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x18, 4), dataSize);

        using var buffer = new MemoryStream();

        void WritePadded(byte[] section)
        {
            buffer.Write(section, 0, section.Length);
            var padded = (buffer.Length + 0x3F) & ~0x3FL;
            buffer.Write(new byte[padded - buffer.Length]);
        }

        WritePadded(header);
        WritePadded(new byte[certSize]);
        // The crl section has size zero, so no bytes are written between the cert and the ticket.
        WritePadded(new byte[ticketSize]);
        WritePadded(tmd);
        WritePadded(new byte[dataSize]);
        return buffer.ToArray();
    }

    /// <summary>
    /// Builds an installable WAD whose TMD declares the given content sections (size, fill byte), laid
    /// out exactly as rcheevos' <c>rc_hash_wiiware</c> walks them — each content placed at its
    /// 0x40-aligned address after the TMD. Used to exercise the RetroAchievements WAD hash; the
    /// construction is byte-for-byte identical to the independent Python reference fixture.
    /// </summary>
    public static byte[] BuildWadWithContents(string gameCode, params (int Size, byte Fill)[] contents)
    {
        ArgumentNullException.ThrowIfNull(gameCode);
        if (gameCode.Length != 4)
            throw new ArgumentException("A WAD game code is four characters.", nameof(gameCode));

        const int certRaw = 0x40;
        const int ticketRaw = 0x2A4;
        const int tmdRaw = 0x240; // already 0x40-aligned; large enough for several content records

        var certAligned = Align64(certRaw);
        var ticketAligned = Align64(ticketRaw);
        var tmdAligned = Align64(tmdRaw);
        var tmdStart = 0x40 + certAligned + ticketAligned; // crl size is zero, as rcheevos assumes

        var low = ((uint)gameCode[0] << 24) | ((uint)gameCode[1] << 16) |
                  ((uint)gameCode[2] << 8) | gameCode[3];
        var titleId = ((ulong)0x00010001 << 32) | low;

        var tmd = new byte[tmdAligned];
        BinaryPrimitives.WriteUInt32BigEndian(tmd.AsSpan(0, 4), 0x00010001u); // RSA-2048 signature type
        BinaryPrimitives.WriteUInt64BigEndian(tmd.AsSpan(0x140 + 0x4C, 8), titleId);
        BinaryPrimitives.WriteUInt16BigEndian(tmd.AsSpan(0x1DE, 2), (ushort)contents.Length);
        for (var ix = 0; ix < contents.Length; ix++)
        {
            var record = 0x1E4 + ix * 0x24;
            BinaryPrimitives.WriteUInt64BigEndian(tmd.AsSpan(record + 0x08, 8), (ulong)contents[ix].Size);
        }

        var header = new byte[0x20];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x00, 4), 0x20);
        header[0x04] = (byte)'I';
        header[0x05] = (byte)'s';
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x08, 4), certRaw);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x10, 4), ticketRaw);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x14, 4), tmdRaw);

        using var buffer = new MemoryStream();

        void Pad(long target)
        {
            if (buffer.Length < target)
                buffer.Write(new byte[target - buffer.Length]);
        }

        buffer.Write(header);
        Pad(0x40);
        buffer.Write(new byte[certAligned]);
        buffer.Write(new byte[ticketAligned]);
        buffer.Write(tmd);

        var contentAddr = (long)tmdStart + tmdAligned;
        foreach (var (size, fill) in contents)
        {
            Pad(contentAddr);
            var data = new byte[size];
            Array.Fill(data, fill);
            buffer.Write(data);
            contentAddr = Align64(contentAddr + Align16(size));
        }
        Pad(contentAddr);

        return buffer.ToArray();
    }

    private static long Align16(long value) => (value + 0x0F) & ~0x0FL;
    private static long Align64(long value) => (value + 0x3F) & ~0x3FL;
}
