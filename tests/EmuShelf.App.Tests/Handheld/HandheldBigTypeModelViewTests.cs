using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using EmuShelf.App.Rendering;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Handheld;
using EmuShelf.Rendering.Stage;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B12, the model view (proposals.html, approved 2026-11-15): its maths at fixed moments (rising through the hinge, the showcase's
/// full turn, the flip between games, the stick's hold and the swing back, held still, resting after a minute), its numbers held to
/// tokens.js, and the shell's rule: LB and RB switch a focused game (in a folder or pinned to home) between the name and the model;
/// anything else on home keeps its page turns.
/// </summary>
public class HandheldBigTypeModelMotionTests
{
    private static readonly (float X, float Y, float Scale) Off = BigTypeModelMotion.OffPlace(1476, 512, 960, 492);

    private static string TokensJs() =>
        File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));

    private static double Entry(string tokens, string name, string field)
    {
        var match = Regex.Match(tokens, $@"\b{name}:\s*\{{[^}}]*\b{field}:\s*(?<value>-?[0-9.]+)");
        Assert.True(match.Success, $"tokens.js has no {name}.{field}");
        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    private static double Number(string tokens, string name)
    {
        var match = Regex.Match(tokens, $@"\b{name}:\s*(?<value>-?[0-9.]+)");
        Assert.True(match.Success, $"tokens.js has no {name}");
        return double.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TheNumbers_AreTokensJs()
    {
        var t = TokensJs();
        (string Name, double Value)[] stage =
        [
            ("ModelRight", BigTypeModelMotion.ModelRight), ("ModelTop", BigTypeModelMotion.ModelTop), ("ModelScale", BigTypeModelMotion.ModelScale),
            ("ModelFloorLeft", BigTypeModelMotion.FloorLeft), ("ModelFloorRight", BigTypeModelMotion.FloorRight),
            ("ModelFloorBottom", BigTypeModelMotion.FloorBottom), ("ModelFloorHeight", BigTypeModelMotion.FloorHeight),
            ("ModelFloorBlur", BigTypeModelMotion.FloorBlur), ("ModelMaxWidth", BigTypeModelMotion.MaxWidth),
        ];
        foreach (var (name, value) in stage)
            Assert.True(Math.Abs(Number(t, name) - value) < 1e-4, $"{name}: tokens.js {Number(t, name)}, the app {value}");
        (string Name, string Field, double Value)[] motion =
        [
            ("ModelIn", "ms", BigTypeModelMotion.InMilliseconds), ("ModelIn", "fadeMs", BigTypeModelMotion.InFadeMilliseconds),
            ("ModelIn", "drop", BigTypeModelMotion.InDrop), ("ModelIn", "fromScale", BigTypeModelMotion.InFromScale),
            ("ModelIn", "turnDeg", BigTypeModelMotion.InTurn), ("ModelIn", "turnMs", BigTypeModelMotion.InTurnMilliseconds),
            ("ModelFlip", "ms", BigTypeModelMotion.FlipMilliseconds), ("ModelFlip", "turnDeg", BigTypeModelMotion.FlipTurn),
            ("ModelFloat", "msEach", BigTypeModelMotion.FloatEachMilliseconds), ("ModelFloat", "from", BigTypeModelMotion.FloatFrom),
            ("ModelFloat", "to", BigTypeModelMotion.FloatTo), ("ModelFloat", "shadeScale", BigTypeModelMotion.ShadeScale),
            ("ModelFloat", "shadeOpacity", BigTypeModelMotion.ShadeOpacity),
            ("ModelShowcase", "periodMs", BigTypeModelMotion.ShowcaseMilliseconds), ("ModelShowcase", "pitchDeg", BigTypeModelMotion.RestPitch),
            ("ModelShowcase", "yawDeg", BigTypeModelMotion.RestYaw), ("ModelShowcase", "driftAt", BigTypeModelMotion.DriftAt),
            ("ModelShowcase", "holdPitchDeg", BigTypeModelMotion.HoldPitch), ("ModelShowcase", "holdYawDeg", BigTypeModelMotion.HoldYaw),
            ("ModelShowcase", "turnAt", BigTypeModelMotion.TurnAt), ("ModelSwing", "ms", BigTypeModelMotion.SwingMilliseconds),
            ("HintKey", "padding", BigTypeModelMotion.HintKeyPadding), ("HintKey", "radius", BigTypeModelMotion.HintKeyRadius),
        ];
        foreach (var (name, field, value) in motion)
            Assert.True(Math.Abs(Entry(t, name, field) - value) < 1e-4, $"{name}.{field}: tokens.js {Entry(t, name, field)}, the app {value}");
        Assert.Contains("ModelFloorColour: 'rgba(8,8,20,0.3)'", t);
        Assert.Contains("easing: 'cubic-bezier(.2, 1.4, .3, 1)'", t);
        Assert.Contains("turnEasing: 'cubic-bezier(.2, 1.3, .3, 1)'", t);
        Assert.Contains("driftEasing: 'cubic-bezier(.45, 0, .3, 1)', turnEasing: 'cubic-bezier(.5, 0, .25, 1)'", t);
        // Each new entry says where it comes from.
        Assert.Equal(3, Regex.Matches(t, "proposals.html, approved 2026-11-15|proposals.html \\(approved 2026-11-15").Count);
    }

    [Fact]
    public void TheModelsBox_IsTheProposals_DoubledAndCentred()
    {
        // The proposal's .case.big: right 382, top 108 on its 960 x 540 Stage, a 196 x 276 box: its middle is the Stage's middle across.
        Assert.Equal(382 * 2, BigTypeModelMotion.ModelRight);
        Assert.Equal(108 * 2, BigTypeModelMotion.ModelTop);
        var (x, y) = BigTypeModelMotion.Centre(1920, 552);
        Assert.Equal(1920 - BigTypeModelMotion.ModelRight - (392 / 2f), x);
        Assert.Equal(492, y);
    }

    [Fact]
    public void OnTheNarrowStage_TheModelIsCentred_BetweenTheClockAndTheLines()
    {
        // M47 B11's narrow Stage: 1240 wide, the hints on their own row, the lines raised a hint row (96 px).
        Assert.Equal(189, Number(TokensJs(), "NarrowModelTop"));
        Assert.Equal(0.96, Number(TokensJs(), "NarrowModelScale"), 3);
        var (x, y) = BigTypeModelMotion.Centre(1240, 552);
        Assert.Equal(620, x);
        var s = BigTypeModelMotion.ScaleFor(1240);
        Assert.Equal(BigTypeModelMotion.NarrowModelScale, s);
        Assert.Equal(BigTypeModelMotion.ModelScale, BigTypeModelMotion.ScaleFor(1920));
        var top = y - (552 * s / 2) + (BigTypeModelMotion.FloatTo * s);    // the float's highest
        var bottom = y + (552 * s / 2) + (BigTypeModelMotion.FloatFrom * s); // and lowest
        // Under the clock (its line box ends 48 + 92 × 1.15 from the top) and above the line (raised 96: 132 + 96 from the bottom, 42 × 1.15 tall).
        Assert.True(top > 48 + (92 * 1.15), $"the model's top {top} reaches the clock");
        var lineTop = 1080 - (132 + 96) - (42 * 1.15);
        Assert.True(bottom < lineTop, $"the model's bottom {bottom} reaches the line at {lineTop}");
        // Its shadow's middle stays above the line too.
        var rect = BigTypeModelMotion.FloorRect(392, 552);
        var shadow = y - (552 * s / 2) + ((rect.Y + (rect.Height / 2)) * s);
        Assert.True(shadow < lineTop, $"the shadow's middle {shadow} reaches the line");
    }

    [Fact]
    public void AQuickRunOfPresses_FlipsShort_AndNeverStacks()
    {
        var model = new BigTypeModelState();
        model.Apply(BigTypeModelChange.Rise, 0, holdStill: false, animate: false);
        // The first change of a run is quick only if it came within the run of the one before; here every press is quick.
        model.Apply(BigTypeModelChange.FlipForward, 1000, holdStill: false, quick: true);
        Assert.Equal(BigTypeModelMotion.FlipTurn * BigTypeMotion.RunFromShare, model.Pose(1000, false, Off).Kick, 2);
        Assert.Equal(0f, model.Pose(1000 + BigTypeMotion.RunInMilliseconds, false, Off).Kick);
        // Ten presses 90 ms apart (a fast tapper): the turn never goes past a short flip's, never stacks into whole turns.
        var now = 2000f;
        var widest = 0f;
        for (var i = 0; i < 10; i++, now += 90f)
        {
            model.Apply(BigTypeModelChange.FlipForward, now, holdStill: false, quick: true);
            for (var t = 0f; t < 90f; t += 10f)
                widest = Math.Max(widest, Math.Abs(model.Pose(now + t, false, Off).Kick));
        }
        Assert.True(widest <= BigTypeModelMotion.FlipTurn * BigTypeMotion.RunFromShare + 0.01f, $"the turn reached {widest}");
        // Turning back mid-run carries the turn on screen, never a jump back to a whole flip.
        var onScreen = model.Pose(now, false, Off).Kick;
        model.Apply(BigTypeModelChange.FlipBack, now, holdStill: false, quick: true);
        Assert.InRange(Math.Abs(model.Pose(now, false, Off).Kick), 0f, BigTypeModelMotion.FlipTurn * BigTypeMotion.RunFromShare + 0.01f);
        Assert.True(Math.Abs(onScreen) <= 72.01f);
        // The press a run rests on (not quick) is the whole flip, from what is on screen.
        model.Apply(BigTypeModelChange.FlipForward, now + 5000, holdStill: false);
        Assert.Equal(BigTypeModelMotion.FlipTurn, model.Pose(now + 5000, false, Off).Kick, 2);
    }

    [Fact]
    public void Rising_ComesFromBelowTheHinge_TurningIn_AndLands()
    {
        var model = new BigTypeModelState();
        model.Apply(BigTypeModelChange.Rise, 1000, holdStill: false);
        var start = model.Pose(1000, false, Off);
        // From its old place beside the name, 380 px lower, at 0.8 of its size there, clear, turned -250 degrees on top of the showcase.
        Assert.Equal(1476 - 960, start.OffsetX, 2);
        Assert.Equal(512 - 492 + 380, start.OffsetY, 2);
        Assert.Equal(0.8f / 1.36f, start.Scale, 3);
        Assert.Equal(0f, start.Opacity);
        Assert.Equal(-250f, start.Kick, 2);
        Assert.Equal(BigTypeModelMotion.RestYaw - 250f, start.Yaw, 2);
        // Whole after the fade (250 ms); still turning; in place by 550 ms, the turn done by 950.
        Assert.Equal(1f, model.Pose(1250, false, Off).Opacity);
        var mid = model.Pose(1300, false, Off);
        Assert.InRange(mid.OffsetY, -60f, 200f);
        Assert.NotEqual(0f, mid.Kick);
        var placed = model.Pose(1550, false, Off);
        Assert.Equal(0f, placed.OffsetX);
        Assert.Equal(0f, placed.OffsetY);
        Assert.Equal(1f, placed.Scale);
        Assert.Equal(0f, model.Pose(1950, false, Off).Kick);
        Assert.Equal("rise", model.Motion(1100, false));
        Assert.True(model.IsMoving(1100, false));
    }

    [Fact]
    public void Leaving_DropsOutTheWayItCame_FromWhereItGotTo()
    {
        var model = new BigTypeModelState();
        model.Apply(BigTypeModelChange.Rise, 0, holdStill: false);
        // Left mid-rise: it goes back from where it was, never jumping to its resting place first.
        var before = model.Pose(200, false, Off);
        model.Apply(BigTypeModelChange.Leave, 200, holdStill: false);
        var after = model.Pose(200, false, Off);
        Assert.Equal(before.OffsetY, after.OffsetY, 2);
        Assert.Equal(before.Opacity, after.Opacity, 3);
        Assert.False(model.IsOn);
        Assert.True(model.Pose(460, false, Off).Opacity <= 0f);
        Assert.True(model.IsGone(800, false));
    }

    [Fact]
    public void TheShowcase_DriftsHoldsAndTurnsOnceAllTheWayRound_Every15Seconds()
    {
        Assert.Equal((4f, 32f), BigTypeModelMotion.Showcase(0));
        Assert.Equal((2f, 14f), BigTypeModelMotion.Showcase(5400));
        Assert.Equal((2f, 14f), BigTypeModelMotion.Showcase(7400));
        var turning = BigTypeModelMotion.Showcase(11250);
        Assert.InRange(turning.Yaw, -328f, 14f);
        // The end of the turn is a whole turn on from where it began: the back and the spine have been seen, and it is at rest again.
        var end = BigTypeModelMotion.Showcase(14999.9f);
        Assert.Equal(32f - 360f, end.Yaw, 0);
        Assert.Equal((4f, 32f), BigTypeModelMotion.Showcase(15000));
        // The float: down 6, up to -12, 4.6 s each way; the shadow shrinks and fades with it.
        Assert.Equal(6f, BigTypeModelMotion.Float(0));
        Assert.Equal(-12f, BigTypeModelMotion.Float(4600), 3);
        Assert.Equal(6f, BigTypeModelMotion.Float(9200), 3);
        var shade = BigTypeModelMotion.Shade(4600);
        Assert.Equal(0.9f, shade.Scale, 4);
        Assert.Equal(0.7f, shade.Opacity, 4);
    }

    [Fact]
    public void ChangingGame_IsOneFlip_TheWayYouMoved_AndCarriesOnMidFlip()
    {
        var model = new BigTypeModelState();
        model.Apply(BigTypeModelChange.Rise, 0, holdStill: false, animate: false);
        model.Apply(BigTypeModelChange.FlipForward, 2000, holdStill: false);
        Assert.Equal(180f, model.Pose(2000, false, Off).Kick, 2);
        Assert.Equal(0f, model.Pose(2720, false, Off).Kick);
        model.Apply(BigTypeModelChange.FlipBack, 3000, holdStill: false);
        Assert.Equal(-180f, model.Pose(3000, false, Off).Kick, 2);
        // Another press mid-flip carries on from the turn on screen: half a turn more, never back to rest first.
        var onScreen = model.Pose(3200, false, Off).Kick;
        model.Apply(BigTypeModelChange.FlipBack, 3200, holdStill: false);
        Assert.Equal(BigTypeModelMotion.FlipFrom(onScreen, forward: false), model.Pose(3200, false, Off).Kick, 2);
        Assert.InRange(BigTypeModelMotion.FlipFrom(-300, forward: false), -360f, 0f);
        Assert.Equal("flip", model.Motion(3300, false));
    }

    [Fact]
    public void TheStick_TakesHoldOfThePoseOnScreen_AndLettingGoSwingsBack()
    {
        var model = new BigTypeModelState();
        model.Apply(BigTypeModelChange.Rise, 0, holdStill: false, animate: false);
        var onScreen = model.Pose(5400, false, Off);
        model.Hold(0f, 0f, 5400);
        var held = model.Pose(5400, false, Off);
        Assert.Equal(onScreen.Yaw, held.Yaw, 2);
        Assert.Equal(onScreen.Pitch, held.Pitch, 2);
        model.Hold(10f, 120f, 5600);
        Assert.Equal(onScreen.Yaw + 120f, model.Pose(5600, false, Off).Yaw, 2);
        Assert.Equal("turn", model.Motion(5600, false));
        model.Release(6000);
        var let = model.Pose(6000, false, Off);
        Assert.Equal(BigTypeModelMotion.NearRest(onScreen.Yaw + 120f), let.Yaw, 2);
        Assert.Equal("swing", model.Motion(6300, false));
        var back = model.Pose(6620, false, Off);
        Assert.Equal(BigTypeModelMotion.RestYaw, back.Yaw, 2);
        Assert.Equal(BigTypeModelMotion.RestPitch, back.Pitch, 2);
        // The showcase starts again from rest after the swing.
        Assert.Equal(BigTypeModelMotion.Showcase(1000).Yaw, model.Pose(7620, false, Off).Yaw, 2);
        // It swings back the short way: never more than half a turn.
        Assert.InRange(BigTypeModelMotion.NearRest(32f + 350f) - 32f, -180f, 180f);
    }

    [Fact]
    public void HeldStill_ItStandsAtRest_NoFloatNoTurn_ChangesAtOnce_TheStickStillTurnsIt()
    {
        var model = new BigTypeModelState();
        model.Apply(BigTypeModelChange.Rise, 100, holdStill: true);
        var pose = model.Pose(100, true, Off);
        Assert.Equal((0f, 0f, 1f, 1f), (pose.OffsetX, pose.OffsetY, pose.Scale, pose.Opacity));
        Assert.Equal((BigTypeModelMotion.RestPitch, BigTypeModelMotion.RestYaw), (pose.Pitch, pose.Yaw));
        Assert.Equal(0f, pose.Bob);
        Assert.Equal(pose, model.Pose(8000, true, Off) with { });
        Assert.False(model.IsMoving(150, true));
        model.Apply(BigTypeModelChange.FlipForward, 200, holdStill: true);
        Assert.Equal(0f, model.Pose(200, true, Off).Kick);
        model.Hold(0f, 45f, 300, holdStill: true);
        Assert.Equal(BigTypeModelMotion.RestYaw + 45f, model.Pose(300, true, Off).Yaw, 2);
        model.Release(400);
        Assert.Equal(BigTypeModelMotion.RestYaw, model.Pose(400, true, Off).Yaw, 2);
        model.Apply(BigTypeModelChange.Leave, 500, holdStill: true);
        Assert.True(model.IsGone(500, true));
    }

    [Fact]
    public void AMinuteAlone_TheShowcaseStopsAtRest_AndTheFloatEasesOut_APressWakesIt()
    {
        var model = new BigTypeModelState();
        model.Apply(BigTypeModelChange.Rise, 0, holdStill: false, animate: false);
        // The first turn to end a minute or more after the last touch is the last: 60 s is exactly the end of the fourth.
        Assert.True(model.IsMoving(59_000, false));
        var rested = model.Pose(60_000, false, Off);
        Assert.Equal((BigTypeModelMotion.RestPitch, BigTypeModelMotion.RestYaw), (rested.Pitch, rested.Yaw));
        Assert.True(model.IsMoving(61_000, false));
        Assert.False(model.IsMoving(62_100, false));
        Assert.Equal(0f, model.Pose(63_000, false, Off).Bob);
        model.Touch(70_000);
        Assert.True(model.IsMoving(70_100, false));
        Assert.Equal((BigTypeModelMotion.RestPitch, BigTypeModelMotion.RestYaw), (model.Pose(70_000, false, Off).Pitch, model.Pose(70_000, false, Off).Yaw));
    }

    [Fact]
    public void TheFloorsShadow_IsTheProposalsEllipse_Blurred()
    {
        var rect = BigTypeModelMotion.FloorRect(392, 552);
        // From 4% outside the box's left to 10% outside its right, its bottom 56 under the box's, 44 high, with room for the blur.
        Assert.Equal(-0.04f * 392 - 60f, rect.X, 2);
        Assert.Equal((1.14f * 392) + 120f, rect.Width, 2);
        Assert.Equal(552 + 56 - 44 - 60f, rect.Y, 2);
        var pixels = BigTypeModelMotion.Floor(392, 552);
        var middle = ((pixels.Height / 2 * pixels.Width) + (pixels.Width / 2)) * 4;
        // Darkest in its middle (the colour at about its 30%), nothing at the edge of the room.
        Assert.InRange(pixels.Data[middle + 3], 60, 77);
        Assert.InRange(pixels.Data[3], (byte)0, (byte)2);
        Assert.InRange(pixels.Data[middle + 2], (20 * pixels.Data[middle + 3] / 255) - 1, (20 * pixels.Data[middle + 3] / 255) + 1);
    }
}

public partial class HandheldShellViewModelTests
{
    /// <summary>
    /// M47 B12: LB and RB on a focused game (in a folder, or pinned to home: the owner, 2026-11-15, "Switch the view") switch the name
    /// view and the model view; anything else on home keeps its page turns, and the d-pad at the grid's edge still turns the page.
    /// </summary>
    [Collection(nameof(HandheldTokenLookCollection))]
    public class BigTypeModelView
    {
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

        private static async Task<HandheldShellViewModel> InFolderAsync(FakeLibrary library)
        {
            var shell = await CreateAsync(library);
            Press(shell, GamepadAction.Confirm);
            for (var i = 0; i < 200 && shell.OpenFolder is not { IsLoaded: true }; i++)
                await Task.Delay(5);
            shell.SettleStage();
            Assert.Equal(HandheldStageKind.Game, shell.Stage.Kind);
            return shell;
        }

        [AvaloniaFact]
        public Task RB_InAFolder_SwitchesToTheModel_AndBack() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("ps2", 6).WithPlatform("gc", 3);
            using var shell = await InFolderAsync(library);
            Assert.Empty(library.StageCoverRequests);
            var title = shell.Stage.Name;
            Assert.True(shell.CanSwitchGameView);
            Assert.False(shell.Stage.IsModelView);
            Assert.True(shell.Stage.ShowsBigTypeName);
            // The name has the whole box now: no medium beside it.
            Assert.Equal(HandheldTokens.StageNameWidth, shell.Stage.NameBoxWidth);
            Assert.False(shell.Stage.HasBigTypeCover);
            Assert.False(shell.Stage.HasBigTypeMedium);
            Assert.Equal(["A:Play", "Y:More", "B:Back", "RB:Model"], shell.Hints.Select(h => $"{h.Button}:{h.Label}"));

            Press(shell, GamepadAction.NextPlatform);
            Assert.True(shell.Stage.IsModelView);
            Assert.Equal(shell.Stage.GameId, Assert.Single(library.StageCoverRequests));
            Assert.False(shell.Stage.ShowsBigTypeName);
            Assert.True(shell.Stage.HasModelCover); // the test library's games have no authored medium: the flat cover stands in
            Assert.Equal(title, shell.Stage.Line);
            Assert.StartsWith("Not played yet", shell.Stage.Facts);
            Assert.Equal(["A:Play", "B:Back", "RB:Name"], shell.Hints.Select(h => $"{h.Button}:{h.Label}"));
            Assert.True(shell.Hints.Single(h => h.Button == "RB").IsWide);

            // LB does the same.
            Press(shell, GamepadAction.PreviousPlatform);
            Assert.False(shell.Stage.IsModelView);
            Assert.Single(library.StageCoverRequests);
            Assert.Equal(title, shell.Stage.Name);
            Assert.NotEqual(title, shell.Stage.Line);
            Assert.Null(shell.LastMoveDirection);
        });

        [AvaloniaFact]
        public Task TheChosenView_IsKeptAcrossGamesAndFolders_AFolderTileOnHomeShowsTheName() => BigType(async () =>
        {
            using var shell = await InFolderAsync(new FakeLibrary().WithPlatform("ps2", 6).WithPlatform("gc", 3));
            Press(shell, GamepadAction.NextPlatform);
            Press(shell, GamepadAction.NavigateRight);
            Assert.True(shell.Stage.IsModelView);
            Assert.Equal(shell.OpenFolder!.SelectedTile!.Entry.Title, shell.Stage.Line);
            Assert.Equal(shell.OpenFolder.SelectedTile.Entry.Id, shell.Stage.GameId);
            // The header is not a game: the model goes and LB / RB do nothing new there.
            Press(shell, GamepadAction.NavigateUp, 3);
            Assert.Equal(HandheldZone.Header, shell.Zone);
            Assert.False(shell.Stage.IsModelView);
            Assert.False(shell.CanSwitchGameView);
            Press(shell, GamepadAction.NavigateDown);
            Assert.True(shell.Stage.IsModelView);

            // Home, on the folder's tile (not a game): the name view; LB / RB turn the page as before (one page here: they bump).
            Press(shell, GamepadAction.Cancel);
            Assert.Null(shell.OpenFolder);
            Assert.False(shell.Stage.IsModelView);
            Assert.False(shell.CanSwitchGameView);
            var bumped = 0;
            shell.Bumped += _ => bumped++;
            var page = shell.HomePosition.Page;
            Press(shell, GamepadAction.NextPlatform);
            Assert.True(bumped == 1 || shell.HomePosition.Page == page + 1);
            Assert.False(shell.Stage.IsModelView);
            if (shell.HomePosition.Page != page)
                Press(shell, GamepadAction.PreviousPlatform);
            Assert.Equal(page, shell.HomePosition.Page);

            // Back into a folder (another one): the model again.
            Press(shell, GamepadAction.NavigateRight);
            Press(shell, GamepadAction.Confirm);
            for (var i = 0; i < 200 && shell.OpenFolder is not { IsLoaded: true }; i++)
                await Task.Delay(5);
            shell.SettleStage();
            Assert.True(shell.Stage.IsModelView);
        });

        [AvaloniaFact]
        public Task OverTheModel_ASheetOrTheMenu_ShowsTheName_AndClosingBringsTheModelBack() => BigType(async () =>
        {
            using var shell = await InFolderAsync(new FakeLibrary().WithPlatform("ps2", 6));
            Press(shell, GamepadAction.NextPlatform);
            Press(shell, GamepadAction.Actions);
            Assert.NotNull(shell.Sheet);
            Assert.False(shell.Stage.IsModelView);
            Assert.False(shell.CanSwitchGameView);
            Press(shell, GamepadAction.Cancel);
            Assert.Null(shell.Sheet);
            Assert.True(shell.Stage.IsModelView);
            Press(shell, GamepadAction.Menu);
            Assert.False(shell.Stage.IsModelView);
            Press(shell, GamepadAction.Cancel);
            Assert.True(shell.Stage.IsModelView);
        });

        // Two pages of home: a folder, then a game pinned at the first row's right edge, the rest empty, and one slot on page two.
        private static async Task<(HandheldShellViewModel Shell, FakeLibrary Library)> HomeWithPinnedGameAsync()
        {
            var library = new FakeLibrary().WithPlatform("ps2", 6);
            var pinned = library.Games["ps2"][2];
            var slots = Enumerable.Repeat(HandheldSlot.Empty, 21).ToArray();
            slots[0] = HandheldSlot.PlatformFolder("ps2");
            slots[4] = HandheldSlot.Game(pinned.Id);
            var store = new InMemoryHandheldHomeLayoutStore();
            store.Save(HandheldHomeLayout.FromSlots(slots));
            var shell = await CreateAsync(library, store);
            Assert.Equal(2, shell.PageCount);
            Press(shell, GamepadAction.NavigateRight, 4);
            Assert.Equal(HandheldStageKind.Game, shell.Stage.Kind);
            Assert.Equal(pinned.Title, shell.Stage.Name);
            return (shell, library);
        }

        [AvaloniaFact]
        public Task Home_APinnedGame_SwitchesTheView_AndDoesNotTurnThePage() => BigType(async () =>
        {
            var (shell, _) = await HomeWithPinnedGameAsync();
            using var _ = shell;
            Assert.True(shell.CanSwitchGameView);
            // B does nothing on home, so it has no hint there.
            Assert.Equal(["A:Play", "Y:More", "RB:Model"], shell.Hints.Select(h => $"{h.Button}:{h.Label}"));
            var page = shell.HomePosition.Page;
            var bumped = 0;
            shell.Bumped += _ => bumped++;
            Press(shell, GamepadAction.NextPlatform);
            Assert.True(shell.Stage.IsModelView);
            Assert.Equal(page, shell.HomePosition.Page);
            Assert.Equal(0, bumped);
            Assert.Equal(["A:Play", "RB:Name"], shell.Hints.Select(h => $"{h.Button}:{h.Label}"));
            Press(shell, GamepadAction.PreviousPlatform);
            Assert.False(shell.Stage.IsModelView);
            Assert.Equal(page, shell.HomePosition.Page);
        });

        [AvaloniaFact]
        public Task Home_AnyOtherTile_TurnsThePage_AndDoesNotSwitchTheView() => BigType(async () =>
        {
            var (shell, _) = await HomeWithPinnedGameAsync();
            using var _ = shell;
            Press(shell, GamepadAction.NavigateLeft); // an empty slot
            Assert.False(shell.CanSwitchGameView);
            Assert.DoesNotContain(shell.Hints, h => h.Button == "RB");
            Press(shell, GamepadAction.NextPlatform);
            Assert.Equal(1, shell.HomePosition.Page);
            Assert.False(shell.IsModelViewChosen);
            Press(shell, GamepadAction.PreviousPlatform);
            Assert.Equal(0, shell.HomePosition.Page);
            // The folder too.
            Press(shell, GamepadAction.NavigateLeft, 3);
            Assert.False(shell.CanSwitchGameView);
            Press(shell, GamepadAction.NextPlatform);
            Assert.Equal(1, shell.HomePosition.Page);
        });

        [AvaloniaFact]
        public Task Home_TheDpadAtTheEdge_StillTurnsThePage_FromAPinnedGame() => BigType(async () =>
        {
            var (shell, _) = await HomeWithPinnedGameAsync();
            using var _ = shell;
            Press(shell, GamepadAction.NextPlatform);
            Assert.True(shell.Stage.IsModelView);
            Press(shell, GamepadAction.NavigateRight);
            Assert.Equal(1, shell.HomePosition.Page);
            // Not a game there: the model steps aside; back to the game, it shows again.
            Assert.False(shell.Stage.IsModelView);
            Press(shell, GamepadAction.NavigateLeft);
            Assert.Equal(0, shell.HomePosition.Page);
            Assert.True(shell.Stage.IsModelView);
        });

        [AvaloniaFact]
        public Task Home_FromAPinnedGamesModel_ToAFolderTileAndBack_TheModelReturns_AndCarriesIntoFolders() => BigType(async () =>
        {
            var (shell, library) = await HomeWithPinnedGameAsync();
            using var _ = shell;
            Press(shell, GamepadAction.NextPlatform);
            Assert.True(shell.Stage.IsModelView);
            Press(shell, GamepadAction.NavigateLeft, 4);
            Assert.Equal(HandheldStageKind.Folder, shell.Stage.Kind);
            Assert.False(shell.Stage.IsModelView);
            Assert.True(shell.Stage.ShowsBigTypeName);
            Press(shell, GamepadAction.NavigateRight, 4);
            Assert.True(shell.Stage.IsModelView);
            Assert.Equal(library.Games["ps2"][2].Title, shell.Stage.Line);
            // The choice goes into a folder too.
            Press(shell, GamepadAction.NavigateLeft, 4);
            Press(shell, GamepadAction.Confirm);
            for (var i = 0; i < 200 && shell.OpenFolder is not { IsLoaded: true }; i++)
                await Task.Delay(5);
            shell.SettleStage();
            Assert.True(shell.Stage.IsModelView);
        });

        [AvaloniaFact]
        public Task TheIdleDrift_DoesNotStartOverTheModel() => BigType(async () =>
        {
            using var shell = await InFolderAsync(new FakeLibrary().WithPlatform("ps2", 6));
            Press(shell, GamepadAction.NextPlatform);
            Assert.True(shell.Stage.IsModelView);
            Assert.False(shell.CanDriftForTest);
            Press(shell, GamepadAction.PreviousPlatform);
            Assert.False(shell.Stage.IsModelView);
        });

        [AvaloniaFact]
        public Task AMissingFile_InTheModel_SaysSoOnTheSecondLine() => BigType(async () =>
        {
            var library = new FakeLibrary().WithPlatform("ps2", 3);
            library.Games["ps2"][0] = library.Games["ps2"][0] with { IsAvailable = false };
            using var shell = await InFolderAsync(library);
            Assert.Equal("File not found", shell.Stage.Line);
            Press(shell, GamepadAction.NextPlatform);
            Assert.Equal(library.Games["ps2"][0].Title, shell.Stage.Line);
            Assert.Equal("File not found", shell.Stage.Facts);
            Assert.True(shell.Stage.IsMissing);
        });

        [AvaloniaFact]
        public Task TheStick_OnTheModel_SaysHowFarItTurned_AndLettingGoReleases() => BigType(async () =>
        {
            using var shell = await InFolderAsync(new FakeLibrary().WithPlatform("ps2", 3));
            Press(shell, GamepadAction.NextPlatform);
            // A flat cover cannot be turned: the stick does nothing to it.
            shell.ApplyRightStick(1f, 0f, 16);
            Assert.False(shell.Stage.ModelHold.Held);
            // A 3D medium (the test library's games have none of their own) can.
            shell.Stage.Medium = MediaShellMap.ForSystem("snes");
            Assert.True(shell.Stage.HasModelMedium);
            var version = shell.Stage.ModelHoldVersion;
            shell.ApplyRightStick(1f, 0f, 16);
            Assert.True(shell.Stage.ModelHold.Held);
            Assert.NotEqual(0f, shell.Stage.ModelHold.Yaw);
            Assert.True(shell.Stage.ModelHoldVersion > version);
            shell.ApplyRightStick(0f, 0f, 16);
            Assert.False(shell.Stage.ModelHold.Held);
            // The old sway's clock does not run for the model.
            Assert.False(shell.MediumMotion.IsSettling);
        });

        [AvaloniaFact]
        public async Task Classic_LBAndRBInAFolder_DoNothing()
        {
            using var shell = await InFolderAsync(new FakeLibrary().WithPlatform("ps2", 6));
            Assert.False(shell.CanSwitchGameView);
            var hints = shell.Hints.Select(h => $"{h.Button}:{h.Label}").ToList();
            Press(shell, GamepadAction.NextPlatform);
            Assert.False(shell.Stage.IsModelView);
            Assert.False(shell.IsModelViewChosen);
            Assert.Equal(hints, shell.Hints.Select(h => $"{h.Button}:{h.Label}"));
            Assert.DoesNotContain(shell.Hints, h => h.Button == "RB");
        }
    }
}
