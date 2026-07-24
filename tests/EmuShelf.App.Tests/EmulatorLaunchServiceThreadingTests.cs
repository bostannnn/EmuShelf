using Avalonia.Headless.XUnit;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;

namespace EmuShelf.App.Tests;

public class EmulatorLaunchServiceThreadingTests
{
    [AvaloniaFact]
    public async Task LaunchAsync_PreflightRunsOffUiThread_AndFrontendStaysOnUiThread()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "EmuShelfLaunchThreadTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var gamePath = Path.Combine(directory, "game.cue");
            var executablePath = Path.Combine(directory, "emulator.exe");
            File.WriteAllText(gamePath, "game");
            File.WriteAllText(executablePath, "emulator");
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(executablePath);
                File.SetUnixFileMode(executablePath, mode |
                    UnixFileMode.UserExecute |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherExecute);
            }
            var uiThreadId = Environment.CurrentManagedThreadId;
            var configurations = new ThreadRecordingConfigurationStore(
                new EmulatorConfiguration("test-system", executablePath, null));
            var frontend = new ThreadRecordingFrontend();
            var service = new EmulatorLaunchService(
                configurations,
                new SuccessfulProcessRunner(),
                frontend,
                [new EmulatorDefinition("test", "Test", ["test-system"], "\"{GamePath}\"")]);

            var result = await service.LaunchAsync(new Game
            {
                Id = 1,
                SystemId = "test-system",
                Path = gamePath,
                Title = "Game",
                DateAdded = DateTimeOffset.UtcNow,
            });

            Assert.True(result.Succeeded);
            Assert.NotEqual(uiThreadId, configurations.GetThreadId);
            Assert.Equal(uiThreadId, frontend.MinimizeThreadId);
            Assert.Equal(uiThreadId, frontend.RestoreThreadId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ThreadRecordingConfigurationStore(EmulatorConfiguration configuration)
        : IEmulatorConfigurationStore
    {
        public int GetThreadId { get; private set; }

        public EmulatorConfiguration? Get(string systemId)
        {
            GetThreadId = Environment.CurrentManagedThreadId;
            return configuration;
        }

        public void Save(EmulatorConfiguration value) { }
        public void SaveAll(IReadOnlyList<EmulatorConfiguration> configurations) { }
    }

    private sealed class SuccessfulProcessRunner : ITrackedProcessRunner
    {
        public Task<int> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class ThreadRecordingFrontend : IFrontendController
    {
        public int MinimizeThreadId { get; private set; }
        public int RestoreThreadId { get; private set; }

        public void Minimize() => MinimizeThreadId = Environment.CurrentManagedThreadId;
        public void Restore() => RestoreThreadId = Environment.CurrentManagedThreadId;
    }
}
