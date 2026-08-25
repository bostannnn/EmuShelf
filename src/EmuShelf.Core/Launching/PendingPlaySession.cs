namespace EmuShelf.Core.Launching;

/// <summary>
/// A launch that has started but whose post-play work (play-time accrual, save sync) has not run yet,
/// persisted durably so it survives EmuShelf being killed while the emulator is in the foreground — the
/// normal case on a handheld after launching a heavy emulator. On Android the launch is fire-and-forget
/// (there is no child process to await), so completion is deferred until EmuShelf returns to the
/// foreground; this record is what carries the session across that gap, and across process death.
/// </summary>
/// <param name="GameId">The launched game's library id.</param>
/// <param name="GameTitle">The display title, for the completion status message.</param>
/// <param name="StartedAtUnixMs">When the launch fired, Unix epoch milliseconds (UTC).</param>
/// <param name="EmulatorPackage">
/// Android only: the package name of the emulator this launch fired at, so the return handler can ask
/// Android to close it (see <c>AppSettings.CloseEmulatorOnReturn</c>). Null on desktop and for older
/// records written before the field existed, in which case no emulator is closed.
/// </param>
public sealed record PendingPlaySession(
    long GameId,
    string GameTitle,
    long StartedAtUnixMs,
    string? EmulatorPackage = null);
