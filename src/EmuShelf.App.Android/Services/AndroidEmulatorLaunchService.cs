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
/// play time nor runs post-play save sync). The pre-launch hook supplied to <see cref="LaunchAsync"/> still runs, so
/// a cloud-save <em>pull</em> happens before the emulator reads the save. Automatic return detection
/// (<c>onTopResumedActivityChanged</c>, surviving process death) and push-on-return are the next Milestone B
/// step — until then, post-play sync is manual.
/// </summary>
public sealed class AndroidEmulatorLaunchService(
    AndroidGameLauncher launcher,
    IEmulatorConfigurationStore configurations,
    IPendingPlaySessionStore pendingSessions,
    IGameLibrary library,
    IAppLogger logger,
    Action<Game, string>? gameStarted = null) : IEmulatorLaunchService
{
    public async Task<GameLaunchResult> LaunchAsync(
        Game game,
        string? displayName = null,
        Func<CancellationToken, Task>? beforeStart = null,
        CancellationToken cancellationToken = default)
    {
        var title = string.IsNullOrWhiteSpace(displayName) ? game.Title : displayName;

        // Preflight off the calling (UI) thread: the existence probe stats removable storage and the
        // config/library lookups hit SQLite, either of which can hitch the launch frame — and on a slow
        // SD card risk a short ANR — if run inline. The await resumes on the UI thread (ConfigureAwait
        // true), so the Context/StartActivity handoff below still runs where Android expects it.
        var preflight = await Task.Run(() => Preflight(game), cancellationToken).ConfigureAwait(true);
        if (!preflight.Ok)
            return new GameLaunchResult(false, $"Cannot launch {title}: {preflight.Failure}");

        var resolution = preflight.Resolution!;
        var profile = resolution.Profile!;

        // No silent fallback: the emulator resolved here is the one the user intends (their configured
        // choice, or the maintained-first default for the system). If it is not installed, say so and
        // stop — do not start a different emulator, which would run with a different save format. The
        // package is declared in the Android head's <queries> block, so the check works on API 30+.
        if (!launcher.IsInstalled(profile.PackageName))
            return new GameLaunchResult(
                false, $"Cannot launch {title}: {profile.DisplayName} is not installed.");

        // Pull cloud saves (if wired) before the emulator can read them — once, and only now that a
        // launch is actually going ahead, so a fail-loud path above never reconciles saves needlessly.
        if (beforeStart is not null)
            await beforeStart(cancellationToken);

        logger.Information($"Launching {profile.DisplayName} for {game.Title}.");
        if (launcher.Launch(resolution.Intent!))
        {
            // Record the session durably *before* returning: EmuShelf is now a prime kill candidate
            // (a heavy emulator just took the foreground), so the return signal — or the next startup
            // if we are killed — completes play-time accrual and save sync from this record.
            pendingSessions.Set(new PendingPlaySession(
                game.Id,
                title,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            try
            {
                gameStarted?.Invoke(game, title);
            }
            catch (Exception ex)
            {
                // Companion chrome is optional. Once the emulator has started and the durable session
                // exists, a notification/Presentation failure must not turn the launch into a false error.
                logger.Warning("The game launched, but the second-screen session could not start.", ex);
            }
            return new GameLaunchResult(true, $"Launched {title} in {profile.DisplayName}");
        }

        // Installed but the activity handoff was rejected — a real, unexpected failure, so name it
        // rather than guessing "not installed" or silently trying something else.
        return new GameLaunchResult(
            false, $"Cannot launch {title}: {profile.DisplayName} did not start.");
    }

    /// <summary>
    /// The synchronous, IO-bound part of a launch — path probe, emulator selection and intent
    /// resolution — factored out so it can run on a background thread. Picks exactly one emulator (the
    /// configured choice, else the maintained-first default) and never falls back to another.
    /// </summary>
    private LaunchPreflight Preflight(Game game)
    {
        if (!File.Exists(game.Path) && !Directory.Exists(game.Path))
        {
            return LaunchPreflight.Failed(
                "the game path is unavailable (grant all-files access, or the SD card is not mounted).");
        }

        var configuration = configurations.Get(game.SystemId);

        var candidates = AndroidEmulatorLaunchProfiles.ForSystem(game.SystemId, configuration?.EmulatorId);
        if (candidates.Count == 0)
            return LaunchPreflight.Failed("no Android emulator supports this system.");

        // The single intended emulator: the user's configured choice sorts first, otherwise the
        // maintained-first default. Everything past this point commits to it.
        var intended = candidates[0];

        // Scope the launch URI's tree to the folder the game was imported from — normally the same folder
        // the emulator was granted (e.g. roms/psx). Without this, the resolver falls back to the game's own
        // sub-folder, which a nested multi-disc game's emulator has no grant to, and the launch is denied.
        var grantRoot = AndroidLibraryGrantRoot.ForGame(library.GetLibraryFolders(game.SystemId), game.Path);

        var resolution = AndroidLaunchResolver.Resolve(
            game.SystemId,
            game.Path,
            preferredEmulatorId: intended.Id,
            retroArchCorePath: configuration?.CorePath,
            emulatorGrantRoot: grantRoot);

        return resolution.Success
            ? LaunchPreflight.Ready(resolution)
            : LaunchPreflight.Failed(resolution.FailureReason ?? "the chosen emulator could not accept it.");
    }

    private readonly record struct LaunchPreflight(AndroidLaunchResolution? Resolution, string? Failure)
    {
        public bool Ok => Resolution is not null;

        public static LaunchPreflight Failed(string reason) => new(null, reason);

        public static LaunchPreflight Ready(AndroidLaunchResolution resolution) => new(resolution, null);
    }
}
