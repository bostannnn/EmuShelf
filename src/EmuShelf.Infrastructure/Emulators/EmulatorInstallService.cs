using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Emulators;
using EmuShelf.Core.Launching;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.Updates;

namespace EmuShelf.Infrastructure.Emulators;

/// <summary>
/// Installs and updates the supported emulators into the portable <c>Emulators/&lt;id&gt;/</c> folder. Because
/// EmuShelf is the installer, the installed version is read from the manifest it writes rather than probed
/// out of the binary, and only a manifest-tracked managed install is ever overwritten — a user-provided
/// executable is read-only to this service.
/// </summary>
public sealed class EmulatorInstallService : IEmulatorInstallService
{
    private readonly IReadOnlyList<EmulatorDefinition> _definitions;
    private readonly IAppPaths _paths;
    private readonly IEmulatorInstallManifestStore _manifest;
    private readonly IEmulatorReleaseClient _releaseClient;
    private readonly IAppLogger _logger;
    private readonly Func<string, string?>? _userProvidedExecutableProbe;

    public EmulatorInstallService(
        IReadOnlyList<EmulatorDefinition> definitions,
        IAppPaths paths,
        IEmulatorInstallManifestStore manifest,
        IEmulatorReleaseClient releaseClient,
        IAppLogger logger,
        Func<string, string?>? userProvidedExecutableProbe = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(releaseClient);
        ArgumentNullException.ThrowIfNull(logger);
        _definitions = definitions;
        _paths = paths;
        _manifest = manifest;
        _releaseClient = releaseClient;
        _logger = logger;
        _userProvidedExecutableProbe = userProvidedExecutableProbe;
    }

    public async Task<EmulatorInstallStatus> GetStatusAsync(
        string emulatorId,
        CancellationToken cancellationToken = default)
    {
        var definition = FindDefinition(emulatorId);
        if (definition?.ReleaseSource is not { } source)
            return new EmulatorInstallStatus.Unsupported("EmuShelf doesn't manage this emulator.", null);

        var record = _manifest.Get(emulatorId);
        var managedInstalled = record is not null && File.Exists(AbsoluteFromBaseRelative(record.ExecutableRelativePath));

        if (!source.IsManaged)
        {
            if (managedInstalled)
                return new EmulatorInstallStatus.Managed(record!.InstalledVersion);
            return new EmulatorInstallStatus.Unsupported(
                "EmuShelf can't install this emulator yet — use its download page.", source.DownloadPageUrl);
        }

        var platform = CurrentPlatform();
        var assetRule = platform is { } p ? source.SelectAsset(p.Os, p.Arch) : null;

        if (managedInstalled)
        {
            var latest = await TryGetLatestTagAsync(source, assetRule, cancellationToken).ConfigureAwait(false);
            if (latest is not null && !string.Equals(latest, record!.SourceTag, StringComparison.Ordinal))
                return new EmulatorInstallStatus.UpdateAvailable(record.InstalledVersion, latest);
            return new EmulatorInstallStatus.Managed(record!.InstalledVersion);
        }

        var userExecutable = _userProvidedExecutableProbe?.Invoke(emulatorId);
        if (!string.IsNullOrWhiteSpace(userExecutable))
        {
            var latest = await TryGetLatestTagAsync(source, assetRule, cancellationToken).ConfigureAwait(false);
            return new EmulatorInstallStatus.UserProvided(userExecutable, latest);
        }

        if (assetRule is null)
            return new EmulatorInstallStatus.Unsupported(
                "No build is published for your platform.", source.DownloadPageUrl);

        var latestTag = await TryGetLatestTagAsync(source, assetRule, cancellationToken).ConfigureAwait(false);
        return new EmulatorInstallStatus.NotInstalled(latestTag);
    }

    public Task<EmulatorInstallResult> InstallAsync(
        string emulatorId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        InstallCoreAsync(emulatorId, isUpdate: false, progress, cancellationToken);

    public Task<EmulatorInstallResult> UpdateAsync(
        string emulatorId,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default) =>
        InstallCoreAsync(emulatorId, isUpdate: true, progress, cancellationToken);

    private async Task<EmulatorInstallResult> InstallCoreAsync(
        string emulatorId,
        bool isUpdate,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var definition = FindDefinition(emulatorId);
        if (definition?.ReleaseSource is not { } source)
            return new EmulatorInstallResult.Failed("EmuShelf doesn't manage this emulator.");
        if (!source.IsManaged)
            return new EmulatorInstallResult.Refused(
                "EmuShelf can't install this emulator yet — use its download page.");

        if (CurrentPlatform() is not { } platform)
            return new EmulatorInstallResult.Failed("Unsupported operating system or architecture.");
        var assetRule = source.SelectAsset(platform.Os, platform.Arch);
        if (assetRule is null)
            return new EmulatorInstallResult.Failed($"No {definition.Name} build is published for your platform.");

        var repository = source.RepositoryFor(assetRule);
        if (string.IsNullOrWhiteSpace(repository))
            return new EmulatorInstallResult.Failed("This emulator has no download repository configured.");

        var installDirectory = Path.Combine(_paths.EmulatorsDirectory, emulatorId);
        var record = _manifest.Get(emulatorId);
        // Never clobber files in the managed folder that EmuShelf did not install.
        if (record is null && Directory.Exists(installDirectory) && HasAnyEntry(installDirectory))
            return new EmulatorInstallResult.Refused(
                "The managed folder already has files EmuShelf didn't install; not overwriting them.");

        GitHubEmulatorRelease? release;
        try
        {
            release = await _releaseClient.GetLatestReleaseAsync(repository, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not reach {repository} to install {definition.Name}.", ex);
            return new EmulatorInstallResult.Failed($"Couldn't reach {repository} to download {definition.Name}.");
        }

        if (release is null)
            return new EmulatorInstallResult.Failed($"Couldn't read the latest {definition.Name} release.");

        if (isUpdate && record is not null &&
            string.Equals(record.SourceTag, release.Tag, StringComparison.Ordinal) &&
            File.Exists(AbsoluteFromBaseRelative(record.ExecutableRelativePath)))
            return new EmulatorInstallResult.AlreadyCurrent(record.InstalledVersion);

        Regex pattern;
        try
        {
            pattern = new Regex(assetRule.AssetNamePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            _logger.Error($"The asset pattern for {definition.Name} is invalid.", ex);
            return new EmulatorInstallResult.Failed($"The download rule for {definition.Name} is misconfigured.");
        }

        var asset = release.Assets.FirstOrDefault(a => pattern.IsMatch(a.Name));
        if (asset is null)
            return new EmulatorInstallResult.Failed(
                $"The latest {definition.Name} release has no download matching your platform.");

        // Reclaim any staging/backup leftovers from a previous interrupted install of this emulator.
        CleanInstallLeftovers(emulatorId);

        var stagingRoot = Path.Combine(_paths.CacheDirectory, "emulator-installs", emulatorId);
        RecreateDirectory(stagingRoot);
        var extractDirectory = Path.Combine(
            _paths.EmulatorsDirectory, $".staging-{emulatorId}-{Guid.NewGuid():N}");
        string? backupDirectory = null;

        try
        {
            var downloadPath = Path.Combine(stagingRoot, asset.Name);
            await _releaseClient.DownloadAsync(asset.DownloadUrl, downloadPath, progress, cancellationToken)
                .ConfigureAwait(false);

            if (source.PublishesChecksums)
                await VerifyChecksumIfPublishedAsync(release, asset, downloadPath, cancellationToken)
                    .ConfigureAwait(false);

            RecreateDirectory(extractDirectory);
            EmulatorArchiveExtractor.Extract(downloadPath, assetRule.ArchiveKind, extractDirectory);

            var relativeExecutable = ResolveExecutable(extractDirectory, assetRule);
            if (relativeExecutable is null)
                return new EmulatorInstallResult.Failed(
                    $"Couldn't find {definition.Name}'s executable in the downloaded archive.");

            // Swap the freshly extracted tree into the managed directory. Move any existing managed
            // install aside first so a failed move or manifest write can be rolled back — a failed
            // update must never uninstall a working emulator, and a failed fresh install must not leave
            // unmanaged files behind that then block every future install.
            Directory.CreateDirectory(Path.GetDirectoryName(installDirectory)!);
            if (Directory.Exists(installDirectory))
            {
                backupDirectory = $"{installDirectory}.old-{Guid.NewGuid():N}";
                Directory.Move(installDirectory, backupDirectory);
            }

            try
            {
                Directory.Move(extractDirectory, installDirectory);

                var absoluteExecutable = Path.Combine(
                    installDirectory, relativeExecutable.Replace('/', Path.DirectorySeparatorChar));
                EmulatorArchiveExtractor.MarkExecutable(absoluteExecutable);
                StripQuarantine(installDirectory);

                var executableRelativeToBase = ToBaseRelative(absoluteExecutable);
                _manifest.Save(new EmulatorInstallRecord(
                    emulatorId, release.Tag, DateTimeOffset.UtcNow, executableRelativeToBase, release.Tag));
                _logger.Information($"Installed {definition.Name} {release.Tag} to {installDirectory}.");
                return new EmulatorInstallResult.Installed(release.Tag, absoluteExecutable);
            }
            catch
            {
                // Roll back: drop the half-applied new tree and restore the previous install.
                TryDeleteDirectory(installDirectory);
                if (backupDirectory is not null && Directory.Exists(backupDirectory))
                    Directory.Move(backupDirectory, installDirectory);
                backupDirectory = null;
                throw;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"Installing {definition.Name} failed.", ex);
            return new EmulatorInstallResult.Failed($"Installing {definition.Name} failed: {ex.Message}");
        }
        finally
        {
            if (Directory.Exists(extractDirectory))
                TryDeleteDirectory(extractDirectory);
            // On a successful swap the old install's backup is no longer needed; on a rollback it was
            // already consumed (set to null), so this only removes the superseded backup after success.
            if (backupDirectory is not null)
                TryDeleteDirectory(backupDirectory);
            TryDeleteDirectory(stagingRoot);
        }
    }

    private void CleanInstallLeftovers(string emulatorId)
    {
        if (!Directory.Exists(_paths.EmulatorsDirectory))
            return;
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(
                _paths.EmulatorsDirectory, $".staging-{emulatorId}-*"))
                TryDeleteDirectory(directory);
            foreach (var directory in Directory.EnumerateDirectories(
                _paths.EmulatorsDirectory, $"{emulatorId}.old-*"))
                TryDeleteDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private EmulatorDefinition? FindDefinition(string emulatorId) =>
        string.IsNullOrWhiteSpace(emulatorId)
            ? null
            : _definitions.FirstOrDefault(d => string.Equals(d.Id, emulatorId, StringComparison.Ordinal));

    private async Task<string?> TryGetLatestTagAsync(
        EmulatorReleaseSource source,
        EmulatorReleaseAsset? assetRule,
        CancellationToken cancellationToken)
    {
        var repository = assetRule is not null ? source.RepositoryFor(assetRule) : source.GitHubRepository;
        if (string.IsNullOrWhiteSpace(repository))
            return null;
        try
        {
            var release = await _releaseClient.GetLatestReleaseAsync(repository, cancellationToken).ConfigureAwait(false);
            return release?.Tag;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Could not check the latest release for {repository}.", ex);
            return null;
        }
    }

    /// <summary>
    /// The relative path (with <c>/</c> separators) of the launchable executable inside an extracted tree,
    /// preferring the shallowest match, or null when nothing matches the rule's pattern.
    /// </summary>
    internal static string? ResolveExecutable(string extractDirectory, EmulatorReleaseAsset assetRule)
    {
        Regex pattern;
        try
        {
            pattern = new Regex(assetRule.ExecutablePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return Directory
            .EnumerateFiles(extractDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(extractDirectory, path).Replace('\\', '/'))
            .Where(relative => pattern.IsMatch(relative))
            .OrderBy(relative => relative.Count(c => c == '/'))
            .ThenBy(relative => relative, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static (string Os, string Arch)? CurrentPlatform()
    {
        var os = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "macos"
            : null;
        if (os is null)
            return null;

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        return arch is null ? null : (os, arch);
    }

    private async Task VerifyChecksumIfPublishedAsync(
        GitHubEmulatorRelease release,
        GitHubEmulatorReleaseAsset asset,
        string downloadedPath,
        CancellationToken cancellationToken)
    {
        var checksumAsset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, asset.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (checksumAsset is null)
            return;

        var checksumPath = downloadedPath + ".sha256";
        await _releaseClient.DownloadAsync(checksumAsset.DownloadUrl, checksumPath, null, cancellationToken)
            .ConfigureAwait(false);
        var expected = GitHubReleaseParser.ParseChecksum(
            await File.ReadAllTextAsync(checksumPath, cancellationToken).ConfigureAwait(false));
        TryDeleteFile(checksumPath);
        if (expected is null)
            return;

        var actual = await ComputeSha256Async(downloadedPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded emulator failed its checksum check.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private void StripQuarantine(string directory)
    {
        if (!OperatingSystem.IsMacOS())
            return;
        // Downloaded emulators are unsigned and EmuShelf is unsigned, so clear the quarantine flag the
        // same way the macOS self-update applier does. Best-effort; a failure here does not fail the install.
        UpdateProcess.Run(
            "/usr/bin/xattr",
            ["-dr", "com.apple.quarantine", directory],
            throwOnError: false);
    }

    private string AbsoluteFromBaseRelative(string relative) =>
        Path.Combine(_paths.BaseDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

    private string ToBaseRelative(string absolute) =>
        Path.GetRelativePath(_paths.BaseDirectory, absolute).Replace('\\', '/');

    private static bool HasAnyEntry(string directory) =>
        Directory.EnumerateFileSystemEntries(directory).Any();

    private static void RecreateDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
