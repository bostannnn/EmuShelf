using System;
using System.Threading;
using System.Threading.Tasks;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Emulators.Android;

namespace EmuShelf.App.Android.Services;

/// <summary>
/// The Android launch path. It deliberately does <em>not</em> reuse the shared
/// <see cref="EmulatorLaunchService"/>: that service resolves an executable, expands an argument
/// template and tracks a child process to an exit code, none of which exist on Android, where launching
/// is firing an <c>Intent</c> at another app. Instead it turns the game into an
/// <c>AndroidIntentRequest</c> (via the tested <see cref="AndroidLaunchResolver"/>) and hands it to
/// <see cref="AndroidGameLauncher"/>.
///
/// First cut, by design: it is fire-and-forget. There is no process to await, so it returns as soon as the
/// emulator is started (<see cref="GameLaunchResult.ProcessExited"/> is false, so the caller neither accrues
/// play time nor runs post-play save sync). The pre-launch <paramref name="beforeStart"/> hook still runs, so
/// a cloud-save <em>pull</em> happens before the emulator reads the save. Automatic return detection
/// (<c>onTopResumedActivityChanged</c>, surviving process death) and push-on-return are the next Milestone B
/// step — until then, post-play sync is manual.
/// </summary>
public sealed class AndroidEmulatorLaunchService(
    AndroidGameLauncher launcher,
    IEmulatorConfigurationStore configurations,
    IPendingPlaySessionStore pendingSessions,
    IAppLogger logger) : IEmulatorLaunchService
{
    public async Task<GameLaunchResult> LaunchAsync(
        Game game,
        string? displayName = null,
        Func<CancellationToken, Task>? beforeStart = null,
        CancellationToken cancellationToken = default)
    {
        var title = string.IsNullOrWhiteSpace(displayName) ? game.Title : displayName;

        if (!File.Exists(game.Path) && !Directory.Exists(game.Path))
        {
            return new GameLaunchResult(
                false,
                $"Cannot launch {title}: the game path is unavailable (grant all-files access, or the SD card is not mounted).");
        }

        var configuration = configurations.Get(game.SystemId);

        // Try the maintained emulators first, falling through when one cannot be satisfied — e.g.
        // RetroArch needs a core path, so on a system it shares with a standalone emulator (PS1: RetroArch
        // + DuckStation) the standalone wins when no core is configured, rather than the launch failing.
        var candidates = AndroidEmulatorLaunchProfiles.ForSystem(game.SystemId);
        if (candidates.Count == 0)
            return new GameLaunchResult(false, $"Cannot launch {title}: no Android emulator supports this system.");

        AndroidLaunchResolution? lastFailure = null;
        foreach (var candidate in candidates)
        {
            var resolution = AndroidLaunchResolver.Resolve(
                game.SystemId,
                game.Path,
                preferredEmulatorId: candidate.Id,
                retroArchCorePath: configuration?.CorePath);

            if (!resolution.Success)
            {
                lastFailure = resolution;
                continue;
            }

            // Pull cloud saves (if wired) before the emulator can read them, mirroring desktop ordering.
            if (beforeStart is not null)
                await beforeStart(cancellationToken);

            logger.Information($"Launching {resolution.Profile!.DisplayName} for {game.Title}.");
            if (launcher.Launch(resolution.Intent!))
            {
                // Record the session durably *before* returning: EmuShelf is now a prime kill candidate
                // (a heavy emulator just took the foreground), so the return signal — or the next startup
                // if we are killed — completes play-time accrual and save sync from this record.
                pendingSessions.Set(new PendingPlaySession(
                    game.Id,
                    title,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
                return new GameLaunchResult(true, $"Launched {title} in {resolution.Profile!.DisplayName}");
            }

            return new GameLaunchResult(
                false,
                $"Could not start {resolution.Profile!.DisplayName} — is it installed?");
        }

        return new GameLaunchResult(
            false,
            $"Cannot launch {title}: {lastFailure?.FailureReason ?? "no configured emulator could accept it."}");
    }
}
