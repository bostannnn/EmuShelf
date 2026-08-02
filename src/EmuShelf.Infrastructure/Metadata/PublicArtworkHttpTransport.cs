using System.Net.Sockets;

namespace EmuShelf.Infrastructure.Metadata;

/// <summary>
/// Creates a direct web-artwork transport that connects only to addresses approved by the
/// policy. Pinning the validated address to the socket closes the DNS-rebinding gap between a
/// pre-request policy check and HttpClient's own connection lookup.
/// </summary>
public static class PublicArtworkHttpTransport
{
    public static SocketsHttpHandler CreateHandler(PublicArtworkUriPolicy uriPolicy)
    {
        ArgumentNullException.ThrowIfNull(uriPolicy);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            MaxConnectionsPerServer = 8,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await uriPolicy.ResolvePublicAddressesAsync(
                    context.DnsEndPoint.Host,
                    cancellationToken);
                if (addresses is null)
                    throw new HttpRequestException("The artwork host is not publicly routable.");

                Exception? lastFailure = null;
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                    {
                        NoDelay = true,
                    };
                    try
                    {
                        await socket.ConnectAsync(
                            address,
                            context.DnsEndPoint.Port,
                            cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                    {
                        socket.Dispose();
                        if (ex is OperationCanceledException)
                            throw;
                        lastFailure = ex;
                    }
                }

                throw new HttpRequestException(
                    "Could not connect to the public artwork host.",
                    lastFailure);
            },
        };
    }
}
