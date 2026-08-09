using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Core.Diagnostics;

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
    private readonly RecordingLogger _logger = new();
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
    public async Task LaunchAsync_DoesNotInvokeBeforeStartWhenPreflightFails()
    {
        var game = CreateGameFile();
        var callbackInvoked = false;
        var service = CreateService();

        var result = await service.LaunchAsync(
            game,
            beforeStart: _ =>
            {
                callbackInvoked = true;
                return Task.CompletedTask;
            });

        Assert.False(result.Succeeded);
        Assert.False(callbackInvoked);
        Assert.False(_runner.WasRun);
    }

    [Fact]
    public async Task LaunchAsync_InvokesBeforeStartAfterPreflightAndBeforeFrontendSuspends()
    {
        var game = CreateGameFile();
        var executable = CreateExecutableFile("emulator.exe");
        _configurations.Configuration = new(game.SystemId, executable, null);
        var callbackInvoked = false;
        var service = CreateService();

        var result = await service.LaunchAsync(
            game,
            beforeStart: _ =>
            {
                Assert.False(_frontend.WasMinimized);
                Assert.False(_runner.WasRun);
                callbackInvoked = true;
                return Task.CompletedTask;
            });

        Assert.True(result.Succeeded);
        Assert.True(callbackInvoked);
        Assert.True(_runner.WasRun);
    }

    [Fact]
    public async Task LaunchAsync_NamesTheGameByTheDisplayName_OnCompletion()
    {
        var game = CreateGameFile();
        var executable = CreateExecutableFile("test-emulator.exe");
        _configurations.Configuration = new(game.SystemId, executable, null);
        var service = CreateService();

        // The App passes the normalized scraped title so the completion status matches the library,
        // rather than the game's own (filename-derived) Title.
        var result = await service.LaunchAsync(game, displayName: "Prince of Persia: The Sands of Time");

        Assert.True(result.Succeeded);
        Assert.Equal("Prince of Persia: The Sands of Time finished", result.StatusText);
        Assert.DoesNotContain("Test Game", result.StatusText);
    }

    [Fact]
    public async Task LaunchAsync_NamesTheGameByTheDisplayName_OnFailure()
    {
        var service = CreateService();

        var result = await service.LaunchAsync(
            GameAt(Path.Combine(_directory, "missing.cue")),
            displayName: "Prince of Persia: The Sands of Time");

        Assert.False(result.Succeeded);
        Assert.Contains("Cannot launch Prince of Persia: The Sands of Time", result.StatusText);
    }

    [Fact]
    public async Task LaunchAsync_FallsBackToTheGameTitle_WhenNoDisplayNameIsGiven()
    {
        var game = CreateGameFile();
        var executable = CreateExecutableFile("test-emulator.exe");
        _configurations.Configuration = new(game.SystemId, executable, null);
        var service = CreateService();

        var result = await service.LaunchAsync(game);

        Assert.True(result.Succeeded);
        Assert.Equal("Test Game finished", result.StatusText);
    }

    [Fact]
    public async Task LaunchAsync_PassesArgumentArrayTracksExitAndRestoresFrontend()
    {
        var game = CreateGameFile("Game With Spaces.cue");
        var executable = CreateExecutableFile("Emulator Folder/test-emulator.exe");
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
        Assert.Equal(
            [
                "Launching Test Emulator for Test Game.",
                "Test Emulator exited with code 0.",
                "Restored EmuShelf after Test Emulator exited.",
            ],
            _logger.InformationMessages);
    }

    [Fact]
    public async Task LaunchAsync_ProcessStartFailureRestoresFrontendAndReturnsFeedback()
    {
        var game = CreateGameFile();
        var executable = CreateExecutableFile("emulator.exe");
        _configurations.Configuration = new(game.SystemId, executable, null);
        _runner.Exception = new InvalidOperationException("start failed");
        var service = CreateService();

        var result = await service.LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("start failed", result.StatusText);
        Assert.True(_frontend.WasRestored);
    }

    [Fact]
    public async Task LaunchAsync_FlatpakTarget_UsesShellFreeFlatpakArgumentArray()
    {
        var game = CreateGameFile("Game With Spaces.iso");
        _configurations.Configuration = new EmulatorConfiguration(game.SystemId, null, "--batch \"{GamePath}\"")
        {
            LaunchTarget = new FlatpakApplicationTarget("net.example.Emulator"),
        };
        var service = new EmulatorLaunchService(
            _configurations,
            _runner,
            _frontend,
            [_emulator],
            _logger,
            new PassingTargetInspector(),
            new FixedDependencyResolver(game.Path));

        var result = await service.LaunchAsync(game);

        Assert.True(result.Succeeded);
        Assert.Equal("flatpak", _runner.StartSpec!.FileName);
        // A read-only grant for the game's directory precedes the app id so the sandbox can see the
        // ROM; EmuShelf never persistently changes the emulator's Flatpak permissions.
        Assert.Equal(
            ["run", $"--filesystem={_directory}:ro", "net.example.Emulator", "--batch", game.Path],
            _runner.StartSpec.Arguments);
        Assert.DoesNotContain("\"", string.Join(' ', _runner.StartSpec.Arguments));
    }

    [Fact]
    public async Task LaunchAsync_BranchPinnedFlatpakTarget_RunsThatBranchRef()
    {
        var game = CreateGameFile("Game.iso");
        _configurations.Configuration = new EmulatorConfiguration(game.SystemId, null, "--batch \"{GamePath}\"")
        {
            LaunchTarget = new FlatpakApplicationTarget("net.pcsx2.PCSX2", "beta"),
        };
        var service = new EmulatorLaunchService(
            _configurations,
            _runner,
            _frontend,
            [_emulator],
            _logger,
            new PassingTargetInspector(),
            new FixedDependencyResolver(game.Path));

        var result = await service.LaunchAsync(game);

        Assert.True(result.Succeeded);
        // The pinned branch travels to the command line as an appId//branch ref so flatpak launches the
        // nightly rather than "whichever branch is current" when both are installed.
        Assert.Equal(
            ["run", $"--filesystem={_directory}:ro", "net.pcsx2.PCSX2//beta", "--batch", game.Path],
            _runner.StartSpec!.Arguments);
    }

    [Fact]
    public async Task LaunchAsync_FlatpakRetroArch_PassesCorePathThroughAfterAppId()
    {
        var game = CreateGameFile("Game With Spaces.gba", "core-system");
        var core = CreateGameFile("Flatpak cores/mgba core.so", "core-system").Path;
        _configurations.Configuration = new EmulatorConfiguration(game.SystemId, null, null)
        {
            LaunchTarget = new FlatpakApplicationTarget("org.libretro.RetroArch"),
            CorePath = core,
        };
        var service = new EmulatorLaunchService(
            _configurations,
            _runner,
            _frontend,
            [_coreEmulator],
            _logger,
            new PassingTargetInspector(),
            new FixedDependencyResolver(game.Path));

        var result = await service.LaunchAsync(game);

        Assert.True(result.Succeeded);
        Assert.Equal("flatpak", _runner.StartSpec!.FileName);
        // The sandbox is granted read-only access to both the game's directory and the core's
        // directory: the core lives outside the sandbox's default-visible paths, so without its own
        // grant RetroArch cannot load it even though EmuShelf's host-side preflight passed.
        Assert.Equal(
            [
                "run",
                $"--filesystem={_directory}:ro",
                $"--filesystem={Path.GetDirectoryName(core)}:ro",
                "org.libretro.RetroArch",
                "-L",
                core,
                game.Path,
            ],
            _runner.StartSpec.Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task LaunchAsync_FlatpakRetroArch_RequiresConfiguredCoreBeforeMinimizing(
        string? corePath)
    {
        var game = CreateGameFile("game.gba", "core-system");
        _configurations.Configuration = new EmulatorConfiguration(game.SystemId, null, null)
        {
            LaunchTarget = new FlatpakApplicationTarget("org.libretro.RetroArch"),
            CorePath = corePath,
        };
        var service = new EmulatorLaunchService(
            _configurations,
            _runner,
            _frontend,
            [_coreEmulator],
            _logger,
            new PassingTargetInspector(),
            new FixedDependencyResolver(game.Path));

        var result = await service.LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("select an installed Test RetroArch core", result.StatusText);
        Assert.False(_frontend.WasMinimized);
        Assert.False(_runner.WasRun);
    }

    [Fact]
    public async Task LaunchAsync_FlatpakTarget_GrantsOneReadOnlyDirectoryPerDistinctDependency()
    {
        var game = CreateGameFile("playlist/game.m3u");
        var discOne = CreateGameFile("playlist/game (Disc 1).chd").Path;
        var discTwo = CreateGameFile("other/game (Disc 2).chd").Path;
        _configurations.Configuration = new EmulatorConfiguration(game.SystemId, null, "--batch \"{GamePath}\"")
        {
            LaunchTarget = new FlatpakApplicationTarget("net.example.Emulator"),
        };
        var service = new EmulatorLaunchService(
            _configurations,
            _runner,
            _frontend,
            [_emulator],
            _logger,
            new PassingTargetInspector(),
            // The playlist and both discs share two directories; each is granted once, in order.
            new FixedDependencyResolver(game.Path, discOne, discTwo));

        var result = await service.LaunchAsync(game);

        Assert.True(result.Succeeded);
        var playlistDirectory = Path.GetDirectoryName(game.Path);
        var otherDirectory = Path.GetDirectoryName(discTwo);
        Assert.Equal(
            [
                "run",
                $"--filesystem={playlistDirectory}:ro",
                $"--filesystem={otherDirectory}:ro",
                "net.example.Emulator",
                "--batch",
                game.Path,
            ],
            _runner.StartSpec!.Arguments);
    }

    [Fact]
    public async Task LaunchAsync_FlatpakTarget_RejectsEmulatorDirectoryPlaceholder()
    {
        var game = CreateGameFile();
        _configurations.Configuration = new EmulatorConfiguration(game.SystemId, null, "--from {EmulatorDirectory}")
        {
            LaunchTarget = new FlatpakApplicationTarget("net.example.Emulator"),
        };
        var service = new EmulatorLaunchService(
            _configurations,
            _runner,
            _frontend,
            [_emulator],
            _logger,
            new PassingTargetInspector(),
            new FixedDependencyResolver(game.Path));

        var result = await service.LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("cannot use {EmulatorDirectory}", result.StatusText);
        Assert.False(_runner.WasRun);
    }

    [Fact]
    public async Task LaunchAsync_RequiresConfiguredCoreBeforeMinimizing()
    {
        var game = CreateGameFile("game.gba", "core-system");
        var executable = CreateExecutableFile("retroarch.exe", "core-system");
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
        var executable = CreateExecutableFile("RetroArch/retroarch.exe", "core-system");
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
        var executable = CreateExecutableFile("RetroArch/retroarch.exe", "core-system");
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
        var executable = CreateExecutableFile("RetroArch/retroarch.exe", "core-system");
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

    [Theory]
    [InlineData("\"{GamePath}\"")]
    [InlineData("-L \"{CorePath}\"")]
    [InlineData("\"{CorePath}\" \"{GamePath}\"")]
    [InlineData("-L \"{GamePath}\" \"{CorePath}\"")]
    public async Task LaunchAsync_RequiresExplicitCoreAndContentTemplateBeforeMinimizing(
        string launchArguments)
    {
        var game = CreateGameFile("game.gba", "core-system");
        var executable = CreateExecutableFile("RetroArch/retroarch.exe", "core-system");
        var core = CreateGameFile("RetroArch/cores/mgba_libretro.dll", "core-system").Path;
        _configurations.Configuration = new(game.SystemId, executable, launchArguments)
        {
            CorePath = core,
        };
        var service = CreateCoreService();

        var result = await service.LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("must use -L {CorePath} followed by {GamePath}", result.StatusText);
        Assert.False(_frontend.WasMinimized);
        Assert.False(_runner.WasRun);
    }

    [Fact]
    public async Task LaunchAsync_RejectsMalformedCoreTemplateBeforeMinimizing()
    {
        var game = CreateGameFile("game.gba", "core-system");
        var executable = CreateExecutableFile("RetroArch/retroarch.exe", "core-system");
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

    [Fact]
    public async Task LaunchAsync_WhenTwoEmulatorsSupportTheSystem_LaunchesTheActiveProfilesEmulator()
    {
        // DuckStation and RetroArch both list "test-system"; the active profile names RetroArch, so
        // the core-and-content launcher runs even though the plain emulator is first in the list.
        var game = CreateGameFile("Game With Spaces.cue");
        var core = CreateGameFile("RetroArch/cores/swanstation_libretro.dll").Path;
        var retroArch = new EmulatorDefinition(
            "test-retroarch",
            "Test RetroArch",
            ["test-system"],
            "-L \"{CorePath}\" \"{GamePath}\"",
            RequiresCorePath: true,
            RequiresContentFile: true);
        _configurations.Configuration = new EmulatorConfiguration(game.SystemId, CreateExecutableFile("retroarch.exe"), null)
        {
            EmulatorId = "test-retroarch",
            CorePath = core,
        };
        var service = new EmulatorLaunchService(
            _configurations,
            _runner,
            _frontend,
            [_emulator, retroArch],
            _logger);

        var result = await service.LaunchAsync(game);

        Assert.True(result.Succeeded);
        Assert.Equal(["-L", core, game.Path], _runner.Arguments);
        Assert.Contains("Launching Test RetroArch for Test Game.", _logger.InformationMessages);
    }

    [Fact]
    public async Task LaunchAsync_StaleActiveEmulatorThatNoLongerSupportsTheSystem_FallsBackToSupporting()
    {
        var game = CreateGameFile();
        var executable = CreateExecutableFile("emulator.exe");
        _configurations.Configuration = new EmulatorConfiguration(game.SystemId, executable, null)
        {
            // Names an emulator that is not registered / does not support this system.
            EmulatorId = "removed-emulator",
        };
        var service = CreateService();

        var result = await service.LaunchAsync(game);

        Assert.True(result.Succeeded);
        Assert.Contains("Launching Test Emulator for Test Game.", _logger.InformationMessages);
    }

    private EmulatorLaunchService CreateService() => new(
        _configurations,
        _runner,
        _frontend,
        [_emulator],
        _logger);

    private EmulatorLaunchService CreateCoreService() => new(
        _configurations,
        _runner,
        _frontend,
        [_coreEmulator],
        _logger);

    private Game CreateGameFile(string relativePath = "game.cue", string systemId = "test-system")
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test");
        return GameAt(path, systemId);
    }

    private string CreateExecutableFile(string relativePath, string systemId = "test-system")
    {
        var path = CreateGameFile(relativePath, systemId).Path;
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, mode |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherExecute);
        }

        return path;
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
        public ProcessStartSpec? StartSpec { get; private set; }
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

        public Task<int> RunAsync(ProcessStartSpec startSpec, CancellationToken cancellationToken = default)
        {
            StartSpec = startSpec;
            return RunAsync(startSpec.FileName, startSpec.Arguments, startSpec.WorkingDirectory, cancellationToken);
        }
    }

    private sealed class RecordingFrontend : IFrontendController
    {
        public bool WasMinimized { get; private set; }
        public bool WasRestored { get; private set; }
        public void Minimize() => WasMinimized = true;
        public void Restore() => WasRestored = true;
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> InformationMessages { get; } = [];

        public void Information(string message) => InformationMessages.Add(message);
        public void Warning(string message, Exception? exception = null) { }
        public void Error(string message, Exception? exception = null) { }
    }

    private sealed class PassingTargetInspector : ILaunchTargetInspector
    {
        public LaunchTargetInspection Inspect(EmulatorLaunchTarget target, IReadOnlyList<string> requiredPaths) =>
            LaunchTargetInspection.Passed();
    }

    private sealed class FixedDependencyResolver(params string[] paths) : IGameLaunchDependencyResolver
    {
        public GameLaunchDependencies Resolve(Game game) => new(true, paths);
    }
}
