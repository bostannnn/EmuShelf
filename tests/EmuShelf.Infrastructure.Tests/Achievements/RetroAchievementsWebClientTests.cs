using System.Net;
using System.Text;
using EmuShelf.Core.Achievements;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Integrations.Achievements;
using EmuShelf.Infrastructure.Achievements;

namespace EmuShelf.Infrastructure.Tests.Achievements;

public class RetroAchievementsWebClientTests
{
    private static readonly RetroAchievementsCredentials Credentials = new("Player", "SECRETKEY");

    [Fact]
    public async Task GetUserProfile_ParsesProfileAndUlid()
    {
        var client = Client(Ok(
            """{"User":"Player","ULID":"ULID-123","TotalPoints":1200,"TotalSoftcorePoints":40}"""));

        var response = await client.GetUserProfileAsync(Credentials, Cancellation);

        Assert.True(response.IsSuccess);
        Assert.Equal("Player", response.Value!.Username);
        Assert.Equal("ULID-123", response.Value.UserUlid);
        Assert.Equal(1200, response.Value.TotalPoints);
    }

    [Fact]
    public async Task GetGameList_ParsesGamesAndLowercasesHashes()
    {
        var client = Client(Ok(
            """[{"ID":1234,"Title":"Spyro","NumAchievements":40,"Hashes":["ABCDEF00","abcdef01"]}]"""));

        var response = await client.GetGameListAsync(Credentials, 12, Cancellation);

        Assert.True(response.IsSuccess);
        var game = Assert.Single(response.Value!);
        Assert.Equal(1234, game.GameId);
        Assert.Equal(40, game.AchievementCount);
        Assert.Equal(["abcdef00", "abcdef01"], game.Hashes);
    }

    [Fact]
    public async Task GetUserProgress_ParsesKeyedObject()
    {
        var client = Client(Ok(
            """{"1234":{"NumPossibleAchievements":40,"NumAchieved":12,"NumAchievedHardcore":3}}"""));

        var response = await client.GetUserProgressAsync(Credentials, [1234], Cancellation);

        Assert.True(response.IsSuccess);
        var progress = Assert.Single(response.Value!);
        Assert.Equal(1234, progress.GameId);
        Assert.Equal(40, progress.AchievementCount);
        Assert.Equal(12, progress.NumAwarded);
        Assert.Equal(3, progress.NumAwardedHardcore);
    }

    [Fact]
    public async Task GetUserProgress_EmptyIdList_ShortCircuitsWithoutRequest()
    {
        var handler = new StubHandler { Throw = new HttpRequestException("should not be called") };
        var client = new RetroAchievementsWebClient(new HttpClient(handler));

        var response = await client.GetUserProgressAsync(Credentials, [], Cancellation);

        Assert.True(response.IsSuccess);
        Assert.Empty(response.Value!);
        Assert.Null(handler.LastRequestUri);
    }

    [Fact]
    public async Task Unauthorized_MapsToAuthenticationFailed()
    {
        var client = Client(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await client.GetUserProfileAsync(Credentials, Cancellation);

        Assert.Equal(RetroAchievementsRequestStatus.AuthenticationFailed, response.Status);
    }

    [Fact]
    public async Task TooManyRequests_MapsToRateLimitedWithRetryAfter()
    {
        var message = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        message.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(
            TimeSpan.FromSeconds(30));
        var client = Client(message);

        var response = await client.GetGameListAsync(Credentials, 12, Cancellation);

        Assert.Equal(RetroAchievementsRequestStatus.RateLimited, response.Status);
        Assert.Equal(TimeSpan.FromSeconds(30), response.RetryAfter);
    }

    [Fact]
    public async Task ServerError_MapsToServerError()
    {
        var client = Client(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var response = await client.GetUserProfileAsync(Credentials, Cancellation);

        Assert.Equal(RetroAchievementsRequestStatus.ServerError, response.Status);
    }

    [Fact]
    public async Task AuthenticationTimeout419_MapsToAuthenticationFailed()
    {
        // RetroAchievements runs on Laravel, which answers an unauthenticated request with 419.
        var client = Client(new HttpResponseMessage((HttpStatusCode)419));

        var response = await client.GetUserProfileAsync(Credentials, Cancellation);

        Assert.Equal(RetroAchievementsRequestStatus.AuthenticationFailed, response.Status);
    }

    [Fact]
    public async Task GetUserProgress_TooManyIds_ThrowsSoTheCallerBatches()
    {
        var client = Client(Ok("{}"));
        var ids = Enumerable
            .Range(1, RetroAchievementsWebClient.MaxUserProgressBatchSize + 1)
            .ToArray();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.GetUserProgressAsync(Credentials, ids, Cancellation));
    }

    [Fact]
    public async Task TransportFailure_MapsToOffline()
    {
        var handler = new StubHandler { Throw = new HttpRequestException("no network") };
        var client = new RetroAchievementsWebClient(new HttpClient(handler));

        var response = await client.GetUserProfileAsync(Credentials, Cancellation);

        Assert.Equal(RetroAchievementsRequestStatus.Offline, response.Status);
    }

    [Fact]
    public async Task MalformedBody_MapsToMalformedResponse()
    {
        var client = Client(Ok("this is not json"));

        var response = await client.GetUserProfileAsync(Credentials, Cancellation);

        Assert.Equal(RetroAchievementsRequestStatus.MalformedResponse, response.Status);
    }

    [Fact]
    public async Task ApiKey_IsSentInQuery_ButNeverLogged()
    {
        var handler = new StubHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.InternalServerError),
        };
        var logger = new RecordingLogger();
        var client = new RetroAchievementsWebClient(new HttpClient(handler), logger);

        await client.GetUserProfileAsync(Credentials, Cancellation);

        Assert.Contains("y=SECRETKEY", handler.LastRequestUri!.Query); // sent to the API as required
        Assert.NotEmpty(logger.Messages); // the failure was logged
        Assert.DoesNotContain(logger.Messages, message => message.Contains("SECRETKEY"));
    }

    private static CancellationToken Cancellation => CancellationToken.None;

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static RetroAchievementsWebClient Client(HttpResponseMessage response) =>
        new(new HttpClient(new StubHandler { Response = response }));

    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage? Response { get; set; }
        public Exception? Throw { get; set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(Response!);
        }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Messages { get; } = [];

        public void Information(string message) => Messages.Add(message);
        public void Warning(string message, Exception? exception = null) => Messages.Add(message);
        public void Error(string message, Exception? exception = null) => Messages.Add(message);
    }
}
