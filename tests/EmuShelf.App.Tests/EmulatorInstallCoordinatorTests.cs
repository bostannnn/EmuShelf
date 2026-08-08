using EmuShelf.App.Services;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Emulators;
using EmuShelf.Core.Launching;

namespace EmuShelf.App.Tests;

public class EmulatorInstallCoordinatorTests
{
    private sealed class FakeInstallService : IEmulatorInstallService
    {
        public Task<EmulatorInstallStatus> GetStatusAsync(string emulatorId, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmulatorInstallStatus>(new EmulatorInstallStatus.NotInstalled("1.0"));

        public Task<EmulatorInstallResult> InstallAsync(string emulatorId, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmulatorInstallResult>(new EmulatorInstallResult.Installed("1.0", "/x"));

        public Task<EmulatorInstallResult> UpdateAsync(string emulatorId, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<EmulatorInstallResult>(new EmulatorInstallResult.AlreadyCurrent("1.0"));
    }

    private static EmulatorDefinition WithSource(string id) =>
        new(id, id.ToUpperInvariant(), ["sys"], "\"{GamePath}\"")
        {
            ReleaseSource = EmulatorReleaseSource.CustomServerPlaceholder("https://vendor/dl"),
        };

    private static EmulatorDefinition WithoutSource(string id) =>
        new(id, id.ToUpperInvariant(), ["sys"], "\"{GamePath}\"");

    [Fact]
    public void BuildsOneRowPerDefinitionThatDeclaresAReleaseSource()
    {
        var coordinator = new EmulatorInstallCoordinator(
            new FakeInstallService(),
            [WithSource("duckstation"), WithoutSource("mystery")],
            NullAppLogger.Instance);

        var row = Assert.Single(coordinator.Rows);
        Assert.Equal("duckstation", row.EmulatorId);
    }

    [Fact]
    public async Task RefreshAsync_RefreshesEveryRow()
    {
        var coordinator = new EmulatorInstallCoordinator(
            new FakeInstallService(),
            [WithSource("duckstation"), WithSource("pcsx2")],
            NullAppLogger.Instance);

        await coordinator.RefreshAsync();

        Assert.All(coordinator.Rows, row => Assert.True(row.CanInstall));
    }

    [Fact]
    public void CreateSettingsContext_SurfacesTheSameRows()
    {
        var coordinator = new EmulatorInstallCoordinator(
            new FakeInstallService(),
            [WithSource("duckstation")],
            NullAppLogger.Instance);

        var context = coordinator.CreateSettingsContext();

        Assert.Same(coordinator.Rows, context.Rows);
    }
}
