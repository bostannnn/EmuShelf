namespace EmuShelf.Core.Achievements;

/// <summary>
/// The connected account's non-secret identity. The Web API key is never stored here; it lives
/// only in <see cref="IRetroAchievementsCredentialStore"/> behind platform-specific protection.
/// </summary>
public sealed record RetroAchievementsAccount(string Username, string UserUlid);

/// <summary>
/// Platform-specific, secure storage for the RetroAchievements Web API key. Implementations must
/// keep the key out of ordinary <c>settings.json</c>, diagnostics, and exception text. The v1
/// recommendation is a DPAPI-protected blob under portable <c>Settings/</c> on Windows and a
/// session-only (in-memory) provider on macOS; see DECISIONS.md.
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
