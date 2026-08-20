namespace EmuShelf.Core.Launching;

/// <summary>
/// Durable storage for the one <see cref="PendingPlaySession"/> awaiting completion. At most one session
/// is pending at a time (a game is launched, then completed on return before another launch matters), so
/// this is a single-slot store: <see cref="Set"/> overwrites, <see cref="Clear"/> empties. It must persist
/// to disk, not memory, so the session is recoverable after EmuShelf is killed mid-game.
/// </summary>
public interface IPendingPlaySessionStore
{
    /// <summary>Records the pending session, replacing any existing one.</summary>
    void Set(PendingPlaySession session);

    /// <summary>The pending session, or null if none is recorded (or the record is unreadable).</summary>
    PendingPlaySession? Get();

    /// <summary>Clears the pending session after its post-play work has run.</summary>
    void Clear();
}
