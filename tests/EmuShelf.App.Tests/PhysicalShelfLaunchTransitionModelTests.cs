using EmuShelf.App.Services;

namespace EmuShelf.App.Tests;

public class PhysicalShelfLaunchTransitionModelTests
{
    [Fact]
    public void FullSequence_LiftsTurnsExactlyThreeTimesAndHoldsInserted()
    {
        var model = new PhysicalShelfLaunchTransitionModel();
        model.Start(42, yaw: 0.7f, pitch: 0.2f);

        AdvanceUntil(model, PhysicalShelfLaunchPhase.Spin);
        Assert.True(model.Pose.VerticalOffset > 0.2f);
        Assert.True(model.Pose.DepthOffset > 0.35f);
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
        model.Start(7, yaw: 0.91f, pitch: -0.31f);
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
        model.Start(3, MediaRotationModel.RestYaw, MediaRotationModel.RestPitch, reducedMotion: true);
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
        model.Start(9, yaw: -0.8f, pitch: 0.25f);
        model.Update(160d);
        var interrupted = model.Pose;

        Assert.True(model.BeginReturn());
        Assert.Equal(interrupted with { Phase = PhysicalShelfLaunchPhase.Return }, model.Pose);
        AdvanceUntil(model, PhysicalShelfLaunchPhase.Idle);

        Assert.True(model.IsIdle);
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
