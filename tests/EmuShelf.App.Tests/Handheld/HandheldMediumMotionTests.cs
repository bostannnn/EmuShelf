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
    public void ANewGameAtRest_StaysAtRest_NothingRestarts()
    {
        var motion = new HandheldMediumMotion();

        Assert.False(motion.Recentre());

        Assert.False(motion.IsSettling);
        Assert.Equal(MediaRotationModel.RestYaw, motion.Yaw, 3);
    }

    [Fact]
    public void ANewGameMidSway_ContinuesFromThePoseOnScreen_AndSettles()
    {
        var motion = new HandheldMediumMotion();
        Run(motion, 3000, 50); // swaying
        var onScreen = motion.Yaw;
        Assert.NotEqual(MediaRotationModel.RestYaw, onScreen, 4);

        motion.Recentre(); // the selection changed

        Assert.Equal(onScreen, motion.Yaw, 4); // no jump
        Run(motion, 1500);
        Assert.Equal(MediaRotationModel.RestYaw, motion.Yaw, 3);
    }

    [Fact]
    public void ChangingGamesMidSettle_KeepsTheSpringsSpeed()
    {
        var motion = new HandheldMediumMotion();
        for (var i = 0; i < 20; i++)
            motion.ApplyStick(1f, 0f, 16);
        motion.Recentre();
        motion.Tick(48);
        var before = motion.Yaw;
        motion.Tick(16);
        var stepBefore = motion.Yaw - before;

        motion.Recentre(); // another game, mid-swing
        var after = motion.Yaw;
        motion.Tick(16);
        var stepAfter = motion.Yaw - after;

        Assert.Equal(before + stepBefore, after, 4); // retargeting did not move it
        Assert.True(Math.Abs(stepAfter) > Math.Abs(stepBefore) * 0.8); // and the swing kept its speed
    }

    [Fact]
    public void TheSpring_OvershootsOnceRatherThanEasing()
    {
        var motion = new HandheldMediumMotion();
        for (var i = 0; i < 20; i++)
            motion.ApplyStick(1f, 0f, 16);
        motion.Recentre();
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
