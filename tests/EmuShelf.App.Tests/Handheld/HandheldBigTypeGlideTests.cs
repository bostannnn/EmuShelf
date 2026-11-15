using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EmuShelf.Rendering.Stage.BigType;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

/// <summary>
/// M47 B11: the ring under held and rapid input, as maths at fixed moments. A single press is the mock's move to the byte; a move
/// that arrives before the last one has finished glides on from where the ring is and how fast it is going, never jumps, never
/// stretches over a ring already stretched, and never falls further behind the focus however long a direction is held.
/// </summary>
public class HandheldBigTypeGlideTests
{
    // Home's tiles: 176 px on a 212 px pitch, the ring 26 out from each (228 across), radius 50.
    private const float Pitch = 212f;
    private static BigTypeRing Tile(int column, int row = 0) => new(82 + (column * Pitch), 18 + (row * Pitch), 228, 228, 50);

    // The repeat a held direction makes on the Thor (GamepadNavigationController): 320 ms, then 90 ms ramping to 38 over 8 repeats.
    private static IEnumerable<float> HeldPresses(int count, float floor = 38f)
    {
        var t = 0f;
        yield return t;
        t += 320f;
        for (var i = 0; i < count - 1; i++)
        {
            yield return t;
            var share = Math.Min(1f, (i + 1) / 8f);
            t += 90f + ((floor - 90f) * share);
        }
    }

    [Fact]
    public void TheLag_IsTokensJs()
    {
        var tokens = File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));
        var lag = Regex.Match(tokens, @"RingGlide:\s*\{\s*lagMs:\s*(?<v>[0-9.]+)");
        Assert.True(lag.Success, "tokens.js has no motion.RingGlide");
        Assert.Equal(double.Parse(lag.Groups["v"].Value, CultureInfo.InvariantCulture), BigTypeRingGlide.LagMilliseconds);
        Assert.Equal(BigTypeMotion.RingMilliseconds, BigTypeRingGlide.Milliseconds);
    }

    [Fact]
    public void ASinglePress_IsTheMocksMove_ToTheByte()
    {
        var motion = new BigTypeLauncherMotion();
        motion.MoveRing(0f, instant: true, pop: false);
        motion.Ring(Tile(0), 0f, out _, out _);
        motion.Ring(Tile(0), 500f, out _, out _);
        motion.MoveRing(1000f, instant: false, pop: false);
        for (var t = 0f; t <= 300f; t += 2.5f)
        {
            var drawn = motion.Ring(Tile(1), 1000f + t, out _, out var moving);
            Assert.Equal(BigTypeMotion.RingAt(Tile(0), Tile(1), t / 300f), drawn);
            Assert.Equal(t < 300f, moving);
        }
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.25f)]
    [InlineData(0.38f)]
    [InlineData(0.5f)]
    [InlineData(0.74f)]
    [InlineData(0.9f)]
    public void AMoveInterrupted_GoesOnFromWhereItIs_AndNeverStretchesPastTheTwoTiles(float at)
    {
        var motion = new BigTypeLauncherMotion();
        motion.MoveRing(0f, instant: true, pop: false);
        motion.Ring(Tile(0), 0f, out _, out _);
        motion.MoveRing(100f, instant: false, pop: false);
        var cut = 100f + (at * 300f);
        BigTypeRing last = default;
        for (var t = 100f; t < cut; t += 1f)
            last = motion.Ring(Tile(1), t, out _, out _);
        var before = motion.Ring(Tile(1), cut, out _, out _);
        var speedBefore = before.CentreX - last.CentreX;

        motion.MoveRing(cut, instant: false, pop: false);
        var after = motion.Ring(Tile(2), cut, out _, out var moving);
        Assert.True(moving);
        // No jump: the same ring at the moment the move arrives.
        AssertClose(before, after, 0.01f);
        // No restart from rest: the middle goes on at the speed it had (within what one ms of easing changes).
        var next = motion.Ring(Tile(2), cut + 1f, out _, out _);
        Assert.InRange(next.CentreX - after.CentreX, speedBefore - 0.6f, speedBefore + 0.6f);

        // Never longer than the ring it was and never longer than the two tiles it is between; never the old union's union.
        var bound = Math.Max(before.Width, BigTypeMotion.Union(Tile(1), Tile(2)).Width);
        for (var t = cut; t <= cut + 320f; t += 2f)
        {
            var ring = motion.Ring(Tile(2), t, out _, out _);
            Assert.True(ring.Width <= bound + 0.01f, $"interrupted at {at}: {ring.Width} wide at {t - cut} ms (bound {bound})");
            Assert.True(ring.X >= Math.Min(before.X, Tile(1).X) - 0.5f, $"interrupted at {at}: it reaches back past the tile it left ({ring.X})");
        }
        // It lands on the new target 300 ms after the move, at rest.
        Assert.Equal(Tile(2), motion.Ring(Tile(2), cut + 300f, out _, out moving));
        Assert.False(moving);
    }

    [Theory]
    [InlineData(38f)]
    [InlineData(72f)]
    public void AHeldDirection_StaysOneRingWide_AndNeverFallsFurtherBehind(float floor)
    {
        var motion = new BigTypeLauncherMotion();
        motion.MoveRing(-1000f, instant: true, pop: false);
        motion.Ring(Tile(0), -1000f, out _, out _);
        var presses = HeldPresses(40, floor).ToList();
        var gaps = new List<float>();
        var widest = 0f;
        var column = 0;
        for (var i = 0; i < presses.Count; i++)
        {
            if (i > 0)
            {
                column++;
                motion.MoveRing(presses[i], instant: false, pop: false);
            }
            var end = i + 1 < presses.Count ? presses[i + 1] : presses[i] + 400f;
            var worst = 0f;
            for (var t = presses[i]; t < end; t += 1f)
            {
                var ring = motion.Ring(Tile(column), t, out _, out _);
                widest = Math.Max(widest, ring.Width);
                worst = Math.Max(worst, Math.Abs(Tile(column).CentreX - ring.CentreX));
            }
            gaps.Add(worst);
        }
        // Bounded: never more than the two tiles it is between (the first press's mock stretch is exactly that).
        Assert.True(widest <= BigTypeMotion.Union(Tile(0), Tile(1)).Width + 0.01f, $"the ring grew to {widest}");
        // From its first glide on it keeps its own size within a few px once the hold is under way.
        // Not falling behind: the gap over the last ten presses is no larger than over ten presses at the same pace earlier.
        var settled = gaps.Skip(15).Take(10).Max();
        var late = gaps.Skip(gaps.Count - 11).Take(10).Max();
        Assert.True(late <= settled + 0.5f, $"the ring falls behind: {settled} then {late}");
        Assert.True(late <= 1.6f * Pitch, $"the ring trails by {late} px at a {floor} ms repeat");
        // After the hold it lands on the last tile.
        Assert.Equal(Tile(column), motion.Ring(Tile(column), presses[^1] + 300f, out _, out var moving));
        Assert.False(moving);
    }

    [Fact]
    public void WhenTheHoldEnds_ItSettlesWithTheMocksSquash()
    {
        var motion = new BigTypeLauncherMotion();
        motion.MoveRing(0f, instant: true, pop: false);
        motion.Ring(Tile(0), 0f, out _, out _);
        var presses = new[] { 100f, 160f, 220f, 280f };
        for (var i = 0; i < presses.Length; i++)
        {
            motion.MoveRing(presses[i], instant: false, pop: false);
            for (var t = presses[i]; t < (i + 1 < presses.Length ? presses[i + 1] : presses[i] + 1f); t += 1f)
                motion.Ring(Tile(i + 1), t, out _, out _);
        }
        var last = presses[^1];
        var target = Tile(presses.Length);
        // Deepest at 74%: shorter along the travel, longer across it, as the mock's single move lands.
        var deepest = motion.Ring(target, last + (0.74f * 300f), out _, out _);
        Assert.True(deepest.Height > target.Height + 6f, $"no squash across ({deepest.Height})");
        Assert.True(deepest.Width < target.Width - 6f, $"no squash along ({deepest.Width})");
        Assert.Equal(1f, BigTypeRingGlide.SquashWeight(0.74f), 3);
        Assert.Equal(0f, BigTypeRingGlide.SquashWeight(0.38f));
        Assert.Equal(0f, BigTypeRingGlide.SquashWeight(1f));
        // While the direction is held (the next move inside 38%) there is never a squash.
        Assert.Equal(0f, BigTypeRingGlide.SquashWeight(90f / 300f));
    }

    [Fact]
    public void AGlide_OntoAnotherPage_StillPops_AndHeldStillItLands()
    {
        var motion = new BigTypeLauncherMotion();
        motion.MoveRing(0f, instant: true, pop: false);
        motion.Ring(Tile(0), 0f, out _, out _);
        motion.MoveRing(100f, instant: false, pop: false);
        motion.Ring(Tile(1), 150f, out _, out _);
        motion.MoveRing(160f, instant: false, pop: false);
        motion.Ring(Tile(2), 170f, out _, out _);
        motion.MoveRing(180f, instant: false, pop: true);
        var popped = motion.Ring(Tile(0, 1), 180f, out var opacity, out _);
        Assert.Equal(Tile(0, 1).Scaled(1.25f), popped);
        Assert.Equal(0f, opacity);

        // A move while it pops in glides from the ring as drawn (its pop's size easing away), never stretched over it.
        var mid = motion.Ring(Tile(0, 1), 260f, out _, out _);
        motion.MoveRing(260f, instant: false, pop: false);
        Assert.Equal(mid, motion.Ring(Tile(1, 1), 260f, out _, out _));
        for (var t = 260f; t <= 560f; t += 5f)
            Assert.True(motion.Ring(Tile(1, 1), t, out _, out _).Width <= mid.Width + 0.01f);
        Assert.Equal(Tile(1, 1), motion.Ring(Tile(1, 1), 560f, out _, out _));

        var still = new BigTypeLauncherMotion { HoldStill = true };
        still.MoveRing(0f, instant: true, pop: false);
        still.Ring(Tile(0), 0f, out _, out _);
        still.MoveRing(10f, instant: false, pop: false);
        Assert.Equal(Tile(1), still.Ring(Tile(1), 10f, out _, out var moving));
        Assert.False(moving);
    }

    // ---- The name in a run ----

    [Fact]
    public void TheRunsNumbers_AreTokensJs()
    {
        var tokens = File.ReadAllText(Path.Combine(HandheldGroundColourTests.RepositoryRoot(), "docs", "prototypes", "handheld-big-type", "tokens.js"));
        double Field(string field) => double.Parse(Regex.Match(tokens, $@"LetterRun:\s*\{{[^}}]*\b{field}:\s*(?<v>[0-9.]+)").Groups["v"].Value, CultureInfo.InvariantCulture);
        Assert.Equal(Field("withinMs"), BigTypeMotion.RunWithinMilliseconds);
        Assert.Equal(Field("ms"), BigTypeMotion.RunInMilliseconds);
        Assert.Equal(Field("fromShare"), BigTypeMotion.RunFromShare, 3);
        Assert.Equal(Field("outMs"), BigTypeMotion.RunOutMilliseconds);
    }

    [Fact]
    public void AHeldDirection_NeverQueuesAFullLanding_PerTile()
    {
        // A press, then the Thor's held repeat: the first two names (320 ms apart) land as the mock's; every one after is in a run.
        var presses = HeldPresses(20).ToList();
        var runs = presses.Zip(presses.Skip(1), (a, b) => BigTypeMotion.InRun(b - a)).ToList();
        Assert.False(runs[0]);
        Assert.All(runs.Skip(1), Assert.True);
        // Quick taps are a run; a deliberate pace is not.
        Assert.True(BigTypeMotion.InRun(150f));
        Assert.False(BigTypeMotion.InRun(300f));

        // In a run every letter of the name comes together, a short way, and is in place well before the next repeat at 90 ms ends the
        // ones after it; a full landing would take the whole name 930 ms.
        var start = BigTypeMotion.ArriveInRun(BigTypeEntrance.Right, 0f);
        Assert.Equal(BigTypeMotion.LetterInFromEm * BigTypeMotion.RunFromShare, start.X, 4);
        Assert.Equal(0f, start.Opacity);
        Assert.Equal(BigTypeLetterPose.Rest, BigTypeMotion.ArriveInRun(BigTypeEntrance.Right, BigTypeMotion.RunInMilliseconds));
        Assert.True(BigTypeMotion.ArriveInRun(BigTypeEntrance.Right, 90f).Opacity > 0.9f);
        Assert.True(BigTypeMotion.ArrivalMilliseconds(30) > 900f);
        // The name it replaces goes in 90 ms, all together, the short way; held still, as the mock's reduced motion.
        Assert.Equal(1f, BigTypeMotion.DepartInRun(BigTypeEntrance.Right, 0f)!.Value.Opacity);
        Assert.Null(BigTypeMotion.DepartInRun(BigTypeEntrance.Right, BigTypeMotion.RunOutMilliseconds));
        Assert.Equal(BigTypeLetterPose.Rest, BigTypeMotion.ArriveInRun(BigTypeEntrance.Right, 0f, holdStill: true));
        Assert.Equal(0f, BigTypeMotion.RunArrivalMilliseconds(holdStill: true));
    }

    private static void AssertClose(BigTypeRing expected, BigTypeRing actual, float tolerance)
    {
        Assert.InRange(actual.X, expected.X - tolerance, expected.X + tolerance);
        Assert.InRange(actual.Y, expected.Y - tolerance, expected.Y + tolerance);
        Assert.InRange(actual.Width, expected.Width - tolerance, expected.Width + tolerance);
        Assert.InRange(actual.Height, expected.Height - tolerance, expected.Height + tolerance);
        Assert.InRange(actual.Radius, expected.Radius - tolerance, expected.Radius + tolerance);
    }
}

public partial class HandheldShellViewModelTests
{
    /// <summary>M47 B11: the shell says when a press came within a run of the one before; a single press, and the first after a pause, never.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async System.Threading.Tasks.Task InputRun_IsAHeldDirectionOrQuickTaps_NeverASinglePress()
    {
        var shell = await CreateAsync(new FakeLibrary().WithPlatform("ps2", 3).WithPlatform("gc", 3).WithPlatform("wii", 3));
        Assert.False(shell.InputRun);
        Press(shell, EmuShelf.App.Services.GamepadAction.NavigateRight);
        Assert.False(shell.InputRun);
        Press(shell, EmuShelf.App.Services.GamepadAction.NavigateRight);
        Assert.True(shell.InputRun);
        // A held direction: the Stage waits, and the name it settles on is never the short form.
        Assert.False(shell.QuickChange);
        await System.Threading.Tasks.Task.Delay((int)BigTypeMotion.RunWithinMilliseconds + 60);
        Press(shell, EmuShelf.App.Services.GamepadAction.NavigateLeft);
        Assert.False(shell.InputRun);
        Assert.False(shell.QuickChange);
        // Quick taps, slower than a held direction (150 ms) but inside a run: the Stage follows each, in the short form.
        await System.Threading.Tasks.Task.Delay(170);
        Assert.True(shell.DispatchGamepadAction(EmuShelf.App.Services.GamepadAction.NavigateRight));
        Assert.True(shell.InputRun);
        Assert.False(shell.IsStagePending);
        Assert.True(shell.QuickChange);
        shell.Dispose();
    }
}
