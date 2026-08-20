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
            new Uri("avares://EmuShelf.UI/"))
        {
            Source = new Uri("avares://EmuShelf.UI/Styles/EmuShelfTheme.axaml"),
        });
        var fluent = new FluentTheme();
        fluent.Palettes.Add(
            ThemeVariant.Light,
            new ColorPaletteResources { Accent = Color.Parse("#D43F4A") });
        fluent.Palettes.Add(
            ThemeVariant.Dark,
            new ColorPaletteResources { Accent = Color.Parse("#EF4855") });
        Styles.Add(fluent);
        // The shared EmuShelf control/class styles used to live in MainWindow.axaml's Window.Styles,
        // so instantiating MainWindow brought them along. They now live at Application scope (App.axaml
        // StyleInclude); the render/snapshot tests build MainWindow under this stand-in Application, so
        // it must include the same styles or the gamepad overlays and labels lose their sizing.
        Styles.Add(new StyleInclude(new Uri("avares://EmuShelf.UI/"))
        {
            Source = new Uri("avares://EmuShelf.UI/Styles/EmuShelfStyles.axaml"),
        });
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
