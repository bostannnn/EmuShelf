using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>
/// Holds the connected account's refresh token — the one persisted secret cloud sync has. The access
/// token is never stored: it lives for an hour and is cheap to re-mint.
/// </summary>
public interface IGoogleDriveTokenStore
{
    /// <summary>The stored refresh token, or null when no account is connected.</summary>
    string? Read();

    /// <summary>Replaces the stored refresh token.</summary>
    void Write(string refreshToken);

    /// <summary>Forgets the connected account.</summary>
    void Clear();
}

/// <summary>
/// Chooses the platform-appropriate protected blob, exactly as the RetroAchievements key does:
/// DPAPI on Windows, an application-embedded AES-GCM wrap elsewhere. Both write into the portable
/// <c>Settings/</c> directory so a connection survives restarts, updates, and moving the drive.
/// </summary>
/// <remarks>
/// The obfuscated variant is deliberate obfuscation, not confidentiality — the wrap key ships in the
/// binary. That trade-off is the one already documented for the achievements key: a login that
/// persists is worth more here than at-rest secrecy EmuShelf cannot actually provide on a portable
/// install. What it does buy is that the token is not sitting in settings.json in plain sight.
/// </remarks>
public static class GoogleDriveTokenStoreFactory
{
    public const string BlobFileName = "googledrive.token";

    private const string CredentialName = "EmuShelf Google Drive";

    public static IGoogleDriveTokenStore Create(IAppPaths paths, IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var blobPath = Path.Combine(paths.SettingsDirectory, BlobFileName);
        return new ProtectedGoogleDriveTokenStore(
            OperatingSystem.IsWindows()
                ? new WindowsDpapiProtectedTextStore(blobPath, CredentialName, logger)
                : new PortableObfuscatedTextStore(blobPath, CredentialName, logger));
    }
}

internal sealed class ProtectedGoogleDriveTokenStore(IProtectedTextStore inner) : IGoogleDriveTokenStore
{
    public string? Read()
    {
        var value = inner.Read();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public void Write(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        inner.Write(refreshToken);
    }

    public void Clear() => inner.Clear();
}
