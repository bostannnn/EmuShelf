using EmuShelf.Core.Launching;
using EmuShelf.Core.Library;
using EmuShelf.Infrastructure.Launching;
using EmuShelf.Infrastructure.Library;
using EmuShelf.Infrastructure.Persistence;
using EmuShelf.Infrastructure.Storage;
using EmuShelf.Integrations.Emulators.RetroArch;

namespace EmuShelf.Infrastructure.Tests.Launching;

public sealed class RetroArchLaunchTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "EmuShelfRetroArchLaunchTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SharedPortableInstallation_LaunchesEachSystemCoreWithoutChangingOverrides()
    {
        var originalBase = Path.Combine(_directory, "Portable");
        var originalPaths = new AppPaths(originalBase);
        originalPaths.EnsureDirectoriesExist();
        var originalDatabase = new LibraryDatabase(originalPaths);
        originalDatabase.Initialize();
        var originalResolver = new RelativePathResolver(originalPaths);
        var originalConfigurations = new SqliteEmulatorConfigurationStore(
            originalDatabase,
            originalResolver);
        var originalLibrary = new GameLibrary(originalDatabase, originalResolver);
        var executable = WriteFile(originalBase, "Emulators/RetroArch/retroarch");
        var overrides = WriteFile(originalBase, "Emulators/RetroArch/config/overrides.cfg", "unchanged");
        var overridesBefore = File.ReadAllBytes(overrides);
        var overridesTimeBefore = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(overrides, overridesTimeBefore);

        var systems = new[]
        {
            (Id: "megadrive", Core: "genesis_plus_gx_libretro", Extension: ".md"),
            (Id: "nds", Core: "melonds_libretro", Extension: ".nds"),
            (Id: "gba", Core: "mgba_libretro", Extension: ".gba"),
        };
        originalConfigurations.SaveAll(systems.Select(system =>
            new EmulatorConfiguration(system.Id, executable, RetroArchDefinition.Instance.DefaultLaunchArguments)
            {
                EmulatorId = RetroArchDefinition.Instance.Id,
                EmulatorInstallationId = RetroArchDefinition.Instance.Id,
                CorePath = WriteFile(originalBase, $"Emulators/RetroArch/cores/{system.Core}.dll"),
            }).ToArray());
        originalLibrary.AddGames(systems.Select(system => new Game
        {
            SystemId = system.Id,
            Path = WriteFile(originalBase, $"Library/{system.Id}/game{system.Extension}"),
            Title = system.Id,
            DateAdded = DateTimeOffset.UtcNow,
        }));

        var movedBase = Path.Combine(_directory, "MovedPortable");
        Directory.Move(originalBase, movedBase);
        var movedPaths = new AppPaths(movedBase);
        var movedDatabase = new LibraryDatabase(movedPaths);
        movedDatabase.Initialize();
        var movedResolver = new RelativePathResolver(movedPaths);
        var configurations = new SqliteEmulatorConfigurationStore(movedDatabase, movedResolver);
        var library = new GameLibrary(movedDatabase, movedResolver);
        var runner = new RecordingProcessRunner();
        var frontend = new RecordingFrontend();
        var service = new EmulatorLaunchService(
            configurations,
            runner,
            frontend,
            [RetroArchDefinition.Instance]);

        foreach (var system in systems)
        {
            var game = Assert.Single(library.GetGames(system.Id));

            var result = await service.LaunchAsync(game);

            Assert.True(result.Succeeded);
            Assert.Equal(
                [
                    "-L",
                    Path.Combine(movedBase, "Emulators", "RetroArch", "cores", $"{system.Core}.dll"),
                    game.Path,
                ],
                runner.Calls[^1].Arguments);
        }

        Assert.Equal(3, frontend.MinimizeCount);
        Assert.Equal(3, frontend.RestoreCount);
        Assert.Equal(overridesBefore, File.ReadAllBytes(
            Path.Combine(movedBase, "Emulators", "RetroArch", "config", "overrides.cfg")));
        Assert.Equal(overridesTimeBefore, File.GetLastWriteTimeUtc(
            Path.Combine(movedBase, "Emulators", "RetroArch", "config", "overrides.cfg")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static string WriteFile(string baseDirectory, string relativePath, string contents = "fixture")
    {
        var path = Path.Combine(baseDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private sealed class RecordingProcessRunner : ITrackedProcessRunner
    {
        public List<(string ExecutablePath, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Task<int> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((executablePath, arguments));
            return Task.FromResult(0);
        }
    }

    private sealed class RecordingFrontend : IFrontendController
    {
        public int MinimizeCount { get; private set; }
        public int RestoreCount { get; private set; }

        public void Minimize() => MinimizeCount++;
        public void Restore() => RestoreCount++;
    }
}
