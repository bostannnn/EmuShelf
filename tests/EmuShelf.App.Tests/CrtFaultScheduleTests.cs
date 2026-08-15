using EmuShelf.Rendering;

namespace EmuShelf.App.Tests;

/// <summary>
/// The tube's fault schedule, which is on the CPU precisely so that it can be checked here.
/// </summary>
/// <remarks>
/// The first version hashed the schedule out of <c>uTime</c> inside the fragment shader, where none
/// of this is observable. Reproducing it off-GPU to work out when to screenshot each fault gave
/// times that were near misses — GLSL's sine agrees with a host language's to a few digits and the
/// hash multiplies the disagreement by forty thousand — so four of the eight faults were reviewed on
/// frames where they were not happening, and the amplitudes were then "tuned" against nothing.
/// </remarks>
public class CrtFaultScheduleTests
{
    private const float Period = 14f;
    private const float Strength = 0.85f;

    [Fact]
    public void NothingHappensWhenTheEffectIsOff()
    {
        for (var t = 0f; t < 300f; t += 0.05f)
        {
            Assert.Equal(CrtFault.None, CrtFaultSchedule.Sample(t, Period, strength: 0f).Fault);
        }
    }

    [Fact]
    public void EveryFaultTypeFiresWithinAReasonableWait()
    {
        var seen = new HashSet<CrtFault>();
        for (var t = 0f; t < 60f * Period; t += 0.02f)
        {
            var state = CrtFaultSchedule.Sample(t, Period, Strength);
            if (state.Fault != CrtFault.None)
            {
                seen.Add(state.Fault);
            }
        }

        var expected = Enum.GetValues<CrtFault>().Where(fault => fault != CrtFault.None);
        Assert.Equal(expected.OrderBy(fault => fault), seen.OrderBy(fault => fault));
    }

    /// <summary>
    /// The reason the hash is integer arithmetic rather than a sine: over eight buckets a sine's
    /// correlations showed badly, firing one fault twenty-nine times per cycle and another eight.
    /// </summary>
    [Fact]
    public void FaultTypesAreSpreadEvenlyAndDoNotRepeatInLongRuns()
    {
        var perWindow = Enumerable.Range(0, 512)
            .Select(window => CrtFaultSchedule.Sample(
                ((window + 0.5f) * Period) + 0.0001f, Period, Strength))
            .ToList();

        var counts = Enumerable.Range(0, 512)
            .Select(window => WindowFault(window))
            .GroupBy(fault => fault)
            .ToDictionary(group => group.Key, group => group.Count());

        Assert.Equal(8, counts.Count);
        var spread = counts.Values.Max() - counts.Values.Min();
        Assert.True(spread < 512 / 8 * 0.6, $"fault types are unevenly distributed; spread {spread}");

        var longestRun = 1;
        var run = 1;
        for (var window = 1; window < 512; window++)
        {
            run = WindowFault(window) == WindowFault(window - 1) ? run + 1 : 1;
            longestRun = Math.Max(longestRun, run);
        }

        Assert.True(longestRun <= 3, $"the same fault repeated {longestRun} windows in a row");
    }

    [Fact]
    public void OneFaultFiresPerWindowAndClearsBeforeTheNext()
    {
        for (var window = 0; window < 40; window++)
        {
            var faults = new List<CrtFault>();
            var wasFaulting = false;
            var bursts = 0;

            for (var step = 0; step < 2000; step++)
            {
                var t = (window * Period) + (step * Period / 2000f);
                var state = CrtFaultSchedule.Sample(t, Period, Strength);
                var faulting = state.Fault != CrtFault.None;
                if (faulting && !wasFaulting)
                {
                    bursts++;
                    faults.Add(state.Fault);
                }

                Assert.InRange(state.Amount, 0f, Strength);
                wasFaulting = faulting;
            }

            Assert.Equal(1, bursts);
            // And it is over by the time the window is: the onset is capped at 0.8 of a window and
            // the longest span is 1.1s, which has to fit in whatever is left.
            Assert.Equal(
                CrtFault.None,
                CrtFaultSchedule.Sample(((window + 1) * Period) - 0.001f, Period, Strength).Fault);
        }
    }

    [Fact]
    public void TheSameSecondAlwaysGivesTheSameFault()
    {
        foreach (var t in new[] { 0.5f, 9.75f, 41.2f, 128.03f, 999.9f })
        {
            var first = CrtFaultSchedule.Sample(t, Period, Strength);
            var second = CrtFaultSchedule.Sample(t, Period, Strength);
            Assert.Equal(first, second);
        }
    }

    /// <summary>The seed a fault uses to place itself holds still for that fault's whole duration.</summary>
    [Fact]
    public void TheSeedIsStableAcrossOneFault()
    {
        var window = FindWindowWithFault();
        float? seed = null;
        for (var step = 0; step < 4000; step++)
        {
            var t = (window * Period) + (step * Period / 4000f);
            var state = CrtFaultSchedule.Sample(t, Period, Strength);
            if (state.Fault == CrtFault.None)
            {
                continue;
            }

            seed ??= state.Seed;
            Assert.Equal(seed.Value, state.Seed);
        }

        Assert.NotNull(seed);
    }

    private static CrtFault WindowFault(int window)
    {
        for (var step = 0; step < 4000; step++)
        {
            var t = (window * Period) + (step * Period / 4000f);
            var state = CrtFaultSchedule.Sample(t, Period, Strength);
            if (state.Fault != CrtFault.None)
            {
                return state.Fault;
            }
        }

        throw new InvalidOperationException($"no fault fired in window {window}.");
    }

    private static int FindWindowWithFault()
    {
        for (var window = 0; window < 10; window++)
        {
            for (var step = 0; step < 4000; step++)
            {
                var t = (window * Period) + (step * Period / 4000f);
                if (CrtFaultSchedule.Sample(t, Period, Strength).Fault != CrtFault.None)
                {
                    return window;
                }
            }
        }

        throw new InvalidOperationException("no fault fired in the first ten windows.");
    }
}
