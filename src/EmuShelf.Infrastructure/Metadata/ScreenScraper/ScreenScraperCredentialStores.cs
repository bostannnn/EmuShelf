using System.Runtime.Versioning;
using System.Text.Json;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Metadata.ScreenScraper;

public static class ScreenScraperCredentialStoreFactory
{
    public const string BlobFileName = "screenscraper.account";

    public static IScreenScraperCredentialStore Create(
        IAppPaths paths,
        IAppLogger? logger = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsScreenScraperCredentialStore(
                Path.Combine(paths.SettingsDirectory, BlobFileName),
                logger);
        }

        return new SessionOnlyScreenScraperCredentialStore();
    }
}

public sealed class SessionOnlyScreenScraperCredentialStore : IScreenScraperCredentialStore
{
    private ScreenScraperUserCredentials? _credentials;

    public ScreenScraperUserCredentials? GetCredentials() => _credentials;

    public void SaveCredentials(ScreenScraperUserCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        _credentials = credentials;
    }

    public void ClearCredentials() => _credentials = null;
}

[SupportedOSPlatform("windows")]
public sealed class WindowsScreenScraperCredentialStore : IScreenScraperCredentialStore
{
    private readonly WindowsDpapiProtectedTextStore _store;
    private readonly IAppLogger _logger;

    public WindowsScreenScraperCredentialStore(string blobPath, IAppLogger? logger = null)
    {
        _store = new WindowsDpapiProtectedTextStore(blobPath, "ScreenScraper", logger);
        _logger = logger ?? NullAppLogger.Instance;
    }

    public ScreenScraperUserCredentials? GetCredentials()
    {
        var serialized = _store.Read();
        if (serialized is null)
            return null;
        try
        {
            return JsonSerializer.Deserialize<ScreenScraperUserCredentials>(serialized);
        }
        catch (JsonException ex)
        {
            _logger.Warning("Could not decode the ScreenScraper credential blob.", ex);
            return null;
        }
    }

    public void SaveCredentials(ScreenScraperUserCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (string.IsNullOrWhiteSpace(credentials.Username) || string.IsNullOrWhiteSpace(credentials.Password))
            throw new ArgumentException("ScreenScraper account credentials are incomplete.", nameof(credentials));
        _store.Write(JsonSerializer.Serialize(credentials));
    }

    public void ClearCredentials() => _store.Clear();
}

public static class ScreenScraperDeveloperCredentialSource
{
    public const string DeveloperIdVariable = "SCREENSCRAPER_DEV_ID";
    public const string DeveloperPasswordVariable = "SCREENSCRAPER_DEV_PASSWORD";
    public const string SoftwareNameVariable = "SCREENSCRAPER_SOFTNAME";

    public static bool TryLoadFromEnvironment(out ScreenScraperDeveloperCredentials? credentials)
    {
        var developerId = Environment.GetEnvironmentVariable(DeveloperIdVariable);
        var developerPassword = Environment.GetEnvironmentVariable(DeveloperPasswordVariable);
        var softwareName = Environment.GetEnvironmentVariable(SoftwareNameVariable);
        if (string.IsNullOrWhiteSpace(developerId) ||
            string.IsNullOrWhiteSpace(developerPassword) ||
            string.IsNullOrWhiteSpace(softwareName))
        {
            credentials = null;
            return false;
        }

        credentials = new ScreenScraperDeveloperCredentials(
            developerId.Trim(),
            developerPassword.Trim(),
            softwareName.Trim());
        return true;
    }
}
