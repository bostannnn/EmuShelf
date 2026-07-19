using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Integrations.Emulators.Ppsspp;

namespace EmuShelf.Infrastructure.Tests.Launching;

public sealed class PpssppLaunchTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "EmuShelfPpssppTests", Guid.NewGuid().ToString("N"));
    private readonly RecordingConfigurationStore _configurations = new();
    private readonly RecordingProcessRunner _runner = new();
    private readonly RecordingFrontend _frontend = new();

    public PpssppLaunchTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task LaunchAsync_PassesSpacedPspPathAsOneArgumentAndTracksExit()
    {
        var game = CreateFile("PSP Games/Lumines With Spaces.cso");
        var executable = CreateFile("PPSSPP Portable/PPSSPPWindows.exe").Path;
        _configurations.Configuration = new("psp", executable, null)
        {
            EmulatorId = "ppsspp",
            EmulatorInstallationId = "ppsspp-psp",
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
    public async Task LaunchAsync_MissingPpssppExecutableFailsBeforeMinimizing()
    {
        var game = CreateFile("game.iso");
        _configurations.Configuration = new("psp", Path.Combine(_directory, "missing PPSSPP.exe"), null);

        var result = await CreateService().LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.Contains("configured PPSSPP executable was not found", result.StatusText);
        Assert.False(_runner.WasRun);
        Assert.False(_frontend.WasMinimized);
    }

    [Fact]
    public async Task LaunchAsync_PpssppNonZeroExitRestoresFrontendAndReportsFailure()
    {
        var game = CreateFile("game.iso");
        var executable = CreateFile("PPSSPPWindows.exe").Path;
        _configurations.Configuration = new("psp", executable, null);
        _runner.ExitCode = 7;

        var result = await CreateService().LaunchAsync(game);

        Assert.False(result.Succeeded);
        Assert.True(result.ProcessExited);
        Assert.Contains("PPSSPP exited with code 7", result.StatusText);
        Assert.True(_frontend.WasRestored);
    }

    private EmulatorLaunchService CreateService() => new(
        _configurations,
        _runner,
        _frontend,
        [PpssppDefinition.Instance]);

    private Game CreateFile(string relativePath)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        return new Game
        {
            Id = 1,
            SystemId = "psp",
            Path = path,
            Title = "Lumines",
            DateAdded = DateTimeOffset.UtcNow,
        };
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
