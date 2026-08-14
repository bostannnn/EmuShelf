using Avalonia.Headless.XUnit;
using Avalonia.Input;
using EmuShelf.App.Services;

namespace EmuShelf.App.Tests;

/// <summary>
/// The keyboard path must reach the same <see cref="MediaRotationModel"/> the right stick does, in
/// the same axis convention, or the two inputs turn the medium different ways.
/// </summary>
public class KeyboardShelfRotationTests
{
    [Theory]
    [InlineData(Key.Left, KeyModifiers.Shift, true)]
    [InlineData(Key.Right, KeyModifiers.Shift, true)]
    [InlineData(Key.Up, KeyModifiers.Shift, true)]
    [InlineData(Key.Down, KeyModifiers.Shift, true)]
    // Plain arrows stay shelf navigation.
    [InlineData(Key.Left, KeyModifiers.None, false)]
    [InlineData(Key.Enter, KeyModifiers.Shift, false)]
    [InlineData(Key.A, KeyModifiers.Shift, false)]
    public void IsRotationKey_ClaimsOnlyShiftedArrows(Key key, KeyModifiers modifiers, bool expected) =>
        Assert.Equal(expected, KeyboardShelfRotation.IsRotationKey(key, modifiers));

    [AvaloniaFact]
    public void Deflection_MatchesThePadsAxisConvention()
    {
        using var rotation = new KeyboardShelfRotation((_, _, _) => { });

        rotation.Press(Key.Right, KeyModifiers.Shift);
        Assert.Equal((1f, 0f), rotation.Deflection);

        rotation.Release(Key.Right);
        rotation.Press(Key.Up, KeyModifiers.Shift);
        // SDL reports up as negative Y, and MediaRotationModel is written against that.
        Assert.Equal((0f, -1f), rotation.Deflection);
    }

    [AvaloniaFact]
    public void OpposingKeys_CancelRatherThanFighting()
    {
        using var rotation = new KeyboardShelfRotation((_, _, _) => { });

        rotation.Press(Key.Left, KeyModifiers.Shift);
        rotation.Press(Key.Right, KeyModifiers.Shift);

        Assert.Equal((0f, 0f), rotation.Deflection);
        Assert.True(rotation.IsRotating);
    }

    [AvaloniaFact]
    public void ReleasingShift_StopsEverything()
    {
        using var rotation = new KeyboardShelfRotation((_, _, _) => { });
        rotation.Press(Key.Left, KeyModifiers.Shift);
        rotation.Press(Key.Up, KeyModifiers.Shift);

        Assert.True(rotation.Release(Key.LeftShift));

        Assert.False(rotation.IsRotating);
        Assert.Equal((0f, 0f), rotation.Deflection);
    }

    [AvaloniaFact]
    public void ReleasingOneArrow_LeavesTheOtherTurning()
    {
        using var rotation = new KeyboardShelfRotation((_, _, _) => { });
        rotation.Press(Key.Left, KeyModifiers.Shift);
        rotation.Press(Key.Down, KeyModifiers.Shift);

        rotation.Release(Key.Left);

        Assert.True(rotation.IsRotating);
        Assert.Equal((0f, 1f), rotation.Deflection);
    }

    /// <summary>A held key is a fully deflected stick, so it must clear the model's deadzone.</summary>
    [AvaloniaFact]
    public void HeldKey_DeflectsPastTheRotationDeadzone()
    {
        using var rotation = new KeyboardShelfRotation((_, _, _) => { });
        rotation.Press(Key.Right, KeyModifiers.Shift);

        var (x, y) = rotation.Deflection;
        var magnitude = MathF.Sqrt((x * x) + (y * y));

        Assert.True(magnitude > MediaRotationModel.Deadzone);

        var model = new MediaRotationModel();
        Assert.True(model.Update(x, y, 16d));
        Assert.NotEqual(MediaRotationModel.RestYaw, model.Yaw);
    }
}
