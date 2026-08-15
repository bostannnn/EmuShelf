using EmuShelf.App.Services;

namespace EmuShelf.App.Tests;

public class PhysicalShelfLaunchTransitionModelTests
{
    [Fact]
    public void FullSequence_LiftsTurnsExactlyThreeTimesAndHoldsInserted()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(42, PhysicalShelfLaunchStyle.Cartridge, yaw: 0.7f, pitch: 0.2f);

        AdvanceUntil(model, PhysicalShelfLaunchPhase.Spin);
        // Relationships, not magnitudes: how far the medium lifts and steps forward is set by what
        // the shelf camera can frame, and the camera now moves with the tallest medium on show.
        // MediaShellTests.LaunchChoreography_StaysInsideTheShelfCameraFrame owns the magnitudes.
        Assert.True(model.Pose.VerticalOffset > 0f);
        Assert.True(model.Pose.DepthOffset > 0f);
        Assert.True(model.Pose.Scale > 1.05f);

        AdvanceUntil(model, PhysicalShelfLaunchPhase.Align);
        Assert.Equal(
            MediaRotationModel.RestYaw + (3f * MathF.Tau),
            model.Pose.Yaw,
            3);

        AdvanceUntil(model, PhysicalShelfLaunchPhase.Committed);
        Assert.True(model.IsCommitted);
        Assert.Equal(42, model.Pose.GameId);
        Assert.True(model.Pose.VerticalOffset < -0.8f);
        Assert.Equal(1f, model.Pose.Scale, 3);
        Assert.Equal(0f, model.Pose.Yaw, 3);
        Assert.Equal(0f, model.Pose.Pitch, 3);

        var held = model.Pose;
        Assert.False(model.Update(1000d));
        Assert.Equal(held, model.Pose);
    }

    [Fact]
    public void Return_RestoresTheExactCapturedShelfPose()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(7, PhysicalShelfLaunchStyle.Cartridge, yaw: 0.91f, pitch: -0.31f);
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Committed);

        Assert.True(model.BeginReturn());
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Idle);

        Assert.True(model.IsIdle);
        Assert.Equal(default(PhysicalShelfLaunchPose), model.Pose);
    }

    [Fact]
    public void ReducedMotion_SkipsTheSpinPhase()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(
            3,
            PhysicalShelfLaunchStyle.Cartridge,
            MediaRotationModel.RestYaw,
            MediaRotationModel.RestPitch,
            reducedMotion: true);
        var phases = new HashSet<PhysicalShelfLaunchPhase>();

        while (!model.IsCommitted)
        {
            phases.Add(model.Phase);
            model.Update(16d);
        }

        Assert.Contains(PhysicalShelfLaunchPhase.Lift, phases);
        Assert.Contains(PhysicalShelfLaunchPhase.Insert, phases);
        Assert.DoesNotContain(PhysicalShelfLaunchPhase.Spin, phases);
        Assert.DoesNotContain(PhysicalShelfLaunchPhase.Align, phases);
    }

    [Fact]
    public void ReturningDuringLift_ReversesFromTheCurrentPoseWithoutJumping()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(9, PhysicalShelfLaunchStyle.Cartridge, yaw: -0.8f, pitch: 0.25f);
        model.Update(160d);
        var interrupted = model.Pose;

        Assert.True(model.BeginReturn());
        Assert.Equal(interrupted with { Phase = PhysicalShelfLaunchPhase.Return }, model.Pose);
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Idle);

        Assert.True(model.IsIdle);
    }

    /// <summary>
    /// A cartridge's launch has no disc at any point, which is what keeps a Game Pak from throwing
    /// one out of itself.
    /// </summary>
    [Fact]
    public void CartridgeSequence_NeverProducesADisc()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(11, PhysicalShelfLaunchStyle.Cartridge, yaw: 0.2f, pitch: 0f);

        for (var frame = 0; frame < 500 && !model.IsCommitted; frame++)
        {
            Assert.Null(model.Pose.Disc);
            model.Update(16d);
        }

        Assert.Null(model.Pose.Disc);
    }

    [Fact]
    public void DiscSequence_TakesTheDiscOutAndLeavesTheCaseOnTheShelf()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(5, PhysicalShelfLaunchStyle.Disc, yaw: 0.4f, pitch: -0.1f);

        // Stowed while the case is still closed: the case encloses it, so any separation here
        // would be the disc hanging visibly out of an unopened box.
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Reveal);
        Assert.Equal(0f, model.Pose.Disc!.Value.DepthOffset - model.Pose.DepthOffset, 3);
        Assert.Equal(0f, model.Pose.Disc!.Value.HorizontalOffset, 3);

        // Slid clear of the case's edge, and the case has not moved yet.
        AdvanceUntil(model, PhysicalShelfLaunchPhase.SpinUp);
        var revealed = model.Pose;
        Assert.True(revealed.Disc!.Value.HorizontalOffset > 0.3f);
        Assert.True(revealed.VerticalOffset > 0f);

        // Out in front of the case only once it is being set down.
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Insert);
        Assert.True(model.Pose.Disc!.Value.DepthOffset > revealed.DepthOffset + 0.1f);

        // The case settles back onto the shelf rather than dropping through it: nothing is being
        // pushed into a console here, so the only body that leaves is the disc.
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Insert);
        var emptied = model.Pose;
        Assert.Equal(0f, emptied.VerticalOffset, 3);
        Assert.Equal(0f, emptied.DepthOffset, 3);
        Assert.Equal(1f, emptied.Scale, 3);
        Assert.True(emptied.Disc!.Value.VerticalOffset > 0f);

        // And the disc leaves alone, lying flat the way a tray takes it, with the case still there.
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Committed);
        Assert.True(model.Pose.Disc!.Value.VerticalOffset < -0.9f);
        Assert.Equal(-MathF.PI / 2f, model.Pose.Disc!.Value.Tilt, 3);
        Assert.Equal(0f, model.Pose.VerticalOffset, 3);
    }

    /// <summary>
    /// The three turns a cartridge spends tumbling are the disc's, and they are spent on its own
    /// axis. It must never turn backwards: a disc that stalls or reverses mid-launch is a drive
    /// nobody has ever heard.
    /// </summary>
    [Fact]
    public void DiscSequence_SpinsForwardThroughoutAndAcceleratesIntoTheHandover()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(5, PhysicalShelfLaunchStyle.Disc, yaw: 0f, pitch: 0f);
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Reveal);

        var previous = model.Pose.Disc!.Value.Spin;
        var fastestSlide = 0f;
        var fastestDrop = 0f;
        while (!model.IsCommitted)
        {
            var phase = model.Phase;
            model.Update(16d);
            var spin = model.Pose.Disc!.Value.Spin;
            var step = spin - previous;
            Assert.True(step >= -1e-4f, $"The disc turned back by {step:F4} radians.");

            // Peak rate per phase rather than first frame against last. The slide is eased out, so
            // it is at its quickest on its very first frame — comparing endpoints measures the
            // easing rather than the choreography, and would call a launch that plainly speeds up
            // a launch that slows down.
            if (phase == PhysicalShelfLaunchPhase.Reveal)
            {
                fastestSlide = MathF.Max(fastestSlide, step);
            }
            else if (phase == PhysicalShelfLaunchPhase.Insert)
            {
                fastestDrop = MathF.Max(fastestDrop, step);
            }

            previous = spin;
        }

        Assert.True(previous > 4f * MathF.Tau);
        Assert.True(
            fastestDrop > fastestSlide * 2f,
            $"The disc peaks at {fastestDrop:F4} radians a frame as it goes into the drive against "
            + $"{fastestSlide:F4} coming out of the case; the handover is supposed to be the "
            + "fastest the disc ever turns.");
    }

    /// <summary>
    /// The disc must stay at the case's own depth for every frame of the slide.
    /// </summary>
    /// <remarks>
    /// This is what makes the reveal a reveal. The case is opaque and encloses the disc, so holding
    /// the two at one depth is what lets the case's front uncover the disc edge-first as it travels;
    /// a disc that steps toward the camera has stepped in front of the case instead, and then it
    /// does not emerge from anywhere — it simply appears, which is how the first version of this
    /// looked and the reason the phase is split from the one that follows it.
    /// </remarks>
    [Fact]
    public void DiscSequence_StaysBehindTheCaseForTheWholeSlide()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(12, PhysicalShelfLaunchStyle.Disc, yaw: 0f, pitch: 0f);
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Reveal);

        var travelled = 0f;
        while (model.Phase == PhysicalShelfLaunchPhase.Reveal)
        {
            var pose = model.Pose;
            Assert.Equal(pose.DepthOffset, pose.Disc!.Value.DepthOffset, 4);
            travelled = MathF.Max(travelled, pose.Disc!.Value.HorizontalOffset);
            model.Update(16d);
        }

        // And it really does travel far enough to be uncovered: a case is 0.711 wide and a disc
        // 0.632 across, so this clears the case's edge by about a third of the disc.
        Assert.True(travelled > 0.35f, $"The disc only slid {travelled:F3} out of the case.");
    }

    /// <summary>
    /// Reduced motion is a plain lift and dip for every medium. Opening the case is the part with
    /// all the movement in it, so it is the part that goes.
    /// </summary>
    [Fact]
    public void DiscSequence_ReducedMotionKeepsTheDiscInTheCase()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(6, PhysicalShelfLaunchStyle.Disc, yaw: 0f, pitch: 0f, reducedMotion: true);
        var phases = new HashSet<PhysicalShelfLaunchPhase>();

        while (!model.IsCommitted)
        {
            phases.Add(model.Phase);
            Assert.Equal(0f, model.Pose.Disc!.Value.Spin, 5);
            Assert.Equal(
                model.Pose.VerticalOffset, model.Pose.Disc!.Value.VerticalOffset, 3);
            model.Update(16d);
        }

        Assert.Contains(PhysicalShelfLaunchPhase.Lift, phases);
        Assert.Contains(PhysicalShelfLaunchPhase.Insert, phases);
        Assert.DoesNotContain(PhysicalShelfLaunchPhase.Reveal, phases);
        Assert.DoesNotContain(PhysicalShelfLaunchPhase.SpinUp, phases);
    }

    [Fact]
    public void DiscSequence_ReturnPutsTheDiscBackInTheCase()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(8, PhysicalShelfLaunchStyle.Disc, yaw: 0.5f, pitch: -0.2f);
        AdvanceUntil(model, PhysicalShelfLaunchPhase.SpinUp);

        Assert.True(model.BeginReturn());
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Idle);

        Assert.True(model.IsIdle);
        Assert.Equal(default(PhysicalShelfLaunchPose), model.Pose);
    }

    /// <summary>
    /// Closing the game plays the launch backwards: the disc comes out of the drive, spins down and
    /// goes back into its case.
    /// </summary>
    /// <remarks>
    /// Walked phase by phase in reverse order, because that is the property worth pinning — the
    /// reversal is the forward arithmetic with one number inverted, so anything that reaches the
    /// end by some other route has stopped being a mirror of what the player watched.
    /// </remarks>
    [Fact]
    public void ClosingTheGame_PlaysTheDiscLaunchBackwards()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(21, PhysicalShelfLaunchStyle.Disc, yaw: 0f, pitch: 0f);
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Committed);

        Assert.True(model.BeginReturn());
        Assert.True(model.IsReversing);

        // Never the one-shot Return: that is for a launch abandoned part way, which never happened.
        var visited = new List<PhysicalShelfLaunchPhase>();
        for (var frame = 0; frame < 500 && !model.IsIdle; frame++)
        {
            if (visited.Count == 0 || visited[^1] != model.Phase)
            {
                visited.Add(model.Phase);
            }

            Assert.NotEqual(PhysicalShelfLaunchPhase.Return, model.Phase);
            model.Update(16d);
        }

        Assert.Equal(
            [
                PhysicalShelfLaunchPhase.Insert,
                PhysicalShelfLaunchPhase.SpinUp,
                PhysicalShelfLaunchPhase.Flip,
                PhysicalShelfLaunchPhase.Reveal,
                PhysicalShelfLaunchPhase.Lift,
            ],
            visited);
        Assert.True(model.IsIdle);
        Assert.Equal(default(PhysicalShelfLaunchPose), model.Pose);
    }

    /// <summary>
    /// A launch that never finished eases back from where it got to instead of replaying a story
    /// that did not happen.
    /// </summary>
    [Fact]
    public void AbandoningADiscLaunch_EasesBackRatherThanReversing()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(22, PhysicalShelfLaunchStyle.Disc, yaw: 0.3f, pitch: 0f);
        AdvanceUntil(model, PhysicalShelfLaunchPhase.SpinUp);

        Assert.True(model.BeginReturn());
        Assert.False(model.IsReversing);
        Assert.Equal(PhysicalShelfLaunchPhase.Return, model.Phase);

        AdvanceUntil(model, PhysicalShelfLaunchPhase.Idle);
        Assert.True(model.IsIdle);
    }

    /// <summary>
    /// A disc launch is longer than a cartridge's by exactly the flip, and by nothing else.
    /// </summary>
    /// <remarks>
    /// The two used to hand over together, on the reasoning that how long a player waits should not
    /// depend on what the game shipped on. The flip broke that deliberately: a disc has a second
    /// face worth showing and a cartridge does not. Pinned to the flip's own duration rather than
    /// dropped, so the sequences cannot drift apart by anything nobody chose — which is the part
    /// the original test was really protecting.
    /// </remarks>
    [Fact]
    public void ADiscLaunchIsLongerThanACartridgeByExactlyTheFlip()
    {
        Assert.Equal(
            ElapsedToCommit(PhysicalShelfLaunchStyle.Cartridge)
                + PhysicalShelfLaunchTransitionModel.FlipDurationMilliseconds,
            ElapsedToCommit(PhysicalShelfLaunchStyle.Disc));
    }

    /// <summary>
    /// The flip turns the disc a whole number of times, so it ends facing the way it began.
    /// </summary>
    [Fact]
    public void DiscSequence_FlipsAWholeNumberOfTurnsAndShowsTheFarFace()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(31, PhysicalShelfLaunchStyle.Disc, yaw: 0f, pitch: 0f);
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Flip);

        Assert.Equal(0f, model.Pose.Disc!.Value.Flip, 3);

        var showedFarFace = false;
        while (model.Phase == PhysicalShelfLaunchPhase.Flip)
        {
            model.Update(16d);
            // Past a quarter turn and short of three quarters is the player looking at the back.
            var quarter = MathF.IEEERemainder(model.Pose.Disc!.Value.Flip, MathF.Tau) / MathF.Tau;
            showedFarFace |= MathF.Abs(quarter) > 0.25f;
        }

        Assert.True(showedFarFace, "The disc never turned far enough to show its data side.");
        Assert.Equal(3f * MathF.Tau, model.Pose.Disc!.Value.Flip, 2);

        // And it holds that whole number for the rest of the launch rather than snapping back.
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Committed);
        Assert.Equal(3f * MathF.Tau, model.Pose.Disc!.Value.Flip, 2);
    }

    private static double ElapsedToCommit(PhysicalShelfLaunchStyle style)
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(1, style, yaw: 0f, pitch: 0f);

        var elapsed = 0d;
        for (var frame = 0; frame < 1000 && !model.IsCommitted; frame++)
        {
            model.Update(4d);
            elapsed += 4d;
        }

        Assert.True(model.IsCommitted);
        return elapsed;
    }

    private static void AdvanceUntil(
        PhysicalShelfLaunchTransitionModel model,
        PhysicalShelfLaunchPhase target)
    {
        for (var frame = 0; frame < 500 && model.Phase != target; frame++)
        {
            model.Update(16d);
        }

        Assert.Equal(target, model.Phase);
    }
}
