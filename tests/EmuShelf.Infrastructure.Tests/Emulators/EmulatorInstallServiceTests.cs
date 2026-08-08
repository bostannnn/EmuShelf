using System.IO.Compression;
using System.Text;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Emulators;
using EmuShelf.Core.Launching;
using EmuShelf.Infrastructure.Emulators;

namespace EmuShelf.Infrastructure.Tests.Emulators;

public class EmulatorInstallServiceTests : TempAppDirectoryTestBase
{
    private const string EmulatorId = "testmu";

    private static IReadOnlyList<EmulatorReleaseAsset> AllPlatforms(
        string assetPattern, EmulatorArchiveKind kind, string exePattern)
    {
        var assets = new List<EmulatorReleaseAsset>();
        foreach (var os in new[] { "windows", "linux", "macos" })
        foreach (var arch in new[] { "x64", "arm64" })
            assets.Add(new EmulatorReleaseAsset(os, arch, assetPattern, kind, exePattern));
        return assets;
    }

    private static EmulatorDefinition Def(EmulatorReleaseSource source) =>
        new(EmulatorId, "TestMu", ["sys"], "\"{GamePath}\"") { ReleaseSource = source };

    private string MakeZip(string content)
    {
        Directory.CreateDirectory(BaseDirectory);
        var zipPath = Path.Combine(BaseDirectory, $"asset-{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("payload/tool.exe");
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
        return zipPath;
    }

    private EmulatorInstallService Service(
        EmulatorDefinition definition,
        FakeReleaseClient client,
        Func<string, string?>? userProbe = null) =>
        new(
            [definition],
            AppPaths,
            new JsonEmulatorInstallManifestStore(AppPaths),
            client,
            NullAppLogger.Instance,
            userProbe);

    private static EmulatorReleaseSource ManagedZipSource() =>
        EmulatorReleaseSource.GitHub("test/repo", AllPlatforms(@"asset.*\.zip", EmulatorArchiveKind.Zip, @"tool\.exe$"));

    private static GitHubEmulatorRelease ReleaseWithZip(string tag) =>
        new(tag, $"TestMu {tag}", null, [new GitHubEmulatorReleaseAsset("asset-emu.zip", "https://example/asset-emu.zip", 10)]);

    [Fact]
    public async Task Install_DownloadsExtractsResolvesExecutableAndRecordsManifest()
    {
        var client = new FakeReleaseClient
        {
            Release = ReleaseWithZip("tag-1"),
            SourceFileToCopy = MakeZip("MANAGED-EXE"),
        };
        var service = Service(Def(ManagedZipSource()), client);

        var result = await service.InstallAsync(EmulatorId);

        var installed = Assert.IsType<EmulatorInstallResult.Installed>(result);
        Assert.Equal("tag-1", installed.Version);
        var expectedExe = Path.Combine(AppPaths.EmulatorsDirectory, EmulatorId, "payload", "tool.exe");
        Assert.Equal(expectedExe, installed.ExecutablePath);
        Assert.Equal("MANAGED-EXE", File.ReadAllText(installed.ExecutablePath));

        var record = new JsonEmulatorInstallManifestStore(AppPaths).Get(EmulatorId);
        Assert.NotNull(record);
        Assert.Equal("tag-1", record!.SourceTag);
        Assert.Equal($"Emulators/{EmulatorId}/payload/tool.exe", record.ExecutableRelativePath);
    }

    [Fact]
    public async Task GetStatus_AfterInstall_ReportsManaged_WhenNoNewerBuild()
    {
        var client = new FakeReleaseClient { Release = ReleaseWithZip("tag-1"), SourceFileToCopy = MakeZip("x") };
        var service = Service(Def(ManagedZipSource()), client);
        await service.InstallAsync(EmulatorId);

        var status = await service.GetStatusAsync(EmulatorId);

        Assert.Equal(new EmulatorInstallStatus.Managed("tag-1"), status);
    }

    [Fact]
    public async Task GetStatus_AfterInstall_ReportsUpdateAvailable_WhenNewerTagPublished()
    {
        var client = new FakeReleaseClient { Release = ReleaseWithZip("tag-1"), SourceFileToCopy = MakeZip("x") };
        var service = Service(Def(ManagedZipSource()), client);
        await service.InstallAsync(EmulatorId);

        client.Release = ReleaseWithZip("tag-2");
        var status = await service.GetStatusAsync(EmulatorId);

        Assert.Equal(new EmulatorInstallStatus.UpdateAvailable("tag-1", "tag-2"), status);
    }

    [Fact]
    public async Task Update_WhenTagUnchanged_ReturnsAlreadyCurrent()
    {
        var client = new FakeReleaseClient { Release = ReleaseWithZip("tag-1"), SourceFileToCopy = MakeZip("x") };
        var service = Service(Def(ManagedZipSource()), client);
        await service.InstallAsync(EmulatorId);

        var result = await service.UpdateAsync(EmulatorId);

        Assert.Equal(new EmulatorInstallResult.AlreadyCurrent("tag-1"), result);
    }

    [Fact]
    public async Task Update_WhenNewerTag_ReinstallsToNewVersion()
    {
        var client = new FakeReleaseClient { Release = ReleaseWithZip("tag-1"), SourceFileToCopy = MakeZip("old") };
        var service = Service(Def(ManagedZipSource()), client);
        await service.InstallAsync(EmulatorId);

        client.Release = ReleaseWithZip("tag-2");
        client.SourceFileToCopy = MakeZip("new");
        var result = await service.UpdateAsync(EmulatorId);

        var installed = Assert.IsType<EmulatorInstallResult.Installed>(result);
        Assert.Equal("tag-2", installed.Version);
        Assert.Equal("new", File.ReadAllText(installed.ExecutablePath));
    }

    [Fact]
    public async Task Install_RefusesToClobberUnmanagedFilesInTheManagedFolder()
    {
        var strayDir = Path.Combine(AppPaths.EmulatorsDirectory, EmulatorId);
        Directory.CreateDirectory(strayDir);
        File.WriteAllText(Path.Combine(strayDir, "stray.txt"), "not ours");

        var client = new FakeReleaseClient { Release = ReleaseWithZip("tag-1"), SourceFileToCopy = MakeZip("x") };
        var service = Service(Def(ManagedZipSource()), client);

        var result = await service.InstallAsync(EmulatorId);

        Assert.IsType<EmulatorInstallResult.Refused>(result);
        Assert.True(File.Exists(Path.Combine(strayDir, "stray.txt")));
        Assert.Empty(client.DownloadedUrls);
    }

    [Fact]
    public async Task Status_And_Install_ForCustomServerPlaceholder_AreUnsupportedAndRefused()
    {
        var source = EmulatorReleaseSource.CustomServerPlaceholder("https://vendor/download");
        var service = Service(Def(source), new FakeReleaseClient());

        var status = await service.GetStatusAsync(EmulatorId);
        var unsupported = Assert.IsType<EmulatorInstallStatus.Unsupported>(status);
        Assert.Equal("https://vendor/download", unsupported.DownloadPageUrl);

        Assert.IsType<EmulatorInstallResult.Refused>(await service.InstallAsync(EmulatorId));
    }

    [Fact]
    public async Task Status_WhenNoBuildForPlatform_IsUnsupportedWithDownloadPage()
    {
        var source = EmulatorReleaseSource.GitHub(
            "test/repo",
            [new EmulatorReleaseAsset("plan9", "sparc", @"asset.*\.zip", EmulatorArchiveKind.Zip, @"tool\.exe$")],
            downloadPageUrl: "https://vendor/download");
        var service = Service(Def(source), new FakeReleaseClient { Release = ReleaseWithZip("tag-1") });

        var status = await service.GetStatusAsync(EmulatorId);

        var unsupported = Assert.IsType<EmulatorInstallStatus.Unsupported>(status);
        Assert.Equal("https://vendor/download", unsupported.DownloadPageUrl);
        Assert.IsType<EmulatorInstallResult.Failed>(await service.InstallAsync(EmulatorId));
    }

    [Fact]
    public async Task Status_WhenUserConfiguredTheirOwnExecutable_IsUserProvided()
    {
        var client = new FakeReleaseClient { Release = ReleaseWithZip("tag-9") };
        var service = Service(Def(ManagedZipSource()), client, userProbe: id => "/opt/testmu/testmu");

        var status = await service.GetStatusAsync(EmulatorId);

        var userProvided = Assert.IsType<EmulatorInstallStatus.UserProvided>(status);
        Assert.Equal("/opt/testmu/testmu", userProvided.ExecutablePath);
        Assert.Equal("tag-9", userProvided.LatestVersion);
    }

    [Fact]
    public async Task Status_WhenNotInstalledAndReachable_ReportsNotInstalledWithLatest()
    {
        var client = new FakeReleaseClient { Release = ReleaseWithZip("tag-1") };
        var service = Service(Def(ManagedZipSource()), client);

        Assert.Equal(new EmulatorInstallStatus.NotInstalled("tag-1"), await service.GetStatusAsync(EmulatorId));
    }

    [Fact]
    public async Task Status_WhenNotInstalledAndOffline_ReportsNotInstalledWithNullLatest()
    {
        var service = Service(Def(ManagedZipSource()), new FakeReleaseClient { Release = null });

        Assert.Equal(new EmulatorInstallStatus.NotInstalled(null), await service.GetStatusAsync(EmulatorId));
    }

    private sealed class FakeReleaseClient : IEmulatorReleaseClient
    {
        public GitHubEmulatorRelease? Release { get; set; }
        public string? SourceFileToCopy { get; set; }
        public List<string> DownloadedUrls { get; } = [];

        public Task<GitHubEmulatorRelease?> GetLatestReleaseAsync(string repository, CancellationToken cancellationToken) =>
            Task.FromResult(Release);

        public Task DownloadAsync(string url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            DownloadedUrls.Add(url);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(SourceFileToCopy!, destinationPath, overwrite: true);
            progress?.Report(1.0);
            return Task.CompletedTask;
        }
    }
}
