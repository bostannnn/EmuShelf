using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmuShelf.App.Services;
using EmuShelf.App.ViewModels;
using EmuShelf.App.Views;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Storage;
using EmuShelf.Integrations.Emulators;
using EmuShelf.Integrations.Emulators.Android;
using EmuShelf.Integrations.Systems;

namespace EmuShelf.App.Tests;

/// <summary>
/// Renders both halves of the Android setup wizard headlessly at the Thor's couch canvas (1280×720 dip)
/// and, when EMUSHELF_SNAPSHOT_DIR is set, writes PNGs to look at. The assertions keep the two halves
/// honest about sharing one design: both list the same rail, both use the shared row control.
/// </summary>
public class SetupWizardSnapshotTests
{
    private static readonly string? OutputDirectory = Environment.GetEnvironmentVariable("EMUSHELF_SNAPSHOT_DIR");

    [AvaloniaFact]
    public async Task PreBootPage_StorageAccessThenDataFolder_At1280x720()
    {
        var bootstrap = new SnapshotBootstrap
        {
            RequiresStoragePermission = true,
            IsStoragePermissionGranted = false,
            ShowSecondScreenReturnStep = true,
            RecommendedBaseDirectory = "/storage/emulated/0/EmuShelf",
            ExistingDataFolder = "/storage/emulated/0/User/EmuShelf",
        };
        var viewModel = new SetupWizardViewModel(bootstrap, DataLocationOnboardingReason.FirstRun, _ => { });
        var window = new Window
        {
            Content = new SetupWizardView { DataContext = viewModel },
            Width = 1280,
            Height = 720,
        };
        window.Show();
        try
        {
            await PumpAsync();
            Assert.Equal(2, window.GetVisualDescendants().OfType<GamepadSettingsRowView>().Count());
            await SaveAsync(window, "setup-wizard-a1-storage.png");

            bootstrap.IsStoragePermissionGranted = true;
            bootstrap.RaisePermissionMaybeChanged();
            await PumpAsync();
            Assert.Equal(SetupStep.DataFolder, viewModel.CurrentStep);
            Assert.Equal(4, window.GetVisualDescendants().OfType<GamepadSettingsRowView>().Count());
            await SaveAsync(window, "setup-wizard-a2-folder.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task InAppSteps_SecondScreenClosingGamesEmulators_At1280x720()
    {
        var maintenance = new LibraryMaintenanceActions(
            (_, _) => Task.FromResult(string.Empty),
            _ => Task.FromResult(string.Empty),
            GetCloseEmulatorOnReturn: () => true,
            SetCloseEmulatorOnReturn: _ => Task.CompletedTask);
        var desktopSettings = new EmulatorSettingsViewModel(
            KnownSystems.All,
            KnownEmulators.All,
            KnownSystems.All.ToDictionary(system => system.Id, _ => (EmulatorConfiguration?)null, StringComparer.Ordinal),
            new NullEmulatorConfigurationStore(),
            new NullDialogService(),
            maintenance,
            fixedEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["playstation2"] = 234, ["psp"] = 65, ["nds"] = 131, ["snes"] = 52,
        };
        var wizard = new GamepadSettingsViewModel(
            desktopSettings,
            androidEmulatorChoices: AndroidEmulatorChoiceCatalog.BySystem,
            gameCountBySystem: systemId => counts.GetValueOrDefault(systemId),
            isEmulatorChoiceInstalled: choice => choice.EmulatorId != "pcsx2",
            closeOnReturnWarning: () => "Shizuku permission not granted · press Y to allow it",
            grantCloseOnReturnPrivilege: () => Task.CompletedTask,
            setup: new SetupWizardOptions(true, () => false, () => { }, "User/EmuShelf"));
        var viewModel = new MainViewModel
        {
            IsGamepadMode = true,
            GamepadSettings = wizard,
            GamepadOverlay = GamepadOverlayKind.Settings,
        };
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        var window = new MainWindow { DataContext = viewModel, Width = 1280, Height = 720 };
        window.Show();
        try
        {
            await PumpAsync();
            Assert.True(viewModel.IsGamepadSetupLegendVisible);
            Assert.False(viewModel.IsGamepadSettingsLegendVisible);
            Assert.Single(window.GetVisualDescendants().OfType<SetupWizardRailView>(), rail => rail.IsVisible);
            await SaveAsync(window, "setup-wizard-b1-second-screen.png");

            wizard.Dispatch(GamepadAction.Menu);
            await PumpAsync();
            Assert.Equal(SetupStep.ClosingGames, wizard.CurrentSetupStep);
            await SaveAsync(window, "setup-wizard-b2-closing-games.png");

            wizard.Dispatch(GamepadAction.Menu);
            await PumpAsync();
            Assert.Equal(SetupStep.GamesAndEmulators, wizard.CurrentSetupStep);
            await SaveAsync(window, "setup-wizard-b3-games-emulators.png");
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task PumpAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static async Task SaveAsync(Window window, string fileName)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.Equal(new PixelSize(1280, 720), frame.PixelSize);
        if (OutputDirectory is null)
            return;
        Directory.CreateDirectory(OutputDirectory);
        using var output = File.Create(Path.Combine(OutputDirectory, fileName));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }

    private sealed class SnapshotBootstrap : IDataLocationBootstrap
    {
        public DataLocationResolution Resolve() => DataLocationResolution.Onboarding(DataLocationOnboardingReason.FirstRun);
        public bool RequiresStoragePermission { get; set; }
        public bool IsStoragePermissionGranted { get; set; }
        public string? RecommendedBaseDirectory { get; set; }
        public string? ExistingDataFolder { get; set; }
        public bool ShowSecondScreenReturnStep { get; set; }
        public bool IsSecondScreenReturnEnabled => false;
        public event Action? StoragePermissionMaybeChanged;
        public void RaisePermissionMaybeChanged() => StoragePermissionMaybeChanged?.Invoke();
        public void RequestStoragePermission() { }
        public void RequestSecondScreenReturn() { }
        public string? FindExistingDataFolder() => ExistingDataFolder;
        public Task<DataLocationPickResult> UseExistingFolderAsync(string baseDirectory) =>
            Task.FromResult(DataLocationPickResult.Success(baseDirectory));
        public Task<DataLocationPickResult> UseRecommendedFolderAsync() => Task.FromResult(DataLocationPickResult.Cancelled());
        public Task<DataLocationPickResult> PickFolderAsync() => Task.FromResult(DataLocationPickResult.Cancelled());
    }
}
