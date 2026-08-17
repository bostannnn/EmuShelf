using System.Diagnostics;
using Avalonia.Input;
using Avalonia.Threading;

namespace EmuShelf.App.Services;

/// <summary>
/// Drives the shelf hero's rotation from held keys, for machines with no controller attached.
/// </summary>
/// <remarks>
/// Deliberately synthesizes a right-stick deflection and feeds
/// <c>MainViewModel.ApplyRightStickRotation</c> rather than rotating anything itself, so the
/// keyboard and the pad share one <see cref="MediaRotationModel"/>: the same speed curve, the same
/// pitch clamp, the same rest-where-released behaviour. A second rotation path would drift from the
/// first the moment either was tuned.
///
/// Keys are digital, so a held key is a fully deflected stick. That is a real difference from the
/// pad — there is no fine control near centre, only full speed — and it is why this is a way to
/// inspect the medium rather than a replacement for the analog axis.
/// </remarks>
public sealed class KeyboardShelfRotation : IDisposable
{
    private readonly Action<float, float, double> _apply;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly HashSet<Key> _held = [];
    private long _previousTickMs;

    public KeyboardShelfRotation(Action<float, float, double> apply)
    {
        _apply = apply;
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, OnTick);
    }

    /// <summary>The deflection the currently held keys stand for, in the pad's axis convention.</summary>
    /// <remarks>
    /// Y is positive downward, matching SDL — so Up is negative, which is what makes the top of the
    /// medium tip toward the viewer exactly as pushing the stick up does.
    /// </remarks>
    public (float X, float Y) Deflection
    {
        get
        {
            var x = (_held.Contains(Key.Right) ? 1f : 0f) - (_held.Contains(Key.Left) ? 1f : 0f);
            var y = (_held.Contains(Key.Down) ? 1f : 0f) - (_held.Contains(Key.Up) ? 1f : 0f);
            return (x, y);
        }
    }

    public bool IsRotating => _held.Count > 0;

    /// <summary>Whether this key, with these modifiers, is a rotation input rather than navigation.</summary>
    /// <remarks>
    /// Shift plus an arrow. The plain arrows are the shelf's navigation and must stay that way;
    /// Shift with an arrow previously did the same thing as the arrow alone, so nothing is displaced.
    /// </remarks>
    public static bool IsRotationKey(Key key, KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Shift) && IsArrow(key);

    private static bool IsArrow(Key key) =>
        key is Key.Left or Key.Right or Key.Up or Key.Down;

    /// <summary>Starts or continues rotating. Returns whether the key was consumed.</summary>
    public bool Press(Key key, KeyModifiers modifiers)
    {
        if (!IsRotationKey(key, modifiers))
        {
            return false;
        }

        if (_held.Add(key) && _held.Count == 1)
        {
            // Start the clock with this tick, not with whatever elapsed since the last rotation, so
            // the first frame of a new hold cannot integrate a huge dt and jump the medium round.
            _previousTickMs = _clock.ElapsedMilliseconds;
            _timer.Start();
        }

        return true;
    }

    /// <summary>
    /// Stops rotating in one direction. Releasing Shift releases everything, since the arrows go
    /// back to being navigation the moment it is up.
    /// </summary>
    public bool Release(Key key)
    {
        if (key is Key.LeftShift or Key.RightShift)
        {
            var wasRotating = IsRotating;
            Stop();
            return wasRotating;
        }

        if (!IsArrow(key) || !_held.Remove(key))
        {
            return false;
        }

        if (_held.Count == 0)
        {
            _timer.Stop();
        }

        return true;
    }

    /// <summary>Drops every held key. Used when the window loses focus or the layout changes.</summary>
    public void Stop()
    {
        _held.Clear();
        _timer.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.ElapsedMilliseconds;
        var delta = now - _previousTickMs;
        _previousTickMs = now;

        var (x, y) = Deflection;
        if (x == 0f && y == 0f)
        {
            // Opposing keys held at once cancel out; there is nothing to integrate.
            return;
        }

        _apply(x, y, delta);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
