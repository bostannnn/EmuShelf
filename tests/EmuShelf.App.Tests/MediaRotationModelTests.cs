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

    // --- Idle sway: a resting hero breathes so it reads as a 3-D object and quietly invites the stick. ---

    // The idle sway is driven by its own clock (AdvanceSway), not the input path, so exercise it that
    // way — Update(0,0,...) is now a no-op on the input path.
    private static bool AdvanceCentred(MediaRotationModel model, int ticks, double msPerTick = 100d)
    {
        var moved = false;
        for (var i = 0; i < ticks; i++)
        {
            moved |= model.AdvanceSway(msPerTick);
        }

        return moved;
    }

    [Fact]
    public void RestingHeroBeginsToSwayOnlyAfterTheSettleDelay()
    {
        var model = new MediaRotationModel();

        // Well inside the settle delay the hero holds its pose and asks for no redraws — a freshly
        // focused cover gets a beat to arrive before it starts to move.
        Assert.False(AdvanceCentred(model, ticks: 10));
        Assert.Equal(MediaRotationModel.RestYaw, model.Yaw);

        // Past the delay, it begins to drift.
        Assert.True(AdvanceCentred(model, ticks: 20));
        Assert.NotEqual(MediaRotationModel.RestYaw, model.Yaw);
    }

    [Fact]
    public void IdleSwayGenuinelyMovesButStaysGentle()
    {
        var model = new MediaRotationModel();
        AdvanceCentred(model, ticks: 25);

        var maxYawTravel = 0f;
        var maxPitchTravel = 0f;
        // A little over one full yaw cycle, so the sway is sampled through a peak in each axis.
        for (var i = 0; i < 70; i++)
        {
            model.AdvanceSway(100d);
            maxYawTravel = MathF.Max(maxYawTravel, MathF.Abs(model.Yaw - MediaRotationModel.RestYaw));
            maxPitchTravel = MathF.Max(maxPitchTravel, MathF.Abs(model.Pitch - MediaRotationModel.RestPitch));
        }

        // It genuinely turns...
        Assert.True(maxYawTravel > 0.04f, $"yaw travel {maxYawTravel}");
        // ...but only a few degrees — a sheen of life, never a spin (0.12 rad is ~7 degrees).
        Assert.True(maxYawTravel < 0.12f, $"yaw travel {maxYawTravel}");
        Assert.True(maxPitchTravel is > 0f and < 0.05f, $"pitch travel {maxPitchTravel}");
    }

    [Fact]
    public void AHeroTurnedByHandHoldsItsPoseInsteadOfSwaying()
    {
        var model = new MediaRotationModel();
        model.Update(1f, 0f, 100d);
        var parked = model.Yaw;

        // Left alone away from rest, it keeps the inspection pose the player chose — the sway never
        // fights a deliberate turn.
        Assert.False(AdvanceCentred(model, ticks: 40));
        Assert.Equal(parked, model.Yaw);
    }

    [Fact]
    public void TheStickTakesOverAnIdleSwayAndDoesNotResumeIt()
    {
        var model = new MediaRotationModel();
        AdvanceCentred(model, ticks: 25);
        Assert.NotEqual(MediaRotationModel.RestYaw, model.Yaw);

        // Grabbing the stick drives the hero and hands the sway off.
        Assert.True(model.Update(1f, 0f, 100d));
        Assert.False(model.IsAtRest);

        // The sway does not restart: the pose is no longer at rest.
        Assert.False(model.AdvanceSway(100d));
    }

    [Fact]
    public void RecentreClearsAnIdleSwayAndRestartsTheSettleDelay()
    {
        var model = new MediaRotationModel();
        AdvanceCentred(model, ticks: 25);
        Assert.NotEqual(MediaRotationModel.RestYaw, model.Yaw);

        Assert.True(model.Recentre());
        Assert.Equal(MediaRotationModel.RestYaw, model.Yaw);
        Assert.Equal(MediaRotationModel.RestPitch, model.Pitch);

        // The delay restarts, so the freshly-centred hero holds still again briefly.
        Assert.False(model.AdvanceSway(100d));
    }

    [Fact]
    public void DisabledIdleSwayNeverMovesTheHero()
    {
        var model = new MediaRotationModel { IdleSwayEnabled = false };

        // No matter how long it rests, a disabled sway never moves the hero nor asks for a redraw.
        Assert.False(AdvanceCentred(model, ticks: 60));
        Assert.Equal(MediaRotationModel.RestYaw, model.Yaw);
        Assert.True(model.IsAtRest);
    }

    // --- IsSwaying: whether the resting hero is currently drifting on its own. ---

    [Fact]
    public void IsSwaying_IsFalseUntilTheDriftActuallyBegins_ThenTrue()
    {
        var model = new MediaRotationModel();

        // A freshly-rested hero is not yet swaying (still inside the settle delay).
        Assert.False(model.IsSwaying);
        AdvanceCentred(model, ticks: 10);
        Assert.False(model.IsSwaying);

        // Once past the delay and drifting, it reports swaying.
        AdvanceCentred(model, ticks: 20);
        Assert.True(model.IsSwaying);
    }

    [Fact]
    public void IsSwaying_IsAlwaysFalseWhenTheSwayIsDisabled()
    {
        // With the sway disabled, IsSwaying must never latch on, however long the hero sits.
        var model = new MediaRotationModel { IdleSwayEnabled = false };

        AdvanceCentred(model, ticks: 60);
        Assert.False(model.IsSwaying);
    }

    [Fact]
    public void IsSwaying_StopsAfterTheStickTakesOver()
    {
        var model = new MediaRotationModel();
        AdvanceCentred(model, ticks: 25);
        Assert.True(model.IsSwaying);

        // Driving the hero hands the sway off; a hero parked away from rest no longer breathes.
        model.Update(1f, 0f, 100d);
        Assert.False(model.IsSwaying);
        model.AdvanceSway(100d);
        Assert.False(model.IsSwaying);
    }
}
