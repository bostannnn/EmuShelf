using System;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public class HandheldMediumMotionTests
{
    private static void Run(HandheldMediumMotion motion, double milliseconds, double step = 16)
    {
        for (var elapsed = 0d; elapsed < milliseconds; elapsed += step)
            motion.Tick(step);
    }

    [Fact]
    public void TurnIn_StartsTurnedFromTheSide_AndSpringsToTheRestingPose()
    {
        var motion = new HandheldMediumMotion();

        motion.TurnIn(1);

        Assert.True(motion.IsSettling);
        Assert.Equal(MediaRotationModel.RestYaw + HandheldMediumMotion.TurnInYaw, motion.Yaw, 3);

        Run(motion, 1500);

        Assert.False(motion.IsSettling);
        Assert.Equal(MediaRotationModel.RestYaw, motion.Yaw, 3);
    }

    [Fact]
    public void TurnIn_FromTheLeft_StartsOnTheOtherSide()
    {
        var motion = new HandheldMediumMotion();

        motion.TurnIn(-1);

        Assert.Equal(MediaRotationModel.RestYaw - HandheldMediumMotion.TurnInYaw, motion.Yaw, 3);
    }

    [Fact]
    public void TheSpring_OvershootsOnceRatherThanEasing()
    {
        var motion = new HandheldMediumMotion();
        motion.TurnIn(1);
        var crossed = false;
        for (var i = 0; i < 60; i++)
        {
            motion.Tick(16);
            crossed |= motion.Yaw < MediaRotationModel.RestYaw - 0.005f;
        }

        Assert.True(crossed);
    }

    [Fact]
    public void RightStick_TurnsTheMedium_AndR3SwingsItBackWithoutAJump()
    {
        var motion = new HandheldMediumMotion();
        for (var i = 0; i < 20; i++)
            motion.ApplyStick(1f, 0f, 16);
        var turned = motion.Yaw;
        Assert.True(turned > MediaRotationModel.RestYaw + 0.5f);

        Assert.True(motion.Recentre());

        // The swing starts from the pose on screen…
        Assert.Equal(turned, motion.Yaw, 3);
        Run(motion, 1500);
        // …and ends at rest.
        Assert.Equal(MediaRotationModel.RestYaw, motion.Yaw, 3);
        Assert.Equal(MediaRotationModel.RestPitch, motion.Pitch, 3);
    }

    [Fact]
    public void AStickInsideTheDeadzone_DoesNothing()
    {
        var motion = new HandheldMediumMotion();

        Assert.False(motion.ApplyStick(0.1f, 0.05f, 16));
        Assert.Equal(MediaRotationModel.RestYaw, motion.Yaw, 3);
    }

    [Fact]
    public void AtRest_TheMediumSwaysAfterItsDelay()
    {
        var motion = new HandheldMediumMotion();
        Assert.True(motion.IsSwaying);

        Run(motion, 3000, 50);

        Assert.NotEqual(MediaRotationModel.RestYaw, motion.Yaw, 4);
    }
}
