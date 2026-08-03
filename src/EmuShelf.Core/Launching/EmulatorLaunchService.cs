using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;

namespace EmuShelf.Core.Launching;

/// <summary>Shared validation, argument expansion, process tracking, and frontend lifecycle.</summary>
public sealed class EmulatorLaunchService : IEmulatorLaunchService
{
    private readonly IEmulatorConfigurationStore _configurations;
    private readonly ITrackedProcessRunner _processRunner;
    private readonly IFrontendController _frontend;
    private readonly IReadOnlyList<EmulatorDefinition> _emulators;
    private readonly IAppLogger _logger;
    private readonly ILaunchTargetInspector _targetInspector;
    private readonly IGameLaunchDependencyResolver _dependencyResolver;

    public EmulatorLaunchService(
        IEmulatorConfigurationStore configurations,
        ITrackedProcessRunner processRunner,
        IFrontendController frontend,
        IReadOnlyList<EmulatorDefinition> emulators,
        IAppLogger? logger = null,
        ILaunchTargetInspector? targetInspector = null,
        IGameLaunchDependencyResolver? dependencyResolver = null)
    {
        _configurations = configurations;
        _processRunner = processRunner;
        _frontend = frontend;
        _emulators = emulators;
        _logger = logger ?? NullAppLogger.Instance;
        _targetInspector = targetInspector ?? new DefaultLaunchTargetInspector();
        _dependencyResolver = dependencyResolver ?? new DefaultGameLaunchDependencyResolver();
    }

    public async Task<GameLaunchResult> LaunchAsync(
        Game game,
        Func<CancellationToken, Task>? beforeStart = null,
        CancellationToken cancellationToken = default)
    {
        // Portable installs commonly live on external drives. Keep the SQLite read and
        // filesystem probes off the UI thread so drive spin-up/disconnects cannot freeze
        // the window before it has a chance to repaint the launch status.
        var preparation = await Task.Run(
            () => PrepareLaunch(game),
            cancellationToken);
        if (preparation.Failure is not null)
        {
            _logger.Warning(preparation.Failure.StatusText);
            return preparation.Failure;
        }

        // Lifecycle work such as pulling cloud saves belongs after every launch check succeeds,
        // but before the emulator can read or write those saves. Keeping the callback here avoids
        // duplicating launch validation in the UI and guarantees a failed preflight has no sync
        // side effects.
        if (beforeStart is not null)
            await beforeStart(cancellationToken);

        _logger.Information($"Launching {preparation.EmulatorName} for {game.Title}.");
        _frontend.SuspendForGame();
        try
        {
            var exitCode = await _processRunner.RunAsync(preparation.StartSpec!, cancellationToken);
            _logger.Information($"{preparation.EmulatorName} exited with code {exitCode}.");
            if (exitCode == 0)
                return new GameLaunchResult(true, $"{game.Title} finished", ProcessExited: true);

            var failure = new GameLaunchResult(
                false,
                $"{preparation.EmulatorName} exited with code {exitCode}.",
                ProcessExited: true);
            _logger.Warning(failure.StatusText);
            return failure;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Error($"Could not start {preparation.EmulatorName}.", ex);
            return Failure($"Could not start {preparation.EmulatorName}: {ex.Message}");
        }
        finally
        {
            _frontend.ResumeAfterGame();
            _logger.Information($"Restored EmuShelf after {preparation.EmulatorName} exited.");
        }
    }

    private LaunchPreparation PrepareLaunch(Game game)
    {
        if (!File.Exists(game.Path) && !Directory.Exists(game.Path))
            return LaunchPreparation.Failed(
                $"Cannot launch {game.Title}: the game path is unavailable.");

        var emulator = _emulators.FirstOrDefault(candidate => candidate.Supports(game.SystemId));
        if (emulator is null)
            return LaunchPreparation.Failed(
                $"Cannot launch {game.Title}: no emulator supports this system.");

        if (emulator.RequiresContentFile && !File.Exists(game.Path))
        {
            return LaunchPreparation.Failed(
                $"Cannot launch {game.Title}: {emulator.Name} requires a game content file, not a folder.");
        }

        var configuration = _configurations.Get(game.SystemId);
        var target = configuration?.LaunchTarget ??
            (string.IsNullOrWhiteSpace(configuration?.ExecutablePath)
                ? null
                : new DirectExecutableTarget(configuration.ExecutablePath));
        if (target is null)
            return LaunchPreparation.Failed(
                $"Configure {emulator.Name} for this system in Settings before launching.");

        if (target is DirectExecutableTarget directTarget && !File.Exists(directTarget.Path))
            return LaunchPreparation.Failed(
                $"Cannot launch {game.Title}: the configured {emulator.Name} executable was not found.");

        if (emulator.RequiresCorePath)
        {
            if (string.IsNullOrWhiteSpace(configuration!.CorePath))
            {
                return LaunchPreparation.Failed(
                    $"Cannot launch {game.Title}: select an installed {emulator.Name} core in Settings first.");
            }

            if (!File.Exists(configuration.CorePath))
            {
                return LaunchPreparation.Failed(
                    $"Cannot launch {game.Title}: the configured {emulator.Name} core was not found.");
            }

        }

        var launchArguments = configuration!.LaunchArguments ?? emulator.DefaultLaunchArguments;
        try
        {
            if (target is FlatpakApplicationTarget &&
                ArgumentTemplate.ContainsPlaceholder(launchArguments, "EmulatorDirectory"))
            {
                return LaunchPreparation.Failed(
                    $"Cannot launch {game.Title}: Flatpak launch arguments cannot use {{EmulatorDirectory}}.");
            }

            if (emulator.RequiresCorePath &&
                !ArgumentTemplate.HasExplicitCoreAndContentForm(launchArguments))
            {
                return LaunchPreparation.Failed(
                    $"Cannot launch {game.Title}: the launch arguments for {emulator.Name} must use " +
                    "-L {CorePath} followed by {GamePath}.");
            }

            var dependencies = target is FlatpakApplicationTarget
                ? _dependencyResolver.Resolve(game)
                : new GameLaunchDependencies(true, [game.Path]);
            if (!dependencies.IsComplete)
            {
                return LaunchPreparation.Failed(
                    $"Cannot launch {game.Title}: {dependencies.FailureMessage ?? "all descriptor dependencies could not be resolved."}");
            }

            var inspection = _targetInspector.Inspect(target, dependencies.Paths);
            if (!inspection.CanLaunch)
            {
                return LaunchPreparation.Failed(
                    $"Cannot launch {game.Title}: {inspection.FailureMessage}");
            }

            var templateExecutablePath = target switch
            {
                DirectExecutableTarget direct => direct.Path,
                FlatpakApplicationTarget => "flatpak",
                _ => throw new InvalidOperationException("Unsupported launch target."),
            };
            var arguments = ArgumentTemplate.Expand(
                launchArguments,
                game.Path,
                templateExecutablePath,
                configuration.CorePath);
            var startSpec = target switch
            {
                DirectExecutableTarget direct => new ProcessStartSpec(
                    direct.Path,
                    arguments,
                    Path.GetDirectoryName(direct.Path) ?? Environment.CurrentDirectory),
                FlatpakApplicationTarget flatpak => new ProcessStartSpec(
                    "flatpak",
                    ["run", .. BuildReadOnlyFilesystemGrants(dependencies.Paths), flatpak.AppId, .. arguments],
                    Path.GetDirectoryName(game.Path) ?? Environment.CurrentDirectory),
                _ => throw new InvalidOperationException("Unsupported launch target."),
            };
            if (!string.IsNullOrWhiteSpace(inspection.WarningMessage))
                _logger.Warning(inspection.WarningMessage);

            return new LaunchPreparation(emulator.Name, startSpec, null);
        }
        catch (FormatException ex)
        {
            return LaunchPreparation.Failed($"Cannot launch {game.Title}: {ex.Message}");
        }
    }

    // A Flatpak emulator runs inside a sandbox that, by default, cannot see the user's ROM
    // folders — so a launch that passes EmuShelf's own File.Exists check still fails inside the
    // emulator with a bare "file does not exist". Grant read-only access to exactly the
    // directories this launch needs (the game plus any resolved CUE/M3U dependencies), scoped to
    // this single `flatpak run` invocation. The grant is ephemeral: it vanishes when the emulator
    // exits, so EmuShelf never persistently alters the emulator's stored Flatpak permissions and
    // never widens access beyond the files being launched. Read-only honors the rule that game
    // files are never modified.
    private static IReadOnlyList<string> BuildReadOnlyFilesystemGrants(IReadOnlyList<string> requiredPaths)
    {
        var grants = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in requiredPaths)
        {
            var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !seen.Add(directory))
                continue;
            grants.Add($"--filesystem={directory}:ro");
        }

        return grants;
    }

    private static GameLaunchResult Failure(string status) => new(false, status);

    private sealed record LaunchPreparation(
        string? EmulatorName,
        ProcessStartSpec? StartSpec,
        GameLaunchResult? Failure)
    {
        public static LaunchPreparation Failed(string status) =>
            new(null, null, EmulatorLaunchService.Failure(status));
    }
}
