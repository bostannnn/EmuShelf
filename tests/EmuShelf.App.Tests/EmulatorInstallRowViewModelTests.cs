using EmuShelf.App.ViewModels;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Emulators;

namespace EmuShelf.App.Tests;

public class EmulatorInstallRowViewModelTests
{
    private sealed class FakeInstallService : IEmulatorInstallService
    {
        public Func<EmulatorInstallStatus> StatusFactory { get; set; } =
            () => new EmulatorInstallStatus.NotInstalled(null);
        public EmulatorInstallResult InstallResult { get; set; } =
            new EmulatorInstallResult.Installed("v1", "/managed/exe");
        public EmulatorInstallResult UpdateResult { get; set; } =
            new EmulatorInstallResult.AlreadyCurrent("v1");
        public int InstallCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<EmulatorInstallStatus> GetStatusAsync(string emulatorId, CancellationToken cancellationToken = default) =>
            Task.FromResult(StatusFactory());

        public Task<EmulatorInstallResult> InstallAsync(string emulatorId, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            progress?.Report(0.5);
            return Task.FromResult(InstallResult);
        }

        public Task<EmulatorInstallResult> UpdateAsync(string emulatorId, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            return Task.FromResult(UpdateResult);
        }
    }

    private static EmulatorInstallRowViewModel Row(
        FakeInstallService service,
        Func<string, string, Task>? onInstalled = null,
        Func<string, Task>? openDownloadPage = null) =>
        new("duckstation", "DuckStation", service, NullAppLogger.Instance, onInstalled, openDownloadPage);

    [Fact]
    public async Task Refresh_NotInstalled_EnablesInstallAndShowsLatest()
    {
        var service = new FakeInstallService { StatusFactory = () => new EmulatorInstallStatus.NotInstalled("1.2") };
        var row = Row(service);

        await row.RefreshAsync();

        Assert.True(row.CanInstall);
        Assert.False(row.CanUpdate);
        Assert.Contains("Not installed", row.StatusText);
        Assert.Contains("1.2", row.StatusText);
    }

    [Fact]
    public async Task Refresh_Managed_ShowsVersionAndNoActions()
    {
        var service = new FakeInstallService { StatusFactory = () => new EmulatorInstallStatus.Managed("2.0") };
        var row = Row(service);

        await row.RefreshAsync();

        Assert.False(row.CanInstall);
        Assert.False(row.CanUpdate);
        Assert.Equal("2.0", row.InstalledVersion);
    }

    [Fact]
    public async Task Refresh_UpdateAvailable_EnablesUpdate()
    {
        var service = new FakeInstallService { StatusFactory = () => new EmulatorInstallStatus.UpdateAvailable("1.0", "2.0") };
        var row = Row(service);

        await row.RefreshAsync();

        Assert.True(row.CanUpdate);
        Assert.False(row.CanInstall);
        Assert.Contains("2.0", row.StatusText);
    }

    [Fact]
    public async Task Refresh_Unsupported_ExposesDownloadPage()
    {
        var service = new FakeInstallService
        {
            StatusFactory = () => new EmulatorInstallStatus.Unsupported("No build for your platform.", "https://vendor/dl"),
        };
        var row = Row(service);

        await row.RefreshAsync();

        Assert.True(row.HasDownloadPage);
        Assert.Equal("https://vendor/dl", row.DownloadPageUrl);
        Assert.False(row.CanInstall);
    }

    [Fact]
    public async Task Refresh_UserProvided_ReportsOwnInstall()
    {
        var service = new FakeInstallService { StatusFactory = () => new EmulatorInstallStatus.UserProvided("/opt/duck", null) };
        var row = Row(service);

        await row.RefreshAsync();

        Assert.Contains("your own", row.StatusText);
        Assert.False(row.CanInstall);
        Assert.False(row.CanUpdate);
    }

    [Fact]
    public async Task Install_Success_InvokesOnInstalledAndResyncsToManaged()
    {
        var service = new FakeInstallService { InstallResult = new EmulatorInstallResult.Installed("v3", "/managed/duck") };
        // Before install: not installed; after: managed.
        service.StatusFactory = () => service.InstallCalls > 0
            ? new EmulatorInstallStatus.Managed("v3")
            : new EmulatorInstallStatus.NotInstalled(null);
        string? installedId = null, installedPath = null;
        var row = Row(service, onInstalled: (id, path) => { installedId = id; installedPath = path; return Task.CompletedTask; });

        await row.RefreshAsync();
        await row.InstallCommand.ExecuteAsync(null);

        Assert.Equal(1, service.InstallCalls);
        Assert.Equal("duckstation", installedId);
        Assert.Equal("/managed/duck", installedPath);
        Assert.Equal("v3", row.InstalledVersion);
        Assert.False(row.CanInstall);
        Assert.False(row.IsBusy);
    }

    [Fact]
    public async Task Install_Refused_KeepsReasonVisibleAndDoesNotWireConfig()
    {
        var service = new FakeInstallService { InstallResult = new EmulatorInstallResult.Refused("Not overwriting your files.") };
        var wired = false;
        var row = Row(service, onInstalled: (_, _) => { wired = true; return Task.CompletedTask; });

        await row.RefreshAsync();
        await row.InstallCommand.ExecuteAsync(null);

        Assert.Equal("Not overwriting your files.", row.StatusText);
        Assert.False(wired);
    }

    [Fact]
    public async Task Install_Failed_KeepsReasonVisible()
    {
        var service = new FakeInstallService { InstallResult = new EmulatorInstallResult.Failed("Download failed.") };
        var row = Row(service);

        await row.RefreshAsync();
        await row.InstallCommand.ExecuteAsync(null);

        Assert.Equal("Download failed.", row.StatusText);
    }

    [Fact]
    public async Task Update_Command_CallsUpdate()
    {
        var service = new FakeInstallService
        {
            StatusFactory = () => new EmulatorInstallStatus.UpdateAvailable("1.0", "2.0"),
            UpdateResult = new EmulatorInstallResult.Installed("2.0", "/managed/duck"),
        };
        var row = Row(service);

        await row.RefreshAsync();
        await row.UpdateCommand.ExecuteAsync(null);

        Assert.Equal(1, service.UpdateCalls);
    }

    [Fact]
    public async Task OpenDownloadPage_InvokesTheOpener()
    {
        string? opened = null;
        var service = new FakeInstallService
        {
            StatusFactory = () => new EmulatorInstallStatus.Unsupported("Use the page.", "https://vendor/dl"),
        };
        var row = Row(service, openDownloadPage: url => { opened = url; return Task.CompletedTask; });

        await row.RefreshAsync();
        await row.OpenDownloadPageCommand.ExecuteAsync(null);

        Assert.Equal("https://vendor/dl", opened);
    }
}
