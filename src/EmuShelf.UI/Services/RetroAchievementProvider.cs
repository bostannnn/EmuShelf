using System.Globalization;
using EmuShelf.Core.Achievements;

namespace EmuShelf.App.Services;

public sealed class RetroAchievementProvider : IAchievementProvider, IDisposable
{
    private readonly IRetroAchievementsDetailsService _details;
    private readonly IRetroAchievementsAccountService _account;
    private readonly IRetroAchievementsBadgeCache? _badges;
    public RetroAchievementProvider(IRetroAchievementsDetailsService details, IRetroAchievementsAccountService account,
        IRetroAchievementsBadgeCache? badges)
    {
        _details = details; _account = account; _badges = badges;
        _details.DetailsRefreshed += OnRefreshed;
    }
    public string Id => "retroachievements";
    public string DisplayName => "RetroAchievements";
    public string? AccountId => _account.Account?.UserUlid;
    public bool IsConnected => _account.IsConnected;
    public bool SupportsPoints => true;
    public bool SupportsHardcore => true;
    public event Action? Changed;
    private void OnRefreshed(RetroAchievementsDetailsSnapshot snapshot) => Changed?.Invoke();
    public AchievementSnapshot? GetCached(AchievementGameRef game) => _details.GetCached(int.Parse(game.GameId, CultureInfo.InvariantCulture)) is { } cached
        ? Convert(cached, AccountId ?? "") : null;
    public async Task<AchievementResult> RefreshAsync(AchievementGameRef game, CancellationToken cancellationToken = default, bool manual = false)
    {
        if (_account.CurrentCredentials is not { } credentials) return new(AchievementStatus.NotConnected);
        var result = await _details.RefreshAsync(credentials, int.Parse(game.GameId, CultureInfo.InvariantCulture), cancellationToken, manual);
        return new(result.Status switch
        {
            RetroAchievementsRequestStatus.Success => AchievementStatus.Success,
            RetroAchievementsRequestStatus.AuthenticationFailed => AchievementStatus.AuthenticationFailed,
            RetroAchievementsRequestStatus.NotConnected => AchievementStatus.NotConnected,
            RetroAchievementsRequestStatus.Offline => AchievementStatus.Offline,
            RetroAchievementsRequestStatus.RateLimited => AchievementStatus.RateLimited,
            _ => AchievementStatus.ServerError,
        }, result.Value is { } value ? Convert(value, AccountId ?? "") : null, result.RetryAfter);
    }
    public Task<string?> GetIconPathAsync(string icon, CancellationToken cancellationToken = default) =>
        _badges?.GetBadgePathAsync(icon, cancellationToken) ?? Task.FromResult<string?>(null);
    public static AchievementSnapshot Convert(RetroAchievementsDetailsSnapshot snapshot, string account) => new(
        new("retroachievements", snapshot.Details.GameId.ToString(CultureInfo.InvariantCulture)), account,
        snapshot.Details.Title, snapshot.Details.Achievements.Select(a => new AchievementEntry(
            a.AchievementId.ToString(CultureInfo.InvariantCulture), a.Title, a.Description, a.BadgeName,
            a.DisplayOrder, a.IsEarned, a.DateEarnedHardcore ?? a.DateEarned, a.Points, a.IsHardcore)).ToArray(), snapshot.LastRefreshedAt);
    public void Dispose() => _details.DetailsRefreshed -= OnRefreshed;
}
