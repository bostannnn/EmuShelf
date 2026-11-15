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
using EmuShelf.Core.Launching;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B8: opening and closing a folder, the launch and the in-game companion as maths that renders nothing, at fixed moments
/// (tokens.js <c>FolderOut</c>, <c>FolderDeal</c>, <c>FolderClose</c>, <c>FolderPopIn</c>, <c>RingPop</c>, <c>Launch</c>, <c>InGameIn</c>,
/// <c>InGameTab</c>, <c>InGameOut</c>, <c>Message</c>), and everything in place when held still. The numbers are held to tokens.js.
/// </summary>
public class HandheldBigTypeFolderLaunchMotionTests
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
        var t = TokensJs();
        (string Name, string Field, double Value)[] expected =
        [
            ("FolderOut", "ms", BigTypeFolderMotion.OutMilliseconds), ("FolderOut", "staggerPerTile", BigTypeFolderMotion.OutStagger),
            ("FolderOut", "push", BigTypeFolderMotion.OutPush), ("FolderOut", "toScale", BigTypeFolderMotion.OutToScale),
            ("FolderOut", "openedScale", BigTypeFolderMotion.OutOpenedScale), ("FolderOut", "openedDelay", BigTypeFolderMotion.OutOpenedDelay),
            ("FolderOut", "staggerSteps", BigTypeFolderMotion.OutStaggerSteps), ("FolderOut", "holdMs", BigTypeFolderMotion.OutHoldMilliseconds),
            ("FolderDeal", "ms", BigTypeFolderMotion.DealMilliseconds), ("FolderDeal", "staggerPerTile", BigTypeFolderMotion.DealStagger),
            ("FolderDeal", "rise", BigTypeFolderMotion.DealRise), ("FolderDeal", "fromScale", BigTypeFolderMotion.DealFromScale),
            ("FolderDeal", "staggerSteps", BigTypeFolderMotion.DealStaggerSteps), ("FolderDeal", "ringAfterMs", BigTypeFolderMotion.DealRingAfter),
            ("FolderClose", "ms", BigTypeFolderMotion.CloseMilliseconds), ("FolderClose", "staggerPerTile", BigTypeFolderMotion.CloseStagger),
            ("FolderClose", "drop", BigTypeFolderMotion.CloseDrop), ("FolderClose", "toScale", BigTypeFolderMotion.CloseToScale),
            ("FolderClose", "staggerFrom", BigTypeFolderMotion.CloseStaggerFrom), ("FolderClose", "holdMs", BigTypeFolderMotion.CloseHoldMilliseconds),
            ("FolderClose", "ringAfterMs", BigTypeFolderMotion.CloseRingAfter),
            ("FolderPopIn", "ms", BigTypeFolderMotion.PopInMilliseconds), ("FolderPopIn", "staggerPerTile", BigTypeFolderMotion.PopInStagger),
            ("FolderPopIn", "fromScale", BigTypeFolderMotion.PopInFromScale), ("FolderPopIn", "staggerSteps", BigTypeFolderMotion.PopInStaggerSteps),
            ("RingPop", "ms", BigTypeMotion.PopMilliseconds), ("RingPop", "fromScale", BigTypeMotion.PopFromScale),
            ("RingPop", "folderFromScale", BigTypeFolderMotion.RingPopFromScale),
            ("Launch", "flightMs", BigTypeLaunchMotion.FlightMilliseconds), ("Launch", "takeoverMs", BigTypeLaunchMotion.TakeoverMilliseconds),
            ("Launch", "flightScale", BigTypeLaunchMotion.FlightScale), ("Launch", "flightBeyond", BigTypeLaunchMotion.FlightBeyond),
            ("Launch", "bumpAtMs", BigTypeLaunchMotion.BumpAt), ("Launch", "bumpMs", BigTypeLaunchMotion.BumpMilliseconds),
            ("Launch", "bumpScale", BigTypeLaunchMotion.BumpScale), ("Launch", "bumpRise", BigTypeLaunchMotion.BumpRise),
            ("Launch", "takeoverAtMs", BigTypeLaunchMotion.TakeoverAt), ("Launch", "takeoverX", BigTypeLaunchMotion.TakeoverX),
            ("Launch", "takeoverY", BigTypeLaunchMotion.TakeoverY), ("Launch", "takeoverRadius", BigTypeLaunchMotion.TakeoverRadius),
            ("Launch", "titleFromEm", BigTypeLaunchMotion.TitleFromEm), ("Launch", "gridFadeMs", BigTypeLaunchMotion.GridFadeMilliseconds),
            ("Launch", "companionAtMs", BigTypeLaunchMotion.CompanionAt),
            ("InGameIn", "ms", BigTypeLaunchMotion.PartInMilliseconds), ("InGameIn", "stagger", BigTypeLaunchMotion.PartInStagger),
            ("InGameIn", "rise", BigTypeLaunchMotion.PartInRise),
            ("InGameTab", "ms", BigTypeLaunchMotion.TabMilliseconds), ("InGameTab", "slide", BigTypeLaunchMotion.TabSlide),
            ("InGameOut", "ms", BigTypeLaunchMotion.PartOutMilliseconds), ("InGameOut", "stagger", BigTypeLaunchMotion.PartOutStagger),
            ("InGameOut", "drop", BigTypeLaunchMotion.PartOutDrop), ("InGameOut", "launchFadeMs", BigTypeLaunchMotion.LaunchFadeMilliseconds),
            ("InGameOut", "holdMs", BigTypeLaunchMotion.ReturnHoldMilliseconds), ("InGameOut", "popAfterMs", BigTypeLaunchMotion.ReturnRingAfter),
            ("UnlockLine", "holdMs", BigTypeLaunchMotion.UnlockLineHoldMilliseconds),
            ("InGameRest", "afterMs", BigTypeLaunchMotion.RestAfterMilliseconds), ("InGameRest", "fadeMs", BigTypeLaunchMotion.RestFadeMilliseconds),
            ("SessionAchievementBar", "ms", BigTypeLaunchMotion.BarMilliseconds),
            ("Message", "holdMs", BigTypeLaunchMotion.HoldMilliseconds), ("Message", "errorHoldMs", BigTypeLaunchMotion.ErrorHoldMilliseconds),
            ("Message", "shakeMs", BigTypeLaunchMotion.ShakeMilliseconds), ("Message", "shake", BigTypeLaunchMotion.Shake),
            ("Message", "shakeBack", BigTypeLaunchMotion.ShakeBack), ("Message", "shakeSettle", BigTypeLaunchMotion.ShakeSettle),
        ];
        foreach (var (name, field, value) in expected)
            Assert.True(Math.Abs(Entry(t, name, field) - value) < 1e-4, $"{name}.{field}: tokens.js {Entry(t, name, field)}, the app {value}");
        Assert.Contains("flightEasing: 'cubic-bezier(.6, 0, .9, .5)'", t);
        Assert.Contains("takeoverEasing: 'cubic-bezier(.3, .6, .2, 1)'", t);
        Assert.Contains("LaunchTitle:        { sizes: [224, 160, 128]", t);
    }

    [Fact]
    public void FallAway_PushesFromTheOpenedTile_TheOpenedOneGrows_AllFade()
    {
        // A tile one column right and one row down of the opened one: pushed 56 px each way, shrinking to 0.55.
        var start = BigTypeFolderMotion.FallAway(1, 1, opened: false, since: 0f);
        Assert.Equal(0f, start.Progress);
        Assert.Equal(1f, start.Opacity);
        Assert.Equal((56f, 56f, 0.55f), (start.ToX, start.ToY, start.ToScale));
        // It waits √2 × 20 ms, then leaves on the Leave curve; at its end it has gone.
        var delay = MathF.Sqrt(2f) * 20f;
        Assert.Equal(0f, BigTypeFolderMotion.FallAway(1, 1, false, delay - 1f).Progress);
        var half = BigTypeFolderMotion.FallAway(1, 1, false, delay + 125f);
        Assert.Equal(BigTypeMotion.Leave(0.5f), half.Progress, 4);
        Assert.Equal(1f - BigTypeMotion.Leave(0.5f), half.Opacity, 4);
        Assert.Equal(1f, BigTypeFolderMotion.FallAway(1, 1, false, delay + 250f).Progress);
        // Far tiles wait at most six steps.
        Assert.Equal(0f, BigTypeFolderMotion.FallAway(9, 0, false, 119f).Progress);
        Assert.True(BigTypeFolderMotion.FallAway(9, 0, false, 121f).Progress > 0f);
        // The opened tile grows to 1.7 after 70 ms, where it is.
        var opened = BigTypeFolderMotion.FallAway(0, 0, opened: true, since: 70f + 250f);
        Assert.Equal((0f, 0f, 1.7f, 1f), (opened.ToX, opened.ToY, opened.ToScale, opened.Progress));
        Assert.Equal(0f, BigTypeFolderMotion.FallAway(0, 0, true, 69f).Progress);
        // Blending from a tile's own resting pose (its lift, its push).
        Assert.Equal((8f + ((56f - 8f) * 0.5f), 28f, 1.1f + ((0.55f - 1.1f) * 0.5f)),
            new BigTypeTileLeave(0.5f, 56f, 56f, 0.55f).From(8f, 0f, 1.1f));
        // Home goes and the folder shows 370 ms after the press.
        Assert.False(BigTypeFolderMotion.HomeGone(369f));
        Assert.True(BigTypeFolderMotion.HomeGone(370f));
    }

    [Fact]
    public void Deal_RisesByColumnAndRow_AndTheRingPopsIn230MsAfterTheFolderShows()
    {
        Assert.Equal(new BigTypeTilePose(0f, 160f, 0.55f, 0f), BigTypeFolderMotion.Deal(0, 0, 0f));
        // Column 1, row 2: three steps, 135 ms later.
        Assert.Equal(new BigTypeTilePose(0f, 160f, 0.55f, 0f), BigTypeFolderMotion.Deal(1, 2, 135f));
        var quarter = BigTypeFolderMotion.Deal(0, 0, 120f);
        var e = BigTypeMotion.Land(0.25f);
        Assert.Equal(160f * (1f - e), quarter.Y, 3);
        Assert.Equal(0.55f + (0.45f * e), quarter.Scale, 4);
        Assert.Equal(BigTypeTilePose.Rest, BigTypeFolderMotion.Deal(0, 0, 480f));
        // At most eight steps.
        Assert.Equal(BigTypeTilePose.Rest, BigTypeFolderMotion.Deal(12, 2, (8 * 45f) + 480f));
        Assert.Equal(370f + 230f, BigTypeFolderMotion.OpenRingAt());
        Assert.Equal(370f + (8 * 45f) + 480f, BigTypeFolderMotion.OpenTotal());
        Assert.Equal((1.3f, 0f), BigTypeFolderMotion.RingPop(0f));
        Assert.Equal((1f, 1f), BigTypeFolderMotion.RingPop(320f));
        Assert.Equal((1.3f, 0f), BigTypeMotion.Pop(0f, 1.3f));
    }

    [Fact]
    public void Close_TheGamesDrop_HomePopsBackInAroundTheFolder_TheRingLands()
    {
        // The top-left game drops last: five steps short of staggerFrom, 80 ms.
        Assert.Equal(0f, BigTypeFolderMotion.Drop(0, 0, 79f).Progress);
        Assert.True(BigTypeFolderMotion.Drop(0, 0, 81f).Progress > 0f);
        // A game five steps out goes at once.
        Assert.True(BigTypeFolderMotion.Drop(3, 2, 1f).Progress > 0f);
        var gone = BigTypeFolderMotion.Drop(0, 0, 80f + 200f);
        Assert.Equal((1f, 0f, 120f, 0.6f), (gone.Progress, gone.ToX, gone.ToY, gone.ToScale));
        Assert.False(BigTypeFolderMotion.FolderGone(299f));
        Assert.True(BigTypeFolderMotion.FolderGone(300f));
        // Home's tiles pop in from 0.45, nearest first, 30 ms a cell.
        Assert.Equal(new BigTypeTilePose(0f, 0f, 0.45f, 0f), BigTypeFolderMotion.PopIn(0, 0, 0f));
        Assert.Equal(new BigTypeTilePose(0f, 0f, 0.45f, 0f), BigTypeFolderMotion.PopIn(2, 0, 60f));
        Assert.True(BigTypeFolderMotion.PopIn(2, 0, 61f).Opacity > 0f);
        Assert.Equal(BigTypeTilePose.Rest, BigTypeFolderMotion.PopIn(0, 0, 440f));
        Assert.Equal(300f + 130f, BigTypeFolderMotion.CloseRingAt());
    }

    [Fact]
    public void Launch_TheTileFlies_TheCaseBumps_TheColourTakesOver_TheTitleLands()
    {
        // The tile, its top 200 px down the Launcher, ends 540 px up (out through the hinge) at 1.25.
        Assert.Equal((0f, 1f), BigTypeLaunchMotion.Flight(0f, 200f));
        Assert.Equal((-540f, 1.25f), BigTypeLaunchMotion.Flight(340f, 200f));
        Assert.False(BigTypeLaunchMotion.Flown(339f));
        Assert.True(BigTypeLaunchMotion.Flown(340f));
        // The case bumps from 270 ms: at its middle 1.16 and up 20 px × 1.16; at its end at rest.
        Assert.Equal((1f, 0f), BigTypeLaunchMotion.CaseBump(270f));
        var peak = Enumerable.Range(0, 421).Select(i => BigTypeLaunchMotion.CaseBump(270f + i)).MaxBy(b => b.Scale);
        Assert.Equal(1.16f, peak.Scale, 2);
        Assert.Equal(20f * 1.16f, peak.Rise, 1);
        Assert.Equal((1f, 0f), BigTypeLaunchMotion.CaseBump(690f));
        // The circle: nothing until 430 ms, all of the Stage by 1070.
        Assert.Equal(0f, BigTypeLaunchMotion.TakeoverRadiusAt(430f));
        Assert.InRange(BigTypeLaunchMotion.TakeoverRadiusAt(750f), 1f, 2199f);
        Assert.Equal(2200f, BigTypeLaunchMotion.TakeoverRadiusAt(1070f));
        Assert.True(BigTypeLaunchMotion.TakenOver(1070f));
        // The title's letters land on HeadingIn from 0.8 em, starting with the takeover.
        Assert.Equal((0.8f, 6f, 0f), BigTypeLaunchMotion.TitleLetter(0, 430f + 60f));
        Assert.Equal((0f, 0f, 1f), BigTypeLaunchMotion.TitleLetter(0, 430f + 60f + 520f));
        Assert.Equal(224f, BigTypeLaunchMotion.TitleSize("Okami"));
        Assert.Equal(160f, BigTypeLaunchMotion.TitleSize("Final Fantasy VII"));
        Assert.Equal(128f, BigTypeLaunchMotion.TitleSize("Metal Gear Solid : The Twin Snakes"));
        // The grid goes over 280 ms from the takeover.
        Assert.Equal(1f, BigTypeLaunchMotion.GridOpacity(430f));
        Assert.Equal(0f, BigTypeLaunchMotion.GridOpacity(710f));
    }

    [Fact]
    public void Companion_PartsArriveAndLeave_TabsSlide_TheBarStretches_ItRests_AnErrorShakes()
    {
        Assert.Equal((80f, 0f), BigTypeLaunchMotion.PartIn(2, 110f));
        Assert.Equal((0f, 1f), BigTypeLaunchMotion.PartIn(6, (6 * 55f) + 460f));
        Assert.Equal(6 * 55f + 460f, BigTypeLaunchMotion.PartsInTotal(7));
        Assert.Equal((0f, 1f), BigTypeLaunchMotion.PartOut(3, 54f));
        Assert.Equal((60f, 0f), BigTypeLaunchMotion.PartOut(3, 54f + 200f));
        Assert.Equal(1f, BigTypeLaunchMotion.LaunchFade(0f));
        Assert.Equal(0.5f, BigTypeLaunchMotion.LaunchFade(160f), 3);
        Assert.Equal(0f, BigTypeLaunchMotion.LaunchFade(320f));
        // A tab moved to the right comes in from the right.
        Assert.Equal((120f, 0f), BigTypeLaunchMotion.TabIn(1, 0f));
        Assert.Equal((-120f, 0f), BigTypeLaunchMotion.TabIn(-1, 0f));
        Assert.Equal((0f, 1f), BigTypeLaunchMotion.TabIn(1, 420f));
        // The bar overshoots on its curve and lands on the share.
        Assert.True(Enumerable.Range(0, 800).Any(i => BigTypeLaunchMotion.Bar(0.2f, 0.4f, i) > 0.4f));
        Assert.Equal(0.4f, BigTypeLaunchMotion.Bar(0.2f, 0.4f, 800f));
        Assert.Equal(0.28f, BigTypeLaunchMotion.RestOpacity(1f, true, 1200f, 0.28f));
        Assert.Equal(1f, BigTypeLaunchMotion.RestOpacity(0.28f, false, 1200f, 0.28f));
        // The shake: -22, +18, -12, 0 at quarters of 380 ms.
        Assert.Equal(-22f, BigTypeLaunchMotion.ShakeAt(95f), 2);
        Assert.Equal(18f, BigTypeLaunchMotion.ShakeAt(190f), 2);
        Assert.Equal(-12f, BigTypeLaunchMotion.ShakeAt(285f), 2);
        Assert.Equal(0f, BigTypeLaunchMotion.ShakeAt(380f));
    }

    [Fact]
    public void HeldStill_EverythingIsWhereItGoes_NothingWaits()
    {
        Assert.Equal(1f, BigTypeFolderMotion.FallAway(1, 0, false, 0f, holdStill: true).Progress);
        Assert.True(BigTypeFolderMotion.HomeGone(0f, holdStill: true));
        Assert.Equal(BigTypeTilePose.Rest, BigTypeFolderMotion.Deal(3, 2, -500f, holdStill: true));
        Assert.Equal(BigTypeTilePose.Rest, BigTypeFolderMotion.PopIn(3, 2, -500f, holdStill: true));
        Assert.Equal(0f, BigTypeFolderMotion.OpenRingAt(true));
        Assert.Equal((1f, 1f), BigTypeFolderMotion.RingPop(0f, true));
        Assert.True(BigTypeLaunchMotion.Flown(0f, true));
        Assert.Equal(2200f, BigTypeLaunchMotion.TakeoverRadiusAt(0f, true));
        Assert.Equal((0f, 0f, 1f), BigTypeLaunchMotion.TitleLetter(0, 0f, true));
        Assert.Equal((1f, 0f), BigTypeLaunchMotion.CaseBump(400f, true));
        Assert.Equal((0f, 1f), BigTypeLaunchMotion.PartIn(5, 0f, true));
        Assert.Equal((0f, 1f), BigTypeLaunchMotion.TabIn(1, 0f, true));
        Assert.Equal(0f, BigTypeLaunchMotion.ShakeAt(95f, true));
    }
}

public partial class HandheldShellViewModelTests
{
    /// <summary>
    /// M47 B8 in the Big Type look: A on a game plays (through the Title page's route, never drawn), a missing file does not launch and
    /// says so on the Stage's line, a console set to ask shows "Which screen?" as a sheet, the launch's status comes from the app's own
    /// steps, and the companion's in-game layout sits where the mock puts it. Switching the look is process-wide, so these never run beside
    /// another test.
    /// </summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypePlay
    {
        private static async Task<(FakeLibrary Library, HandheldShellViewModel Shell, HandheldGameEntry Game)> InFolderAsync(
            HandheldTitleDetails? details = null, bool available = true)
        {
            var library = new FakeLibrary().WithPlatform("gc", 3);
            library.Games["gc"][0] = library.Games["gc"][0] with { IsAvailable = available };
            var game = library.Games["gc"][0];
            library.Details[game.Id] = details ?? Details();
            var shell = await CreateAsync(library);
            Press(shell, GamepadAction.Confirm); // open the folder
            for (var i = 0; i < 100 && shell.OpenFolder is not { IsLoaded: true }; i++)
                await Task.Delay(5);
            return (library, shell, game);
        }

        private static async Task WaitFor(Func<bool> done)
        {
            for (var i = 0; i < 200 && !done(); i++)
                await Task.Delay(5);
        }

        [Fact]
        public async Task A_OnAGame_Plays_WithNoTitlePageDrawn_AndTheStatusFollowsTheLaunch()
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                var (library, shell, game) = await InFolderAsync();
                var launch = new TaskCompletionSource<bool>();
                library.PendingLaunch = launch;
                Press(shell, GamepadAction.Confirm);
                await WaitFor(() => shell.Playing is not null);

                Assert.True(shell.IsTitleOpen);
                Assert.False(shell.ShowsTitlePage);
                Assert.Equal(HandheldTitleState.Launching, shell.TitlePage!.State);
                var play = shell.Playing!;
                Assert.Equal(game.Title, play.Title);
                Assert.Equal("Starting Dolphin…", play.Status);
                Assert.Equal(game.Title, shell.Stage.Name); // the Stage stays on the game, not a hero page
                Assert.Empty(shell.Hints);

                shell.SetLaunchSaveSync(true);
                Assert.Equal(HandheldPlayViewModel.SyncingText, play.Status);
                shell.SetLaunchSaveSync(false);
                launch.SetResult(true);
                await WaitFor(() => play.IsRunning);
                Assert.Equal("Running in Dolphin", play.Status);
                Assert.Equal([game.Id], library.Launched);
                Assert.Equal([GameLaunchScreen.BuiltIn], library.LaunchScreens);
                Assert.False(shell.IsTitleOpen);

                // The takeover floods the Launcher to the in-game colour; the shelf comes back after the game.
                await WaitFor(() => play.IsTakenOver);
                Assert.Equal(play.InGameColour, shell.LauncherGround);
                await shell.EndPlayAsync();
                Assert.Null(shell.Playing);
                Assert.Equal(play.Colour, shell.LauncherGround);
                Assert.Equal(shell.LauncherGround, shell.MiddleFloodGround);
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        [Fact]
        public async Task A_OnAMissingGame_DoesNotLaunch_TheLineSaysSoAndShakes()
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                var (library, shell, _) = await InFolderAsync(available: false);
                var shakes = shell.LineShakes;
                Press(shell, GamepadAction.Confirm);
                Assert.Null(shell.TitlePage);
                Assert.Null(shell.Playing);
                Assert.Empty(library.Launched);
                Assert.Equal(HandheldShellViewModel.MissingFileText, shell.Stage.Line);
                Assert.Equal(shakes + 1, shell.LineShakes);
                // A move gives the line back to what the focus says.
                Press(shell, GamepadAction.NavigateDown);
                Assert.NotEqual(HandheldShellViewModel.MissingFileText, shell.Stage.Line);
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        [Fact]
        public async Task AConsoleThatAsks_ShowsWhichScreenAsASheet_ThePickLaunchesThere_BDropsTheLaunch()
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                var (library, shell, game) = await InFolderAsync(Details(LaunchScreenDecision.Prompt));
                Press(shell, GamepadAction.Confirm);
                await WaitFor(() => shell.Sheet is not null);
                var sheet = shell.Sheet!;
                Assert.Equal("Which screen?", sheet.Title);
                Assert.Equal("Play", sheet.Kicker);
                Assert.Equal(["Top screen", "Bottom screen", "Always the top for gc", "Always the bottom for gc"], sheet.Items.Select(item => item.Label));
                Assert.False(shell.ShowsTitlePage);
                // B closes the sheet and the launch with it.
                Press(shell, GamepadAction.Cancel);
                Assert.Null(shell.Sheet);
                Assert.Null(shell.TitlePage);
                Assert.Empty(library.Launched);

                // Again: the bottom screen.
                Press(shell, GamepadAction.Confirm);
                await WaitFor(() => shell.Sheet is not null);
                Press(shell, GamepadAction.NavigateDown);
                Press(shell, GamepadAction.Confirm);
                await WaitFor(() => library.Launched.Count > 0);
                Assert.Equal([game.Id], library.Launched);
                Assert.Equal([GameLaunchScreen.External], library.LaunchScreens);
                Assert.Empty(library.Remembered);
                await shell.EndPlayAsync();

                // "Always the top" remembers it for the console.
                Press(shell, GamepadAction.Confirm);
                await WaitFor(() => shell.Sheet is not null);
                Press(shell, GamepadAction.NavigateDown, 2);
                Press(shell, GamepadAction.Confirm);
                await WaitFor(() => library.Launched.Count > 1);
                Assert.Equal([("gc", GameLaunchScreen.BuiltIn)], library.Remembered);
                await shell.EndPlayAsync();
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        [Fact]
        public async Task TodaysLook_StillOpensTheTitlePage()
        {
            var (library, shell, _) = await InFolderAsync();
            Press(shell, GamepadAction.Confirm);
            Assert.True(shell.IsTitleOpen);
            Assert.True(shell.ShowsTitlePage);
            Assert.Null(shell.Playing);
            Assert.Empty(library.Launched);
        }

        [AvaloniaFact]
        public void TheLaunchTitle_TakesItsSizeFromItsLength_AndCentresItsLines()
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                var title = new HandheldBigTypeLaunchTitle { Text = "Metal Gear Solid : The Twin Snakes" };
                var host = new UserControl { Content = title };
                HandheldTokens.ApplyFace(host);
                var window = new Window { Content = host, Width = 1920, Height = 1080 };
                window.Show();
                var fitted = title.Layout!;
                Assert.Equal(128, fitted.Size);
                Assert.True(fitted.Width <= HandheldTokens.LaunchTitleMaxWidth);
                // Lines are 0.92 of the size apart; every letter's line is one of them.
                var lines = fitted.Letters.Select(letter => Math.Round(letter.Centre.Y, 2)).Distinct().ToList();
                Assert.True(lines.Count >= 1);
                Assert.Equal(lines.Count * 0.92 * 128, fitted.Height, 3);
                Assert.Equal("MetalGearSolid:TheTwinSnakes".Length, fitted.Letters.Count);
                var atlas = title.Letters(new Point(100, 200))!;
                Assert.Equal(fitted.Letters.Count, atlas.Letters.Count);
                Assert.Equal(128f, atlas.Size);
                window.Close();
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        [Fact]
        public void TheInGameLayout_IsTokensJs()
        {
            var tokens = File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));
            // The in-game names are looked up in tokens.js's ingame block first (the Stage has a LabelTop of its own).
            var ingame = tokens[tokens.IndexOf("ingame: {", StringComparison.Ordinal)..];
            ingame = ingame[..ingame.IndexOf("\n  },", StringComparison.Ordinal)];
            double N(string name)
            {
                var match = Regex.Match(ingame, $@"\b{name}:\s*(?<value>-?[0-9.]+)");
                if (!match.Success)
                    match = Regex.Match(tokens, $@"\b{name}:\s*(?<value>-?[0-9.]+)");
                Assert.True(match.Success, $"tokens.js has no {name}");
                return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            }
            double Size(string role) => double.Parse(Regex.Match(tokens, $@"\b{role}:\s*\{{\s*size:\s*(?<v>[0-9.]+)").Groups["v"].Value, CultureInfo.InvariantCulture);
            foreach (var (name, value) in new (string, double)[]
            {
                ("KickerLeft", HandheldTokens.InGameKickerLeft), ("KickerTop", HandheldTokens.InGameKickerTop),
                ("TitleLeft", HandheldTokens.InGameTitleLeft), ("TitleTop", HandheldTokens.InGameTitleTop), ("TitleMaxWidth", HandheldTokens.InGameTitleMaxWidth),
                ("TabsRight", HandheldTokens.InGameTabsRight), ("TabsTop", HandheldTokens.InGameTabsTop), ("TabsGap", HandheldTokens.InGameTabsGap),
                ("TimerLeft", HandheldTokens.InGameTimerLeft), ("TimerTop", HandheldTokens.InGameTimerTop),
                ("LabelLeft", HandheldTokens.InGameLabelLeft), ("LabelTop", HandheldTokens.InGameLabelTop),
                ("StatsInset", HandheldTokens.InGameStatsInset), ("StatsTop", HandheldTokens.InGameStatsTop), ("StatGap", HandheldTokens.InGameStatGap),
                ("FigureLeft", HandheldTokens.InGameFigureLeft), ("FigureTop", HandheldTokens.InGameFigureTop), ("FigureLabelTop", HandheldTokens.InGameFigureLabelTop),
                ("RecentInset", HandheldTokens.InGameRecentInset), ("RecentTop", HandheldTokens.InGameRecentTop), ("RecentPitch", HandheldTokens.InGameRecentPitch),
                ("ShotLeft", HandheldTokens.InGameShotLeft), ("ShotTop", HandheldTokens.InGameShotTop), ("ShotPitchX", HandheldTokens.InGameShotPitchX),
                ("ShotPitchY", HandheldTokens.InGameShotPitchY), ("ShotWidth", HandheldTokens.InGameShotWidth), ("ShotHeight", HandheldTokens.InGameShotHeight),
                ("ShotRadius", HandheldTokens.InGameShotRadius), ("ShotsLabelTop", HandheldTokens.InGameShotsLabelTop),
                ("BarInset", HandheldTokens.InGameBarInset), ("BarTop", HandheldTokens.InGameBarTop), ("BarRadius", HandheldTokens.InGameBarRadius),
                ("BarTrackOpacity", HandheldTokens.InGameBarTrackOpacity),
                ("LaunchTitleMaxWidth", HandheldTokens.LaunchTitleMaxWidth), ("LaunchTitleLineHeight", HandheldTokens.LaunchTitleLineHeight),
                ("LaunchGap", HandheldTokens.LaunchGap), ("InGameMix", HandheldTokens.InGameMix), ("InGameRestOpacity", HandheldTokens.InGameRestOpacity),
            })
            {
                Assert.True(N(name) == value, $"{name}: tokens.js {N(name)}, HandheldTokens {value}");
            }
            Assert.Equal(Size("SessionTimer"), HandheldTokens.SessionTimer);
            Assert.Equal(Size("InGameFigure"), HandheldTokens.InGameFigure);
            Assert.Equal(Size("InGameStat"), HandheldTokens.InGameStat);
            Assert.Equal(Size("InGameLabel"), HandheldTokens.InGameLabel);
            Assert.Equal(Size("InGameTitle"), HandheldTokens.InGameTitle);
            Assert.Equal(Size("InGameTab"), HandheldTokens.InGameTab);
            Assert.Equal(Size("InGameStatLabel"), HandheldTokens.InGameStatLabel);
            Assert.Equal(Size("InGameRecent"), HandheldTokens.InGameRecent);
            Assert.Equal(Size("InGameRecentWhen"), HandheldTokens.InGameRecentWhen);
            Assert.Equal(Size("LaunchStatus"), HandheldTokens.LaunchStatus);
            // The in-game colour: the console's mixed 64% toward #0B0A18 (PS2's #2B5BC7).
            var ps2InGame = HandheldTokens.InGameGround(Avalonia.Media.Color.Parse("#2B5BC7"));
            Assert.Equal(Avalonia.Media.Color.FromRgb(23, 39, 87), ps2InGame);
        }

        /// <summary>
        /// The mock sets the companion's lines at line-height 1.15 and clips none: descenders draw below the line box (found by the
        /// orchestrating session in B8's frames, where a TextBlock's own clip cut p, y and g flat). Each line is rendered and must have
        /// ink below its box, the title too when it shrinks to fit.
        /// </summary>
        [AvaloniaFact]
        public void TheCompanionsLines_ShowTheirDescenders_PastTheirLineBox()
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                var model = new SecondScreenViewModel();
                var companion = new HandheldCompanionViewModel(model) { HoldsStill = true };
                companion.SetNowPlaying(new HandheldGameEntry(1, "Mary-Kate and Ashley: Pocket Planner, the Typography Quest", "ps1", "PlayStation",
                    null, 3, TimeSpan.FromHours(13), true), "PlayStation");
                model.IsGameRunning = true;
                var view = new HandheldCompanionView(false) { DataContext = companion };
                var window = new Window { Content = view, Width = 1240, Height = 1080 };
                window.Show();
                Assert.True(companion.StepTab(1) && companion.StepTab(1)); // Photos: "No photos of this game yet"
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var body = view.FindControl<Control>("BtBody")!;
                foreach (var text in body.GetVisualDescendants().OfType<TextBlock>())
                    Assert.False(text.ClipToBounds, $"\"{text.Text}\" clips to its line box");

                using var frame = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(1240, 1080));
                frame.Render(window);
                var pixels = new byte[1240 * 1080 * 4];
                var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                frame.CopyPixels(new PixelRect(0, 0, 1240, 1080), handle.AddrOfPinnedObject(), pixels.Length, 1240 * 4);
                handle.Free();
                // White type (some at 0.75) on the dark ground: ink in the rows just below a line's box, across its width.
                bool InkBelow(Control line)
                {
                    var topLeft = line.TranslatePoint(default, window)!.Value;
                    var bottomRight = line.TranslatePoint(new Point(line.Bounds.Width, line.Bounds.Height), window)!.Value;
                    for (var y = (int)Math.Ceiling(bottomRight.Y) + 1; y < Math.Min(1080, (int)bottomRight.Y + 12); y++)
                    {
                        for (var x = (int)topLeft.X; x < Math.Min(1240, (int)bottomRight.X); x++)
                        {
                            var i = ((y * 1240) + x) * 4;
                            if (pixels[i] > 150 && pixels[i + 1] > 150 && pixels[i + 2] > 150)
                                return true;
                        }
                    }
                    return false;
                }
                var kicker = view.FindControl<Control>("BtKicker")!;
                var title = view.FindControl<Viewbox>("BtTitle")!.GetVisualDescendants().OfType<TextBlock>().Single();
                var photos = view.FindControl<Control>("BtPhotos")!.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text!.Contains("photos"));
                Assert.True(InkBelow(kicker), "Now playing's descenders are cut");
                Assert.True(InkBelow(title), "the shrunk title's descenders are cut");
                Assert.True(InkBelow(photos), "the photos label's descenders are cut");
                window.Close();
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }

        [AvaloniaFact]
        public void TheCompanion_SitsWhereTheMockPutsIt()
        {
            HandheldTokens.Use(HandheldLook.BigType);
            try
            {
                var model = new SecondScreenViewModel();
                var companion = new HandheldCompanionViewModel(model) { HoldsStill = true };
                companion.SetNowPlaying(new HandheldGameEntry(1, "Okami", "ps2", "PlayStation 2", null, 3, TimeSpan.FromHours(13), true), "PlayStation 2");
                model.IsGameRunning = true;
                var view = new HandheldCompanionView(false) { DataContext = companion };
                var window = new Window { Content = view, Width = 1240, Height = 1080 };
                window.Show();
                Assert.True(companion.IsSessionTab);
                Assert.True(companion.ShowsBigTypeBody);
                Assert.False(companion.ShowsClassicBody);
                var canvas = view.FindControl<Grid>("Canvas")!;
                Point At(string name) => view.FindControl<Control>(name)!.TranslatePoint(default, canvas)!.Value;
                Assert.Equal(new Point(80, 60), At("BtKicker"));
                Assert.Equal(new Point(80, 108), At("BtTitle"));
                Assert.Equal(1240 - 80, At("BtTabs").X + view.FindControl<Control>("BtTabs")!.Bounds.Width, 1);
                Assert.Equal("0:00", companion.SessionTime);
                // Left and right move along the tabs; past either end nothing changes.
                Assert.False(companion.StepTab(-1));
                Assert.True(companion.StepTab(1));
                Assert.True(companion.IsAchievementsTab);
                Assert.True(companion.StepTab(1));
                Assert.True(companion.IsPhotosTab);
                Assert.False(companion.StepTab(1));
                window.Close();
            }
            finally
            {
                HandheldTokens.Use(HandheldLook.Classic);
            }
        }
    }
}
