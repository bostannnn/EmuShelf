namespace EmuShelf.Rendering.Stage.BigType;

/// <summary>
/// The ring when a move arrives before the last one has finished (M47 B11, the app's own: the mock's ring is one keyframed move a
/// press, and a held direction there restarts it from wherever it was drawn, which stretches it again over a ring already stretched).
/// From the moment the move arrives the ring glides: its middle from where it is drawn and how fast it is going toward the new target
/// on a critically damped spring that trails a moving focus by at most <see cref="LagMilliseconds"/> of its travel however long the
/// direction is held, and its size and corner from what they are to the target's, never growing past either. Nothing restarts from
/// rest, nothing stacks, nothing jumps. When no further move comes it settles by <see cref="Milliseconds"/> after the last one, squashed
/// on the mock's keyframes (deepest at 74%) when the last two targets are the same size, as a single move lands. A pure function of
/// time, shared by the native Launcher and the Avalonia one.
/// </summary>
public sealed class BigTypeRingGlide(float start, BigTypeRing drawn, BigTypeRing velocity, BigTypeRing leaving)
{
    /// <summary>A glide lands as a move does, this long after the move that started it (tokens.js <c>Ring.ms</c>).</summary>
    public const float Milliseconds = BigTypeMotion.RingMilliseconds;

    /// <summary>
    /// tokens.js <c>RingGlide.lagMs</c> (the app's own): how far behind a focus moving at a steady pace the ring's middle trails, in
    /// time (the spring's rate is 2 ÷ this). 40 ms keeps it within about one tile of a direction held at its fastest repeat (38 ms).
    /// </summary>
    public const float LagMilliseconds = 40f;

    // The spring's rate, per ms: critically damped, it trails a steady pace by 2 / Rate.
    private const float Rate = 2f / LagMilliseconds;

    /// <summary>When the move that started this glide arrived.</summary>
    public float Start { get; } = start;

    /// <summary>The target the ring was leaving: with the new one, which way it travelled (for the squash).</summary>
    public BigTypeRing Leaving { get; } = leaving;

    /// <summary>Whether it has landed: from then on the ring is its target.</summary>
    public bool Settled(float now) => now - Start >= Milliseconds;

    /// <summary>Where the ring is <paramref name="now"/>, gliding toward <paramref name="target"/> (where its track has it now).</summary>
    public BigTypeRing At(BigTypeRing target, float now)
    {
        var t = Math.Clamp(now - Start, 0f, Milliseconds);
        if (t >= Milliseconds)
            return target;
        var decay = MathF.Exp(-Rate * t);
        // The middle carries its speed (x(t) = T + (e0 + (v0 + k e0) t) e^-kt): the ring goes on as it was going.
        float Carry(float from, float to, float speed)
        {
            var e0 = from - to;
            return to + ((e0 + ((speed + (Rate * e0)) * t)) * decay);
        }
        // The size and the corner do not (x(t) = T + e0 (1 + k t) e^-kt): they ease from what they are to the target's and never
        // overshoot, so the ring keeps its own size and never grows past what it was or what it is going to.
        float Ease(float from, float to) => to + ((from - to) * (1f + (Rate * t)) * decay);
        var centreX = Carry(drawn.CentreX, target.CentreX, velocity.X + (velocity.Width / 2f));
        var centreY = Carry(drawn.CentreY, target.CentreY, velocity.Y + (velocity.Height / 2f));
        var width = Ease(drawn.Width, target.Width);
        var height = Ease(drawn.Height, target.Height);
        var radius = Ease(drawn.Radius, target.Radius);
        var at = new BigTypeRing(centreX - (width / 2f), centreY - (height / 2f), width, height, radius);
        // Landing: the mock's squash on its keyframes (none before 38%, deepest at 74%, gone at 100%), which a held direction never
        // reaches (its next move comes long before 38%).
        var squash = SquashWeight(t / Milliseconds);
        if (squash <= 0f)
            return at;
        var squashed = BigTypeMotion.Squash(Leaving, target);
        return new BigTypeRing(at.X + ((squashed.X - target.X) * squash), at.Y + ((squashed.Y - target.Y) * squash),
            at.Width + ((squashed.Width - target.Width) * squash), at.Height + ((squashed.Height - target.Height) * squash), at.Radius);
    }

    /// <summary>How much of the squash shows <paramref name="progress"/> (0–1) of the way to landing, on the mock's keyframes.</summary>
    public static float SquashWeight(float progress)
    {
        if (progress <= BigTypeMotion.RingCoversBothAt || progress >= 1f)
            return 0f;
        if (progress <= BigTypeMotion.RingSquashAt)
            return BigTypeMotion.RingEase((progress - BigTypeMotion.RingCoversBothAt) / (BigTypeMotion.RingSquashAt - BigTypeMotion.RingCoversBothAt));
        return 1f - ((progress - BigTypeMotion.RingSquashAt) / (1f - BigTypeMotion.RingSquashAt));
    }
}
