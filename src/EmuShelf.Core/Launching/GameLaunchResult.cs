namespace EmuShelf.Core.Launching;

/// <summary>
/// Launch outcome. <see cref="ProcessExited"/> distinguishes a preflight/start failure from an
/// emulator process that was successfully tracked to exit, even when that process returned a
/// non-zero code. <see cref="PlayDuration"/> is the tracked process's wall-clock runtime, present
/// whenever <see cref="ProcessExited"/> is true (on a zero or non-zero exit) and null when the
/// process never started; the caller accrues it as play time.
/// </summary>
public sealed record GameLaunchResult(
    bool Succeeded,
    string StatusText,
    bool ProcessExited = false,
    TimeSpan? PlayDuration = null);
