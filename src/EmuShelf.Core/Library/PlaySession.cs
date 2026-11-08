namespace EmuShelf.Core.Library;

/// <summary>How a play session's length was measured, so an estimate can be labelled as one (M46).</summary>
public enum PlaySessionMeasurement
{
    /// <summary>The runtime of the emulator process EmuShelf started and waited for (desktop).</summary>
    TrackedProcess = 1,

    /// <summary>The emulator's foreground time from Android's usage events (M46 P2).</summary>
    UsageAccess = 2,

    /// <summary>The wall-clock gap from launch to EmuShelf's return (Android without usage access).</summary>
    WallClockEstimate = 3,

    /// <summary>A length the user corrected by hand (M46 P3).</summary>
    Edited = 4,
}

/// <summary>
/// One sitting with one game, as recorded in EmuShelf's own database. A session is opened when the
/// launch starts and closed when the emulator exits or EmuShelf returns; one that is never closed (EmuShelf
/// killed, an implausible Android gap) keeps a null <see cref="Duration"/>: a play of unknown length.
/// </summary>
/// <param name="Id">The session's row id, unique on this device.</param>
/// <param name="GameId">The game's library id at the time of the session.</param>
/// <param name="GameTitle">The game's title when the session started, kept so a removed game still reads.</param>
/// <param name="SystemId">The game's system id when the session started.</param>
/// <param name="StartedAt">When the launch started (UTC).</param>
/// <param name="Duration">How long it lasted, or null while open or when the length is unknown.</param>
/// <param name="Measurement">How <see cref="Duration"/> was measured; null while the session is open.</param>
/// <param name="DeviceId">The install that recorded it (<see cref="IDeviceIdentity"/>).</param>
/// <param name="IsGameRemoved">The game has since been removed from the library; the session is kept.</param>
public sealed record PlaySession(
    long Id,
    long GameId,
    string GameTitle,
    string SystemId,
    DateTimeOffset StartedAt,
    TimeSpan? Duration,
    PlaySessionMeasurement? Measurement,
    string DeviceId,
    bool IsGameRemoved = false);

/// <summary>
/// What a game's sessions say beyond its running totals. Play recorded before the session log existed
/// (schema v21) lives only in <see cref="Game.Playtime"/> and <see cref="Game.PlayCount"/>; it is reported
/// as "earlier play" and never invented as sessions.
/// </summary>
/// <param name="EarlierPlaytime">Play time in the totals that no session accounts for.</param>
/// <param name="EarlierPlayCount">Plays in the totals that no session accounts for.</param>
/// <param name="FirstPlayedAt">
/// The earliest session's start, or null when the game was never played or has earlier play (its true
/// first play predates the log).
/// </param>
public sealed record GamePlayHistory(TimeSpan EarlierPlaytime, int EarlierPlayCount, DateTimeOffset? FirstPlayedAt)
{
    /// <summary>Summarises <paramref name="sessions"/> (the game's own) against the game's totals.</summary>
    public static GamePlayHistory From(Game game, IReadOnlyCollection<PlaySession> sessions)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(sessions);

        var logged = TimeSpan.FromSeconds(sessions.Sum(session => (long)(session.Duration?.TotalSeconds ?? 0)));
        var earlierPlaytime = game.Playtime > logged ? game.Playtime - logged : TimeSpan.Zero;
        var earlierCount = Math.Max(0, game.PlayCount - sessions.Count);
        DateTimeOffset? firstPlayed = earlierCount > 0 || earlierPlaytime > TimeSpan.Zero || sessions.Count == 0
            ? null
            : sessions.Min(session => session.StartedAt);
        return new GamePlayHistory(earlierPlaytime, earlierCount, firstPlayed);
    }
}
