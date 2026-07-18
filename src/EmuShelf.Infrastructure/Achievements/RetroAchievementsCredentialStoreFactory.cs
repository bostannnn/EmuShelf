using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>
/// Chooses the platform-appropriate credential store: a DPAPI-protected blob under portable
/// <c>Settings/</c> on Windows (the v1 ship target), and a session-only store elsewhere so
/// macOS development never persists the secret without a verified protection story.
/// </summary>
public static class RetroAchievementsCredentialStoreFactory
{
    public const string BlobFileName = "retroachievements.key";

    public static IRetroAchievementsCredentialStore Create(
        IAppPaths paths,
        IAppLogger? logger = null)
    {
        if (OperatingSystem.IsWindows())
        {
            var blobPath = Path.Combine(paths.SettingsDirectory, BlobFileName);
            return new WindowsDpapiCredentialStore(blobPath, logger);
        }

        return new SessionOnlyCredentialStore();
    }
}
