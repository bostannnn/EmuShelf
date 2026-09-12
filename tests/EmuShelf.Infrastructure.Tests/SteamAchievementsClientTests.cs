using System.Net;
using System.Text;
using EmuShelf.Core.Achievements;
using EmuShelf.Infrastructure.Achievements;

namespace EmuShelf.Infrastructure.Tests;

public sealed class SteamAchievementsClientTests
{
    private const string Schema = """
        {"game":{"gameName":"Fixture","availableGameStats":{"achievements":[
          {"name":"FIRST","displayName":"First","description":"Start","icon":"https://cdn.steamstatic.com/first.png","icongray":"https://cdn.steamstatic.com/first-gray.png","hidden":0},
          {"name":"SECRET","displayName":"Spoiler","description":"Finish","icon":"https://cdn.steamstatic.com/secret.png","icongray":"https://cdn.steamstatic.com/secret-gray.png","hidden":1}
        ]}}}
        """;

    [Fact]
    public async Task JoinsByApiName_UsesIntegerAppIdentity_AndNeverPutsKeyInUri()
    {
        var handler = new Responses((HttpStatusCode.OK, Schema), (HttpStatusCode.OK,
            """{"playerstats":{"success":true,"achievements":[{"apiname":"SECRET","achieved":0,"unlocktime":0},{"apiname":"FIRST","achieved":1,"unlocktime":1700000000}]}}"""));
        using var http = new HttpClient(handler);
        var result = await new SteamAchievementsClient(http).GetAchievementsAsync("76561198000000001", "381780", "private-key", CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.True(result.Snapshot!.ProgressKnown);
        Assert.True(result.Snapshot.Achievements[0].IsUnlocked);
        Assert.NotNull(result.Snapshot.Achievements[0].EarnedAt);
        Assert.True(result.Snapshot.Achievements[1].IsHidden);
        Assert.False(result.Snapshot.Achievements[1].IsUnlocked);
        Assert.All(handler.Requests, r =>
        {
            Assert.StartsWith("https://api.steampowered.com/", r.Uri);
            Assert.DoesNotContain("private-key", r.Uri);
            Assert.DoesNotContain("key=", r.Uri);
            Assert.Equal("private-key", r.Key);
        });
    }

    [Fact]
    public async Task PrivateProgressKeepsSchemaWithUnknownUnlocks()
    {
        using var http = new HttpClient(new Responses((HttpStatusCode.OK, Schema),
            (HttpStatusCode.OK, """{"playerstats":{"success":false,"error":"Profile is not public"}}""")));
        var result = await new SteamAchievementsClient(http).GetAchievementsAsync("76561198000000001", "381780", "key", CancellationToken.None);
        Assert.Equal(AchievementStatus.Unavailable, result.Status);
        Assert.False(result.Snapshot!.ProgressKnown);
        Assert.All(result.Snapshot.Achievements, entry => Assert.Null(entry.IsUnlocked));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, AchievementStatus.AuthenticationFailed)]
    [InlineData(HttpStatusCode.TooManyRequests, AchievementStatus.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AchievementStatus.ServerError)]
    public async Task SchemaFailureIsNotAnEmptyAchievementSet(HttpStatusCode code, AchievementStatus expected)
    {
        using var http = new HttpClient(new Responses((code, "failure")));
        var result = await new SteamAchievementsClient(http).GetAchievementsAsync("76561198000000001", "381780", "key", CancellationToken.None);
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, AchievementStatus.AuthenticationFailed)]
    [InlineData(HttpStatusCode.Unauthorized, AchievementStatus.AuthenticationFailed)]
    public async Task PrivatePlayerEndpointAndExpiredAuthenticationAreDistinct(HttpStatusCode code, AchievementStatus expected)
    {
        using var http = new HttpClient(new Responses((HttpStatusCode.OK, Schema), (code, "unavailable")));
        var result = await new SteamAchievementsClient(http).GetAchievementsAsync("76561198000000001", "381780", "key", CancellationToken.None);
        Assert.Equal(expected, result.Status);
        Assert.False(result.Snapshot!.ProgressKnown);
    }

    [Fact]
    public async Task DefinitionsAreReusedAcrossAccountsButProgressIsAlwaysFetched()
    {
        const string progress = """{"playerstats":{"success":false}}""";
        var handler = new Responses((HttpStatusCode.OK, Schema), (HttpStatusCode.OK, progress), (HttpStatusCode.OK, progress));
        using var http = new HttpClient(handler);
        var client = new SteamAchievementsClient(http);
        await client.GetAchievementsAsync("76561198000000001", "381780", "key", CancellationToken.None);
        var result = await client.GetAchievementsAsync("76561198000000002", "381780", "key", CancellationToken.None);
        Assert.Equal("76561198000000002", result.Snapshot!.AccountId);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(handler.Requests, request => request.Uri.Contains("GetSchemaForGame"));
    }

    [Fact]
    public async Task RevokedKeyWithCachedSchemaRemainsAnAuthenticationFailure()
    {
        using var http = new HttpClient(new Responses((HttpStatusCode.OK, Schema),
            (HttpStatusCode.OK, """{"playerstats":{"success":true,"achievements":[{"apiname":"FIRST","achieved":1,"unlocktime":0},{"apiname":"SECRET","achieved":0,"unlocktime":0}]}}"""),
            (HttpStatusCode.Forbidden, "revoked")));
        var client = new SteamAchievementsClient(http);
        Assert.True((await client.GetAchievementsAsync("76561198000000001", "381780", "key", CancellationToken.None)).Snapshot!.ProgressKnown);
        var result = await client.GetAchievementsAsync("76561198000000001", "381780", "key", CancellationToken.None);
        Assert.Equal(AchievementStatus.AuthenticationFailed, result.Status);
    }

    [Fact]
    public async Task VanityProfileResolvesAndChecksReturnedIdentity()
    {
        using var http = new HttpClient(new Responses(
            (HttpStatusCode.OK, """{"response":{"success":1,"steamid":"76561198000000001"}}"""),
            (HttpStatusCode.OK, """{"response":{"players":[{"steamid":"76561198000000001","personaname":"Fixture"}]}}""")));
        var result = await new SteamAchievementsClient(http).ResolveProfileAsync("https://steamcommunity.com/id/fixture/", "key", CancellationToken.None);
        Assert.Equal("76561198000000001", result.Profile?.SteamId);
        Assert.Equal("Fixture", result.Profile?.DisplayName);
    }

    [Theory]
    [InlineData("https://example.org/profiles/76561198000000001")]
    [InlineData("http://steamcommunity.com/id/fixture")]
    [InlineData("not-a-profile")]
    public async Task RejectsInvalidProfileWithoutNetwork(string profile)
    {
        var handler = new Responses();
        using var http = new HttpClient(handler);
        var result = await new SteamAchievementsClient(http).ResolveProfileAsync(profile, "key", CancellationToken.None);
        Assert.Equal(AchievementStatus.MalformedResponse, result.Status);
        Assert.Empty(handler.Requests);
    }

    private sealed class Responses(params (HttpStatusCode Status, string Json)[] responses) : HttpMessageHandler
    {
        private int _index;
        public List<(string Uri, string Key)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Headers.GetValues("x-webapi-key").Single()));
            var next = responses[_index++];
            return Task.FromResult(new HttpResponseMessage(next.Status) { Content = new StringContent(next.Json, Encoding.UTF8, "application/json") });
        }
    }
}
