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

    public EmulatorLaunchService(
        IEmulatorConfigurationStore configurations,
        ITrackedProcessRunner processRunner,
        IFrontendController frontend,
        IReadOnlyList<EmulatorDefinition> emulators,
        IAppLogger? logger = null)
    {
        _configurations = configurations;
        _processRunner = processRunner;
        _frontend = frontend;
        _emulators = emulators;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public async Task<GameLaunchResult> LaunchAsync(
        Game game,
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

        _logger.Information($"Launching {preparation.EmulatorName} for {game.Title}.");
        _frontend.Minimize();
        try
        {
            var exitCode = await _processRunner.RunAsync(
                preparation.ExecutablePath!,
                preparation.Arguments!,
                preparation.WorkingDirectory!,
                cancellationToken);
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
            _frontend.Restore();
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
        if (string.IsNullOrWhiteSpace(configuration?.ExecutablePath))
            return LaunchPreparation.Failed(
                $"Configure {emulator.Name} for this system in Settings before launching.");

        var executablePath = configuration.ExecutablePath;
        if (!File.Exists(executablePath))
            return LaunchPreparation.Failed(
                $"Cannot launch {game.Title}: the configured {emulator.Name} executable was not found.");

        if (emulator.RequiresCorePath)
        {
            if (string.IsNullOrWhiteSpace(configuration.CorePath))
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

        var launchArguments = configuration.LaunchArguments ?? emulator.DefaultLaunchArguments;
        try
        {
            if (emulator.RequiresCorePath &&
                !ArgumentTemplate.HasExplicitCoreAndContentForm(launchArguments))
            {
                return LaunchPreparation.Failed(
                    $"Cannot launch {game.Title}: the launch arguments for {emulator.Name} must use " +
                    "-L {CorePath} followed by {GamePath}.");
            }

            return new LaunchPreparation(
                emulator.Name,
                executablePath,
                Path.GetDirectoryName(executablePath)!,
                ArgumentTemplate.Expand(
                    launchArguments,
                    game.Path,
                    executablePath,
                    configuration.CorePath),
                null);
        }
        catch (FormatException ex)
        {
            return LaunchPreparation.Failed($"Cannot launch {game.Title}: {ex.Message}");
        }
    }

    private static GameLaunchResult Failure(string status) => new(false, status);

    private sealed record LaunchPreparation(
        string? EmulatorName,
        string? ExecutablePath,
        string? WorkingDirectory,
        IReadOnlyList<string>? Arguments,
        GameLaunchResult? Failure)
    {
        public static LaunchPreparation Failed(string status) =>
            new(null, null, null, null, EmulatorLaunchService.Failure(status));
    }
}
