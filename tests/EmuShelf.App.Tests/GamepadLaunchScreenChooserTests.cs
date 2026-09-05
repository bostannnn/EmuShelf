using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using EmuShelf.App.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// The pre-launch "which screen?" chooser as it is actually rendered: two screen cards over a remember
/// toggle. The D-pad walk is view-model state (see MainViewModelTests), but the thing the player reads is
/// which control is LIT — so this drives the real overlay in a window and asserts the highlight moves,
/// which a view-model assertion alone cannot catch when a binding is wrong.
/// </summary>
public class GamepadLaunchScreenChooserTests
{
    [AvaloniaFact]
    public async Task Chooser_HighlightFollowsTheDpadAcrossCardsAndTheRememberRow()
    {
        var viewModel = CreateChooserViewModel(out var game);
        await viewModel.LaunchGameCommand.ExecuteAsync(game);
        Assert.Equal(GamepadOverlayKind.LaunchScreen, viewModel.GamepadOverlay);

        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            await Pump();
            Assert.Equal("gamepad-screen-card", FocusedChooserControl(window));

            // The cards are a ROW: Right lights the external card, Left comes back.
            viewModel.DispatchGamepadAction(GamepadAction.NavigateRight);
            await Pump();
            Assert.Equal(1, viewModel.GamepadOverlaySelectionIndex);
            Assert.Equal("gamepad-screen-card", FocusedChooserControl(window));
            Assert.Contains("focused", CardButtons(window)[1].Classes);
            Assert.DoesNotContain("focused", CardButtons(window)[0].Classes);

            // Down leaves the pair entirely: only the remember row may be lit.
            viewModel.DispatchGamepadAction(GamepadAction.NavigateDown);
            await Pump();
            Assert.Equal(2, viewModel.GamepadOverlaySelectionIndex);
            Assert.All(CardButtons(window), card => Assert.DoesNotContain("focused", card.Classes));
            Assert.Equal("gamepad-remember-row", FocusedChooserControl(window));
        }
        finally
        {
            window.Close();
        }
    }


    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task Chooser_FitsInsideTheOverlayOnTheThor(string theme)
    {
        // The only device that ever shows this prompt is the AYN Thor at ~833x468 dip. The first cut
        // stacked the drawing over its caption, which was fine at 1280x720 but on the Thor pushed the
        // remember row into the hint legend and the footer clean off the sheet. Every chooser control
        // must sit inside the overlay border, above the legend, and no two may overlap.
        var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var previous = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = variant;
        var viewModel = CreateChooserViewModel(out var game);
        await viewModel.LaunchGameCommand.ExecuteAsync(game);
        var window = new MainWindow { DataContext = viewModel, Width = 833, Height = 468, MinHeight = 0, MinWidth = 0 };
        window.Show();
        try
        {
            await Pump();
            var overlay = window.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Classes.Contains("gamepad-overlay") && border.IsVisible);
            var legend = window.FindNamed<StackPanel>("GamepadOverlayHints");
            Assert.NotNull(legend);
            var cards = CardButtons(window);
            var remember = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Classes.Contains("gamepad-remember-row"));
            var overlayRect = BoundsIn(window, overlay);
            var legendRect = BoundsIn(window, legend);

            foreach (var control in cards.Append(remember))
            {
                var rect = BoundsIn(window, control);
                Assert.True(rect.Top >= overlayRect.Top && rect.Bottom <= overlayRect.Bottom,
                    $"{control.Classes[0]} spans {rect.Top:F0}..{rect.Bottom:F0} but the sheet is {overlayRect.Top:F0}..{overlayRect.Bottom:F0}");
                Assert.True(rect.Bottom <= legendRect.Top,
                    $"{control.Classes[0]} bottom {rect.Bottom:F0} runs into the hint legend at {legendRect.Top:F0}");
            }
            Assert.True(BoundsIn(window, cards[0]).Bottom <= BoundsIn(window, remember).Top,
                "the cards overlap the remember row");
            Assert.True(BoundsIn(window, cards[0]).Right <= BoundsIn(window, cards[1]).Left,
                "the two cards overlap each other");

            // Every caption renders in full: nothing is trimmed to an ellipsis on the Thor's width, and
            // nothing paints past its own slot (a NoWrap title clips silently rather than collapsing).
            foreach (var text in overlay.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible))
            {
                Assert.False(text.TextLayout.TextLines.Any(line => line.HasCollapsed),
                    $"\"{text.Text}\" was cut short with an ellipsis");
                Assert.True(text.TextLayout.Width <= text.Bounds.Width + 0.5,
                    $"\"{text.Text}\" needs {text.TextLayout.Width:F0}px but has {text.Bounds.Width:F0}px");
            }

            // Both device drawings are on screen, each lighting exactly one panel.
            var glyphs = overlay.GetVisualDescendants().OfType<ThorDeviceGlyph>().ToList();
            Assert.Equal(2, glyphs.Count);
            Assert.True(glyphs[0].IsTopLit && !glyphs[0].IsBottomLit);
            Assert.True(glyphs[1].IsBottomLit && !glyphs[1].IsTopLit);

            var outputDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");
            if (outputDirectory is not null)
            {
                await Task.Delay(50);
                using (window.CaptureRenderedFrame()) { }
                await Pump();
                Directory.CreateDirectory(outputDirectory);
                using var frame = window.CaptureRenderedFrame();
                using var output = File.Create(Path.Combine(outputDirectory, $"chooser-thor-{theme.ToLowerInvariant()}.png"));
                frame!.Save(output, PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = previous;
        }
    }

    private static Rect BoundsIn(Window window, Visual control)
    {
        var origin = control.TranslatePoint(default, window)!.Value;
        return new Rect(origin, control.Bounds.Size);
    }

    private static IReadOnlyList<Button> CardButtons(Window window) => window.GetVisualDescendants()
        .OfType<Button>()
        .Where(button => button.Classes.Contains("gamepad-screen-card"))
        .ToList();

    /// <summary>The class of whichever chooser control currently carries the highlight — "none" if the
    /// highlight is nowhere, or a joined list if it is somehow on two controls at once.</summary>
    private static string FocusedChooserControl(Window window)
    {
        var lit = window.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("focused") &&
                (button.Classes.Contains("gamepad-screen-card") || button.Classes.Contains("gamepad-remember-row")))
            .Select(button => button.Classes.Contains("gamepad-screen-card")
                ? "gamepad-screen-card"
                : "gamepad-remember-row")
            .ToList();
        return lit.Count == 0 ? "none" : string.Join("+", lit);
    }

    private static MainViewModel CreateChooserViewModel(out GameViewModel game)
    {
        var system = KnownSystems.All.Single(candidate => candidate.Id == "megadrive");
        var viewModel = new MainViewModel(
            new EmptyGameLibrary(),
            new NullFolderScanner(),
            new NoImportRules(),
            new AlwaysAvailableChecker(),
            new FakeDialogService(),
            KnownSystems.All,
            externalDisplays: new AlwaysExternalDisplayProbe())
        {
            IsGamepadMode = true,
        };
        game = new GameViewModel(
            new Game
            {
                Id = 1,
                SystemId = system.Id,
                Path = "/Games/megadrive/Aladdin.md",
                Title = "Aladdin",
                IsAvailable = true,
                DateAdded = DateTimeOffset.UtcNow,
            },
            system.Name, system.ShortName, system.AccentColor,
            coverAspectRatio: system.CoverAspectRatio);
        viewModel.Games.ReplaceAll([game]);
        viewModel.HasGames = true;
        viewModel.IsLibraryEmpty = false;
        viewModel.FocusedGame = game;
        return viewModel;
    }

    private static async Task Pump()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private sealed class AlwaysExternalDisplayProbe : IExternalDisplayProbe
    {
        public bool HasExternalDisplay => true;
    }
}
