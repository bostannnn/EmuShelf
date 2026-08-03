using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>
/// Chooses the platform-appropriate credential store, both writing to the same portable
/// <c>Settings/</c> blob so the key survives restarts and updates: a DPAPI-protected blob on Windows
/// (the v1 ship target), and an AES-GCM obfuscated blob elsewhere (Linux/Steam Deck, macOS) where no
/// OS keychain is wired in. The obfuscated blob trades strong at-rest protection for portability and
/// a persistent key; see <see cref="PortableObfuscatedCredentialStore"/> and DECISIONS.md.
/// </summary>
public static class RetroAchievementsCredentialStoreFactory
{
    public const string BlobFileName = "retroachievements.key";

    public static IRetroAchievementsCredentialStore Create(
        IAppPaths paths,
        IAppLogger? logger = null)
    {
        var blobPath = Path.Combine(paths.SettingsDirectory, BlobFileName);
        return OperatingSystem.IsWindows()
            ? new WindowsDpapiCredentialStore(blobPath, logger)
            : new PortableObfuscatedCredentialStore(blobPath, logger);
    }
}
