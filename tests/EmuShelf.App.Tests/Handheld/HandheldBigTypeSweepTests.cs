using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public partial class HandheldShellViewModelTests
{
    /// <summary>
    /// M47 B10's sweep for what still read as today's look under Big Type: the app's messages are the Stage's line (the mock's
    /// <c>toast</c>), not today's pill; a folder's name keeps the mock's gap from its count.
    /// </summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeSweep
    {
        private const string LongError =
            "Could not start NetherSX2: the emulator is not installed. Choose one in Settings → Emulators.";

        private static async Task BigType(Func<Task> test)
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                await test();
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        [AvaloniaFact]
        public Task AFailure_IsTheLine_ItShakes_AndAPressMovesOn() => BigType(async () =>
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2).WithPlatform("gc", 2));
            var own = shell.Stage.Line;
            var shakes = shell.LineShakes;

            shell.ShowStatus(LongError, StatusSeverity.Error);
            Assert.Equal(LongError, shell.Stage.Line);
            Assert.Equal(LongError, shell.LineMessage);
            Assert.True(shell.HasLineMessage);
            Assert.Equal(shakes + 1, shell.LineShakes);
            // The app's status is still what it says (the fallback toast, and Gamepad mode's, read it).
            Assert.Equal(LongError, shell.StatusMessage);

            Press(shell, GamepadAction.NavigateRight);
            Assert.False(shell.HasLineMessage);
            Assert.NotEqual(LongError, shell.Stage.Line);
            Press(shell, GamepadAction.NavigateLeft);
            Assert.Equal(own, shell.Stage.Line);
            shell.Dispose();
        });

        [AvaloniaFact]
        public Task WorkStillRunning_HoldsTheLine_UntilTheAppClearsIt_AndANotice_HoldsForItsTime() => BigType(async () =>
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2));
            var own = shell.Stage.Line;
            var shakes = shell.LineShakes;

            shell.ShowStatus("Scraping covers… 6 of 14", StatusSeverity.Progress);
            Assert.Equal("Scraping covers… 6 of 14", shell.Stage.Line);
            Assert.Equal(shakes, shell.LineShakes); // only a failure shakes
            await Task.Delay(TimeSpan.FromMilliseconds(BigTypeLaunchMotion.ErrorHoldMilliseconds / 20));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Scraping covers… 6 of 14", shell.Stage.Line);
            shell.ShowStatus(null);
            Assert.Equal(own, shell.Stage.Line);
            Assert.False(shell.HasLineMessage);

            // A notice holds as long as its caller asks (Message.holdMs when it does not), then the line's own text is back.
            shell.Announce("Added 14 games to PlayStation 2.", duration: TimeSpan.FromMilliseconds(60));
            Assert.Equal("Added 14 games to PlayStation 2.", shell.Stage.Line);
            for (var i = 0; i < 40 && shell.HasLineMessage; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(shell.HasLineMessage);
            Assert.Equal(own, shell.Stage.Line);
            shell.Dispose();
        });

        [AvaloniaFact]
        public async Task TodaysLook_KeepsItsToast_AndTheLineIsUntouched()
        {
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 2));
            var own = shell.Stage.Line;
            shell.ShowStatus(LongError, StatusSeverity.Error);
            Assert.Equal(own, shell.Stage.Line);
            Assert.False(shell.HasLineMessage);
            Assert.Equal(LongError, shell.StatusMessage);
            shell.Dispose();
        }

        /// <summary>
        /// Drawn: the toast is gone under a name, the long message runs to two lines (none of it cut), and stays below the name's box.
        /// </summary>
        [AvaloniaFact]
        public Task TheStage_SaysAMessage_OnItsLine_WithNoToast_AndALongOneWhole() => BigType(async () =>
        {
            var (shell, _) = await CreateFullHomeAsync();
            var view = new HandheldStageView { DataContext = shell };
            var window = new Window { Content = view, Width = 1920, Height = 1080 };
            window.Show();
            try
            {
                shell.ShowStatus(LongError, StatusSeverity.Error);
                await SettleAsync();
                Assert.True(shell.Stage.HasBigTypeName);
                Assert.False(view.FindControl<Control>("MessageShot")!.IsVisible);

                var line = view.FindControl<TextBlock>("BtLine")!;
                Assert.Contains("message", line.Classes);
                Assert.Equal(LongError, line.Text);
                Assert.Equal(2, line.TextLayout.TextLines.Count);
                Assert.DoesNotContain(line.TextLayout.TextLines, text => text.HasCollapsed);

                var canvas = view.FindControl<Control>("StageCanvas")!;
                // The two lines grow up from the line's place; the first line's em box stays under the name's box (stage.NameBottom), only
                // its leading reaches past it.
                var first = line.TextLayout.TextLines[0];
                var emTop = line.TranslatePoint(default, canvas)!.Value.Y + ((first.Height - line.FontSize) / 2);
                var nameBottom = canvas.Bounds.Height - HandheldTokens.StageNameBottom;
                Assert.True(emTop >= nameBottom - 0.5, $"the message's first line ({emTop}) runs into the name's box (bottom {nameBottom})");

                // The message gone, the line is one line again, as B7 set it.
                Press(shell, GamepadAction.NavigateRight);
                await SettleAsync();
                Assert.DoesNotContain("message", line.Classes);
                Assert.True(line.TextLayout.TextLines.Count == 1, $"\"{line.Text}\" is {line.TextLayout.TextLines.Count} lines");
            }
            finally
            {
                window.Close();
                shell.Dispose();
            }
        });

        /// <summary>
        /// Activity's game page: its card's description is two lines (the plays, then first and last played), which in the one-line
        /// second line grew up over the line ("2h" on "Played 1 time"). The first joins the value on the line, the rest is the second line.
        /// </summary>
        [Fact]
        public void ACardOfSeveralLines_KeepsEachOfTheStagesLines_ToOneLine_AndLosesNothing()
        {
            var text = HandheldListStageText.Of("Activity", "Final Fantasy VII", "2h",
                "Played 1 time · 2h a session\nFirst played 9 Nov 2026 · last played 9 Nov 2026\nAlso played before the log", null);
            Assert.Equal("2h · Played 1 time · 2h a session", text.Line);
            Assert.Equal("First played 9 Nov 2026 · last played 9 Nov 2026 · Also played before the log", text.Facts);
            Assert.DoesNotContain('\n', text.Line + text.Facts);

            // One line, as before: the value is the line and the description under it, or the description is the line and the status under it.
            Assert.Equal(new HandheldListStageText("A", "B", "2h", "in all"), HandheldListStageText.Of("A", "B", "2h", "in all", "status"));
            Assert.Equal(new HandheldListStageText("A", "B", "in all", "status"), HandheldListStageText.Of("A", "B", null, "in all", "status"));
        }

        /// <summary>
        /// A folder's name keeps <c>HeaderGap</c> (the mock's <c>.fhead</c> gap) from its count: "Game Boy Advance" beside 106 is set
        /// smaller rather than touching it (B3's "Game Boy Advance106"). A short name keeps its full size.
        /// </summary>
        [AvaloniaTheory]
        [InlineData("Game Boy Advance", 106, true)]
        [InlineData("PlayStation 2", 14, false)]
        public Task AFolderName_KeepsTheGap_FromItsCount(string name, int games, bool shrinks) => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("gba", games);
            library.Platforms[0] = library.Platforms[0] with { Name = name };
            var shell = await CreateAsync(library);
            var view = new HandheldLauncherView { DataContext = shell };
            var window = new Window { Content = view, Width = 1240, Height = 1080 };
            window.Show();
            try
            {
                Press(shell, GamepadAction.Confirm); // open the folder
                for (var i = 0; i < 100 && shell.OpenFolder is not { IsLoaded: true }; i++)
                    await Task.Delay(5);
                await SettleAsync();

                var heading = view.FindControl<HandheldBigTypeHeading>("BtFolderName")!;
                var count = view.FindControl<Control>("BtFolderCount")!;
                var header = view.FindControl<Control>("BtFolderHeader")!;
                Assert.Equal(games.ToString(System.Globalization.CultureInfo.InvariantCulture), ((TextBlock)count).Text);
                var nameRight = heading.TranslatePoint(default, header)!.Value.X + heading.Layout!.Width;
                var countLeft = count.TranslatePoint(default, header)!.Value.X;
                Assert.True(countLeft - nameRight >= HandheldTokens.HeaderGap - 0.5,
                    $"\"{name}\" ends at {nameRight}, {countLeft - nameRight} px from its count at {countLeft}");
                Assert.Equal(shrinks, heading.Layout.Size < HandheldTokens.FolderHeading);
            }
            finally
            {
                window.Close();
                shell.Dispose();
            }
        });
    }
}
