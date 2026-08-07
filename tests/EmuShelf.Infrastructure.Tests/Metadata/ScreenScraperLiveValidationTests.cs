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
    // Points at a clean No-Intro .3ds/.cci dump so the 3DS whole-file hash route can be validated
    // without a fabricated hash. Absent = that one check is a silent no-op.
    public const string Rom3dsVariable = "SCREENSCRAPER_TEST_3DS_ROM";

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

    [Fact]
    public async Task Serial_Lookup_MatchesNewlyRoutedDiscSystems()
    {
        if (!TryGetLiveContext(out var developer, out var account))
            return;

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var client = new ScreenScraperClient(httpClient, developer!);

        // Systems wired to the serial route after the disc-serial fix. Serials are in the exact shape
        // EmuShelf extracts (GameCube/Wii: the 6-char disc game code; PS3: the hyphenated title id;
        // Dreamcast: the IP.BIN product number, whose Redump spelling varies on the MK- prefix, so
        // both forms are tried). Several games per system so one stale code can't fail a whole system.
        (int System, string Serial, string Hint)[] cases =
        [
            (13, "GALE01", "Super Smash Bros. Melee (GameCube)"),
            (13, "GZLE01", "Zelda: The Wind Waker (GameCube)"),
            (16, "RMCE01", "Mario Kart Wii (Wii)"),
            (16, "RSBE01", "Super Smash Bros. Brawl (Wii)"),
            (59, "BLUS-30443", "Demon's Souls (PS3, US)"),
            (59, "BLES-00932", "Demon's Souls (PS3, EU)"),
            (23, "MK-51000", "Sonic Adventure (Dreamcast, MK- prefix)"),
            (23, "51000", "Sonic Adventure (Dreamcast, no prefix)"),
        ];

        var matchedSystems = new HashSet<int>();
        foreach (var (system, serial, hint) in cases)
        {
            var result = await client.GetGameInfoAsync(
                account!,
                new ScreenScraperGameRequest(system, $"{serial}.rvz", 0, Serial: serial));
            var name = result.Data?.Names.FirstOrDefault()?.Value;
            output.WriteLine($"  [{system,3}] {serial,-12} [{hint}] -> {result.Status} {name}");
            if (result.IsSuccess && !string.IsNullOrWhiteSpace(name))
                matchedSystems.Add(system);
        }

        var unmatched = cases.Select(entry => entry.System).Distinct()
            .Where(system => !matchedSystems.Contains(system))
            .ToArray();
        Assert.True(
            unmatched.Length == 0,
            "ScreenScraper returned no serial match for system id(s): " +
            $"{string.Join(", ", unmatched)}. That route may need a different serial format — see the " +
            "logged results above for what each candidate returned.");
    }

    [Fact]
    public async Task Hash_Lookup_MatchesNintendo3dsCartridgeDump()
    {
        if (!TryGetLiveContext(out var developer, out var account))
            return;

        var romPath = Environment.GetEnvironmentVariable(Rom3dsVariable);
        if (string.IsNullOrWhiteSpace(romPath) || !File.Exists(romPath))
        {
            output.WriteLine(
                $"skipped: set {Rom3dsVariable} to a clean No-Intro .3ds/.cci dump to validate 3DS hashing.");
            return;
        }

        // Compute the same whole-file MD5/SHA-1 + size the fingerprint service would, streaming so a
        // multi-gigabyte dump is never held in memory. If clean-dump hashing is the right route, the
        // No-Intro-aligned hash resolves the game.
        using var md5 = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.MD5);
        using var sha1 = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA1);
        long size = 0;
        var buffer = new byte[1024 * 1024];
        await using (var stream = File.OpenRead(romPath))
        {
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                md5.AppendData(buffer, 0, read);
                sha1.AppendData(buffer, 0, read);
                size += read;
            }
        }

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        var client = new ScreenScraperClient(httpClient, developer!);
        var result = await client.GetGameInfoAsync(
            account!,
            new ScreenScraperGameRequest(
                17,
                Path.GetFileName(romPath),
                size,
                Md5: Convert.ToHexString(md5.GetHashAndReset()),
                Sha1: Convert.ToHexString(sha1.GetHashAndReset())));

        var name = result.Data?.Names.FirstOrDefault()?.Value;
        output.WriteLine($"  3DS {Path.GetFileName(romPath)} ({size} bytes) -> {result.Status} {name}");
        Assert.True(
            result.IsSuccess && !string.IsNullOrWhiteSpace(name),
            "ScreenScraper returned no whole-file hash match for the supplied 3DS dump. If it is a " +
            "clean No-Intro .3ds/.cci, the 3DS hash route may need a different rule; a trimmed or " +
            "decrypted dump legitimately misses and falls back to title search.");
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
