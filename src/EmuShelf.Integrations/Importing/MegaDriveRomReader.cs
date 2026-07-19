using System.Security.Cryptography;

namespace EmuShelf.Integrations.Importing;

/// <summary>
/// Recognizes the deliberately small Mega Drive / Genesis single-ROM set and produces the
/// SHA-1 of its canonical cartridge bytes. The reader never writes to the supplied file.
/// </summary>
public static class MegaDriveRomReader
{
    // Genesis Plus GX uses the same 10 MiB cartridge ceiling. Keeping the importer at that
    // ceiling makes checksum work bounded and prevents a renamed unrelated image from becoming
    // a lengthy automatic scan candidate.
    public const long MaximumNormalizedRomBytes = 10 * 1024 * 1024;

    private const int HeaderOffset = 0x100;
    private const int HeaderLength = 4;
    private const int MinimumNormalizedRomBytes = 0x200;
    private const int CopierHeaderBytes = 512;
    private const int InterleavedBlockBytes = 0x4000;

    private static readonly HashSet<string> RawExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".md", ".gen", ".bin" };

    /// <summary>True only for the extension/layout combinations supported by M16.</summary>
    public static MegaDriveRomEvidence? TryRead(string path)
    {
        var extension = Path.GetExtension(path);
        if (RawExtensions.Contains(extension))
            return TryReadRaw(path);

        return extension.Equals(".smd", StringComparison.OrdinalIgnoreCase)
            ? TryReadInterleavedSmd(path)
            : null;
    }

    private static MegaDriveRomEvidence? TryReadRaw(string path)
    {
        try
        {
            using var stream = OpenRead(path);
            if (!IsValidNormalizedLength(stream.Length) || !HasSegaHeader(stream))
                return null;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            stream.Position = 0;
            AppendExactly(hash, stream, stream.Length);
            return new MegaDriveRomEvidence(
                Convert.ToHexString(hash.GetHashAndReset()),
                MegaDriveRomLayout.Raw);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static MegaDriveRomEvidence? TryReadInterleavedSmd(string path)
    {
        try
        {
            using var stream = OpenRead(path);
            var normalizedLength = stream.Length - CopierHeaderBytes;
            if (normalizedLength <= 0 ||
                normalizedLength > MaximumNormalizedRomBytes ||
                normalizedLength % InterleavedBlockBytes != 0)
            {
                return null;
            }

            stream.Position = CopierHeaderBytes;
            var block = new byte[InterleavedBlockBytes];
            ReadExactly(stream, block);
            DeinterleaveBlock(block);
            if (!HasSegaHeader(block))
                return null;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            hash.AppendData(block);
            for (var offset = InterleavedBlockBytes; offset < normalizedLength; offset += InterleavedBlockBytes)
            {
                ReadExactly(stream, block);
                DeinterleaveBlock(block);
                hash.AppendData(block);
            }

            return new MegaDriveRomEvidence(
                Convert.ToHexString(hash.GetHashAndReset()),
                MegaDriveRomLayout.CopierInterleaved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete,
        bufferSize: 64 * 1024,
        FileOptions.SequentialScan);

    private static bool IsValidNormalizedLength(long length) =>
        length >= MinimumNormalizedRomBytes &&
        length <= MaximumNormalizedRomBytes &&
        length % 2 == 0;

    private static bool HasSegaHeader(Stream stream)
    {
        if (stream.Length < HeaderOffset + HeaderLength)
            return false;

        Span<byte> header = stackalloc byte[HeaderLength];
        stream.Position = HeaderOffset;
        return stream.Read(header) == HeaderLength && header.SequenceEqual("SEGA"u8);
    }

    private static bool HasSegaHeader(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= HeaderOffset + HeaderLength &&
        bytes.Slice(HeaderOffset, HeaderLength).SequenceEqual("SEGA"u8);

    private static void AppendExactly(IncrementalHash hash, Stream stream, long remaining)
    {
        var buffer = new byte[64 * 1024];
        while (remaining > 0)
        {
            var requested = (int)Math.Min(buffer.Length, remaining);
            var read = stream.Read(buffer, 0, requested);
            if (read == 0)
                throw new EndOfStreamException("The Mega Drive ROM changed while it was being read.");

            hash.AppendData(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer[read..]);
            if (count == 0)
                throw new EndOfStreamException("The Mega Drive ROM changed while it was being read.");

            read += count;
        }
    }

    // Matches Genesis Plus GX's deinterleave_block: each 16 KiB SMD payload block stores the
    // odd byte lane first and the even byte lane second.
    private static void DeinterleaveBlock(byte[] block)
    {
        var source = (byte[])block.Clone();
        for (var index = 0; index < InterleavedBlockBytes / 2; index++)
        {
            block[index * 2] = source[(InterleavedBlockBytes / 2) + index];
            block[(index * 2) + 1] = source[index];
        }
    }
}

public enum MegaDriveRomLayout
{
    Raw,
    CopierInterleaved,
}

public sealed record MegaDriveRomEvidence(string Sha1, MegaDriveRomLayout Layout);
