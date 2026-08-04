using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.Metadata.ScreenScraper;
using EmuShelf.Integrations.Metadata;
using Xunit.Abstractions;

namespace EmuShelf.Infrastructure.Tests.Metadata;

/// <summary>
/// Opt-in live checks against the real ScreenScraper API. These make real network requests and
/// consume the connected account's quota, so they only run when <c>EMUSHELF_TEST_SCREENSCRAPER</c>
/// is set AND both developer and account credentials are present in the environment. A normal
/// <c>dotnet test</c> run skips them as silent no-ops.
/// </summary>
public class ScreenScraperLiveValidationTests(ITestOutputHelper output)
{
    public const string OptInVariable = "EMUSHELF_TEST_SCREENSCRAPER";
    public const string AccountUsernameVariable = "SCREENSCRAPER_SSID";
    public const string AccountPasswordVariable = "SCREENSCRAPER_SSPASSWORD";

    [Fact]
    public async Task Account_Connects_AndSystemMapMatchesLiveCatalogue()
    {
        if (!TryGetLiveContext(out var developer, out var account))
            return;

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var client = new ScreenScraperClient(httpClient, developer!);

        var accountResult = await client.GetAccountInfoAsync(account!);
        output.WriteLine(
            $"account: status={accountResult.Status} level={accountResult.Data?.Tier} " +
            $"maxthreads={accountResult.Quota?.MaxThreads} " +
            $"today={accountResult.Quota?.RequestsToday}/{accountResult.Quota?.MaxRequestsPerDay} " +
            $"ko={accountResult.Quota?.FailedRequestsToday}/{accountResult.Quota?.MaxFailedRequestsPerDay}");
        Assert.Equal(ScreenScraperRequestStatus.Success, accountResult.Status);

        var systemsResult = await client.GetSystemsAsync(account!);
        Assert.Equal(ScreenScraperRequestStatus.Success, systemsResult.Status);
        var byId = systemsResult.Data!.ToDictionary(system => system.Id);
        output.WriteLine($"live catalogue: {systemsResult.Data!.Count} systems");

        var missing = new List<string>();
        foreach (var (emuShelfId, screenScraperId) in ScreenScraperSystemMap.Entries.OrderBy(entry => entry.Key))
        {
            if (byId.TryGetValue(screenScraperId, out var system))
            {
                output.WriteLine($"  {emuShelfId,-14} -> {screenScraperId,3}  OK  {system.Name}");
            }
            else
            {
                output.WriteLine($"  {emuShelfId,-14} -> {screenScraperId,3}  MISSING");
                missing.Add($"{emuShelfId}={screenScraperId}");
            }
        }

        Assert.True(
            missing.Count == 0,
            $"ScreenScraper system ids not found in the live catalogue: {string.Join(", ", missing)}");
    }

    [Fact]
    public async Task Serial_Lookup_ReturnsAMatchingGame()
    {
        if (!TryGetLiveContext(out var developer, out var account))
            return;

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var client = new ScreenScraperClient(httpClient, developer!);

        // (ScreenScraper systemId, disc serial as EmuShelf extracts it, expected name hint)
        (int System, string Serial, string Hint)[] cases =
        [
            (57, "SLUS-00594", "Metal Gear Solid (PS1)"),
            (57, "SCUS-94163", "Final Fantasy VII (PS1)"),
            (58, "SCUS-97472", "Shadow of the Colossus (PS2)"),
            (58, "SLUS-21274", "God of War II (PS2)"),
        ];

        var matched = 0;
        foreach (var (system, serial, hint) in cases)
        {
            var result = await client.GetGameInfoAsync(
                account!,
                new ScreenScraperGameRequest(system, $"{serial}.chd", 0, Serial: serial));
            var name = result.Data?.Names.FirstOrDefault()?.Value;
            output.WriteLine($"  {serial,-12} [{hint}] -> {result.Status} {name}");
            if (result.IsSuccess && !string.IsNullOrWhiteSpace(name))
                matched++;
        }

        Assert.True(matched > 0, "No serial lookup matched a game; serial-based matching may need a different format.");
    }

    private bool TryGetLiveContext(
        out ScreenScraperDeveloperCredentials? developer,
        out ScreenScraperUserCredentials? account)
    {
        developer = null;
        account = null;

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OptInVariable)))
        {
            output.WriteLine($"skipped: set {OptInVariable}=1 to run live ScreenScraper checks.");
            return false;
        }

        if (!ScreenScraperDeveloperCredentialSource.TryLoad(out developer))
        {
            output.WriteLine("skipped: developer credentials are not present.");
            return false;
        }

        var username = Environment.GetEnvironmentVariable(AccountUsernameVariable);
        var password = Environment.GetEnvironmentVariable(AccountPasswordVariable);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            output.WriteLine(
                $"skipped: set {AccountUsernameVariable} and {AccountPasswordVariable} to run live checks.");
            return false;
        }

        account = new ScreenScraperUserCredentials(username, password);
        return true;
    }
}
