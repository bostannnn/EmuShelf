namespace EmuShelf.App.Services;

/// <summary>
/// A continuous, critically damped shelf position driven by a discrete focused index.
/// </summary>
/// <remarks>
/// The model is UI- and clock-free. A caller supplies elapsed time, which keeps gamepad, keyboard
/// and headless tests on the same path. Critically damped motion has no overshoot: media carry
/// through the scene and settle at the selected slot without wobbling like a spring toy.
/// </remarks>
public sealed class PhysicalShelfMotionModel
{
    private const double AngularFrequency = 13.5;
    private const double MaxDeltaSeconds = 0.05;
    private const double PositionEpsilon = 0.0005;
    private const double VelocityEpsilon = 0.005;

    public double Position { get; private set; }

    public int TargetIndex { get; private set; }

    public double Velocity { get; private set; }

    public bool IsSettled =>
        Math.Abs(Position - TargetIndex) <= PositionEpsilon && Math.Abs(Velocity) <= VelocityEpsilon;

    public void SnapTo(int index)
    {
        TargetIndex = Math.Max(0, index);
        Position = TargetIndex;
        Velocity = 0d;
    }

    /// <summary>
    /// Changes the destination without discarding current velocity. Reduced motion lands directly
    /// on the same target; the eventual accessibility setting can therefore change policy without
    /// giving the renderer a second movement path.
    /// </summary>
    public bool MoveTo(int index, bool reducedMotion = false)
    {
        var target = Math.Max(0, index);
        if (reducedMotion)
        {
            var changed = Position != target || Velocity != 0d;
            SnapTo(target);
            return changed;
        }

        if (target == TargetIndex && !IsSettled)
        {
            return false;
        }

        TargetIndex = target;
        return !IsSettled;
    }

    /// <summary>Advances by real elapsed time and returns whether the visible position changed.</summary>
    public bool Update(double deltaMilliseconds)
    {
        if (deltaMilliseconds <= 0d || IsSettled)
        {
            return false;
        }

        var previous = Position;
        var seconds = Math.Min(deltaMilliseconds / 1000d, MaxDeltaSeconds);

        // Exact solution of y'' + 2ωy' + ω²y = 0 for a fixed target over this tick.
        var displacement = Position - TargetIndex;
        var slope = Velocity + (AngularFrequency * displacement);
        var decay = Math.Exp(-AngularFrequency * seconds);
        var nextDisplacement = (displacement + (slope * seconds)) * decay;
        var nextVelocity = (Velocity - (AngularFrequency * slope * seconds)) * decay;

        Position = TargetIndex + nextDisplacement;
        Velocity = nextVelocity;

        if (IsSettled)
        {
            Position = TargetIndex;
            Velocity = 0d;
        }

        return Position != previous;
    }
}
