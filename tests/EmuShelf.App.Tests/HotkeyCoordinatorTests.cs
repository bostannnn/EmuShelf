using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Storage;
using EmuShelf.Core.Systems;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.DuckStation;

namespace EmuShelf.App.Tests;

public sealed class HotkeyCoordinatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("emushelf-hotkey-coord").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Describe_ReturnsConfiguredEmulator_WithActionsAndNotAppliedStatus()
    {
        var snapshot = Assert.Single(Build().Describe());

        Assert.Equal(DuckStationDefinition.Instance.Id, snapshot.EmulatorId);
        Assert.Equal("DuckStation", snapshot.DisplayName);
        Assert.Equal(5, snapshot.Actions.Count);
        Assert.True(snapshot.CanOperate);
        Assert.Contains("aren't applied yet", snapshot.StatusText);
    }

    [Fact]
    public void Describe_SkipsEmulatorsThatAreNotConfigured()
    {
        var coordinator = new HotkeyCoordinator(
            new FakePaths(Path.Combine(_root, "Settings")),
            [new GameSystem("playstation", "PlayStation", "PS1", "#fff", 1.0)],
            NullAppLogger.Instance,
            _ => null);

        Assert.Empty(coordinator.Describe());
    }

    [Fact]
    public async Task ApplyAsync_WritesTheKeysIntoTheConfig()
    {
        var snapshot = await Build().ApplyAsync(DuckStationDefinition.Instance.Id, CancellationToken.None);

        Assert.Contains("Applied", snapshot.StatusText);
        Assert.Equal("Keyboard/F8", Read().GetValue("Hotkeys", "PowerOff"));
    }

    [Fact]
    public async Task ApplyAsync_RefusesWhileTheEmulatorIsRunning()
    {
        var snapshot = await Build(running: true).ApplyAsync(DuckStationDefinition.Instance.Id, CancellationToken.None);

        Assert.Contains("running", snapshot.StatusText);
        Assert.Null(Read().GetValue("Hotkeys", "PowerOff"));
    }

    [Fact]
    public async Task RowViewModel_Apply_FoldsTheResultBackIntoTheRow()
    {
        var context = Build().CreateSettingsContext();
        var row = new HotkeyEmulatorRowViewModel(context.Emulators[0], context);

        await row.RunAsync(context.ApplyAsync);

        Assert.Contains("Applied", row.StatusText);
        Assert.False(row.IsBusy);
    }

    private HotkeyCoordinator Build(bool running = false)
    {
        var directory = Path.Combine(_root, "duckstation");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "settings.ini"),
            "[Main]\nSettingsVersion = 3\nRewindEnable = false\n[Hotkeys]\nFastForward = Keyboard/Tab\n");

        var installation = new SaveEmulatorInstallation(
            directory,
            IsFlatpak: false,
            ExecutablePath: Path.Combine(directory, "duckstation.exe"),
            EmulatorId: DuckStationDefinition.Instance.Id);

        return new HotkeyCoordinator(
            new FakePaths(Path.Combine(_root, "Settings")),
            [new GameSystem("playstation", "PlayStation", "PS1", "#fff", 1.0)],
            NullAppLogger.Instance,
            systemId => systemId == "playstation" ? installation : null,
            writeFile: File.WriteAllText,
            isEmulatorRunning: _ => running);
    }

    private EmulatorConfigDocument Read() =>
        new(File.ReadAllText(Path.Combine(_root, "duckstation", "settings.ini")));

    private sealed class FakePaths(string settingsDirectory) : IAppPaths
    {
        public string BaseDirectory => settingsDirectory;
        public string DataDirectory => settingsDirectory;
        public string CoversDirectory => settingsDirectory;
        public string CacheDirectory => settingsDirectory;
        public string LogsDirectory => settingsDirectory;
        public string SettingsDirectory => settingsDirectory;
        public string SavesDirectory => settingsDirectory;
        public string EmulatorsDirectory => settingsDirectory;
        public string DatabaseFilePath => Path.Combine(settingsDirectory, "library.db");
        public string SettingsFilePath => Path.Combine(settingsDirectory, "settings.json");

        public void EnsureDirectoriesExist()
        {
        }
    }
}
