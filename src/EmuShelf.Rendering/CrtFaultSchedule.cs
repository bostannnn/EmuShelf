namespace EmuShelf.Rendering;

/// <summary>One of the things a tired television does now and then.</summary>
public enum CrtFault
{
    /// <summary>Nothing is going wrong at the moment, which is nearly all of the time.</summary>
    None = 0,

    /// <summary>Horizontal lock lets go: whole lines land in the wrong place.</summary>
    Tearing = 1,

    /// <summary>Vertical lock kicks and re-settles, dragging the blanking interval across.</summary>
    RollKick = 2,

    /// <summary>The mask is magnetised and the three beams stop landing on top of each other.</summary>
    Degauss = 3,

    /// <summary>The signal goes away: soft, noisy, and briefly colourless.</summary>
    Dropout = 4,

    /// <summary>Interference bends the raster into a wave that travels down the picture.</summary>
    Wave = 5,

    /// <summary>Cross-colour: fine detail decoded as colour it never had.</summary>
    Rainbow = 6,

    /// <summary>One band of lines displaced hard, the way a tape's head switch tears a frame.</summary>
    BandTear = 7,

    /// <summary>The beam current surges and settles, blooming the whole picture.</summary>
    Surge = 8,
}

/// <summary>Which fault the tube is having, how hard, and a value the shader can vary it by.</summary>
/// <param name="Fault">The fault, or <see cref="CrtFault.None"/>.</param>
/// <param name="Amount">0 when nothing is happening, rising to the presentation's strength.</param>
/// <param name="Seed">Stable for the whole of one fault, so shapes that pick a position hold it.</param>
public readonly record struct CrtFaultState(CrtFault Fault, float Amount, float Seed);

/// <summary>
/// Decides when the tube misbehaves, and which way.
/// </summary>
/// <remarks>
/// <para>
/// This lives on the CPU rather than in the shader, and the reason is worth stating because the
/// first version did it the other way. A schedule derived from <c>fract(sin(...))</c> inside the
/// fragment shader cannot be inspected, cannot be unit tested, and cannot be predicted from outside
/// — GLSL's sine is not the same function as any host language's to more than a few digits, and the
/// hash multiplies that difference by forty thousand. Reproducing the schedule off-GPU to work out
/// when to screenshot a fault produced times that were near misses, so four of the eight faults were
/// reviewed by looking at frames in which they were not happening.
/// </para>
/// <para>
/// Here the hash is integer arithmetic, which is exact everywhere, and the shader is handed the
/// answer. That also makes forcing a particular fault a matter of passing different uniforms rather
/// than of hunting for a timestamp.
/// </para>
/// </remarks>
public static class CrtFaultSchedule
{
    /// <summary>How many faults there are, which is also the range the kind hash is folded into.</summary>
    private const int FaultCount = 8;

    /// <summary>
    /// Fraction of a window the fault takes to reach full strength. Short: a fault arrives, it does
    /// not fade in.
    /// </summary>
    private const float Attack = 0.05f;

    /// <summary>What the tube is doing at this moment.</summary>
    /// <param name="seconds">Elapsed time, on whatever clock the host is running.</param>
    /// <param name="period">Seconds between windows. One fault fires somewhere inside each.</param>
    /// <param name="strength">Peak amount, from the presentation. 0 disables the whole system.</param>
    public static CrtFaultState Sample(float seconds, float period, float strength)
    {
        if (strength <= 0f || period <= 0f || seconds < 0f)
        {
            return default;
        }

        var window = (int)MathF.Floor(seconds / period);
        // Placed randomly inside its window rather than at the start of it, so the faults do not
        // arrive on a beat. Kept clear of both ends so one can never overlap the next.
        var onset = (window + 0.1f + (0.7f * Hash(window, 0x9E37u))) * period;
        var span = 0.25f + (0.85f * Hash(window, 0x85EBu));
        var through = (seconds - onset) / span;

        if (through < 0f || through >= 1f)
        {
            return default;
        }

        // Fast attack, linear decay: it arrives all at once and then recovers.
        var amount = strength * MathF.Min(1f, through / Attack) * (1f - through);
        var fault = (CrtFault)(1 + (int)(Hash(window, 0xC2B2u) * FaultCount));
        return new CrtFaultState(fault, amount, Hash(window, 0x27D4u));
    }

    /// <summary>
    /// Integer avalanche hash, in [0, 1).
    /// </summary>
    /// <remarks>
    /// Exact on every platform, which is the entire point of moving this off the GPU. It also
    /// distributes far better than a sine over the eight buckets the kind is folded into: the
    /// shader version fired one fault twenty-nine times per cycle and another eight, with runs of
    /// five identical faults in a row.
    /// </remarks>
    private static float Hash(int window, uint salt)
    {
        unchecked
        {
            var h = ((uint)window * 2654435761u) ^ (salt * 2246822519u);
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            h *= 3266489917u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / 16777216f;
        }
    }
}
