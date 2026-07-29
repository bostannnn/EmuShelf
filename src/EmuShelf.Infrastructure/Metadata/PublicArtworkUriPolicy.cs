using System.Net;
using System.Net.Sockets;
using EmuShelf.Core.Metadata;

namespace EmuShelf.Infrastructure.Metadata;

/// <summary>Allows HTTPS artwork only when every resolved address is publicly routable.</summary>
public sealed class PublicArtworkUriPolicy : IRemoteArtworkUriPolicy
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolveHost;

    public PublicArtworkUriPolicy()
        : this((host, cancellationToken) => Dns.GetHostAddressesAsync(host, cancellationToken))
    {
    }

    internal PublicArtworkUriPolicy(
        Func<string, CancellationToken, Task<IPAddress[]>> resolveHost)
    {
        _resolveHost = resolveHost;
    }

    public async Task<bool> IsAllowedAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        if (!uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            string.IsNullOrWhiteSpace(uri.DnsSafeHost) ||
            uri.HostNameType is UriHostNameType.Unknown or UriHostNameType.Basic)
        {
            return false;
        }

        return await ResolvePublicAddressesAsync(uri.DnsSafeHost, cancellationToken) is not null;
    }

    internal async Task<IPAddress[]?> ResolvePublicAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literalAddress))
        {
            addresses = [literalAddress];
        }
        else
        {
            try
            {
                addresses = await _resolveHost(host, cancellationToken);
            }
            catch (Exception ex) when (ex is SocketException or ArgumentException)
            {
                return null;
            }
        }

        return addresses.Length > 0 && addresses.All(IsPublicAddress) ? addresses : null;
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                0 or 10 or 127 => false,
                100 when bytes[1] is >= 64 and <= 127 => false,
                169 when bytes[1] == 254 => false,
                172 when bytes[1] is >= 16 and <= 31 => false,
                192 when bytes[1] == 0 && bytes[2] is 0 or 2 => false,
                192 when bytes[1] == 88 && bytes[2] == 99 => false,
                192 when bytes[1] == 168 => false,
                198 when bytes[1] is 18 or 19 => false,
                198 when bytes[1] == 51 && bytes[2] == 100 => false,
                203 when bytes[1] == 0 && bytes[2] == 113 => false,
                >= 224 => false,
                _ => true,
            };
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
        {
            return false;
        }

        // fc00::/7 is unique-local space. fe80::/10 and ff00::/8 are covered by the
        // framework properties above.
        var isUniqueLocal = (bytes[0] & 0xFE) == 0xFC;
        var isDocumentation = bytes[0] == 0x20 && bytes[1] == 0x01 &&
            bytes[2] == 0x0D && bytes[3] == 0xB8;
        return !isUniqueLocal && !isDocumentation;
    }
}
