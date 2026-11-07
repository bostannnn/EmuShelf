using System;
using EmuShelf.App.Services;

namespace EmuShelf.App.ViewModels;

/// <summary>
/// The pose of the game's physical medium on the Handheld Stage: where the player has turned it with the
/// right stick, the gamepad shelf's idle sway (both <see cref="MediaRotationModel"/>), and a spring that
/// carries every change of pose instead of snapping it or restarting it: a new game and R3 both retarget
/// it at the resting three-quarter view from whatever is on screen. Pure and clock-free: the shell ticks it while it is settling or swaying.
/// </summary>
public sealed class HandheldMediumMotion
{
    // A slightly under-damped spring (damping ratio ≈ 0.72): it settles in about half a second with one
    // small overshoot, which reads as a physical object coming to rest rather than an ease curve.
    private const float Stiffness = 140f;
    private const float Damping = 17f;
    private const float RestEpsilon = 0.0015f;
    private const double MaxStepMilliseconds = 34d;

    private readonly MediaRotationModel _rotation = new();
    private float _yawOffset;
    private float _pitchOffset;
    private float _yawVelocity;
    private float _pitchVelocity;

    public float Yaw => _rotation.Yaw + _yawOffset;
    public float Pitch => _rotation.Pitch + _pitchOffset;

    /// <summary>The spring is still carrying a change of pose.</summary>
    public bool IsSettling => _yawOffset != 0f || _pitchOffset != 0f || _yawVelocity != 0f || _pitchVelocity != 0f;

    /// <summary>A resting medium sways (slowly; the shell ticks it at a lower rate than a settle).</summary>
    public bool IsSwaying => !IsSettling && _rotation.IdleSwayEnabled && _rotation.IsAtRest;

    /// <summary>
    /// Retargets the spring at the resting pose, starting from exactly the pose on screen and keeping its
    /// speed: a new game arriving mid-sway or mid-settle carries on from where the eye is, never restarts,
    /// and one already at rest simply stays there (as Gamepad mode's shelf presents a new game face-on).
    /// R3 uses the same path to swing back from wherever the stick left it.
    /// </summary>
    public bool Recentre()
    {
        var yaw = Yaw;
        var pitch = Pitch;
        _rotation.Recentre();
        _yawOffset = Wrap(yaw - _rotation.Yaw);
        _pitchOffset = pitch - _rotation.Pitch;
        if (MathF.Abs(_yawOffset) < RestEpsilon && MathF.Abs(_pitchOffset) < RestEpsilon &&
            MathF.Abs(_yawVelocity) < RestEpsilon && MathF.Abs(_pitchVelocity) < RestEpsilon)
        {
            _yawOffset = _pitchOffset = _yawVelocity = _pitchVelocity = 0f;
            return false;
        }
        return true;
    }

    /// <summary>
    /// One tick of right-stick input. The stick turns the medium directly; a turn-in still settling keeps
    /// settling underneath it, so taking the stick mid-arrival never jumps.
    /// </summary>
    public bool ApplyStick(float x, float y, double deltaMilliseconds) =>
        _rotation.Update(x, y, deltaMilliseconds);

    /// <summary>Advances the spring and the idle sway. Returns whether the pose changed.</summary>
    public bool Tick(double deltaMilliseconds)
    {
        if (deltaMilliseconds <= 0d)
            return false;
        var changed = false;
        var remaining = deltaMilliseconds;
        // Fixed small steps keep the spring stable through a long frame.
        while (remaining > 0d && IsSettling)
        {
            var step = (float)(Math.Min(remaining, MaxStepMilliseconds) / 1000d);
            remaining -= MaxStepMilliseconds;
            Step(ref _yawOffset, ref _yawVelocity, step);
            Step(ref _pitchOffset, ref _pitchVelocity, step);
            changed = true;
        }
        // The sway only starts once the medium is at rest (the model's own rule), after its delay.
        if (!IsSettling)
            changed |= _rotation.AdvanceSway(deltaMilliseconds);
        return changed;
    }

    private static void Step(ref float offset, ref float velocity, float seconds)
    {
        if (offset == 0f && velocity == 0f)
            return;
        var acceleration = (-Stiffness * offset) - (Damping * velocity);
        velocity += acceleration * seconds;
        offset += velocity * seconds;
        if (MathF.Abs(offset) < RestEpsilon && MathF.Abs(velocity) < RestEpsilon * 10f)
        {
            offset = 0f;
            velocity = 0f;
        }
    }

    private static float Wrap(float radians)
    {
        const float turn = 2f * MathF.PI;
        radians %= turn;
        if (radians > MathF.PI)
            radians -= turn;
        else if (radians < -MathF.PI)
            radians += turn;
        return radians;
    }
}
