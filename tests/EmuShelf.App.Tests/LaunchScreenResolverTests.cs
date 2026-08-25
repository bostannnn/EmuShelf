using EmuShelf.Core.Launching;
using Xunit;

namespace EmuShelf.App.Tests;

public class LaunchScreenResolverTests
{
    [Theory]
    [InlineData(GameLaunchScreen.Ask)]
    [InlineData(GameLaunchScreen.BuiltIn)]
    [InlineData(GameLaunchScreen.External)]
    public void NoExternalDisplay_AlwaysBuiltIn(GameLaunchScreen preference)
    {
        // With no second screen attached the answer is always built-in, even for a system pinned to
        // External — unplugging the screen must degrade gracefully, never fail the launch.
        Assert.Equal(
            LaunchScreenDecision.BuiltIn,
            LaunchScreenResolver.Resolve(preference, externalDisplayAvailable: false));
    }

    [Fact]
    public void ExternalAvailable_AskPrompts()
    {
        Assert.Equal(
            LaunchScreenDecision.Prompt,
            LaunchScreenResolver.Resolve(GameLaunchScreen.Ask, externalDisplayAvailable: true));
    }

    [Fact]
    public void ExternalAvailable_BuiltInPreferenceObeyed()
    {
        Assert.Equal(
            LaunchScreenDecision.BuiltIn,
            LaunchScreenResolver.Resolve(GameLaunchScreen.BuiltIn, externalDisplayAvailable: true));
    }

    [Fact]
    public void ExternalAvailable_ExternalPreferenceObeyed()
    {
        Assert.Equal(
            LaunchScreenDecision.External,
            LaunchScreenResolver.Resolve(GameLaunchScreen.External, externalDisplayAvailable: true));
    }

    [Theory]
    [InlineData(GameLaunchScreen.Ask)]
    [InlineData(GameLaunchScreen.BuiltIn)]
    [InlineData(GameLaunchScreen.External)]
    public void DualScreenSystem_AlwaysBuiltIn_NeverPrompts(GameLaunchScreen preference)
    {
        // A dual-screen console (DS/3DS) draws both console screens in one emulator app, so "which
        // physical screen?" has no answer: it always launches built-in and never prompts, even with a
        // second screen attached and whatever preference happens to be stored.
        Assert.Equal(
            LaunchScreenDecision.BuiltIn,
            LaunchScreenResolver.Resolve(preference, externalDisplayAvailable: true, isDualScreenSystem: true));
    }
}
