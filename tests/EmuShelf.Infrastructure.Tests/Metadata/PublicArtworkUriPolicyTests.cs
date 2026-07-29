using System.Net;
using EmuShelf.Infrastructure.Metadata;

namespace EmuShelf.Infrastructure.Tests.Metadata;

public class PublicArtworkUriPolicyTests
{
    [Theory]
    [InlineData("http://93.184.216.34/cover.png")]
    [InlineData("https://127.0.0.1/cover.png")]
    [InlineData("https://10.0.0.4/cover.png")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/cover.png")]
    [InlineData("https://[fc00::1]/cover.png")]
    public async Task IsAllowedAsync_RejectsNonHttpsAndNonPublicTargets(string address)
    {
        var policy = new PublicArtworkUriPolicy();

        Assert.False(await policy.IsAllowedAsync(new Uri(address)));
    }

    [Fact]
    public async Task IsAllowedAsync_RequiresEveryDnsAddressToBePublic()
    {
        var policy = new PublicArtworkUriPolicy((_, _) => Task.FromResult(
            new[] { IPAddress.Parse("93.184.216.34"), IPAddress.Loopback }));

        Assert.False(await policy.IsAllowedAsync(new Uri("https://covers.example/game.png")));
    }

    [Fact]
    public async Task IsAllowedAsync_AcceptsHttpsHostResolvingOnlyToPublicAddresses()
    {
        var policy = new PublicArtworkUriPolicy((_, _) => Task.FromResult(
            new[] { IPAddress.Parse("93.184.216.34"), IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946") }));

        Assert.True(await policy.IsAllowedAsync(new Uri("https://covers.example/game.png")));
    }
}
