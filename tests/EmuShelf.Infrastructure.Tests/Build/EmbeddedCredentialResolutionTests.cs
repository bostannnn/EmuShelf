using EmuShelf.Infrastructure.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

namespace EmuShelf.Infrastructure.Tests.Build;

public class EmbeddedCredentialResolutionTests
{
    [Fact]
    public void ScreenScraper_PrefersEnvironmentValuePerField_AndTrims()
    {
        var resolved = ScreenScraperDeveloperCredentialSource.Resolve(
            environmentId: "  env-id  ",
            environmentPassword: null,
            environmentSoftName: "   ",
            embeddedId: "embedded-id",
            embeddedPassword: "embedded-pw",
            embeddedSoftName: "embedded-soft");

        Assert.NotNull(resolved);
        // Env wins where present; the build value fills the fields env leaves blank.
        Assert.Equal("env-id", resolved!.DeveloperId);
        Assert.Equal("embedded-pw", resolved.DeveloperPassword);
        Assert.Equal("embedded-soft", resolved.SoftwareName);
    }

    [Fact]
    public void ScreenScraper_FallsBackEntirelyToTheEmbeddedBuildValues()
    {
        var resolved = ScreenScraperDeveloperCredentialSource.Resolve(
            environmentId: null,
            environmentPassword: null,
            environmentSoftName: null,
            embeddedId: "embedded-id",
            embeddedPassword: "embedded-pw",
            embeddedSoftName: "embedded-soft");

        Assert.NotNull(resolved);
        Assert.Equal("embedded-id", resolved!.DeveloperId);
    }

    [Fact]
    public void ScreenScraper_ReturnsNullWhenAFieldIsMissingFromBothSources()
    {
        var resolved = ScreenScraperDeveloperCredentialSource.Resolve(
            environmentId: "env-id",
            environmentPassword: null,
            environmentSoftName: null,
            embeddedId: null,
            embeddedPassword: null, // no password anywhere
            embeddedSoftName: "embedded-soft");

        Assert.Null(resolved);
    }

    [Fact]
    public void GoogleClient_UsesTheEmbeddedValuesAndTrims()
    {
        var resolved = GoogleOAuthClientSource.Resolve(" embedded-id ", " embedded-secret ");

        Assert.Equal("embedded-id", resolved!.ClientId);
        Assert.Equal("embedded-secret", resolved.ClientSecret);
        Assert.False(resolved.IsPublicClient);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("embedded-id", null)]
    [InlineData(null, "embedded-secret")]
    [InlineData("  ", "embedded-secret")]
    public void GoogleClient_ReturnsNullUnlessBothHalvesArePresent(string? id, string? secret)
    {
        // There is no shared fallback client for the built-in transport, so a build that embeds
        // nothing usable must report that rather than attempt a sign-in that cannot succeed.
        Assert.Null(GoogleOAuthClientSource.Resolve(id, secret));
    }

    [Fact]
    public void GoogleAndroidClient_IsPublic_UsingOnlyTheEmbeddedIdWithNoSecret()
    {
        // Android's client is bound to package name + signing cert, not a secret, and PKCE secures the
        // exchange — so only the id is required and the resulting client is public.
        var resolved = GoogleOAuthClientSource.ResolveAndroid(" android-id ");

        Assert.Equal("android-id", resolved!.ClientId);
        Assert.Null(resolved.ClientSecret);
        Assert.True(resolved.IsPublicClient);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GoogleAndroidClient_ReturnsNullWithoutAnEmbeddedId(string? id) =>
        Assert.Null(GoogleOAuthClientSource.ResolveAndroid(id));
}
