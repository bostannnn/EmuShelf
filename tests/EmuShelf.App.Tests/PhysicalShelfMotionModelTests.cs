using EmuShelf.App.Services;

namespace EmuShelf.App.Tests;

public class PhysicalShelfMotionModelTests
{
    [Fact]
    public void SnapTo_LandsImmediatelyAndClearsVelocity()
    {
        var model = new PhysicalShelfMotionModel();
        model.MoveTo(3);
        model.Update(16);

        model.SnapTo(7);

        Assert.Equal(7d, model.Position);
        Assert.Equal(7, model.TargetIndex);
        Assert.Equal(0d, model.Velocity);
        Assert.True(model.IsSettled);
    }

    [Fact]
    public void Update_CarriesContinuouslyTowardTargetWithoutOvershoot()
    {
        var model = new PhysicalShelfMotionModel();
        model.MoveTo(1);

        var positions = new List<double>();
        for (var i = 0; i < 60; i++)
        {
            model.Update(16);
            positions.Add(model.Position);
        }

        Assert.All(positions, position => Assert.InRange(position, 0d, 1d));
        Assert.True(positions.Zip(positions.Skip(1), (a, b) => b >= a).All(value => value));
        Assert.Equal(1d, model.Position, 3);
    }

    [Fact]
    public void Retargeting_PreservesContinuousMotion()
    {
        var model = new PhysicalShelfMotionModel();
        model.MoveTo(1);
        model.Update(50);
        var before = model.Position;

        model.MoveTo(2);
        model.Update(16);

        Assert.True(model.Position > before);
        Assert.True(model.Position < 2d);
    }

    [Fact]
    public void StalledTick_IsCapped()
    {
        var stalled = new PhysicalShelfMotionModel();
        var capped = new PhysicalShelfMotionModel();
        stalled.MoveTo(1);
        capped.MoveTo(1);

        stalled.Update(5000);
        capped.Update(50);

        Assert.Equal(capped.Position, stalled.Position, 8);
        Assert.Equal(capped.Velocity, stalled.Velocity, 8);
    }

    [Fact]
    public void ReducedMotion_LandsOnTheTargetWithoutASecondAnimationPath()
    {
        var model = new PhysicalShelfMotionModel();
        model.MoveTo(1);
        model.Update(16);

        Assert.True(model.MoveTo(4, reducedMotion: true));
        Assert.Equal(4d, model.Position);
        Assert.Equal(4, model.TargetIndex);
        Assert.Equal(0d, model.Velocity);
        Assert.True(model.IsSettled);
    }
}
