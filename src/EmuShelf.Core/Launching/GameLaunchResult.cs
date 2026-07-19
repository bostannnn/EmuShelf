namespace EmuShelf.Core.Launching;

/// <summary>
/// Launch outcome. <see cref="ProcessExited"/> distinguishes a preflight/start failure from an
/// emulator process that was successfully tracked to exit, even when that process returned a
/// non-zero code.
/// </summary>
public sealed record GameLaunchResult(
    bool Succeeded,
    string StatusText,
    bool ProcessExited = false);
