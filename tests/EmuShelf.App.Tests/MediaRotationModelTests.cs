using EmuShelf.App.Services;

namespace EmuShelf.App.Tests;

/// <summary>
/// Pins down the feel of the shelf hero's rotation. The hero itself cannot be asserted on — it is a
/// GPU surface — so this is the part of Phase 3 that a test can actually hold to account.
/// </summary>
public class MediaRotationModelTests
{
    [Fact]
    public void StartsAtTheRestingPose()
    {
        var model = new MediaRotationModel();

        Assert.True(model.IsAtRest);
        Assert.Equal(MediaRotationModel.RestYaw, model.Yaw);
        Assert.Equal(MediaRotationModel.RestPitch, model.Pitch);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.1f, 0f)]
    [InlineData(0f, -0.14f)]
    // Inside the radial deadzone even though neither axis is: a resting stick must not creep.
    [InlineData(0.1f, 0.1f)]
    public void IgnoresDeflectionInsideTheDeadzone(float x, float y)
    {
        var model = new MediaRotationModel();

        Assert.False(model.Update(x, y, 16d));
        Assert.True(model.IsAtRest);
    }

    [Fact]
    public void TurnsFasterTheFurtherTheStickIsPushed()
    {
        var gentle = new MediaRotationModel();
        var firm = new MediaRotationModel();

        gentle.Update(0.45f, 0f, 100d);
        firm.Update(1f, 0f, 100d);

        var gentleTravel = Math.Abs(gentle.Yaw - MediaRotationModel.RestYaw);
        var firmTravel = Math.Abs(firm.Yaw - MediaRotationModel.RestYaw);

        Assert.True(gentleTravel > 0f);
        // Squared response: half deflection must be well under half speed, or fine control near
        // centre is impossible.
        Assert.True(firmTravel > gentleTravel * 3f, $"gentle {gentleTravel}, firm {firmTravel}");
    }

    [Fact]
    public void TravelIsProportionalToElapsedTime()
    {
        var slowTick = new MediaRotationModel();
        var doubleTick = new MediaRotationModel();

        slowTick.Update(1f, 0f, 20d);
        doubleTick.Update(1f, 0f, 40d);

        var single = Math.Abs(slowTick.Yaw - MediaRotationModel.RestYaw);
        var doubled = Math.Abs(doubleTick.Yaw - MediaRotationModel.RestYaw);

        Assert.Equal(single * 2f, doubled, 4);
    }

    [Fact]
    public void ClampsAnEnormousTickSoAStallCannotSpinTheHero()
    {
        var stalled = new MediaRotationModel();
        var capped = new MediaRotationModel();

        stalled.Update(1f, 0f, 5_000d);
        capped.Update(1f, 0f, 100d);

        Assert.Equal(capped.Yaw, stalled.Yaw, 4);
    }

    [Fact]
    public void PitchIsClampedButYawKeepsGoing()
    {
        var model = new MediaRotationModel();

        for (var i = 0; i < 400; i++)
        {
            model.Update(1f, 1f, 100d);
        }

        var maxPitch = 60f * MathF.PI / 180f;
        Assert.Equal(maxPitch, model.Pitch, 4);
        // Yaw wraps rather than clamping: you can keep turning the medium over indefinitely.
        Assert.InRange(model.Yaw, -MathF.PI, MathF.PI);
    }

    [Fact]
    public void YawWrapsInsteadOfGrowingWithoutBound()
    {
        var model = new MediaRotationModel();

        for (var i = 0; i < 200; i++)
        {
            model.Update(1f, 0f, 100d);
        }

        Assert.InRange(model.Yaw, -MathF.PI, MathF.PI);
    }

    [Fact]
    public void RestsWhereReleasedRatherThanSpringingBack()
    {
        var model = new MediaRotationModel();
        model.Update(1f, 0f, 100d);
        var turned = model.Yaw;

        Assert.False(model.Update(0f, 0f, 100d));
        Assert.Equal(turned, model.Yaw);
    }

    [Fact]
    public void RecentreReturnsToTheRestingPoseAndReportsWhetherItMoved()
    {
        var model = new MediaRotationModel();
        model.Update(1f, 1f, 100d);

        Assert.True(model.Recentre());
        Assert.True(model.IsAtRest);

        // Already there: no move, so no redraw is requested.
        Assert.False(model.Recentre());
    }

    [Fact]
    public void OppositeDeflectionsTurnOppositeWays()
    {
        var right = new MediaRotationModel();
        var left = new MediaRotationModel();

        right.Update(1f, 0f, 100d);
        left.Update(-1f, 0f, 100d);

        Assert.True(right.Yaw > MediaRotationModel.RestYaw);
        Assert.True(left.Yaw < MediaRotationModel.RestYaw);
    }

    [Fact]
    public void DiagonalDeflectionIsNotFasterThanCardinal()
    {
        var cardinal = new MediaRotationModel();
        var diagonal = new MediaRotationModel();

        cardinal.Update(1f, 0f, 100d);
        diagonal.Update(1f, 1f, 100d);

        // The step is divided by the radial magnitude, so a corner-pinned stick spins at the same
        // rate as a fully deflected cardinal one rather than sqrt(2) faster.
        var cardinalTravel = Math.Abs(cardinal.Yaw - MediaRotationModel.RestYaw);
        var diagonalTotal = Math.Sqrt(
            Math.Pow(diagonal.Yaw - MediaRotationModel.RestYaw, 2)
            + Math.Pow(diagonal.Pitch - MediaRotationModel.RestPitch, 2));

        Assert.True(diagonalTotal <= cardinalTravel + 1e-4, $"{diagonalTotal} vs {cardinalTravel}");
    }

    [Fact]
    public void IgnoresANonAdvancingClock()
    {
        var model = new MediaRotationModel();

        Assert.False(model.Update(1f, 0f, 0d));
        Assert.True(model.IsAtRest);
    }
}
