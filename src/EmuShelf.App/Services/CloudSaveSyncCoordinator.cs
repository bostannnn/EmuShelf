using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Integrations.Emulators.Pcsx2;
using EmuShelf.Integrations.Emulators.Ppsspp;

namespace EmuShelf.App.Services;

/// <summary>
/// Composes the save-sync pipeline (PCSX2 provider + filesystem endpoint + rclone transport +
/// manifest store) from portable settings and runs it under a single-flight gate so a manual sync
/// and a launch-triggered sync can never overlap. The rclone OAuth "connect" step and the Settings
/// view that drives it are added alongside that view; this coordinator owns the reusable logic that
/// stays out of code-behind.
/// </summary>
public sealed class CloudSaveSyncCoordinator
{
    private readonly IAppPaths _paths;
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly string? _rclonePath;
    private readonly Func<string?>? _defaultPcsx2Directory;
    private readonly Func<string?>? _defaultPpssppInstallationDirectory;
    private readonly Func<bool>? _isPpssppFlatpak;
    private readonly FileSaveSyncLog _syncLog;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _settings;

    public CloudSaveSyncCoordinator(
        IAppPaths paths,
        ISettingsService settingsService,
        AppSettings settings,
        IAppLogger logger,
        string? rclonePath = null,
        Func<string?>? defaultPcsx2Directory = null,
        Func<string?>? defaultPpssppInstallationDirectory = null,
        Func<bool>? isPpssppFlatpak = null)
    {
        _paths = paths;
        _settingsService = settingsService;
        _settings = settings;
        _logger = logger;
        _rclonePath = rclonePath;
        _defaultPcsx2Directory = defaultPcsx2Directory;
        _defaultPpssppInstallationDirectory = defaultPpssppInstallationDirectory;
        _isPpssppFlatpak = isPpssppFlatpak;
        _syncLog = new FileSaveSyncLog(paths);
    }

    /// <summary>The PCSX2 config directory derived from the configured emulator, if any.</summary>
    public string? DefaultPcsx2Directory => _defaultPcsx2Directory?.Invoke();

    /// <summary>The PPSSPP installation directory derived from the configured emulator, if any.</summary>
    public string? DefaultPpssppInstallationDirectory => _defaultPpssppInstallationDirectory?.Invoke();

    // The saved directory wins; otherwise fall back to the one derived from the configured
    // emulator, so a user who already set up PCSX2 in Settings need not select it again.
    private string? EffectivePcsx2Directory
    {
        get
        {
            var saved = _settings.CloudSaveSync.Pcsx2ConfigDirectory;
            return !string.IsNullOrWhiteSpace(saved)
                ? ResolvePortablePath(saved)
                : ResolvePortablePath(_defaultPcsx2Directory?.Invoke());
        }
    }

    /// <summary>The current cloud-sync settings snapshot.</summary>
    public CloudSaveSyncSettings Current => _settings.CloudSaveSync;

    /// <summary>Whether a remote is connected and sync is enabled.</summary>
    public bool IsConfigured =>
        _settings.CloudSaveSync is { Enabled: true, RemoteName.Length: > 0, CloudFolder.Length: > 0 };

    /// <summary>Whether the rclone executable EmuShelf will invoke actually exists.</summary>
    public bool IsRcloneAvailable => File.Exists(RcloneExecutable.Resolve(_paths, _rclonePath));

    /// <summary>The path EmuShelf will invoke for rclone — where it is, or where to place it.</summary>
    public string RcloneExpectedPath => RcloneExecutable.Resolve(_paths, _rclonePath);

    /// <summary>Where the human-readable sync activity log is written.</summary>
    public string SyncLogPath => _syncLog.LogPath;

    /// <summary>The memory-card directory PCSX2 is configured to use, or null if no config directory is set.</summary>
    public async Task<string?> GetDetectedMemoryCardsDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var configurationDirectory = EffectivePcsx2Directory;
        if (string.IsNullOrWhiteSpace(configurationDirectory))
            return null;

        return await new Pcsx2SaveLocationProvider(configurationDirectory)
            .GetMemoryCardsDirectoryAsync(cancellationToken);
    }

    /// <summary>The PPSSPP SAVEDATA directory selected or derived for this machine.</summary>
    public async Task<string?> GetDetectedPpssppSaveDataDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var provider = CreatePpssppProvider();
        return provider is null ? null : await provider.GetSaveDataDirectoryAsync(cancellationToken);
    }

    /// <summary>Persists an updated cloud-sync configuration to the portable settings file.</summary>
    public void UpdateConfiguration(CloudSaveSyncSettings configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Persist(configuration);
    }

    /// <summary>Persists a new PCSX2 configuration directory without changing the connection state.</summary>
    public void UpdatePcsx2Directory(string? directory) =>
        Persist(_settings.CloudSaveSync with
        {
            Pcsx2ConfigDirectory = string.IsNullOrWhiteSpace(directory) ? null : directory.Trim(),
        });

    /// <summary>Persists PPSSPP's optional Memory Stick override.</summary>
    public void UpdatePpssppDirectory(string? memoryStickDirectory) =>
        Persist(_settings.CloudSaveSync with
        {
            PpssppMemoryStickDirectory = string.IsNullOrWhiteSpace(memoryStickDirectory)
                ? null
                : memoryStickDirectory.Trim(),
        });

    /// <summary>
    /// Runs rclone's Google Drive OAuth (opening the browser), ensures the cloud folder exists, and
    /// persists the connection. Only the non-secret remote name and folder are stored — the OAuth
    /// token stays in rclone's own config, never in EmuShelf's settings.
    /// </summary>
    public async Task<CloudSaveSyncConnectResult> ConnectGoogleDriveAsync(
        string remoteName,
        string cloudFolder,
        string pcsx2ConfigurationDirectory,
        string ppssppMemoryStickDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteName) ||
            string.IsNullOrWhiteSpace(cloudFolder) ||
            (string.IsNullOrWhiteSpace(pcsx2ConfigurationDirectory) &&
                !HasPpssppConfiguration(ppssppMemoryStickDirectory)))
        {
            return CloudSaveSyncConnectResult.InvalidInput;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var configurator = new RcloneConfigurator(_paths, _rclonePath);
            if (!configurator.IsRcloneAvailable)
                return CloudSaveSyncConnectResult.RcloneMissing;

            var trimmedRemote = remoteName.Trim();
            var trimmedFolder = cloudFolder.Trim();
            await configurator.CreateGoogleDriveRemoteAsync(trimmedRemote, cancellationToken);
            await configurator.EnsureFolderAsync(trimmedRemote, trimmedFolder, cancellationToken);

            Persist(_settings.CloudSaveSync with
            {
                Enabled = true,
                RemoteName = trimmedRemote,
                CloudFolder = trimmedFolder,
                Pcsx2ConfigDirectory = string.IsNullOrWhiteSpace(pcsx2ConfigurationDirectory)
                    ? null
                    : pcsx2ConfigurationDirectory.Trim(),
                PpssppMemoryStickDirectory = string.IsNullOrWhiteSpace(ppssppMemoryStickDirectory)
                    ? null
                    : ppssppMemoryStickDirectory.Trim(),
            });
            return CloudSaveSyncConnectResult.Connected;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            _logger.Error("Cloud save connect failed.", ex);
            return CloudSaveSyncConnectResult.Failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Turns sync off and forgets the remote. rclone's config and the cloud data are left intact.</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Persist(_settings.CloudSaveSync with
            {
                Enabled = false,
                RemoteName = null,
                CloudFolder = null,
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Downloads the official rclone build for this OS and installs it beside EmuShelf.</summary>
    public async Task<bool> DownloadRcloneAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await new RcloneInstaller().InstallAsync(_paths, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or HttpRequestException or PlatformNotSupportedException or InvalidOperationException)
        {
            _logger.Error("Automatic rclone download failed.", ex);
            return false;
        }
    }

    /// <summary>Bundles the coordinator's operations as a delegate context for the Settings view model.</summary>
    public CloudSaveSyncSettingsContext CreateSettingsContext() => new(
        _settings.CloudSaveSync,
        IsRcloneAvailable,
        RcloneExpectedPath,
        DefaultPcsx2Directory,
        DefaultPpssppInstallationDirectory,
        SyncLogPath,
        GetDetectedMemoryCardsDirectoryAsync,
        GetDetectedPpssppSaveDataDirectoryAsync,
        ConnectGoogleDriveAsync,
        DisconnectAsync,
        SyncNowAsync,
        ForceAsync,
        UpdatePcsx2Directory,
        UpdatePpssppDirectory,
        DownloadRcloneAsync);

    private void Persist(CloudSaveSyncSettings configuration)
    {
        _settings = _settings with { CloudSaveSync = configuration };
        _settingsService.Save(_settings);
    }

    /// <summary>Reconciles every enabled provider's local and cloud saves using one shared last-synced baseline.</summary>
    public Task<CloudSaveSyncOutcome> SyncNowAsync(
        IProgress<SaveSyncProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunSyncPipelineAsync(progress, cancellationToken);

    /// <summary>Forces one platform's present units in one direction, still backing up the loser.</summary>
    public Task<CloudSaveSyncOutcome> ForceAsync(
        string systemId,
        SaveSyncDirection direction,
        IProgress<SaveSyncProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunForcePipelineAsync(systemId, direction, progress, cancellationToken);

    private async Task<CloudSaveSyncOutcome> RunForcePipelineAsync(
        string systemId,
        SaveSyncDirection direction,
        IProgress<SaveSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var target = CreateTarget(systemId);
        if (!IsConfigured || target is null)
            return CloudSaveSyncOutcome.NotConfigured();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var transport = new RcloneCloudSyncTransport(
                _paths,
                _settings.CloudSaveSync.RemoteName!,
                _settings.CloudSaveSync.CloudFolder!,
                _rclonePath);
            var service = new SaveSyncService(
                target.LocalEndpoint,
                transport,
                new JsonSaveSyncManifestStore(_paths));

            var report = await service.ForceAsync(target.Provider, direction, progress, cancellationToken);
            var operationLabel = direction == SaveSyncDirection.Upload
                ? $"Upload {systemId} → cloud"
                : $"Download {systemId} → local";
            await WriteSyncLogAsync(operationLabel, report, cancellationToken);
            return CloudSaveSyncOutcome.Completed(report);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidOperationException or ArgumentException or
                Pcsx2ConfigurationFormatException or PpssppConfigurationFormatException)
        {
            _logger.Error("Cloud save sync failed.", ex);
            return CloudSaveSyncOutcome.Failed(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CloudSaveSyncOutcome> RunSyncPipelineAsync(
        IProgress<SaveSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            return CloudSaveSyncOutcome.NotConfigured();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var targets = new List<SaveSyncTarget>();
            if (CreateTarget("playstation2") is { } pcsx2)
                targets.Add(pcsx2);
            if (CreateTarget("psp") is { } ppsspp)
                targets.Add(ppsspp);

            if (targets.Count == 0)
                return CloudSaveSyncOutcome.NotConfigured();

            var transport = new RcloneCloudSyncTransport(
                _paths,
                _settings.CloudSaveSync.RemoteName!,
                _settings.CloudSaveSync.CloudFolder!,
                _rclonePath);
            var service = new SaveSyncService(
                targets[0].LocalEndpoint,
                transport,
                new JsonSaveSyncManifestStore(_paths));
            var report = await service.SyncAllAsync(targets, progress, cancellationToken);
            await WriteSyncLogAsync("Sync", report, cancellationToken);
            return CloudSaveSyncOutcome.Completed(report);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidOperationException or ArgumentException or
                Pcsx2ConfigurationFormatException or PpssppConfigurationFormatException)
        {
            _logger.Error("Cloud save sync failed.", ex);
            return CloudSaveSyncOutcome.Failed(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private PpssppSaveLocationProvider? CreatePpssppProvider()
    {
        var installationDirectory = ResolvePortablePath(_defaultPpssppInstallationDirectory?.Invoke());
        var memoryStickOverride = ResolvePortablePath(_settings.CloudSaveSync.PpssppMemoryStickDirectory);
        var isFlatpak = _isPpssppFlatpak?.Invoke() == true;
        if (string.IsNullOrWhiteSpace(installationDirectory) && string.IsNullOrWhiteSpace(memoryStickOverride) && !isFlatpak)
            return null;

        return new PpssppSaveLocationProvider(
            installationDirectory ?? _paths.BaseDirectory,
            memoryStickDirectoryOverride: memoryStickOverride,
            isFlatpak: isFlatpak);
    }

    private SaveSyncTarget? CreateTarget(string systemId)
    {
        ISaveLocationProvider? provider = systemId switch
        {
            "playstation2" when EffectivePcsx2Directory is { } directory =>
                new Pcsx2SaveLocationProvider(directory),
            "psp" => CreatePpssppProvider(),
            _ => null,
        };
        return provider is null
            ? null
            : new SaveSyncTarget(provider, new FileSystemLocalSaveEndpoint(provider, _paths));
    }

    private bool HasPpssppConfiguration(string? memoryStickDirectory) =>
        !string.IsNullOrWhiteSpace(memoryStickDirectory) ||
        !string.IsNullOrWhiteSpace(_defaultPpssppInstallationDirectory?.Invoke()) ||
        _isPpssppFlatpak?.Invoke() == true;

    private string? ResolvePortablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var trimmed = path.Trim();
        return Path.IsPathFullyQualified(trimmed)
            ? trimmed
            : Path.GetFullPath(Path.Combine(_paths.BaseDirectory, trimmed));
    }

    // The activity log is a convenience for the user; a failure to write it must not fail the sync.
    private async Task WriteSyncLogAsync(string operation, SaveSyncReport report, CancellationToken cancellationToken)
    {
        try
        {
            await _syncLog.AppendAsync(operation, report, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("Could not write the cloud sync activity log.", ex);
        }
    }
}

/// <summary>The result of a cloud save-sync attempt.</summary>
/// <param name="Status">Whether the attempt ran, was skipped as unconfigured, or failed.</param>
/// <param name="Report">The per-unit report when the attempt completed; otherwise null.</param>
/// <param name="Message">A user-facing failure message when the attempt failed; otherwise null.</param>
public sealed record CloudSaveSyncOutcome(CloudSaveSyncStatus Status, SaveSyncReport? Report, string? Message)
{
    public static CloudSaveSyncOutcome NotConfigured() => new(CloudSaveSyncStatus.NotConfigured, null, null);

    public static CloudSaveSyncOutcome Completed(SaveSyncReport report) => new(CloudSaveSyncStatus.Completed, report, null);

    public static CloudSaveSyncOutcome Failed(string message) => new(CloudSaveSyncStatus.Failed, null, message);
}

/// <summary>The outcome category of a cloud save-sync attempt.</summary>
public enum CloudSaveSyncStatus
{
    /// <summary>No remote is connected or sync is disabled; nothing was attempted.</summary>
    NotConfigured,

    /// <summary>The sync ran to completion.</summary>
    Completed,

    /// <summary>The sync was attempted but failed; local saves are untouched.</summary>
    Failed,
}

/// <summary>The outcome of a cloud save-sync connect attempt.</summary>
public enum CloudSaveSyncConnectResult
{
    /// <summary>The remote was created (or reused) and the connection was saved.</summary>
    Connected,

    /// <summary>Required input (remote name, folder, or PCSX2 directory) was missing.</summary>
    InvalidInput,

    /// <summary>The portable rclone executable is not present beside EmuShelf.</summary>
    RcloneMissing,

    /// <summary>rclone reported a failure (e.g. the OAuth flow was declined or the remote is unreachable).</summary>
    Failed,
}

/// <summary>
/// The cloud save-sync operations the Settings view model drives, wrapped as delegates so the view
/// model stays testable with a fake context.
/// </summary>
public sealed record CloudSaveSyncSettingsContext(
    CloudSaveSyncSettings Current,
    bool IsRcloneAvailable,
    string RcloneExpectedPath,
    string? DefaultPcsx2Directory,
    string? DefaultPpssppInstallationDirectory,
    string SyncLogPath,
    Func<CancellationToken, Task<string?>> GetDetectedMemoryCardsDirectoryAsync,
    Func<CancellationToken, Task<string?>> GetDetectedPpssppSaveDataDirectoryAsync,
    Func<string, string, string, string, CancellationToken, Task<CloudSaveSyncConnectResult>> ConnectGoogleDriveAsync,
    Func<CancellationToken, Task> DisconnectAsync,
    Func<IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>> SyncNowAsync,
    Func<string, SaveSyncDirection, IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>> ForceAsync,
    Action<string?> UpdatePcsx2Directory,
    Action<string?> UpdatePpssppDirectory,
    Func<CancellationToken, Task<bool>> DownloadRcloneAsync);
