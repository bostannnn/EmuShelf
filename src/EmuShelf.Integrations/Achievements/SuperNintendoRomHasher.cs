using System.Security.Cryptography;

namespace EmuShelf.Integrations.Achievements;

/// <summary>
/// Reproduces the rcheevos Super Nintendo hash: an optional 512-byte copier header (present when
/// <c>size % 0x2000 == 512</c>) is ignored, then the remaining bytes are MD5-hashed. This differs
/// from the whole-file cartridge hash used for Mega Drive / GBA, so it has its own reader.
/// </summary>
internal static class SuperNintendoRomHasher
{
    private const int CopierHeaderBytes = 512;

    public static string Hash(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        if (stream.Length % 0x2000 == CopierHeaderBytes)
            stream.Position = CopierHeaderBytes;

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            md5.AppendData(buffer, 0, read);
        }

        return Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant();
    }
}
