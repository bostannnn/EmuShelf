using System.Text;

namespace EmuShelf.Infrastructure.Build;

/// <summary>
/// Application-identity credentials baked into the build so a shipped install works without the user
/// (or a launching Steam Deck session) having to export anything. The values are injected at build
/// time from environment variables by <c>Build/EmbeddedSecrets.targets</c> into a generated partial
/// class under <c>obj/</c>; they are therefore never committed to the repository.
/// </summary>
/// <remarks>
/// These are deliberately not treated as confidential: the ScreenScraper developer id identifies
/// EmuShelf-the-app (not a user), and a Google desktop OAuth client secret is, per Google, not a
/// secret. Both are meant to be shared across every install. The light XOR+Base64 encoding below is
/// only to keep the raw strings out of a naive <c>strings</c>/scraper sweep of a public binary, and
/// to guarantee the generated literals are always valid regardless of the source bytes — it is not a
/// security boundary. When an environment variable is absent at build time the corresponding
/// constant is empty and the accessor returns <see langword="null"/>: ScreenScraper then falls back to
/// developer env vars, and the built-in Google Drive transport is simply unavailable in that build.
/// </remarks>
internal static partial class EmbeddedSecrets
{
    // Must match the key used by the encoder in Build/EmbeddedSecrets.targets.
    private static readonly byte[] Key = "EmuShelf.embedded.v1"u8.ToArray();

    public static string? ScreenScraperDevId => Decode(ScreenScraperDevIdEncoded);

    public static string? ScreenScraperDevPassword => Decode(ScreenScraperDevPasswordEncoded);

    public static string? ScreenScraperSoftName => Decode(ScreenScraperSoftNameEncoded);

    public static string? GoogleOAuthClientId => Decode(GoogleOAuthClientIdEncoded);

    public static string? GoogleOAuthClientSecret => Decode(GoogleOAuthClientSecretEncoded);

    internal static string? Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return null;

        var bytes = Convert.FromBase64String(encoded);
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] ^= Key[i % Key.Length];

        var value = Encoding.UTF8.GetString(bytes);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
