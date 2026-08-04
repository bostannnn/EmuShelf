using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.Infrastructure.Storage;

/// <summary>
/// A portable credential blob for platforms with no OS keychain we wire in — currently Linux
/// (including the Steam Deck) and macOS. The value is AES-GCM wrapped with an application-embedded key
/// so it never sits on disk as readable plaintext, yet still survives restarts and updates and travels
/// with the portable drive. Mirrors <see cref="WindowsDpapiProtectedTextStore"/>'s text API so a
/// credential store can use either behind <see cref="IProtectedTextStore"/>.
///
/// This is deliberate <em>obfuscation, not confidentiality</em>: the wrap key ships in the binary, so
/// anyone with EmuShelf can recover it. It is the same accepted trade-off documented for the
/// RetroAchievements key store — chosen so a login persists instead of being dropped on every launch.
/// See DECISIONS.md.
/// </summary>
internal sealed class PortableObfuscatedTextStore : IProtectedTextStore
{
    // Fixed 256-bit wrap key. Its entire purpose is to be identical on every machine so the blob is
    // portable; keeping it in source is intentional. It is NOT a secret — do not treat it as one.
    private static readonly byte[] WrapKey =
    {
        0x93, 0x2c, 0x7f, 0xa1, 0x0e, 0x58, 0xb4, 0xd6,
        0x21, 0xef, 0x3a, 0x8c, 0x75, 0x40, 0xc9, 0x1b,
        0x66, 0xad, 0x02, 0xf8, 0x9e, 0x37, 0x51, 0xba,
        0x4d, 0xe0, 0x28, 0x7c, 0x13, 0xcf, 0x84, 0x69,
    };

    private const int NonceSize = 12; // AES-GCM standard nonce length
    private const int TagSize = 16;   // AES-GCM maximum authentication tag length

    private readonly string _blobPath;
    private readonly string _credentialName;
    private readonly IAppLogger _logger;

    public PortableObfuscatedTextStore(string blobPath, string credentialName, IAppLogger? logger = null)
    {
        _blobPath = blobPath;
        _credentialName = credentialName;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public string? Read()
    {
        if (!File.Exists(_blobPath))
            return null;

        try
        {
            var blob = File.ReadAllBytes(_blobPath);
            if (blob.Length < NonceSize + TagSize)
                return null;

            var nonce = blob.AsSpan(0, NonceSize);
            var tag = blob.AsSpan(NonceSize, TagSize);
            var cipher = blob.AsSpan(NonceSize + TagSize);
            var plain = new byte[cipher.Length];

            using var aes = new AesGcm(WrapKey, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            // A corrupt, truncated, or foreign blob is treated as "nothing stored": the user simply
            // reconnects, exactly as if the file were absent. The value is never put in the message.
            _logger.Warning(
                $"Could not read the {_credentialName} credential blob; a reconnect will be required.", ex);
            return null;
        }
    }

    public void Write(string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(WrapKey, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag);

        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);

        var directory = Path.GetDirectoryName(_blobPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        AtomicFile.WriteAllBytes(_blobPath, blob);
        RestrictToOwner(_blobPath);
    }

    public void Clear()
    {
        if (File.Exists(_blobPath))
            File.Delete(_blobPath);
    }

    // Owner read/write only (0600) on Unix, so the wrapped blob is not world-readable in a shared
    // home. No-op on Windows, where this store is not used in production and NTFS ACLs govern access.
    private void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Portable exFAT/FAT/NTFS mounts have no Unix permission bits and reject chmod. The
            // wrapped blob is already written; owner-only hardening is best-effort, so a filesystem
            // that cannot honour it must never fail the save.
            _logger.Warning(
                $"Could not restrict {_credentialName} credential file permissions on this filesystem.", ex);
        }
    }
}
