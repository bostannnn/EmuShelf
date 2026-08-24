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

    /// <summary>Angular reach of the resting hero's idle sway, in radians (±).</summary>
    /// <remarks>
    /// A centred hero used to sit as one frozen frame, which reads as the flat cover it replaced —
    /// nothing tells the eye it is a 3-D object, and nothing invites the stick. A slow, shallow turn
    /// fixes both. Kept deliberately tiny: this is a sheen of life on the presentation pose, not a
    /// carousel, and it must never compete with the deliberate rotation the stick performs.
    /// </remarks>
    private const float IdleYawAmplitude = 4.5f * MathF.PI / 180f;

    /// <summary>A shallower vertical "breath" layered on the yaw sway. See <see cref="IdleYawAmplitude"/>.</summary>
    private const float IdlePitchAmplitude = 1.5f * MathF.PI / 180f;

    /// <summary>Seconds for one full yaw sway cycle.</summary>
    private const float IdleYawPeriodSeconds = 5.5f;

    /// <summary>
    /// Seconds for one full pitch breath. Deliberately not a multiple of the yaw period, so the two
    /// never realign into a flat back-and-forth wobble — the drift wanders instead.
    /// </summary>
    private const float IdlePitchPeriodSeconds = 7.5f;

    /// <summary>How long the hero must sit untouched before the idle sway begins.</summary>
    /// <remarks>Lets a just-focused cover settle at its pose first; only then does it start to breathe.</remarks>
    private const float IdleDelaySeconds = 2f;

    private const float Tau = 2f * MathF.PI;

    /// <summary>The pose the stick drives, before any idle sway is layered on. 0 faces the viewer.</summary>
    private float _yaw = RestYaw;
    private float _pitch = RestPitch;

    /// <summary>True once <see cref="IdleDelaySeconds"/> has elapsed with the hero left at rest.</summary>
    private bool _idleActive;
    private float _restingSeconds;
    private float _idleYawPhase;
    private float _idlePitchPhase;

    /// <summary>
    /// Whether the resting hero breathes on its own. On by default; the Android head turns it off
    /// (its <c>IsReducedEffectsPlatform</c>) because with the tube effect off an idle sway would drive
    /// the shelf renderer every frame for no user input — the fan-on-scroll cost that head avoids.
    /// </summary>
    public bool IdleSwayEnabled { get; set; } = true;

    /// <summary>
    /// Whether the resting hero is currently drifting on its own, and so needs redrawing every tick
    /// without any stick input. Lets a push-fed poll loop keep ticking for the sway even when the pad
    /// is at rest. Always false on the Android head, where <see cref="IdleSwayEnabled"/> is off.
    /// </summary>
    public bool IsSwaying => IdleSwayEnabled && _idleActive;

    /// <summary>Rotation about the shell's up axis, in radians. 0 faces the viewer.</summary>
    /// <remarks>
    /// The stick-driven pose plus the current idle-sway offset, exposed as one angle so the bound
    /// hero drifts while it rests and turns while it is driven, with no seam between the two.
    /// </remarks>
    public float Yaw => Wrap(_yaw + IdleYawOffset);

    /// <summary>Rotation about the shell's right axis, in radians.</summary>
    public float Pitch => Math.Clamp(_pitch + IdlePitchOffset, -MaxPitch, MaxPitch);

    /// <summary>
    /// True when the stick-driven pose is at its untouched presentation pose. The idle sway rides on
    /// top of this, so the hero can be reported at rest while it is gently drifting.
    /// </summary>
    public bool IsAtRest => _yaw == RestYaw && _pitch == RestPitch;

    private float IdleYawOffset =>
        _idleActive ? IdleYawAmplitude * MathF.Sin(Tau * _idleYawPhase / IdleYawPeriodSeconds) : 0f;

    private float IdlePitchOffset =>
        _idleActive ? IdlePitchAmplitude * MathF.Sin(Tau * _idlePitchPhase / IdlePitchPeriodSeconds) : 0f;

    /// <summary>
    /// Advances the pose by one tick of stick input.
    /// </summary>
    /// <param name="rightStickX">Raw horizontal deflection, -1..1.</param>
    /// <param name="rightStickY">Raw vertical deflection, -1..1, positive down (SDL's convention).</param>
    /// <param name="deltaMilliseconds">Real elapsed time since the previous tick.</param>
    /// <returns>True when the pose changed and the hero needs redrawing.</returns>
    public bool Update(float rightStickX, float rightStickY, double deltaMilliseconds)
    {
        if (deltaMilliseconds <= 0d)
        {
            return false;
        }

        var seconds = (float)(Math.Min(deltaMilliseconds, MaxDeltaMilliseconds) / 1000d);
        var magnitude = MathF.Sqrt((rightStickX * rightStickX) + (rightStickY * rightStickY));

        return magnitude > Deadzone
            ? ApplyStickRotation(rightStickX, rightStickY, magnitude, seconds)
            : Drift(seconds);
    }

    /// <summary>Turns the medium under the stick, taking the hero over cleanly from any idle sway.</summary>
    private bool ApplyStickRotation(float rightStickX, float rightStickY, float magnitude, float seconds)
    {
        // The stick has the hero now: fold whatever gentle offset the idle sway was showing into the
        // pose so the turn continues from exactly where the eye last saw it, with no snap, then stop
        // drifting until the hero is left alone again.
        var handedOff = _idleActive;
        if (_idleActive)
        {
            _yaw = Wrap(_yaw + IdleYawOffset);
            _pitch = Math.Clamp(_pitch + IdlePitchOffset, -MaxPitch, MaxPitch);
        }

        ResetIdle();

        // Rescale past the deadzone so the response starts from zero at the edge of it rather than
        // jumping to 15% speed the instant the stick registers.
        var scaled = Math.Clamp((magnitude - Deadzone) / (1f - Deadzone), 0f, 1f);
        // Squared: fine control near centre for lining a cover up, full speed still available.
        var speed = scaled * scaled * MaxSpeed;
        var step = speed * seconds / magnitude;

        var previousYaw = _yaw;
        var previousPitch = _pitch;

        // Push right, the medium's right edge goes away from you.
        _yaw = Wrap(_yaw + (rightStickX * step));
        // Push up (negative Y on a pad), the top tips towards you.
        _pitch = Math.Clamp(_pitch + (rightStickY * step), -MaxPitch, MaxPitch);

        return handedOff || _yaw != previousYaw || _pitch != previousPitch;
    }

    /// <summary>
    /// Advances the resting hero's idle sway while the stick is centred. Only a hero sitting at its
    /// untouched pose breathes — a medium the player has deliberately turned to inspect stays put,
    /// so the drift never fights a chosen pose.
    /// </summary>
    private bool Drift(float seconds)
    {
        if (!IdleSwayEnabled || _yaw != RestYaw || _pitch != RestPitch)
        {
            return false;
        }

        if (!_idleActive)
        {
            _restingSeconds += seconds;
            if (_restingSeconds < IdleDelaySeconds)
            {
                return false;
            }

            _idleActive = true;
        }

        // Wrap each phase to its own period so the sine argument never grows large enough to lose the
        // precision that keeps the drift smooth over a long idle. Both start near zero, so the sway
        // eases up from a standstill rather than snapping to full deflection.
        _idleYawPhase = (_idleYawPhase + seconds) % IdleYawPeriodSeconds;
        _idlePitchPhase = (_idlePitchPhase + seconds) % IdlePitchPeriodSeconds;
        return true;
    }

    /// <summary>
    /// Returns to the resting presentation pose. Driven by R3 and by any change of focus.
    /// </summary>
    /// <returns>True when this actually moved anything.</returns>
    public bool Recentre()
    {
        // A sway showing on a resting pose still needs clearing, so a change of focus lands the next
        // cover truly face-on rather than mid-drift; hence the idle check beyond the pose check.
        var moved = _yaw != RestYaw || _pitch != RestPitch || _idleActive;

        _yaw = RestYaw;
        _pitch = RestPitch;
        ResetIdle();

        return moved;
    }

    /// <summary>Drops the idle sway and its clock, so the hero starts breathing afresh next time it rests.</summary>
    private void ResetIdle()
    {
        _idleActive = false;
        _restingSeconds = 0f;
        _idleYawPhase = 0f;
        _idlePitchPhase = 0f;
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
