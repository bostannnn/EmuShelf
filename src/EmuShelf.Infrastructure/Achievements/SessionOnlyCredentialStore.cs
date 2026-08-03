using EmuShelf.Core.Achievements;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>
/// Holds the Web API key in memory for the current process only; it is never written to disk, so a
/// reconnect is required after each restart. All shipping platforms now persist the key (see
/// <c>RetroAchievementsCredentialStoreFactory</c>), so this is the non-persisting in-memory
/// implementation used as a test double and available where a key must never touch the install.
/// </summary>
public sealed class SessionOnlyCredentialStore : IRetroAchievementsCredentialStore
{
    private string? _apiKey;

    public string? GetApiKey() => _apiKey;

    public void SaveApiKey(string apiKey) => _apiKey = apiKey;

    public void ClearApiKey() => _apiKey = null;
}
