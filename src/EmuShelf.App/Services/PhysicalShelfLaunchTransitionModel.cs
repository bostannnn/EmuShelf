namespace EmuShelf.App.Services;

public enum PhysicalShelfLaunchPhase
{
    Idle,
    Lift,
    Spin,
    Align,
    Insert,
    Committed,
    Return,
}

/// <summary>Renderer-ready pose for the one shelf item taking part in a launch transition.</summary>
public readonly record struct PhysicalShelfLaunchPose(
    long GameId,
    PhysicalShelfLaunchPhase Phase,
    float Yaw,
    float Pitch,
    float VerticalOffset,
    float DepthOffset,
    float Scale);

/// <summary>
/// Clock- and UI-free launch choreography for physical shelf media.
/// </summary>
/// <remarks>
/// The model stops at <see cref="PhysicalShelfLaunchPhase.Committed"/> so the emulator process can
/// start with the cartridge held half-inserted. After a failed start or emulator exit the caller
/// begins <see cref="PhysicalShelfLaunchPhase.Return"/>, which restores the exact captured shelf
/// pose. A renderer consumes only <see cref="Pose"/> and never starts a process itself.
/// </remarks>
public sealed class PhysicalShelfLaunchTransitionModel
{
    internal const double LiftDurationMilliseconds = 380d;
    internal const double SpinDurationMilliseconds = 780d;
    internal const double AlignDurationMilliseconds = 140d;
    internal const double InsertDurationMilliseconds = 620d;
    internal const double ReturnDurationMilliseconds = 480d;

    // Sized against the shelf camera's framing, not against the world. The camera now pulls back
    // only as far as the tallest medium needs, so the headroom above a cartridge is a fraction of
    // what it was; a lift tuned for the old fixed distance pushed the medium out of the top of the
    // frame on the way up. Insertion still travels far enough to leave the frame entirely.
    private const float HeroLift = 0.10f;
    private const float HeroDepth = 0.16f;
    private const float AlignDepth = 0.145f;
    private const float HeroScale = 1.10f;
    private const float InsertedVerticalOffset = -0.86f;
    private const float InsertedDepthOffset = 0.08f;
    private const float FullTurns = 3f;
    private const float InsertionYaw = 0f;
    private const float InsertionPitch = 0f;

    private double _phaseElapsedMilliseconds;
    private float _startYaw;
    private float _startPitch;
    private PhysicalShelfLaunchPose _returnStart;
    private bool _reducedMotion;

    public PhysicalShelfLaunchPhase Phase { get; private set; }

    public PhysicalShelfLaunchPose Pose { get; private set; }

    public bool IsIdle => Phase == PhysicalShelfLaunchPhase.Idle;

    public bool IsCommitted => Phase == PhysicalShelfLaunchPhase.Committed;

    public void Start(long gameId, float yaw, float pitch, bool reducedMotion = false)
    {
        _startYaw = yaw;
        _startPitch = pitch;
        _reducedMotion = reducedMotion;
        _phaseElapsedMilliseconds = 0d;
        Phase = PhysicalShelfLaunchPhase.Lift;
        Pose = new PhysicalShelfLaunchPose(gameId, Phase, yaw, pitch, 0f, 0f, 1f);
    }

    public bool BeginReturn()
    {
        if (IsIdle || Phase == PhysicalShelfLaunchPhase.Return)
        {
            return false;
        }

        _returnStart = Pose;
        _phaseElapsedMilliseconds = 0d;
        Phase = PhysicalShelfLaunchPhase.Return;
        Pose = Pose with { Phase = Phase };
        return true;
    }

    /// <summary>Advances by elapsed real time and returns whether the visible pose changed.</summary>
    public bool Update(double deltaMilliseconds)
    {
        if (deltaMilliseconds <= 0d || IsIdle || IsCommitted)
        {
            return false;
        }

        var previous = Pose;
        _phaseElapsedMilliseconds += Math.Min(deltaMilliseconds, 100d);

        switch (Phase)
        {
            case PhysicalShelfLaunchPhase.Lift:
                UpdateLift();
                break;
            case PhysicalShelfLaunchPhase.Spin:
                UpdateSpin();
                break;
            case PhysicalShelfLaunchPhase.Align:
                UpdateAlign();
                break;
            case PhysicalShelfLaunchPhase.Insert:
                UpdateInsert();
                break;
            case PhysicalShelfLaunchPhase.Return:
                UpdateReturn();
                break;
        }

        return Pose != previous;
    }

    public void Reset()
    {
        Phase = PhysicalShelfLaunchPhase.Idle;
        _phaseElapsedMilliseconds = 0d;
        Pose = default;
    }

    private void UpdateLift()
    {
        var duration = _reducedMotion ? 180d : LiftDurationMilliseconds;
        var t = Progress(duration);
        var eased = EaseOutCubic(t);
        Pose = Pose with
        {
            Phase = Phase,
            Yaw = Lerp(
                _startYaw,
                _reducedMotion ? InsertionYaw : MediaRotationModel.RestYaw,
                eased),
            Pitch = Lerp(
                _startPitch,
                _reducedMotion ? InsertionPitch : MediaRotationModel.RestPitch,
                eased),
            VerticalOffset = HeroLift * eased,
            DepthOffset = HeroDepth * eased,
            Scale = Lerp(1f, HeroScale, eased),
        };
        if (t >= 1f)
        {
            AdvanceTo(_reducedMotion
                ? PhysicalShelfLaunchPhase.Insert
                : PhysicalShelfLaunchPhase.Spin);
        }
    }

    private void UpdateSpin()
    {
        var t = Progress(SpinDurationMilliseconds);
        Pose = Pose with
        {
            Phase = Phase,
            Yaw = MediaRotationModel.RestYaw + (FullTurns * MathF.Tau * EaseInOutCubic(t)),
            Pitch = MediaRotationModel.RestPitch,
            VerticalOffset = HeroLift,
            DepthOffset = HeroDepth,
            Scale = HeroScale,
        };
        if (t >= 1f)
        {
            AdvanceTo(PhysicalShelfLaunchPhase.Align);
        }
    }

    private void UpdateAlign()
    {
        var t = Progress(AlignDurationMilliseconds);
        var eased = EaseInOutCubic(t);
        Pose = Pose with
        {
            Phase = Phase,
            Yaw = Lerp(
                MediaRotationModel.RestYaw + (FullTurns * MathF.Tau),
                FullTurns * MathF.Tau,
                eased),
            Pitch = Lerp(MediaRotationModel.RestPitch, InsertionPitch, eased),
            DepthOffset = Lerp(HeroDepth, AlignDepth, eased),
            Scale = Lerp(HeroScale, 1.08f, eased),
        };
        if (t >= 1f)
        {
            // Keep the stored value numerically small after the exact three-turn spin. The matrix
            // sees the same orientation, while Return can now interpolate without spinning back.
            Pose = Pose with { Yaw = InsertionYaw, Pitch = InsertionPitch };
            AdvanceTo(PhysicalShelfLaunchPhase.Insert);
        }
    }

    private void UpdateInsert()
    {
        var duration = _reducedMotion ? 420d : InsertDurationMilliseconds;
        var t = Progress(duration);
        var eased = EaseInCubic(t);
        Pose = Pose with
        {
            Phase = Phase,
            Yaw = InsertionYaw,
            Pitch = InsertionPitch,
            VerticalOffset = Lerp(HeroLift, InsertedVerticalOffset, eased),
            DepthOffset = Lerp(_reducedMotion ? HeroDepth : AlignDepth, InsertedDepthOffset, eased),
            Scale = Lerp(_reducedMotion ? HeroScale : 1.08f, 1f, eased),
        };
        if (t >= 1f)
        {
            Phase = PhysicalShelfLaunchPhase.Committed;
            Pose = Pose with { Phase = Phase };
        }
    }

    private void UpdateReturn()
    {
        var t = Progress(_reducedMotion ? 220d : ReturnDurationMilliseconds);
        var eased = EaseOutCubic(t);
        Pose = new PhysicalShelfLaunchPose(
            _returnStart.GameId,
            Phase,
            Lerp(_returnStart.Yaw, _startYaw, eased),
            Lerp(_returnStart.Pitch, _startPitch, eased),
            Lerp(_returnStart.VerticalOffset, 0f, eased),
            Lerp(_returnStart.DepthOffset, 0f, eased),
            Lerp(_returnStart.Scale, 1f, eased));
        if (t >= 1f)
        {
            Reset();
        }
    }

    private float Progress(double duration) =>
        (float)Math.Clamp(_phaseElapsedMilliseconds / duration, 0d, 1d);

    private void AdvanceTo(PhysicalShelfLaunchPhase phase)
    {
        Phase = phase;
        _phaseElapsedMilliseconds = 0d;
        Pose = Pose with { Phase = phase };
    }

    private static float Lerp(float from, float to, float amount) => from + ((to - from) * amount);

    private static float EaseOutCubic(float value) => 1f - MathF.Pow(1f - value, 3f);

    private static float EaseInCubic(float value) => value * value * value;

    private static float EaseInOutCubic(float value) => value < 0.5f
        ? 4f * value * value * value
        : 1f - (MathF.Pow((-2f * value) + 2f, 3f) * 0.5f);
}
