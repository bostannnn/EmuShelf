using System.Security.Cryptography;

namespace EmuShelf.Integrations.Achievements;

/// <summary>Streams an imported cartridge file through the full-file MD5 algorithm used by rcheevos.</summary>
internal static class WholeFileRomHasher
{
    public static string Hash(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
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
