using EmuShelf.Core.Achievements;

namespace EmuShelf.Infrastructure.Achievements;

/// <summary>
/// Holds the Web API key in memory for the current process only. Used on platforms without a
/// verified at-rest protection story (macOS development): the key is never written to disk, so a
/// reconnect is required after each restart. This keeps the secret out of the portable install.
/// </summary>
public sealed class SessionOnlyCredentialStore : IRetroAchievementsCredentialStore
{
    private string? _apiKey;

    public string? GetApiKey() => _apiKey;

    public void SaveApiKey(string apiKey) => _apiKey = apiKey;

    public void ClearApiKey() => _apiKey = null;
}
