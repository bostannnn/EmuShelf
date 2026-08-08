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

    // Prefers the emulator named by the active profile; only that emulator must still support the
    // system, otherwise a stale selection is ignored. With no usable selection the first supporting
    // emulator wins, preserving the original single-emulator-per-system behavior.
    private EmulatorDefinition? ResolveEmulator(string systemId, EmulatorConfiguration? configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration?.EmulatorId))
        {
            var selected = _emulators.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, configuration.EmulatorId, StringComparison.Ordinal) &&
                candidate.Supports(systemId));
            if (selected is not null)
                return selected;
        }

        return _emulators.FirstOrDefault(candidate => candidate.Supports(systemId));
    }

    private LaunchPreparation PrepareLaunch(Game game)
    {
        if (!File.Exists(game.Path) && !Directory.Exists(game.Path))
            return LaunchPreparation.Failed(
                $"Cannot launch {game.Title}: the game path is unavailable.");

        // Several emulators can serve one system, so the active profile's own emulator id decides
        // which one launches. Falling back to the first supporting emulator keeps a system that was
        // never given an explicit profile (and the launch-service tests) behaving exactly as before.
        var configuration = _configurations.Get(game.SystemId);
        var emulator = ResolveEmulator(game.SystemId, configuration);
        if (emulator is null)
            return LaunchPreparation.Failed(
                $"Cannot launch {game.Title}: no emulator supports this system.");

        if (emulator.RequiresContentFile && !File.Exists(game.Path))
        {
            return LaunchPreparation.Failed(
                $"Cannot launch {game.Title}: {emulator.Name} requires a game content file, not a folder.");
        }
        var target = configuration?.LaunchTarget ??
            (string.IsNullOrWhiteSpace(configuration?.ExecutablePath)
                ? null
                : new DirectExecutableTarget(configuration.ExecutablePath));
        if (target is null)
            return LaunchPreparation.Failed(
                $"Configure {emulator.Name} for this system in Settings before launching.");

        // A macOS emulator is a `.app` bundle — a directory, not a file — so File.Exists rejects it
        // and Process.Start cannot exec it. Resolve it to the real inner binary once, up front, so the
        // preflight, target inspection, and the start spec all see the executable. No-op on
        // Windows/Linux and for a normal file path.
        if (target is DirectExecutableTarget bundleTarget)
            target = new DirectExecutableTarget(ResolveExecutablePath(bundleTarget.Path));

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
                    ["run", .. BuildReadOnlyFilesystemGrants(dependencies.Paths, configuration.CorePath), flatpak.Ref, .. arguments],
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
    //
    // The libretro core (RetroArch's -L target) is granted the same way: it lives outside the
    // sandbox's default-visible directories in a portable install, so without a grant the sandboxed
    // emulator cannot read the core file and fails to load it even though EmuShelf's host-side core
    // preflight passed. Read-only is enough — RetroArch only reads the core.
    private static IReadOnlyList<string> BuildReadOnlyFilesystemGrants(
        IReadOnlyList<string> requiredPaths,
        string? corePath = null)
    {
        var paths = new List<string>(requiredPaths);
        if (!string.IsNullOrWhiteSpace(corePath))
            paths.Add(corePath);

        var grants = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !seen.Add(directory))
                continue;
            grants.Add($"--filesystem={directory}:ro");
        }

        return grants;
    }

    // A macOS application is a `.app` bundle — a directory, not a Mach-O file — so File.Exists rejects
    // it and Process.Start (UseShellExecute=false) cannot exec it. Resolve the bundle to the real
    // binary at Contents/MacOS/<executable>, named by Info.plist's CFBundleExecutable, falling back to
    // a binary named after the bundle, then to the sole file under Contents/MacOS. On any other
    // platform, or for a path that is not an existing `.app` directory, the input is returned
    // unchanged so Windows/Linux launches are completely unaffected.
    internal static string ResolveExecutablePath(string path)
    {
        if (!OperatingSystem.IsMacOS() ||
            !path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(path))
        {
            return path;
        }

        var macOsDirectory = Path.Combine(path, "Contents", "MacOS");
        if (!Directory.Exists(macOsDirectory))
            return path;

        var declaredName = ReadBundleExecutableName(Path.Combine(path, "Contents", "Info.plist"));
        if (!string.IsNullOrWhiteSpace(declaredName))
        {
            var declaredPath = Path.Combine(macOsDirectory, declaredName);
            if (File.Exists(declaredPath))
                return declaredPath;
        }

        var namedAfterBundle = Path.Combine(macOsDirectory, Path.GetFileNameWithoutExtension(path));
        if (File.Exists(namedAfterBundle))
            return namedAfterBundle;

        var candidates = Directory.GetFiles(macOsDirectory);
        return candidates.Length == 1 ? candidates[0] : path;
    }

    // Info.plist is an XML property list: its root <dict> holds alternating <key>/<value> elements.
    // Read the string that follows the CFBundleExecutable key. A binary plist or an unreadable file
    // returns null so the caller falls back to its name-based heuristics.
    private static string? ReadBundleExecutableName(string infoPlistPath)
    {
        if (!File.Exists(infoPlistPath))
            return null;
        try
        {
            var dictionary = System.Xml.Linq.XDocument.Load(infoPlistPath).Root?.Element("dict");
            if (dictionary is null)
                return null;
            var elements = dictionary.Elements().ToList();
            for (var index = 0; index < elements.Count - 1; index++)
            {
                if (elements[index].Name.LocalName == "key" &&
                    string.Equals(elements[index].Value, "CFBundleExecutable", StringComparison.Ordinal) &&
                    elements[index + 1].Name.LocalName == "string")
                {
                    var value = elements[index + 1].Value.Trim();
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }
        }
        catch (Exception ex) when (
            ex is System.Xml.XmlException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Binary plist, malformed XML, or an unreadable file — fall back to the name heuristics.
        }

        return null;
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
