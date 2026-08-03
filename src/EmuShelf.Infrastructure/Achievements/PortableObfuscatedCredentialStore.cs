using System.Security.Cryptography;
using System.Text;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>
/// Persists the Web API key beside the portable install (under <c>Settings/</c>) on platforms that
/// have no OS keychain we wire in — currently Linux (including the Steam Deck) and macOS. The key is
/// AES-GCM wrapped with an application-embedded key, so it never sits on disk as readable plaintext
/// yet still travels with the portable drive to any machine that runs EmuShelf.
///
/// This is deliberate <em>obfuscation, not confidentiality</em>: the wrap key ships in the binary, so
/// anyone with EmuShelf can recover it. That is an accepted trade-off for a low-value, one-click
/// resettable RetroAchievements read API key, chosen so the key survives restarts and updates instead
/// of the earlier session-only behaviour that dropped it on every launch. See DECISIONS.md.
/// </summary>
public sealed class PortableObfuscatedCredentialStore : IRetroAchievementsCredentialStore
{
    // Fixed 256-bit wrap key. Its entire purpose is to be identical on every machine so the blob is
    // portable; keeping it in source is intentional. It is NOT a secret — do not treat it as one.
    private static readonly byte[] WrapKey =
    {
        0x1f, 0x8b, 0x4c, 0x2a, 0x77, 0xe3, 0x05, 0x9d,
        0xb6, 0x41, 0xcf, 0x18, 0x62, 0xa0, 0x3e, 0xd7,
        0x54, 0x9c, 0x2b, 0x88, 0x0f, 0x6a, 0xe1, 0x37,
        0xca, 0x59, 0x14, 0xbd, 0x72, 0x86, 0x40, 0xf3,
    };

    private const int NonceSize = 12; // AES-GCM standard nonce length
    private const int TagSize = 16;   // AES-GCM maximum authentication tag length

    private readonly string _blobPath;
    private readonly IAppLogger _logger;

    public PortableObfuscatedCredentialStore(string blobPath, IAppLogger? logger = null)
    {
        _blobPath = blobPath;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public string? GetApiKey()
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
            // A corrupt, truncated, or foreign blob is treated as "no key stored": the user simply
            // reconnects, exactly as if the file were absent. The key is never put in the message.
            _logger.Warning(
                "Could not read the RetroAchievements credential blob; a reconnect will be required.", ex);
            return null;
        }
    }

    public void SaveApiKey(string apiKey)
    {
        var plain = Encoding.UTF8.GetBytes(apiKey);
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

    public void ClearApiKey()
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
                "Could not restrict RetroAchievements credential file permissions on this filesystem.", ex);
        }
    }
}
