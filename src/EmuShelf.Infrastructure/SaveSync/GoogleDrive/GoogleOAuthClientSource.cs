using EmuShelf.Infrastructure.Build;

namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>
/// Resolves the Google OAuth client this build ships, for the managed Drive transport.
/// </summary>
/// <remarks>
/// The same embedded values the rclone path already uses (see
/// <see cref="EmuShelf.Infrastructure.SaveSync.RcloneConfigurator.ResolveGoogleClient"/> and
/// DECISIONS 2026-08-04): the client identifies EmuShelf-the-app, every user still signs into their
/// own Drive, and no per-user token is ever embedded. Unlike the rclone path there is no shared
/// fallback client to degrade to — EmuShelf is the only thing talking to Google here — so a build
/// with no embedded client cannot offer this transport at all, and says so rather than failing at
/// sign-in.
/// </remarks>
public static class GoogleOAuthClientSource
{
    /// <summary>
    /// The credentials for this platform, or <see langword="null"/> when the build embeds none.
    /// </summary>
    public static GoogleOAuthClientCredentials? Resolve() =>
        Resolve(EmbeddedSecrets.GoogleOAuthClientId, EmbeddedSecrets.GoogleOAuthClientSecret);

    /// <summary>Whether this build can offer the managed transport at all.</summary>
    public static bool IsConfigured => Resolve() is not null;

    /// <summary>
    /// Both halves are required for the desktop client: an id without its secret authenticates as
    /// nothing, which is the same all-or-nothing rule the rclone path applies.
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
}
