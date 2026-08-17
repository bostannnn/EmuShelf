namespace EmuShelf.App.Services;

/// <summary>
/// Turns raw right-stick deflection into the couch shelf hero's yaw and pitch.
/// </summary>
/// <remarks>
/// Deliberately pure — no Avalonia, no native input, no clock of its own — so the feel of the
/// rotation can be pinned down in unit tests instead of by holding a controller. It is a *velocity*
/// model, not a position one: the stick sets how fast the medium turns, the way you would spin a
/// case in your hands, rather than mapping deflection to an absolute angle. Absolute mapping runs
/// out of travel exactly when you want to keep going.
/// </remarks>
public sealed class MediaRotationModel
{
    /// <summary>
    /// Radial deadzone on the stick's magnitude.
    /// </summary>
    /// <remarks>
    /// Its own value, much smaller than the left stick's 0.5 direction threshold. That threshold
    /// answers "has the player chosen a direction"; this one answers "is the stick actually
    /// centred", and a worn thumbstick resting at 0.1 must not make the hero creep.
    /// </remarks>
    public const float Deadzone = 0.15f;

    /// <summary>Angular speed at full deflection, in radians per second.</summary>
    private const float MaxSpeed = 3.8f;

    /// <summary>Pitch is clamped; yaw is not.</summary>
    /// <remarks>
    /// You can turn a case all the way round — that is the point — but tipping it past this stops
    /// reading as "looking at the top edge" and starts reading as a broken camera.
    /// </remarks>
    private const float MaxPitch = 60f * MathF.PI / 180f;

    /// <summary>
    /// The pose the hero rests at, and returns to on recentre.
    /// </summary>
    /// <remarks>
    /// Not face-on, which is a deliberate departure from the design doc's "snaps to front". Dead
    /// front-on, a keep case presents a flat rectangle and reads as the flat cover it just
    /// replaced — the thickness, the spine and the sweep of the highlight all disappear at yaw 0.
    /// A slight three-quarter turn is what makes it read as an object you could pick up, and it is
    /// the pose every approved render was shot at.
    /// </remarks>
    public const float RestYaw = -0.42f;

    /// <inheritdoc cref="RestYaw"/>
    public const float RestPitch = -0.10f;

    /// <summary>Longest tick honoured, in milliseconds.</summary>
    /// <remarks>
    /// A stall — a GC pause, a window drag, a breakpoint — otherwise arrives as one enormous dt and
    /// snaps the medium through half a turn in a single frame.
    /// </remarks>
    private const double MaxDeltaMilliseconds = 100d;

    /// <summary>Rotation about the shell's up axis, in radians. 0 faces the viewer.</summary>
    public float Yaw { get; private set; } = RestYaw;

    /// <summary>Rotation about the shell's right axis, in radians.</summary>
    public float Pitch { get; private set; } = RestPitch;

    /// <summary>True when the hero is sitting at its untouched presentation pose.</summary>
    public bool IsAtRest => Yaw == RestYaw && Pitch == RestPitch;

    /// <summary>
    /// Advances the pose by one tick of stick input.
    /// </summary>
    /// <param name="rightStickX">Raw horizontal deflection, -1..1.</param>
    /// <param name="rightStickY">Raw vertical deflection, -1..1, positive down (SDL's convention).</param>
    /// <param name="deltaMilliseconds">Real elapsed time since the previous tick.</param>
    /// <returns>True when the pose changed and the hero needs redrawing.</returns>
    public bool Update(float rightStickX, float rightStickY, double deltaMilliseconds)
    {
        var magnitude = MathF.Sqrt((rightStickX * rightStickX) + (rightStickY * rightStickY));
        if (magnitude <= Deadzone || deltaMilliseconds <= 0d)
        {
            return false;
        }

        // Rescale past the deadzone so the response starts from zero at the edge of it rather than
        // jumping to 15% speed the instant the stick registers.
        var scaled = Math.Clamp((magnitude - Deadzone) / (1f - Deadzone), 0f, 1f);
        // Squared: fine control near centre for lining a cover up, full speed still available.
        var speed = scaled * scaled * MaxSpeed;

        var seconds = (float)(Math.Min(deltaMilliseconds, MaxDeltaMilliseconds) / 1000d);
        var step = speed * seconds / magnitude;

        var previousYaw = Yaw;
        var previousPitch = Pitch;

        // Push right, the medium's right edge goes away from you.
        Yaw = Wrap(Yaw + (rightStickX * step));
        // Push up (negative Y on a pad), the top tips towards you.
        Pitch = Math.Clamp(Pitch + (rightStickY * step), -MaxPitch, MaxPitch);

        return Yaw != previousYaw || Pitch != previousPitch;
    }

    /// <summary>
    /// Returns to the resting presentation pose. Driven by R3 and by any change of focus.
    /// </summary>
    /// <returns>True when this actually moved anything.</returns>
    public bool Recentre()
    {
        if (IsAtRest)
        {
            return false;
        }

        Yaw = RestYaw;
        Pitch = RestPitch;
        return true;
    }

    /// <summary>
    /// Keeps yaw inside -pi..pi.
    /// </summary>
    /// <remarks>
    /// Rotation is unbounded from the player's side — you can keep spinning — but the stored angle
    /// must not be, or a long session accumulates a float large enough to lose the precision that
    /// makes slow rotation smooth.
    /// </remarks>
    private static float Wrap(float radians)
    {
        const float turn = 2f * MathF.PI;
        radians %= turn;
        if (radians > MathF.PI)
        {
            radians -= turn;
        }
        else if (radians < -MathF.PI)
        {
            radians += turn;
        }

        return radians;
    }
}
