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
    public void InitializationWatchdog_RemovesSilentFrameworkFailureAndRequestsFallback()
    {
        var host = new MediaShelf3DHost();
        Exception? failure = null;
        host.InitializationFailed += (_, exception) => failure = exception;
        host.IsActive = true;

        host.ExpireInitializationForTests();

        Assert.IsType<TimeoutException>(failure);
        Assert.False(host.HasAttachedScene);
    }
}
