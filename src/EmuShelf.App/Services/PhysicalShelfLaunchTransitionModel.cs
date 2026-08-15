namespace EmuShelf.App.Services;

public enum PhysicalShelfLaunchPhase
{
    Idle,
    Lift,
    Spin,
    Align,
    Reveal,
    Flip,
    SpinUp,
    Insert,
    Committed,
    Return,
}

/// <summary>
/// Which choreography a medium's launch takes.
/// </summary>
/// <remarks>
/// Selected by a profile's <c>InsertionAnimationId</c> rather than by the shell, because a
/// medium's shape and its motion do not line up: PS2, GameCube and Wii share one stand-in
/// keep case while a PS1 jewel case and a PSP UMD share a cover card, and each of those four wants
/// something different once its own shell exists.
/// </remarks>
public enum PhysicalShelfLaunchStyle
{
    /// <summary>Turned to face its slot and pushed in — a cartridge, a card, a bare cover.</summary>
    Cartridge,

    /// <summary>Opened, emptied and set down: the disc leaves the case and goes on alone.</summary>
    Disc,
}

public static class PhysicalShelfLaunchStyles
{
    /// <summary>Resolves a profile's declared animation to the choreography that plays it.</summary>
    public static PhysicalShelfLaunchStyle ForAnimation(string insertionAnimationId) =>
        insertionAnimationId switch
        {
            "disc-from-case" => PhysicalShelfLaunchStyle.Disc,
            _ => PhysicalShelfLaunchStyle.Cartridge,
        };
}

/// <summary>Where the disc is once a launch has taken it out of its case.</summary>
/// <remarks>
/// Measured from the medium's resting centre rather than from the case's current pose, because the
/// two bodies separate: the case is set down while the disc carries on, and offsets chained onto a
/// falling case would take the disc with it.
/// </remarks>
public readonly record struct PhysicalShelfDiscPose(
    float HorizontalOffset,
    float VerticalOffset,
    float DepthOffset,
    float Spin,
    float Tilt,
    float Scale,
    float Flip = 0f);

/// <summary>Renderer-ready pose for the one shelf item taking part in a launch transition.</summary>
public readonly record struct PhysicalShelfLaunchPose(
    long GameId,
    PhysicalShelfLaunchPhase Phase,
    float Yaw,
    float Pitch,
    float VerticalOffset,
    float DepthOffset,
    float Scale,
    PhysicalShelfDiscPose? Disc = null);

/// <summary>
/// Clock- and UI-free launch choreography for physical shelf media.
/// </summary>
/// <remarks>
/// The model stops at <see cref="PhysicalShelfLaunchPhase.Committed"/> so the emulator process can
/// start with the medium already gone from the frame. After a failed start or emulator exit the
/// caller begins <see cref="PhysicalShelfLaunchPhase.Return"/>, which restores the exact captured
/// shelf pose. A renderer consumes only <see cref="Pose"/> and never starts a process itself.
///
/// Two sequences share those endpoints, and which one plays is the medium's to declare — a
/// cartridge is turned over and pushed into a slot, which is a motion nobody has ever performed on
/// a DVD case. Both run to the same total, so the moment the emulator is asked to start does not
/// depend on what the game shipped on.
/// </remarks>
public sealed class PhysicalShelfLaunchTransitionModel
{
    internal const double LiftDurationMilliseconds = 570d;
    internal const double SpinDurationMilliseconds = 1170d;
    internal const double AlignDurationMilliseconds = 210d;
    internal const double InsertDurationMilliseconds = 930d;
    internal const double ReturnDurationMilliseconds = 720d;

    // The disc sequence's own middle, budgeted to the same 2880ms total as the cartridge's.
    internal const double RevealDurationMilliseconds = 630d;
    internal const double SpinUpDurationMilliseconds = 1050d;
    internal const double DiscInsertDurationMilliseconds = 630d;

    /// <summary>How long the disc spends turning over once it is clear of the case.</summary>
    /// <remarks>
    /// Time the cartridge sequence has no equivalent of, so the two no longer hand over together —
    /// a disc launch is this much longer before the emulator is asked to start. Deliberate: the
    /// disc has a second face worth seeing and a cartridge does not, and showing it is the point.
    /// </remarks>
    internal const double FlipDurationMilliseconds = 900d;

    // Three turns about the up axis, which is what brings the data side round to the player and
    // back. Distinct from the spin: turning in its own plane never shows the other face.
    private const float DiscFlipTurns = 3f;

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

    /// <summary>How far the disc slides out of the case, sideways, before anything else moves.</summary>
    /// <remarks>
    /// This is the phase that has to be occluded to work. The disc stays at the case's own depth
    /// while it travels, so the case's front is in front of it and the disc is progressively
    /// uncovered as it clears the right-hand edge — it is drawn out of the case rather than
    /// appearing beside it. Moving it toward the camera during this phase is what made it pop into
    /// existence: it stepped in front of the case on the very first frame, so there was nothing
    /// left to emerge from.
    ///
    /// Sized to clear the case's edge and no further. A case is 0.711 wide and a disc 0.632 across,
    /// so fully clear is 0.671, and at that distance the disc is well into the neighbouring game's
    /// place on the shelf; 0.44 shows about two thirds of it past the edge, which is more than
    /// enough to read as a disc and stays inside this item's own space.
    /// </remarks>
    private const float DiscSlideOut = 0.44f;

    // A little over half a turn while it slides. A disc emerging with no rotation at all reads as a
    // flat cut-out sliding sideways; turning as it comes is what says the object is round.
    private const float DiscSlideTurns = 0.6f;

    // Where the disc goes once it is out and the case is being set down. It comes forward here,
    // not during the slide, and it is only now that depth is safe to change.
    private const float DiscRevealLift = 0.20f;
    private const float DiscRevealDepth = 0.24f;
    private const float DiscSpinUpLift = 0.10f;
    private const float DiscSpinUpScale = 1.12f;
    /// <summary>Where the disc ends up, which is off the bottom of the frame entirely.</summary>
    /// <remarks>
    /// Past the floor rather than onto it. The shelf camera is elevated and looking down, so a disc
    /// that stops level with the bottom of the frame is still in shot — seen from above, lying flat,
    /// which is the one pose where its silhouette is thinnest and it reads as a sliver rather than
    /// as something that has gone. It has to keep going until it is genuinely out.
    /// </remarks>
    private const float DiscInsertedVerticalOffset = -1.45f;

    // Nothing here for where the emptied case goes, because it does not go anywhere. It was first
    // written to fall below the floor, on the reasoning that the case is finished with and a second
    // body standing in shot would compete with the disc. That is wrong about the object: a cartridge
    // leaves through the bottom of the frame because it is being put into the console, and a case
    // never is — you take the disc out and the case stays exactly where it was. Dropping it read as
    // the shelf swallowing it. It now settles back to its own resting pose while the disc goes on.

    // The disc's turns are the ones the cartridge sequence used to spend tumbling the whole medium.
    // On a disc's own axis they are what the object actually does, and they keep accelerating into
    // the handover so the last frame before the emulator starts is the fastest.
    private const float DiscSpinUpTurns = 2.6f;
    private const float DiscInsertTurns = 2.2f;
    private const float DiscTrayTilt = -MathF.PI / 2f;

    private double _phaseElapsedMilliseconds;
    private float _startYaw;
    private float _startPitch;
    private PhysicalShelfLaunchPose _returnStart;
    private bool _reducedMotion;
    private bool _reversing;
    private PhysicalShelfLaunchStyle _style;

    /// <summary>The phases this launch plays, in order, which reversing walks backwards.</summary>
    /// <remarks>
    /// Replaced a chain of "which phase comes next" decisions spread through the handlers. Stating
    /// the order once is what makes reversal possible at all: a handler that knows its successor
    /// knows it in one direction only, and the alternative to this list is a second set of them.
    /// </remarks>
    private PhysicalShelfLaunchPhase[] Sequence => (_style, _reducedMotion) switch
    {
        (_, true) =>
            [PhysicalShelfLaunchPhase.Lift, PhysicalShelfLaunchPhase.Insert],
        (PhysicalShelfLaunchStyle.Disc, _) =>
        [
            PhysicalShelfLaunchPhase.Lift,
            PhysicalShelfLaunchPhase.Reveal,
            PhysicalShelfLaunchPhase.Flip,
            PhysicalShelfLaunchPhase.SpinUp,
            PhysicalShelfLaunchPhase.Insert,
        ],
        _ =>
        [
            PhysicalShelfLaunchPhase.Lift,
            PhysicalShelfLaunchPhase.Spin,
            PhysicalShelfLaunchPhase.Align,
            PhysicalShelfLaunchPhase.Insert,
        ],
    };

    /// <summary>How long the phase currently playing lasts.</summary>
    private double PhaseDuration => Phase switch
    {
        PhysicalShelfLaunchPhase.Lift => _reducedMotion ? 270d : LiftDurationMilliseconds,
        PhysicalShelfLaunchPhase.Spin => SpinDurationMilliseconds,
        PhysicalShelfLaunchPhase.Align => AlignDurationMilliseconds,
        PhysicalShelfLaunchPhase.Reveal => RevealDurationMilliseconds,
        PhysicalShelfLaunchPhase.Flip => FlipDurationMilliseconds,
        PhysicalShelfLaunchPhase.SpinUp => SpinUpDurationMilliseconds,
        PhysicalShelfLaunchPhase.Insert => _reducedMotion
            ? 630d
            : IsDisc ? DiscInsertDurationMilliseconds : InsertDurationMilliseconds,
        PhysicalShelfLaunchPhase.Return => _reducedMotion ? 330d : ReturnDurationMilliseconds,
        _ => 1d,
    };

    public PhysicalShelfLaunchPhase Phase { get; private set; }

    public PhysicalShelfLaunchPose Pose { get; private set; }

    public bool IsIdle => Phase == PhysicalShelfLaunchPhase.Idle;

    public bool IsCommitted => Phase == PhysicalShelfLaunchPhase.Committed;

    public void Start(
        long gameId,
        PhysicalShelfLaunchStyle style,
        float yaw,
        float pitch,
        bool reducedMotion = false)
    {
        _style = style;
        _startYaw = yaw;
        _startPitch = pitch;
        _reducedMotion = reducedMotion;
        _phaseElapsedMilliseconds = 0d;
        _reversing = false;
        Phase = PhysicalShelfLaunchPhase.Lift;
        Pose = new PhysicalShelfLaunchPose(
            gameId, Phase, yaw, pitch, 0f, 0f, 1f, StowedDisc());
    }

    /// <summary>
    /// Begins putting the medium back, either by playing the launch backwards or by easing out of
    /// wherever it had got to.
    /// </summary>
    /// <remarks>
    /// The two cases are genuinely different events and want different motions. A launch that ran to
    /// the end and is now coming back means the game has been played and closed, and the honest
    /// answer to "where did the disc go" is the one the player watched: it comes back out of the
    /// drive, spins down, slides back into its case. A launch abandoned part way — a failed start, a
    /// cancellation — never got anywhere, and replaying a partial sequence in reverse would dwell on
    /// a story that did not happen. That one still eases directly back from the current pose.
    /// </remarks>
    public bool BeginReturn()
    {
        if (IsIdle || Phase == PhysicalShelfLaunchPhase.Return || _reversing)
        {
            return false;
        }

        if (IsCommitted)
        {
            _reversing = true;
            _phaseElapsedMilliseconds = 0d;
            Phase = Sequence[^1];
            Pose = Pose with { Phase = Phase };
            return true;
        }

        _returnStart = Pose;
        _phaseElapsedMilliseconds = 0d;
        Phase = PhysicalShelfLaunchPhase.Return;
        Pose = Pose with { Phase = Phase };
        return true;
    }

    /// <summary>Whether the launch is currently playing backwards toward the shelf.</summary>
    public bool IsReversing => _reversing;

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
            case PhysicalShelfLaunchPhase.Reveal:
                UpdateReveal();
                break;
            case PhysicalShelfLaunchPhase.Flip:
                UpdateFlip();
                break;
            case PhysicalShelfLaunchPhase.SpinUp:
                UpdateSpinUp();
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
        _reversing = false;
        Pose = default;
    }

    private bool IsDisc => _style == PhysicalShelfLaunchStyle.Disc;

    /// <summary>The disc as it sits inside the closed case, hidden by the case's own front.</summary>
    /// <remarks>
    /// Null for a cartridge, which is what stops the renderer drawing a disc for a Game Pak. For a
    /// case it is present from the first frame rather than appearing at the reveal: the case is
    /// opaque and encloses it, so there is nothing to see until it comes out, and a pose that
    /// exists throughout is one the return can interpolate back into without a pop.
    /// </remarks>
    private PhysicalShelfDiscPose? StowedDisc() =>
        IsDisc ? new PhysicalShelfDiscPose(0f, 0f, 0f, 0f, 0f, 1f) : null;

    private void UpdateLift()
    {
        var duration = _reducedMotion ? 180d : LiftDurationMilliseconds;
        var t = Progress(duration);
        var eased = EaseOutCubic(t);
        // A case squares up to the player on the way up, where a cartridge stops at the resting
        // angle it will spin from. The disc is about to come out of this one face-on, and pulling
        // it out of a case still turned a few degrees away reads as the disc passing through it.
        var squaresUp = _reducedMotion || IsDisc;
        Pose = Pose with
        {
            Phase = Phase,
            Yaw = Lerp(
                _startYaw,
                squaresUp ? InsertionYaw : MediaRotationModel.RestYaw,
                eased),
            Pitch = Lerp(
                _startPitch,
                squaresUp ? InsertionPitch : MediaRotationModel.RestPitch,
                eased),
            VerticalOffset = HeroLift * eased,
            DepthOffset = HeroDepth * eased,
            Scale = Lerp(1f, HeroScale, eased),
            // Still in the case, so it rides with it.
            Disc = IsDisc
                ? new PhysicalShelfDiscPose(0f, HeroLift * eased, HeroDepth * eased, 0f, 0f, 1f)
                : null,
        };
        Advance();
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
        Advance();
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
            // sees the same orientation, while a return can now interpolate without spinning back.
            Pose = Pose with { Yaw = InsertionYaw, Pitch = InsertionPitch };
        }

        Advance();
    }

    /// <summary>
    /// The disc slides sideways out of the case, which stays where the lift left it.
    /// </summary>
    /// <remarks>
    /// Everything here is chosen to keep the case in front of the disc for as long as possible.
    /// Depth is held at the case's own, so the disc is uncovered edge-first instead of appearing;
    /// the rise is slight and eased out, so most of the travel is the sideways part that the
    /// occlusion actually depends on.
    /// </remarks>
    private void UpdateReveal()
    {
        var t = Progress(RevealDurationMilliseconds);
        var eased = EaseOutCubic(t);
        Pose = Pose with
        {
            Phase = Phase,
            Yaw = InsertionYaw,
            Pitch = InsertionPitch,
            VerticalOffset = HeroLift,
            DepthOffset = HeroDepth,
            Scale = HeroScale,
            Disc = new PhysicalShelfDiscPose(
                DiscSlideOut * eased,
                // Level with the case, not above it. A disc sits centred in its case, so any rise
                // here is the disc drifting off the centre it came out of — visible as soon as the
                // case stopped dropping away and the two were on screen together to compare.
                HeroLift,
                // Held, not lerped. See DiscSlideOut: this is the whole trick.
                HeroDepth,
                DiscSlideTurns * MathF.Tau * eased,
                0f,
                1f),
        };
        Advance();
    }

    /// <summary>
    /// The disc turns over where it stands, showing the player both of its faces.
    /// </summary>
    /// <remarks>
    /// It comes forward out of the case's depth first, and has to: it is still overlapping the case
    /// sideways at this point, and a disc turning about the up axis sweeps through the depth it
    /// occupies — flipped where it stood, it would pass through the case it just came out of.
    ///
    /// Three turns, so the data side comes round to the player and back an odd number of times and
    /// the disc finishes facing the way it started. The count is the one the cartridge sequence
    /// spends tumbling; here it buys a look at the face that a disc actually has and a box does not.
    /// </remarks>
    private void UpdateFlip()
    {
        var t = Progress(FlipDurationMilliseconds);
        var eased = EaseInOutCubic(t);
        Pose = Pose with
        {
            Phase = Phase,
            Yaw = InsertionYaw,
            Pitch = InsertionPitch,
            VerticalOffset = HeroLift,
            DepthOffset = HeroDepth,
            Scale = HeroScale,
            Disc = new PhysicalShelfDiscPose(
                DiscSlideOut,
                HeroLift,
                // Forward first, and quickly, so the sweep clears the case before it begins.
                Lerp(HeroDepth, HeroDepth + DiscRevealDepth, EaseOutCubic(MathF.Min(t * 2.5f, 1f))),
                DiscSlideTurns * MathF.Tau,
                0f,
                1f,
                DiscFlipTurns * MathF.Tau * eased),
        };
        Advance();
    }

    /// <summary>The emptied case settles back onto the shelf while the disc centres and spins up.</summary>
    private void UpdateSpinUp()
    {
        var t = Progress(SpinUpDurationMilliseconds);
        var eased = EaseInOutCubic(t);
        var spin = EaseInCubic(t);
        Pose = Pose with
        {
            Phase = Phase,
            Yaw = InsertionYaw,
            Pitch = InsertionPitch,
            // Back to the pose it browses at: down from the hero lift, back from the camera and
            // out of its presentation scale. The case is being put down, not taken away.
            VerticalOffset = Lerp(HeroLift, 0f, eased),
            DepthOffset = Lerp(HeroDepth, 0f, eased),
            Scale = Lerp(HeroScale, 1f, eased),
            Disc = new PhysicalShelfDiscPose(
                // Back to centre. Reads as the disc being brought to the front rather than as a
                // reversal, because it is growing and accelerating while it does.
                Lerp(DiscSlideOut, 0f, eased),
                Lerp(HeroLift, DiscSpinUpLift, eased),
                // Already forward: the flip brought it out of the case's depth.
                HeroDepth + DiscRevealDepth,
                (DiscSlideTurns * MathF.Tau) + (DiscSpinUpTurns * MathF.Tau * spin),
                // Begins to lie back before the drop, so the tray is already being met rather than
                // the disc snapping flat at the last moment.
                DiscTrayTilt * 0.28f * eased,
                Lerp(1f, DiscSpinUpScale, eased),
                DiscFlipTurns * MathF.Tau),
        };
        Advance();
    }

    private void UpdateInsert()
    {
        if (IsDisc && !_reducedMotion)
        {
            UpdateDiscInsert();
            return;
        }

        var duration = _reducedMotion ? 630d : InsertDurationMilliseconds;
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
            // Reduced motion never opens the case, so its disc stays stowed and travels with it.
            Disc = IsDisc
                ? new PhysicalShelfDiscPose(
                    0f,
                    Lerp(HeroLift, InsertedVerticalOffset, eased),
                    Lerp(HeroDepth, InsertedDepthOffset, eased),
                    0f,
                    0f,
                    1f)
                : null,
        };
        Advance();
    }

    /// <summary>The disc lies flat and drops out of frame, still turning, into the drive.</summary>
    private void UpdateDiscInsert()
    {
        var t = Progress(DiscInsertDurationMilliseconds);
        var eased = EaseInCubic(t);
        Pose = Pose with
        {
            Phase = Phase,
            Yaw = InsertionYaw,
            Pitch = InsertionPitch,
            // The case is back on the shelf and stays there. Only the disc is still moving.
            VerticalOffset = 0f,
            DepthOffset = 0f,
            Scale = 1f,
            Disc = new PhysicalShelfDiscPose(
                0f,
                Lerp(DiscSpinUpLift, DiscInsertedVerticalOffset, eased),
                HeroDepth + DiscRevealDepth,
                (DiscSlideTurns * MathF.Tau)
                    + (DiscSpinUpTurns * MathF.Tau)
                    + (DiscInsertTurns * MathF.Tau * eased),
                Lerp(DiscTrayTilt * 0.28f, DiscTrayTilt, EaseInOutCubic(t)),
                Lerp(DiscSpinUpScale, 1f, eased),
                DiscFlipTurns * MathF.Tau),
        };
        Advance();
    }

    private void UpdateReturn()
    {
        var t = Progress(_reducedMotion ? 330d : ReturnDurationMilliseconds);
        var eased = EaseOutCubic(t);
        Pose = new PhysicalShelfLaunchPose(
            _returnStart.GameId,
            Phase,
            Lerp(_returnStart.Yaw, _startYaw, eased),
            Lerp(_returnStart.Pitch, _startPitch, eased),
            Lerp(_returnStart.VerticalOffset, 0f, eased),
            Lerp(_returnStart.DepthOffset, 0f, eased),
            Lerp(_returnStart.Scale, 1f, eased),
            // The disc goes back into the case as the case comes back to the shelf. Interpolating
            // the spin back to zero unwinds it rather than letting it stop dead, which is the one
            // part of this that a real disc would not do — and is still better than a hard cut.
            ReturnedDisc(eased));
        if (t >= 1f)
        {
            Reset();
        }
    }

    private PhysicalShelfDiscPose? ReturnedDisc(float eased)
    {
        if (_returnStart.Disc is not { } from)
        {
            return null;
        }

        return new PhysicalShelfDiscPose(
            Lerp(from.HorizontalOffset, 0f, eased),
            Lerp(from.VerticalOffset, 0f, eased),
            Lerp(from.DepthOffset, 0f, eased),
            Lerp(from.Spin, 0f, eased),
            Lerp(from.Tilt, 0f, eased),
            Lerp(from.Scale, 1f, eased),
            Lerp(from.Flip, 0f, eased));
    }

    private void Commit()
    {
        Phase = PhysicalShelfLaunchPhase.Committed;
        Pose = Pose with { Phase = Phase };
    }

    /// <summary>
    /// How far through the current phase the pose is, running 1 to 0 while reversing.
    /// </summary>
    /// <remarks>
    /// Inverting one number is the whole of reversal, and that is the point of it: every handler
    /// below computes its pose from this and nothing else, so the way back is the way out played
    /// through the same arithmetic rather than through a second choreography that has to be kept in
    /// step with the first.
    /// </remarks>
    private float Progress(double duration)
    {
        var t = (float)Math.Clamp(_phaseElapsedMilliseconds / duration, 0d, 1d);
        return _reversing ? 1f - t : t;
    }

    /// <summary>
    /// Moves to the next phase in the sequence, or the previous one while reversing.
    /// </summary>
    /// <remarks>
    /// Running off the end forward is the handover — the medium is gone and the emulator can start.
    /// Running off the start backwards is the medium home on the shelf, which is where a reversal
    /// finishes and where the model becomes idle again.
    /// </remarks>
    private void Advance()
    {
        if (_phaseElapsedMilliseconds < PhaseDuration)
        {
            return;
        }

        var sequence = Sequence;
        var next = Array.IndexOf(sequence, Phase) + (_reversing ? -1 : 1);
        if (next < 0)
        {
            Reset();
            return;
        }

        if (next >= sequence.Length)
        {
            Commit();
            return;
        }

        Phase = sequence[next];
        _phaseElapsedMilliseconds = 0d;
        Pose = Pose with { Phase = Phase };
    }

    private static float Lerp(float from, float to, float amount) => from + ((to - from) * amount);

    private static float EaseOutCubic(float value) => 1f - MathF.Pow(1f - value, 3f);

    private static float EaseInCubic(float value) => value * value * value;

    private static float EaseInOutCubic(float value) => value < 0.5f
        ? 4f * value * value * value
        : 1f - (MathF.Pow((-2f * value) + 2f, 3f) * 0.5f);
}
