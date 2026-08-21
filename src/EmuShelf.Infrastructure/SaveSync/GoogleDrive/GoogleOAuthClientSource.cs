using EmuShelf.Infrastructure.Build;

namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>
/// Resolves the Google OAuth client this build ships, for the managed Drive transport.
/// </summary>
/// <remarks>
/// The embedded values (see DECISIONS 2026-08-04) identify EmuShelf-the-app: every user still signs
/// into their own Drive, and no per-user token is ever embedded. There is no shared fallback client to
/// degrade to — EmuShelf is the only thing talking to Google here — so a build with no embedded client
/// cannot offer this transport at all, and says so rather than failing at sign-in.
/// </remarks>
public static class GoogleOAuthClientSource
{
    /// <summary>
    /// The credentials for this platform, or <see langword="null"/> when the build embeds none.
    /// </summary>
    public static GoogleOAuthClientCredentials? Resolve()
    {
        var desktop = Resolve(EmbeddedSecrets.GoogleOAuthClientId, EmbeddedSecrets.GoogleOAuthClientSecret);
        // Android reuses the desktop client over the same loopback redirect (HttpListener is swapped for
        // a TcpListener there — see OAuthRedirectHandlerFactory), so no separate client is required. A
        // build that embeds a dedicated public Android client id is still honoured, and takes precedence.
        return OperatingSystem.IsAndroid()
            ? ResolveAndroid(EmbeddedSecrets.GoogleOAuthAndroidClientId) ?? desktop
            : desktop;
    }

    /// <summary>Whether this build can offer the managed transport at all.</summary>
    public static bool IsConfigured => Resolve() is not null;

    /// <summary>
    /// Both halves are required for the desktop client: an id without its secret authenticates as
    /// nothing, so the resolver is all-or-nothing.
    /// </summary>
    /// <remarks>
    /// Android will need a second, secret-less client of its own — Google issues one client per
    /// platform type, and an Android client is bound to the package name and signing certificate
    /// instead of a secret. <see cref="GoogleOAuthClientCredentials"/> already models that case, so
    /// adding it is a new embedded field and one more branch here.
    /// </remarks>
    internal static GoogleOAuthClientCredentials? Resolve(string? embeddedClientId, string? embeddedClientSecret)
    {
        if (string.IsNullOrWhiteSpace(embeddedClientId) || string.IsNullOrWhiteSpace(embeddedClientSecret))
            return null;

        return new GoogleOAuthClientCredentials(embeddedClientId.Trim(), embeddedClientSecret.Trim());
    }

    /// <summary>
    /// The Android client is public (no secret): Google binds it to the package name and signing
    /// certificate instead, and PKCE secures the code exchange. So only the id is required, and it is
    /// carried in its own embedded field rather than the desktop id/secret pair.
    /// </summary>
    internal static GoogleOAuthClientCredentials? ResolveAndroid(string? embeddedAndroidClientId) =>
        string.IsNullOrWhiteSpace(embeddedAndroidClientId)
            ? null
            : new GoogleOAuthClientCredentials(embeddedAndroidClientId.Trim(), ClientSecret: null);
}
