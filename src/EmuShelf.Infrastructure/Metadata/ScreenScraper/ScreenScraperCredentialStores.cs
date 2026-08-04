using System.Text.Json;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Metadata.ScreenScraper;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Build;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.Metadata.ScreenScraper;

/// <summary>
/// Chooses the platform-appropriate credential store, both writing the same portable
/// <c>Settings/screenscraper.account</c> blob so the login survives restarts and updates: a
/// DPAPI-protected blob on Windows (the v1 ship target), and an AES-GCM obfuscated blob elsewhere
/// (Linux/Steam Deck, macOS) where no OS keychain is wired in. Both go through
/// <see cref="TextBackedScreenScraperCredentialStore"/>; only the at-rest protection differs.
/// </summary>
public static class ScreenScraperCredentialStoreFactory
{
    public const string BlobFileName = "screenscraper.account";

    public static IScreenScraperCredentialStore Create(
        IAppPaths paths,
        IAppLogger? logger = null)
    {
        var blobPath = Path.Combine(paths.SettingsDirectory, BlobFileName);
        IProtectedTextStore textStore = OperatingSystem.IsWindows()
            ? new WindowsDpapiProtectedTextStore(blobPath, "ScreenScraper", logger)
            : new PortableObfuscatedTextStore(blobPath, "ScreenScraper", logger);
        return new TextBackedScreenScraperCredentialStore(textStore, logger);
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

/// <summary>
/// Serializes the account login to JSON and hands it to a platform <see cref="IProtectedTextStore"/>,
/// so the username and password are never written as readable plaintext. The username is part of the
/// secret (it is an authentication field), so the whole record lives in the protected blob and nothing
/// account-identifying reaches settings.json.
/// </summary>
public sealed class TextBackedScreenScraperCredentialStore : IScreenScraperCredentialStore
{
    private readonly IProtectedTextStore _store;
    private readonly IAppLogger _logger;

    internal TextBackedScreenScraperCredentialStore(IProtectedTextStore store, IAppLogger? logger = null)
    {
        _store = store;
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
    public const string DeveloperDebugPasswordVariable = "SCREENSCRAPER_DEV_DEBUG_PASSWORD";

    /// <summary>
    /// Reads the developer-only ScreenScraper debug password from the environment. Returns
    /// <see langword="null"/> when it is absent, so debug mode is opt-in and never active by default.
    /// </summary>
    public static string? GetDebugPasswordFromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(DeveloperDebugPasswordVariable);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Resolves the developer credentials for the live client. Each field prefers its environment
    /// variable (so a developer machine keeps overriding freely) and falls back to the value baked
    /// into the build. Returns <see langword="false"/> only when a field is missing from both.
    /// </summary>
    public static bool TryLoad(out ScreenScraperDeveloperCredentials? credentials)
    {
        credentials = Resolve(
            Environment.GetEnvironmentVariable(DeveloperIdVariable),
            Environment.GetEnvironmentVariable(DeveloperPasswordVariable),
            Environment.GetEnvironmentVariable(SoftwareNameVariable),
            EmbeddedSecrets.ScreenScraperDevId,
            EmbeddedSecrets.ScreenScraperDevPassword,
            EmbeddedSecrets.ScreenScraperSoftName);
        return credentials is not null;
    }

    internal static ScreenScraperDeveloperCredentials? Resolve(
        string? environmentId,
        string? environmentPassword,
        string? environmentSoftName,
        string? embeddedId,
        string? embeddedPassword,
        string? embeddedSoftName)
    {
        var developerId = FirstNonBlank(environmentId, embeddedId);
        var developerPassword = FirstNonBlank(environmentPassword, embeddedPassword);
        var softwareName = FirstNonBlank(environmentSoftName, embeddedSoftName);
        if (developerId is null || developerPassword is null || softwareName is null)
            return null;

        return new ScreenScraperDeveloperCredentials(developerId, developerPassword, softwareName);
    }

    private static string? FirstNonBlank(string? preferred, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
            return preferred.Trim();
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback.Trim();
        return null;
    }
}
