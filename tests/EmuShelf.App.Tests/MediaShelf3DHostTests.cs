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
    public void InitializationWatchdog_TimeoutRequestsFallback()
    {
        var host = new MediaShelf3DHost();
        Exception? failure = null;
        host.InitializationFailed += (_, exception) => failure = exception;
        host.IsActive = true;

        // The deadline is one long window, not a retry-by-teardown budget: a single timeout is the
        // verdict. (Rebuilding the scene would only restart the same cold GL start and could never
        // rescue a slow-but-capable driver — see MediaShelf3DHost.InitializationTimeout.)
        host.ExpireInitializationForTests();

        Assert.IsType<TimeoutException>(failure);
        Assert.False(host.HasAttachedScene);
    }
}
