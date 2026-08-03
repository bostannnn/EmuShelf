namespace EmuShelf.Core.Achievements;

/// <summary>
/// The connected account's non-secret identity. The Web API key is never stored here; it lives
/// only in <see cref="IRetroAchievementsCredentialStore"/> behind platform-specific protection.
/// </summary>
public sealed record RetroAchievementsAccount(string Username, string UserUlid);

/// <summary>
/// Platform-specific storage for the RetroAchievements Web API key. Implementations must keep the key
/// out of ordinary <c>settings.json</c>, diagnostics, and exception text, and persist it beside the
/// portable install so it survives restarts and updates. The shipped stores are a DPAPI-protected blob
/// under portable <c>Settings/</c> on Windows and an AES-GCM obfuscated blob elsewhere; see DECISIONS.md.
/// </summary>
public interface IRetroAchievementsCredentialStore
{
    /// <summary>Returns the stored API key, or <c>null</c> if none is stored or it cannot be read.</summary>
    string? GetApiKey();

    /// <summary>Persists the API key using the platform's protection, replacing any prior key.</summary>
    void SaveApiKey(string apiKey);

    /// <summary>Removes any stored API key. Safe to call when none exists.</summary>
    void ClearApiKey();
}
