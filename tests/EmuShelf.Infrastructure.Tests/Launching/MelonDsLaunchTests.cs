using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Emulators;

namespace EmuShelf.Infrastructure.Tests.Launching;

public sealed class MelonDsLaunchTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "EmuShelfMelonDsTests", Guid.NewGuid().ToString("N"));
    private readonly RecordingConfigurationStore _configurations = new();
    private readonly RecordingProcessRunner _runner = new();
    private readonly RecordingFrontend _frontend = new();

    public MelonDsLaunchTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Theory]
    [InlineData("melonds")]
    [InlineData("melonds-nightly")]
    public async Task LaunchAsync_PassesSpacedRomPathAsOneArgumentAndTracksExit(string emulatorId)
    {
        var game = CreateFile("DS Games/Pokemon Platinum Version.nds");
        var executable = CreateExecutableFile($"{emulatorId}/melonDS.exe");
        _configurations.Configuration = new("nds", executable, null)
        {
            EmulatorId = emulatorId,
            EmulatorInstallationId = $"{emulatorId}-nds",
        };

        var result = await CreateService().LaunchAsync(game);

        Assert.True(result.Succeeded);
        Assert.True(result.ProcessExited);
        Assert.Equal([game.Path], _runner.Arguments);
        Assert.Equal(Path.GetDirectoryName(executable), _runner.WorkingDirectory);
        Assert.True(_frontend.WasMinimized);
        Assert.True(_frontend.WasRestored);
    }

    [Fact]
    public async Task LaunchAsync_EachChannelLaunchesItsOwnExecutable()
    {
        // The two channels are separate emulators precisely so both can be installed at once; the
        // active profile's id has to pick between them rather than the first melonDS in the list.
        var game = CreateFile("Tetris DS.nds");
        CreateExecutableFile("release/melonDS.exe");
        var nightly = CreateExecutableFile("nightly/melonDS.exe");
        _configurations.Configuration = new("nds", nightly, null)
        {
            EmulatorId = "melonds-nightly",
            EmulatorInstallationId = "melonds-nightly-nds",
        };

        var result = await CreateService().LaunchAsync(game);

        Assert.True(result.Succeeded);
        Assert.Equal(nightly, _runner.ExecutablePath);
        Assert.Equal(Path.GetDirectoryName(nightly), _runner.WorkingDirectory);
    }

    [Fact]
    public async Task LaunchAsync_MissingMelonDsExecutableFailsBeforeMinimizing()
    {
        var game = CreateFile("game.nds");
        _configurations.Configuration = new("nds", Path.Combine(_directory, "missing melonDS.exe"), null)
        {
            EmulatorId = "melonds",
            EmulatorInstallationId = "melonds-nds",
        };

        var result = await CreateService().LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.False(_runner.WasRun);
        Assert.False(_frontend.WasMinimized);
    }

    [Fact]
    public async Task LaunchAsync_WithNoProfileStillFallsBackToRetroArch()
    {
        // melonDS is registered after RetroArch so a DS system that never picked an emulator keeps
        // launching exactly as it did before melonDS existed.
        var game = CreateFile("game.nds");
        var executable = CreateExecutableFile("RetroArch/retroarch.exe");
        var core = CreateFile("RetroArch/cores/melondsds_libretro.dll").Path;
        _configurations.Configuration = new("nds", executable, null) { CorePath = core };

        var result = await CreateService().LaunchAsync(game);

        Assert.True(result.Succeeded);
        Assert.Equal(executable, _runner.ExecutablePath);
        Assert.Equal(["-L", core, game.Path], _runner.Arguments);
    }

    // The real registration order, so the fallback above is the one a shipped build takes.
    private EmulatorLaunchService CreateService() => new(
        _configurations,
        _runner,
        _frontend,
        KnownEmulators.All);

    private Game CreateFile(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        return new Game
        {
            Id = 1,
            SystemId = "nds",
            Path = path,
            Title = Path.GetFileNameWithoutExtension(path),
            DateAdded = DateTimeOffset.UtcNow,
        };
    }

    private string CreateExecutableFile(string relativePath)
    {
        var path = CreateFile(relativePath).Path;
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

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecordingConfigurationStore : IEmulatorConfigurationStore
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
        public int ExitCode { get; set; }
        public string? ExecutablePath { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public string? WorkingDirectory { get; private set; }

        public Task<int> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            WasRun = true;
            ExecutablePath = executablePath;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            return Task.FromResult(ExitCode);
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
