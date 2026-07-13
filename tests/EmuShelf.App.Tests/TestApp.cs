using Avalonia;
using Avalonia.Media;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using EmuShelf.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace EmuShelf.App.Tests;

/// <summary>Minimal Avalonia app so the headless test runner has a UI thread + dispatcher.</summary>
public class TestApp : Application
{
    public override void Initialize()
    {
        Resources.MergedDictionaries.Add(new ResourceInclude(
            new Uri("avares://EmuShelf/"))
        {
            Source = new Uri("avares://EmuShelf/Styles/EmuShelfTheme.axaml"),
        });
        var fluent = new FluentTheme();
        fluent.Palettes.Add(
            ThemeVariant.Light,
            new ColorPaletteResources { Accent = Color.Parse("#D43F4A") });
        fluent.Palettes.Add(
            ThemeVariant.Dark,
            new ColorPaletteResources { Accent = Color.Parse("#EF4855") });
        Styles.Add(fluent);
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });
}
