namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>
/// Supplies the bearer token for Drive calls. Separated from the API client so the client never
/// knows how a token was obtained or stored, and so tests can drive it without an OAuth flow.
/// </summary>
public interface IGoogleAccessTokenSource
{
    /// <summary>
    /// A currently valid access token.
    /// </summary>
    /// <param name="forceRefresh">
    /// Ignore any cached token and mint a new one. The API client sets this once after a 401: an
    /// access token can expire mid-sync, and re-minting is the difference between a sync that
    /// pauses briefly and one that fails and tells the user to reconnect.
    /// </param>
    Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// The stored authorization is gone — revoked in the Google account, expired past refresh, or never
/// completed. The only recovery is for the user to connect again, so this is distinct from a
/// transport failure the next sync might survive.
/// </summary>
/// <remarks>
/// Derives from <see cref="IOException"/> deliberately. Every caller that handles a cloud failure —
/// the sync pipeline, the launch path, the settings operations — already catches
/// <see cref="IOException"/> as "the remote could not be reached", and this is a species of exactly
/// that. As a bare <see cref="Exception"/> it escaped all of them, so a token revoked mid-sync would
/// surface as an unhandled failure while starting a game instead of a reported "reconnect". Handlers
/// that want to tell the two apart still can, by catching this type first.
/// </remarks>
public sealed class GoogleAuthorizationRequiredException : IOException
{
    public GoogleAuthorizationRequiredException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
