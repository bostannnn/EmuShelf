using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Emulators.Azahar;

namespace EmuShelf.Infrastructure.Tests.Launching;

public sealed class AzaharLaunchTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "EmuShelfAzaharTests", Guid.NewGuid().ToString("N"));
    private readonly RecordingConfigurationStore _configurations = new();
    private readonly RecordingProcessRunner _runner = new();
    private readonly RecordingFrontend _frontend = new();

    public AzaharLaunchTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task LaunchAsync_PassesSpaced3dsPathAsOneArgumentAndTracksExit()
    {
        var game = CreateFile("3DS Games/Ocarina Of Time 3D.z3ds");
        var executable = CreateExecutableFile("Azahar Portable/azahar.exe");
        _configurations.Configuration = new("3ds", executable, null)
        {
            EmulatorId = "azahar",
            EmulatorInstallationId = "azahar-3ds",
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
    public async Task LaunchAsync_MissingAzaharExecutableFailsBeforeMinimizing()
    {
        var game = CreateFile("game.cci");
        _configurations.Configuration = new("3ds", Path.Combine(_directory, "missing azahar.exe"), null);

        var result = await CreateService().LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.False(_runner.WasRun);
        Assert.False(_frontend.WasMinimized);
    }

    private EmulatorLaunchService CreateService() => new(
        _configurations,
        _runner,
        _frontend,
        [AzaharDefinition.Instance]);

    private Game CreateFile(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        return new Game
        {
            Id = 1,
            SystemId = "3ds",
            Path = path,
            Title = "Ocarina Of Time 3D",
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
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public string? WorkingDirectory { get; private set; }

        public Task<int> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            WasRun = true;
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
