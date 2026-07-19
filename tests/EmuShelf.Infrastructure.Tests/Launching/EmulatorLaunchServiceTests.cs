using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;

namespace EmuShelf.Infrastructure.Tests.Launching;

public class EmulatorLaunchServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "EmuShelfLaunchTests",
        Guid.NewGuid().ToString("N"));
    private readonly FakeConfigurationStore _configurations = new();
    private readonly RecordingProcessRunner _runner = new();
    private readonly RecordingFrontend _frontend = new();
    private readonly EmulatorDefinition _emulator = new(
        "test-emulator",
        "Test Emulator",
        ["test-system"],
        "--game \"{GamePath}\"");
    private readonly EmulatorDefinition _coreEmulator = new(
        "test-retroarch",
        "Test RetroArch",
        ["core-system"],
        "-L \"{CorePath}\" \"{GamePath}\"",
        RequiresCorePath: true,
        RequiresContentFile: true);

    public EmulatorLaunchServiceTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task LaunchAsync_ValidatesMissingGameBeforeMinimizing()
    {
        var service = CreateService();

        var result = await service.LaunchAsync(GameAt(Path.Combine(_directory, "missing.cue")));

        Assert.False(result.Succeeded);
        Assert.Contains("game path is unavailable", result.StatusText);
        Assert.False(_frontend.WasMinimized);
        Assert.False(_runner.WasRun);
    }

    [Fact]
    public async Task LaunchAsync_ReportsUnconfiguredEmulatorBeforeMinimizing()
    {
        var game = CreateGameFile();
        var service = CreateService();

        var result = await service.LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("Configure Test Emulator", result.StatusText);
        Assert.False(_frontend.WasMinimized);
        Assert.False(_runner.WasRun);
    }

    [Fact]
    public async Task LaunchAsync_PassesArgumentArrayTracksExitAndRestoresFrontend()
    {
        var game = CreateGameFile("Game With Spaces.cue");
        var executable = CreateGameFile("Emulator Folder/test-emulator.exe").Path;
        _configurations.Configuration = new(
            game.SystemId,
            executable,
            "--batch \"{GamePath}\" --from=\"{EmulatorDirectory}\"");
        var service = CreateService();

        var result = await service.LaunchAsync(game);

        Assert.True(result.Succeeded);
        Assert.Equal(["--batch", game.Path, $"--from={Path.GetDirectoryName(executable)}"], _runner.Arguments);
        Assert.Equal(Path.GetDirectoryName(executable), _runner.WorkingDirectory);
        Assert.True(_frontend.WasMinimized);
        Assert.True(_frontend.WasRestored);
    }

    [Fact]
    public async Task LaunchAsync_ProcessStartFailureRestoresFrontendAndReturnsFeedback()
    {
        var game = CreateGameFile();
        var executable = CreateGameFile("emulator.exe").Path;
        _configurations.Configuration = new(game.SystemId, executable, null);
        _runner.Exception = new InvalidOperationException("start failed");
        var service = CreateService();

        var result = await service.LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("start failed", result.StatusText);
        Assert.True(_frontend.WasRestored);
    }

    [Fact]
    public async Task LaunchAsync_RequiresConfiguredCoreBeforeMinimizing()
    {
        var game = CreateGameFile("game.gba", "core-system");
        var executable = CreateGameFile("retroarch.exe", "core-system").Path;
        _configurations.Configuration = new(game.SystemId, executable, null);
        var service = CreateCoreService();

        var result = await service.LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("select an installed Test RetroArch core", result.StatusText);
        Assert.False(_frontend.WasMinimized);
        Assert.False(_runner.WasRun);
    }

    [Fact]
    public async Task LaunchAsync_RejectsFolderContentForCoreLauncherBeforeMinimizing()
    {
        var contentFolder = Path.Combine(_directory, "content-folder");
        Directory.CreateDirectory(contentFolder);
        var game = GameAt(contentFolder, "core-system");
        var executable = CreateGameFile("RetroArch/retroarch.exe", "core-system").Path;
        var core = CreateGameFile("RetroArch/cores/mgba_libretro.dll", "core-system").Path;
        _configurations.Configuration = new(game.SystemId, executable, null)
        {
            CorePath = core,
        };
        var service = CreateCoreService();

        var result = await service.LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("requires a game content file, not a folder", result.StatusText);
        Assert.False(_frontend.WasMinimized);
        Assert.False(_runner.WasRun);
    }

    [Fact]
    public async Task LaunchAsync_RequiresExistingCoreBeforeMinimizing()
    {
        var game = CreateGameFile("game.gba", "core-system");
        var executable = CreateGameFile("RetroArch/retroarch.exe", "core-system").Path;
        _configurations.Configuration = new(game.SystemId, executable, null)
        {
            CorePath = Path.Combine(_directory, "RetroArch", "cores", "missing.dll"),
        };
        var service = CreateCoreService();

        var result = await service.LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("configured Test RetroArch core was not found", result.StatusText);
        Assert.False(_frontend.WasMinimized);
        Assert.False(_runner.WasRun);
    }

    [Fact]
    public async Task LaunchAsync_PassesConfiguredCoreAsAnArgument()
    {
        var game = CreateGameFile("Game With Spaces.gba", "core-system");
        var executable = CreateGameFile("RetroArch/retroarch.exe", "core-system").Path;
        var core = CreateGameFile("RetroArch/cores/mgba core.dll", "core-system").Path;
        _configurations.Configuration = new(game.SystemId, executable, null)
        {
            CorePath = core,
        };
        var service = CreateCoreService();

        var result = await service.LaunchAsync(game);

        Assert.True(result.Succeeded);
        Assert.Equal(["-L", core, game.Path], _runner.Arguments);
        Assert.True(_frontend.WasMinimized);
        Assert.True(_frontend.WasRestored);
    }

    [Fact]
    public async Task LaunchAsync_RequiresCorePathPlaceholderBeforeMinimizing()
    {
        var game = CreateGameFile("game.gba", "core-system");
        var executable = CreateGameFile("RetroArch/retroarch.exe", "core-system").Path;
        var core = CreateGameFile("RetroArch/cores/mgba_libretro.dll", "core-system").Path;
        _configurations.Configuration = new(game.SystemId, executable, "\"{GamePath}\"")
        {
            CorePath = core,
        };
        var service = CreateCoreService();

        var result = await service.LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("must include {CorePath}", result.StatusText);
        Assert.False(_frontend.WasMinimized);
        Assert.False(_runner.WasRun);
    }

    [Fact]
    public async Task LaunchAsync_RejectsMalformedCoreTemplateBeforeMinimizing()
    {
        var game = CreateGameFile("game.gba", "core-system");
        var executable = CreateGameFile("RetroArch/retroarch.exe", "core-system").Path;
        var core = CreateGameFile("RetroArch/cores/mgba_libretro.dll", "core-system").Path;
        _configurations.Configuration = new(game.SystemId, executable, "-L \"{CorePath}\" \"{GamePath}")
        {
            CorePath = core,
        };
        var service = CreateCoreService();

        var result = await service.LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("unmatched double quote", result.StatusText);
        Assert.False(_frontend.WasMinimized);
        Assert.False(_runner.WasRun);
    }

    private EmulatorLaunchService CreateService() => new(
        _configurations,
        _runner,
        _frontend,
        [_emulator]);

    private EmulatorLaunchService CreateCoreService() => new(
        _configurations,
        _runner,
        _frontend,
        [_coreEmulator]);

    private Game CreateGameFile(string relativePath = "game.cue", string systemId = "test-system")
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test");
        return GameAt(path, systemId);
    }

    private static Game GameAt(string path, string systemId = "test-system") => new()
    {
        Id = 1,
        SystemId = systemId,
        Path = path,
        Title = "Test Game",
        DateAdded = DateTimeOffset.UtcNow,
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeConfigurationStore : IEmulatorConfigurationStore
    {
        public EmulatorConfiguration? Configuration { get; set; }
        public EmulatorConfiguration? Get(string systemId) => Configuration;
        public void Save(EmulatorConfiguration configuration) => Configuration = configuration;
        public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations) =>
            Configuration = configurations.LastOrDefault();
    }

    private sealed class RecordingProcessRunner : ITrackedProcessRunner
    {
        public bool WasRun { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public string? WorkingDirectory { get; private set; }
        public Exception? Exception { get; set; }

        public Task<int> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            WasRun = true;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            if (Exception is not null)
                throw Exception;
            return Task.FromResult(0);
        }
    }

    private sealed class RecordingFrontend : IFrontendController
    {
        public bool WasMinimized { get; private set; }
        public bool WasRestored { get; private set; }
        public void Minimize() => WasMinimized = true;
        public void Restore() => WasRestored = true;
    }
}
