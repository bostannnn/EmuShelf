using EmuShelf.Core.Diagnostics;

namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>
/// Selects the loopback OAuth redirect handler for the current platform: the tested
/// <see cref="HttpListener"/>-based one on desktop, and the sockets-based
/// <see cref="TcpLoopbackOAuthRedirectHandler"/> on Android, where <c>HttpListener</c> is unsupported.
/// Both use the same <c>http://127.0.0.1:port/</c> loopback redirect, so the same OAuth client serves
/// every platform. See docs/android-save-sync-model.md.
/// </summary>
public static class OAuthRedirectHandlerFactory
{
    public static IOAuthRedirectHandler Create(IAppLogger? logger = null) =>
        OperatingSystem.IsAndroid()
            ? new TcpLoopbackOAuthRedirectHandler(logger)
            : new LoopbackOAuthRedirectHandler(logger);
}
