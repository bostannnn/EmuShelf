using EmuShelf.Infrastructure.Metadata.ScreenScraper;
using EmuShelf.Infrastructure.SaveSync;

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
    public void GoogleClient_UsesTheEmbeddedClientAndTrims()
    {
        var resolved = RcloneConfigurator.ResolveGoogleClient(
            embeddedClientId: " embedded-id ",
            embeddedClientSecret: " embedded-secret ");

        Assert.Equal(("embedded-id", "embedded-secret"), resolved);
    }

    [Fact]
    public void GoogleClient_ReturnsNullWhenTheBuildEmbedsNoClient()
    {
        // An unconfigured local build embeds nothing; rclone's shared client is the only fallback,
        // signalled by null.
        var resolved = RcloneConfigurator.ResolveGoogleClient(
            embeddedClientId: null,
            embeddedClientSecret: null);

        Assert.Null(resolved);
    }

    [Fact]
    public void GoogleClient_ReturnsNullWhenOnlyOneHalfIsEmbedded()
    {
        // A half-configured build (id but no secret) must not be used; an id without its secret
        // authenticates as nothing.
        var resolved = RcloneConfigurator.ResolveGoogleClient(
            embeddedClientId: "embedded-id",
            embeddedClientSecret: null);

        Assert.Null(resolved);
    }
}
