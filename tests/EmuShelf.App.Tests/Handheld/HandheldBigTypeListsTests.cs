using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.App.Views.Handheld;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B7: the lists' motion as maths that renders nothing, at fixed moments: rows arriving (tokens.js <c>RowsIn</c>), a Settings
/// section's rows rising in (<c>SectionIn</c>), a heading's letters landing (<c>HeadingIn</c>, the mock's <c>lettersIn</c>), cover
/// search's candidates (<c>CoversIn</c>), the grid stepping back under a sheet (<c>StepBack</c>), and everything in place when held
/// still. The numbers are held to tokens.js.
/// </summary>
public class HandheldBigTypeListMotionTests
{
    private static string TokensJs() =>
        File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));

    private static double Entry(string tokens, string name, string field)
    {
        var match = Regex.Match(tokens, $@"\b{name}:\s*\{{[^}}]*\b{field}:\s*(?<value>-?[0-9.]+)");
        Assert.True(match.Success, $"tokens.js has no {name}.{field}");
        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TheNumbers_AreTokensJs()
    {
        var tokens = TokensJs();
        Assert.Equal(Entry(tokens, "RowsIn", "ms"), BigTypeListMotion.RowsInMilliseconds);
        Assert.Equal(Entry(tokens, "RowsIn", "stagger"), BigTypeListMotion.RowsInStagger);
        Assert.Equal(Entry(tokens, "RowsIn", "slide"), BigTypeListMotion.RowsInSlide);
        Assert.Equal(Entry(tokens, "RowsIn", "delay"), BigTypeListMotion.RowsInDelay);
        Assert.Equal(Entry(tokens, "RowsIn", "railSlide"), BigTypeListMotion.RowsInRailSlide);
        Assert.Equal(Entry(tokens, "SectionIn", "ms"), BigTypeListMotion.SectionInMilliseconds);
        Assert.Equal(Entry(tokens, "SectionIn", "stagger"), BigTypeListMotion.SectionInStagger);
        Assert.Equal(Entry(tokens, "SectionIn", "rise"), BigTypeListMotion.SectionInRise);
        Assert.Equal(Entry(tokens, "CoversIn", "ms"), BigTypeListMotion.CoversInMilliseconds);
        Assert.Equal(Entry(tokens, "CoversIn", "staggerPerTile"), BigTypeListMotion.CoversInStagger);
        Assert.Equal(Entry(tokens, "CoversIn", "rise"), BigTypeListMotion.CoversInRise);
        Assert.Equal(Entry(tokens, "CoversIn", "fromScale"), BigTypeListMotion.CoversInFromScale, 4);
        Assert.Equal(Entry(tokens, "HeadingIn", "ms"), BigTypeListMotion.HeadingInMilliseconds);
        Assert.Equal(Entry(tokens, "HeadingIn", "delay"), BigTypeListMotion.HeadingInDelay);
        Assert.Equal(Entry(tokens, "HeadingIn", "stagger"), BigTypeListMotion.HeadingInStagger);
        Assert.Equal(Entry(tokens, "HeadingIn", "staggerMax"), BigTypeListMotion.HeadingInStaggerMax);
        Assert.Equal(Entry(tokens, "HeadingIn", "fromEm"), BigTypeListMotion.HeadingInFromEm, 4);
        Assert.Equal(Entry(tokens, "HeadingIn", "sheetFromEm"), BigTypeListMotion.HeadingInSheetFromEm, 4);
        Assert.Equal(Entry(tokens, "HeadingIn", "tilt"), BigTypeListMotion.HeadingInTilt);
        Assert.Equal(Entry(tokens, "StepBack", "ms"), BigTypeListMotion.StepBackMilliseconds);
        Assert.Equal(Entry(tokens, "StepBack", "fadeMs"), BigTypeListMotion.StepBackFadeMilliseconds);
        Assert.Contains("StepBack:  { ms: 400, fadeMs: 280, easing: 'cubic-bezier(.2, 1.2, .3, 1)' }", tokens);
        Assert.Equal(Entry(tokens, "RowFocus", "ms"), BigTypeListMotion.RowFocusMilliseconds);
        Assert.Equal(Entry(tokens, "RowFocus", "fadeMs"), BigTypeListMotion.RowFocusFadeMilliseconds);
        Assert.Equal(Entry(tokens, "Switch", "ms"), BigTypeListMotion.SwitchMilliseconds);
        Assert.Equal(Entry(tokens, "Switch", "fadeMs"), BigTypeListMotion.SwitchFadeMilliseconds);
        Assert.Equal(Entry(tokens, "Scroll", "ms"), BigTypeListMotion.ScrollMilliseconds);
    }

    [Fact]
    public void RowsIn_EachRowWaitsItsTurn_SlidesOnTheLandingCurve_AndRests()
    {
        const float slide = BigTypeListMotion.RowsInSlide;
        // Before its delay a row is held at its first keyframe: off to the right and clear (fill: backwards).
        Assert.Equal((slide, 0f), BigTypeListMotion.RowIn(0, 0f));
        Assert.Equal((slide, 0f), BigTypeListMotion.RowIn(0, BigTypeListMotion.RowsInDelay));
        // The third row starts 2 × 32 ms after the first.
        Assert.Equal((slide, 0f), BigTypeListMotion.RowIn(2, BigTypeListMotion.RowsInDelay + 64f));
        // A quarter of the way in: on its way, partly drawn.
        var quarter = BigTypeListMotion.RowIn(0, BigTypeListMotion.RowsInDelay + (BigTypeListMotion.RowsInMilliseconds / 4));
        var eased = BigTypeMotion.Land(0.25f);
        Assert.Equal(slide * (1 - eased), quarter.Offset, 3);
        Assert.InRange(quarter.Offset, 0f, slide);
        Assert.Equal(Math.Clamp(eased, 0f, 1f), quarter.Opacity, 3);
        // Halfway the Land curve has overshot: the row is past its place, fully drawn, and comes back.
        var half = BigTypeListMotion.RowIn(0, BigTypeListMotion.RowsInDelay + (BigTypeListMotion.RowsInMilliseconds / 2));
        Assert.True(half.Offset < 0f);
        Assert.Equal(1f, half.Opacity);
        // At its end it rests.
        Assert.Equal((0f, 1f), BigTypeListMotion.RowIn(0, BigTypeListMotion.RowsInDelay + BigTypeListMotion.RowsInMilliseconds));
        Assert.Equal((0f, 1f), BigTypeListMotion.RowIn(3, BigTypeListMotion.RowsInTotal(4)));
        Assert.Equal(40f + (3 * 32f) + 420f, BigTypeListMotion.RowsInTotal(4));
        // The rail comes in from the left.
        Assert.Equal((BigTypeListMotion.RowsInRailSlide, 0f), BigTypeListMotion.RowIn(0, 0f, BigTypeListMotion.RowsInRailSlide));
        Assert.True(BigTypeListMotion.RowIn(0, 100f, BigTypeListMotion.RowsInRailSlide).Offset < 0f);
    }

    [Fact]
    public void SectionIn_RisesFromTheWayYouMoved()
    {
        Assert.Equal((52f, 0f), BigTypeListMotion.SectionRowIn(0, 0f, BigTypeListMotion.SectionInRise));
        Assert.Equal((-52f, 0f), BigTypeListMotion.SectionRowIn(0, 0f, -BigTypeListMotion.SectionInRise));
        // No delay before the first row; the second 30 ms after it.
        Assert.True(BigTypeListMotion.SectionRowIn(0, 30f, 52f).Opacity > 0f);
        Assert.Equal((52f, 0f), BigTypeListMotion.SectionRowIn(1, 30f, 52f));
        Assert.Equal((0f, 1f), BigTypeListMotion.SectionRowIn(1, 430f, 52f));
        Assert.Equal(30f + 400f, BigTypeListMotion.SectionInTotal(2));
    }

    [Fact]
    public void HeadingIn_LettersLandOneAfterAnother_UpToTheCap()
    {
        const float from = BigTypeListMotion.HeadingInFromEm;
        // 60 ms, then 24 ms a letter, at most 360 ms of stagger.
        Assert.Equal(60f, BigTypeListMotion.HeadingLetterDelay(0));
        Assert.Equal(84f, BigTypeListMotion.HeadingLetterDelay(1));
        Assert.Equal(420f, BigTypeListMotion.HeadingLetterDelay(15));
        Assert.Equal(420f, BigTypeListMotion.HeadingLetterDelay(40));
        Assert.Equal(60f + 360f + 520f, BigTypeListMotion.HeadingInTotal(20));
        // Before its turn a letter waits below, tilted and clear.
        Assert.Equal((from, BigTypeListMotion.HeadingInTilt, 0f), BigTypeListMotion.HeadingLetter(0, 60f));
        Assert.Equal((from, BigTypeListMotion.HeadingInTilt, 0f), BigTypeListMotion.HeadingLetter(3, 100f));
        // On its way it rises and straightens on the landing curve.
        var (em, degrees, opacity) = BigTypeListMotion.HeadingLetter(0, 60f + 130f);
        var eased = BigTypeMotion.Land(0.25f);
        Assert.Equal(from * (1 - eased), em, 4);
        Assert.Equal(BigTypeListMotion.HeadingInTilt * (1 - eased), degrees, 4);
        Assert.Equal(Math.Clamp(eased, 0f, 1f), opacity, 4);
        // Landed.
        Assert.Equal((0f, 0f, 1f), BigTypeListMotion.HeadingLetter(0, 60f + 520f));
        // A sheet's title rises less far.
        Assert.Equal(BigTypeListMotion.HeadingInSheetFromEm, BigTypeListMotion.HeadingLetter(0, 0f, BigTypeListMotion.HeadingInSheetFromEm).Em);
    }

    [Fact]
    public void CoversIn_RiseAndGrow_ByColumnAndRow()
    {
        Assert.Equal((120f, 0.6f, 0f), BigTypeListMotion.CoverIn(0, 0, 0f));
        // The second row's second cover starts two steps (90 ms) after the first.
        Assert.Equal((120f, 0.6f, 0f), BigTypeListMotion.CoverIn(1, 1, 90f));
        Assert.True(BigTypeListMotion.CoverIn(1, 1, 100f).Opacity > 0f);
        Assert.Equal((0f, 1f, 1f), BigTypeListMotion.CoverIn(1, 1, 90f + 460f));
        Assert.Equal(4 * 45f + 460f, BigTypeListMotion.CoversInTotal(4));
    }

    [Fact]
    public void StepBack_TheGridScalesAndFades_BackComesAtOnceWhenHeld()
    {
        Assert.Equal((1f, 1f), BigTypeListMotion.StepBack(0f, 1f, 1f, 0.95f, 0.12f));
        var mid = BigTypeListMotion.StepBack(140f, 1f, 1f, 0.95f, 0.12f);
        Assert.InRange(mid.Opacity, 0.12f, 1f);
        Assert.InRange(mid.Scale, 0.9f, 1f);
        // The fade ends at 280 ms, the scale at 400.
        Assert.Equal(0.12f, BigTypeListMotion.StepBack(280f, 1f, 1f, 0.95f, 0.12f).Opacity, 4);
        Assert.NotEqual(0.95f, BigTypeListMotion.StepBack(280f, 1f, 1f, 0.95f, 0.12f).Scale);
        Assert.Equal((0.95f, 0.12f), BigTypeListMotion.StepBack(400f, 1f, 1f, 0.95f, 0.12f));
        Assert.Equal((0.95f, 0.12f), BigTypeListMotion.StepBack(0f, 1f, 1f, 0.95f, 0.12f, holdStill: true));
    }

    [Fact]
    public void HoldingStill_EverythingIsInPlace()
    {
        Assert.Equal((0f, 1f), BigTypeListMotion.RowIn(5, 0f, holdStill: true));
        Assert.Equal((0f, 1f), BigTypeListMotion.SectionRowIn(5, 0f, 52f, holdStill: true));
        Assert.Equal((0f, 0f, 1f), BigTypeListMotion.HeadingLetter(5, 0f, holdStill: true));
        Assert.Equal((0f, 1f, 1f), BigTypeListMotion.CoverIn(3, 1, 0f, holdStill: true));
    }

    [Fact]
    public void ARowWindow_KeepsTheFocusedRowInView_ByWholeRows()
    {
        // Seven in view: moving down past the last one moves the window one row; back up to the top, it follows.
        Assert.Equal(0, HandheldRowWindow.First(0, 6, 7));
        Assert.Equal(1, HandheldRowWindow.First(0, 7, 7));
        Assert.Equal(4, HandheldRowWindow.First(0, 10, 7));
        Assert.Equal(4, HandheldRowWindow.First(4, 8, 7));
        Assert.Equal(2, HandheldRowWindow.First(4, 2, 7));
        Assert.Equal(0, HandheldRowWindow.First(3, -1, 7));
    }
}

public partial class HandheldShellViewModelTests
{
    /// <summary>
    /// M47 B7: the lists in the Big Type look, hosted headlessly: their tokens are tokens.js's, every row and its ring sit where the
    /// mock's <c>buildList</c> puts them, the Stage names the focused row, and the native Launcher hands the screen over only once
    /// this view has drawn (no white frames). Switching the look is process-wide, so these never run beside another test.
    /// </summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeLists
    {
        private static double Number(string tokens, string name)
        {
            var match = Regex.Match(tokens, $@"\b{name}:\s*(?<value>-?[0-9.]+)");
            Assert.True(match.Success, $"tokens.js has no {name}");
            return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
        }

        private static double Size(string tokens, string role) =>
            double.Parse(Regex.Match(tokens, $@"\b{role}:\s*\{{\s*size:\s*(?<v>[0-9.]+)").Groups["v"].Value, CultureInfo.InvariantCulture);

        private static double Opacity(string tokens, string role) =>
            double.Parse(Regex.Match(tokens, $@"\b{role}:\s*\{{[^}}]*opacity:\s*(?<v>[0-9.]+)").Groups["v"].Value, CultureInfo.InvariantCulture);

        [Fact]
        public void TheListsTokens_AreTokensJs_AndTodaysLookKeepsItsOwn()
        {
            var tokens = File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));
            try
            {
                HandheldTokens.Use(HandheldLook.BigType);
                foreach (var (name, value) in new (string, double)[]
                {
                    ("RowPitch", HandheldTokens.RowPitch), ("RowPitchPanel", HandheldTokens.RowPitchPanel),
                    ("RowPitchSettings", HandheldTokens.RowPitchSettings), ("PanelRowsVisible", HandheldTokens.PanelRowsVisible),
                    ("RowGap", HandheldTokens.RowGap), ("RowValueGap", HandheldTokens.RowValueGap), ("RowFocusShift", HandheldTokens.RowFocusShift),
                    ("RowRingOutsetX", HandheldTokens.RowRingOutsetX), ("RowRingOutsetY", HandheldTokens.RowRingOutsetY),
                    ("RowGroupIndent", HandheldTokens.RowGroupIndent),
                    ("SheetLeft", HandheldTokens.SheetLeft), ("SheetTitleTop", HandheldTokens.SheetTitleTop), ("SheetTop", HandheldTokens.SheetTop),
                    ("SheetWidth", HandheldTokens.SheetWidth), ("SheetRowsVisible", HandheldTokens.SheetRowsVisible),
                    ("SettingsLeft", HandheldTokens.SettingsLeft), ("SettingsHeadTop", HandheldTokens.SettingsHeadTop),
                    ("SettingsHeadMaxWidth", HandheldTokens.SettingsHeadMaxWidth), ("SettingsTop", HandheldTokens.SettingsTop),
                    ("RailWidth", HandheldTokens.RailWidth), ("SettingsRowsLeft", HandheldTokens.SettingsRowsLeft),
                    ("SettingsRowsWidth", HandheldTokens.SettingsRowsWidth), ("SettingsRowsVisible", HandheldTokens.SettingsRowsVisible),
                    ("PanelLeft", HandheldTokens.PanelLeft), ("PanelHeadTop", HandheldTokens.PanelHeadTop), ("PanelHeadHeight", HandheldTokens.PanelHeadHeight),
                    ("PanelNameMaxWidth", HandheldTokens.PanelNameMaxWidth), ("PanelCountRaise", HandheldTokens.PanelCountRaise),
                    ("PanelRowsTop", HandheldTokens.PanelRowsTop), ("PanelRowsInset", HandheldTokens.PanelRowsInset), ("PanelWidth", HandheldTokens.PanelWidth),
                    ("FolderColumnsVisible", HandheldTokens.CoverColumns), ("FolderLeft", HandheldTokens.FolderLeft), ("CoverRowsVisible", HandheldTokens.CoverRowsVisible),
                    ("SwitchWidth", HandheldTokens.SwitchWidth), ("SwitchHeight", HandheldTokens.SwitchHeight), ("SwitchKnob", HandheldTokens.SwitchKnob),
                    ("SwitchStroke", HandheldTokens.SwitchStroke), ("SwitchKnobInset", HandheldTokens.SwitchKnobInset),
                    ("SwitchKnobTravel", HandheldTokens.SwitchKnobTravel), ("SwitchOnKnobScale", HandheldTokens.SwitchOnKnobScale),
                    ("UnderSheetScale", HandheldTokens.UnderSheetScale), ("ProgressHeight", HandheldTokens.ProgressHeight),
                    ("RingRadiusRow", HandheldTokens.RingRadiusRow.TopLeft),
                    ("RowRestOpacity", HandheldTokens.RowRestOpacity), ("RowLockedOpacity", HandheldTokens.RowLockedOpacity),
                    ("RowLockedFocusOpacity", HandheldTokens.RowLockedFocusOpacity), ("SwitchOffOpacity", HandheldTokens.SwitchOffOpacity),
                    ("UnderSheetOpacity", HandheldTokens.UnderSheetOpacity),
                })
                {
                    Assert.True(Number(tokens, name) == value, $"{name}: tokens.js {Number(tokens, name)}, HandheldTokens {value}");
                }
                Assert.Equal(Size(tokens, "Row"), HandheldTokens.Row);
                Assert.Equal(Size(tokens, "RowValue"), HandheldTokens.RowValue);
                Assert.Equal(Opacity(tokens, "RowValue"), HandheldTokens.RowValueOpacity);
                Assert.Equal(Size(tokens, "RailRow"), HandheldTokens.RailRow);
                Assert.Equal(Size(tokens, "SheetTitle"), HandheldTokens.SheetTitle);
                Assert.Equal(Size(tokens, "Heading"), HandheldTokens.Heading);
                Assert.Equal(Size(tokens, "PanelCount"), HandheldTokens.PanelCount);
                Assert.Equal(Opacity(tokens, "PanelCount"), HandheldTokens.PanelCountOpacity);
                Assert.Equal("#FFC9BD", ((Avalonia.Media.ISolidColorBrush)HandheldTokens.Danger).Color.ToString()[3..].Insert(0, "#").ToUpperInvariant());
                Assert.Contains($"Settings: '#{HandheldTokens.AppGround.ToString()[3..].ToUpperInvariant()}'", tokens, StringComparison.OrdinalIgnoreCase);
                // The Title page (B8's) keeps today's sizes under Big Type.
                Assert.Equal(32, HandheldTokens.TitleOptionsTitle);
                Assert.Equal(56, HandheldTokens.RingRadiusTitleChip.TopLeft);
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
            // Today's look is as it was.
            Assert.Equal(28, HandheldTokens.Row);
            Assert.Equal(24, HandheldTokens.RowValue);
            Assert.Equal(28, HandheldTokens.RailRow);
            Assert.Equal(32, HandheldTokens.SheetTitle);
            Assert.Equal(34, HandheldTokens.Heading);
            Assert.Equal(88, HandheldTokens.SwitchWidth);
            Assert.Equal(36, HandheldTokens.SwitchKnob);
            Assert.Equal(56, HandheldTokens.RingRadiusRow.TopLeft);
        }

        // Where a row's slot and its ring are on the lists' canvas.
        private static (Rect Slot, Rect Ring) Placed(Control lists, Control slot)
        {
            var at = slot.TranslatePoint(default, lists)!.Value;
            var ring = slot.GetVisualChildren().OfType<Border>().Single(border => border.Classes.Contains("ring"));
            var ringAt = ring.TranslatePoint(default, lists)!.Value;
            return (new Rect(at, slot.Bounds.Size), new Rect(ringAt, ring.Bounds.Size));
        }

        private static Control[] Slots(Control list) =>
            list.GetVisualDescendants().OfType<Panel>().Where(panel => panel.Classes.Contains("slot")).Cast<Control>().ToArray();

        // The mock's buildList ring for row i of a list at (x, y), w wide, on a pitch (its rect at half scale, doubled).
        private static Rect MockRing(double x, double y, double width, double pitch, int i) =>
            new(x - HandheldTokens.RowRingOutsetX, y + (i * pitch) - HandheldTokens.RowRingOutsetY, width + (2 * HandheldTokens.RowRingOutsetX),
                pitch + (2 * HandheldTokens.RowRingOutsetY) - HandheldTokens.RowGap);

        // Long enough for a list's rows to have arrived and its heading to have landed.
        private static async Task ArrivedAsync()
        {
            for (var i = 0; i < 4; i++)
                await SettleAsync();
        }

        [AvaloniaFact]
        public async Task TheMenuTheSheetAndSettings_SitWhereTheMocksListsDo_AndTheStageNamesTheRow()
        {
            HandheldTokens.Use(HandheldLook.BigType);
            Window? window = null;
            try
            {
                var library = new FakeLibrary().WithPlatform("ps2", 4);
                var shell = await CreateWithSettingsAsync(library);
                var launcher = new HandheldLauncherView(null, pinnedWide: false) { DataContext = shell };
                window = new Window { Content = launcher, Width = 1240, Height = 1080 };
                window.Show();
                await SettleAsync();
                var lists = launcher.FindControl<HandheldBigTypeLists>("BtLists")!;
                var canvas = lists.FindControl<Panel>("Lists")!;

                // The Start menu (from the dock's button, as the pad gets there): its title, then a row every RowPitch from SheetTop;
                // the Stage names the row and says what it does.
                for (var i = 0; i < 6 && shell.Zone != HandheldZone.Dock; i++)
                    Press(shell, GamepadAction.NavigateDown);
                while (shell.DockIndex < shell.DockItems.Count - 1)
                    Press(shell, GamepadAction.NavigateRight);
                Press(shell, GamepadAction.Confirm);
                Assert.True(shell.IsMenuOpen);
                await ArrivedAsync();
                var menu = Slots(lists.FindControl<Control>("MenuRowsPart")!);
                Assert.Equal(shell.MenuItems.Count, menu.Length);
                for (var i = 0; i < menu.Length; i++)
                {
                    var (slot, ring) = Placed(canvas, menu[i]);
                    Assert.Equal(new Rect(HandheldTokens.SheetLeft, HandheldTokens.SheetTop + (i * HandheldTokens.RowPitch), HandheldTokens.SheetWidth,
                        HandheldTokens.RowPitch - HandheldTokens.RowGap), slot);
                    Assert.Equal(MockRing(HandheldTokens.SheetLeft, HandheldTokens.SheetTop, HandheldTokens.SheetWidth, HandheldTokens.RowPitch, i), ring);
                }
                var title = lists.FindControl<HandheldBigTypeHeading>("MenuTitlePart")!;
                Assert.Equal(new Point(HandheldTokens.SheetLeft, HandheldTokens.SheetTitleTop), title.TranslatePoint(default, canvas));
                Assert.Equal(("Menu", "Search", "Find a game by name"), (shell.Stage.Label, shell.Stage.Name, shell.Stage.Line));
                Press(shell, GamepadAction.NavigateDown);
                Assert.Equal(("Add games", HandheldDirection.Down), (shell.Stage.Name, shell.LastMoveDirection));
                // The rows have arrived: in place and drawn.
                Assert.All(menu, slot =>
                {
                    var arrive = slot.GetVisualChildren().OfType<Panel>().Single(panel => panel.Classes.Contains("arrive"));
                    Assert.Equal(1, arrive.Opacity);
                    Assert.Null(arrive.RenderTransform);
                });
                Press(shell, GamepadAction.Cancel);
                await SettleAsync();

                // The Y sheet on a folder: the folder's name is the title; the rows sit as the menu's.
                Press(shell, GamepadAction.NavigateUp);
                FocusHome(shell, 0);
                Press(shell, GamepadAction.Actions);
                await ArrivedAsync();
                var sheet = shell.Sheet!;
                var rows = Slots(lists.FindControl<Control>("SheetRowsPart")!);
                Assert.Equal(sheet.Items.Count, rows.Length);
                for (var i = 0; i < rows.Length; i++)
                {
                    Assert.Equal(MockRing(HandheldTokens.SheetLeft, HandheldTokens.SheetTop, HandheldTokens.SheetWidth, HandheldTokens.RowPitch, i),
                        Placed(canvas, rows[i]).Ring);
                }
                Assert.Equal((sheet.Title, sheet.Items[0].Label), (shell.Stage.Label, shell.Stage.Name));
                Press(shell, GamepadAction.Cancel);
                await SettleAsync();

                // Settings: the rail from SettingsLeft and the rows from SettingsRowsLeft, both from SettingsTop on their pitch.
                await shell.OpenSettingsAsync();
                await ArrivedAsync();
                var rail = Slots(lists.FindControl<Control>("RailPart")!);
                Assert.Equal(shell.Settings!.Sections.Count, rail.Length);
                for (var i = 0; i < rail.Length; i++)
                {
                    Assert.Equal(MockRing(HandheldTokens.SettingsLeft, HandheldTokens.SettingsTop, HandheldTokens.RailWidth, HandheldTokens.RowPitchSettings, i),
                        Placed(canvas, rail[i]).Ring);
                }
                var settingsRows = Slots(lists.FindControl<Control>("SettingsRowsPart")!);
                Assert.NotEmpty(settingsRows);
                Assert.Equal(MockRing(HandheldTokens.SettingsRowsLeft, HandheldTokens.SettingsTop, HandheldTokens.SettingsRowsWidth, HandheldTokens.RowPitchSettings, 0),
                    Placed(canvas, settingsRows[0]).Ring);
                // A row has the pad: the Stage names it, under Settings and its section; Settings has its own colour.
                var section = shell.Settings.Sections.Single(item => item.IsSelected).Title;
                Assert.Equal($"Settings · {section}", shell.Stage.Label);
                Assert.Equal(shell.Settings.Projection.FocusedRow!.Label, shell.Stage.Name);
                Assert.Equal(HandheldTokens.AppGround, shell.LauncherGround);
                // The rail: the section, under "Settings".
                Press(shell, GamepadAction.NavigateLeft);
                Assert.True(shell.Settings.Projection.IsRailFocused);
                Assert.Equal(("Settings", section), (shell.Stage.Label, shell.Stage.Name));
            }
            finally
            {
                window?.Close();
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        [AvaloniaFact]
        public async Task TodaysLook_TheStageKeepsWhatWasUnderTheMenu()
        {
            Assert.True(HandheldTokens.IsClassic);
            var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 4));
            var name = shell.Stage.Name;
            Press(shell, GamepadAction.Menu);
            Assert.True(shell.IsMenuOpen);
            Assert.Equal(name, shell.Stage.Name);
        }

        [AvaloniaFact]
        public async Task ASheetOpening_TheNativeLauncherStaysInFront_UntilThisViewHasDrawn()
        {
            var screen = new RecordingNativeScreen();
            var previous = HandheldNativeStage.Launcher;
            HandheldNativeStage.Launcher = screen;
            Window? window = null;
            try
            {
                var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 4));
                var launcher = new HandheldLauncherView { DataContext = shell };
                window = new Window { Content = launcher, Width = 1240, Height = 1080 };
                window.Show();
                await SettleAsync();
                Assert.True(screen.Active);

                // The Avalonia Launcher takes the screen: it is shown at once, the native one goes only after this view has drawn.
                Press(shell, GamepadAction.Actions);
                Assert.NotNull(shell.Sheet);
                Assert.Equal(1, launcher.Opacity);
                Assert.True(screen.Active);
                await SettleAsync();
                Assert.False(screen.Active);

                // Back to the native Launcher at once.
                Press(shell, GamepadAction.Cancel);
                Assert.True(screen.Active);
                Assert.Equal(0, launcher.Opacity);
            }
            finally
            {
                window?.Close();
                HandheldNativeStage.Launcher = previous;
            }
        }
    }
}
