using Avalonia;
using Avalonia.Headless;
using EmuShelf.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace EmuShelf.App.Tests;

/// <summary>Minimal Avalonia app so the headless test runner has a UI thread + dispatcher.</summary>
public class TestApp : Application
{
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
