using Avalonia.Headless.XUnit;
using EmuShelf.App.Controls;

namespace EmuShelf.App.Tests;

public sealed class MediaShelf3DHostTests
{
    [AvaloniaFact]
    public void Scene_IsAttachedOnlyWhileTheShelfIsActiveAndSupported()
    {
        var host = new MediaShelf3DHost();

        Assert.False(host.HasAttachedScene);

        host.IsActive = true;
        Assert.True(host.HasAttachedScene);

        host.IsSceneSupported = false;
        Assert.False(host.HasAttachedScene);

        host.IsSceneSupported = true;
        Assert.True(host.HasAttachedScene);

        host.IsActive = false;
        Assert.False(host.HasAttachedScene);
    }

    [AvaloniaFact]
    public void InitializationWatchdog_RetriesBeforeRequestingFallback()
    {
        var host = new MediaShelf3DHost();
        Exception? failure = null;
        host.InitializationFailed += (_, exception) => failure = exception;
        host.IsActive = true;

        // First timeout is a retry, not a verdict: a fresh scene is stood up and no fallback fires.
        host.ExpireInitializationForTests();
        Assert.Null(failure);
        Assert.True(host.HasAttachedScene);

        // A timeout that repeats across the retry budget is taken as a real failure.
        host.ExpireInitializationForTests();
        Assert.IsType<TimeoutException>(failure);
        Assert.False(host.HasAttachedScene);
    }

    [AvaloniaFact]
    public void InitializationWatchdog_SuccessAfterATimeoutCancelsTheFallback()
    {
        var host = new MediaShelf3DHost();
        Exception? failure = null;
        host.InitializationFailed += (_, exception) => failure = exception;
        host.IsActive = true;

        // A slow first start times out once, then the rebuilt scene comes up cleanly. The retry
        // budget resets, so a later timeout does not immediately give up.
        host.ExpireInitializationForTests();
        host.SignalInitializationSucceededForTests();
        host.ExpireInitializationForTests();

        Assert.Null(failure);
        Assert.True(host.HasAttachedScene);
    }
}
