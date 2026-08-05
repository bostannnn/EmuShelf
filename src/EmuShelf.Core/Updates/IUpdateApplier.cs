namespace EmuShelf.Core.Updates;

/// <summary>
/// Swaps a downloaded, verified update into place and relaunches the app. Implementations are
/// platform-specific: the Steam Deck's AppImage build replaces one file and re-execs itself with the
/// same process id (so Steam never sees the game stop and the session stays in gaming mode), while
/// Windows and macOS spawn a short-lived helper that waits for the app to exit, replaces the files,
/// and starts the new build.
/// </summary>
public interface IUpdateApplier
{
    /// <summary>
    /// Whether an in-place update can be applied for this build and run. Returns false with a
    /// user-facing <paramref name="reason"/> when it cannot — for example an AppImage update run from
    /// a plain <c>dotnet run</c>, which has no single artifact to replace.
    /// </summary>
    bool CanApply(out string? reason);

    /// <summary>
    /// Applies <paramref name="staged"/> and relaunches. On platforms that re-exec in place this does
    /// not return; on Windows/macOS it returns after launching the detached helper, and the caller is
    /// then responsible for exiting the process so the app's files unlock.
    /// </summary>
    void ApplyAndRelaunch(StagedUpdate staged);
}
