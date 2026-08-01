using System.Runtime.Versioning;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>
/// Stores the Web API key as a DPAPI-protected blob under portable <c>Settings/</c> on Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDpapiCredentialStore : IRetroAchievementsCredentialStore
{
    private readonly WindowsDpapiProtectedTextStore _store;

    public WindowsDpapiCredentialStore(string blobPath, IAppLogger? logger = null)
    {
        _store = new WindowsDpapiProtectedTextStore(blobPath, "RetroAchievements", logger);
    }

    public string? GetApiKey() => _store.Read();

    public void SaveApiKey(string apiKey) => _store.Write(apiKey);

    public void ClearApiKey() => _store.Clear();
}
