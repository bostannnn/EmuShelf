using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Integrations.Emulators.Pcsx2;

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
    private readonly FileSaveSyncLog _syncLog;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _settings;

    public CloudSaveSyncCoordinator(
        IAppPaths paths,
        ISettingsService settingsService,
        AppSettings settings,
        IAppLogger logger,
        string? rclonePath = null,
        Func<string?>? defaultPcsx2Directory = null)
    {
        _paths = paths;
        _settingsService = settingsService;
        _settings = settings;
        _logger = logger;
        _rclonePath = rclonePath;
        _defaultPcsx2Directory = defaultPcsx2Directory;
        _syncLog = new FileSaveSyncLog(paths);
    }

    /// <summary>The PCSX2 config directory derived from the configured emulator, if any.</summary>
    public string? DefaultPcsx2Directory => _defaultPcsx2Directory?.Invoke();

    // The saved directory wins; otherwise fall back to the one derived from the configured
    // emulator, so a user who already set up PCSX2 in Settings need not select it again.
    private string? EffectivePcsx2Directory
    {
        get
        {
            var saved = _settings.CloudSaveSync.Pcsx2ConfigDirectory;
            return !string.IsNullOrWhiteSpace(saved) ? saved : _defaultPcsx2Directory?.Invoke();
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

    /// <summary>
    /// Runs rclone's Google Drive OAuth (opening the browser), ensures the cloud folder exists, and
    /// persists the connection. Only the non-secret remote name and folder are stored — the OAuth
    /// token stays in rclone's own config, never in EmuShelf's settings.
    /// </summary>
    public async Task<CloudSaveSyncConnectResult> ConnectGoogleDriveAsync(
        string remoteName,
        string cloudFolder,
        string pcsx2ConfigurationDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remoteName) ||
            string.IsNullOrWhiteSpace(cloudFolder) ||
            string.IsNullOrWhiteSpace(pcsx2ConfigurationDirectory))
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

            Persist(new CloudSaveSyncSettings
            {
                Enabled = true,
                RemoteName = trimmedRemote,
                CloudFolder = trimmedFolder,
                Pcsx2ConfigDirectory = pcsx2ConfigurationDirectory.Trim(),
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
        SyncLogPath,
        GetDetectedMemoryCardsDirectoryAsync,
        ConnectGoogleDriveAsync,
        DisconnectAsync,
        SyncNowAsync,
        ForceAsync,
        UpdatePcsx2Directory,
        DownloadRcloneAsync);

    private void Persist(CloudSaveSyncSettings configuration)
    {
        _settings = _settings with { CloudSaveSync = configuration };
        _settingsService.Save(_settings);
    }

    /// <summary>Reconciles local and cloud saves for every PCSX2 unit using the last-synced baseline.</summary>
    public Task<CloudSaveSyncOutcome> SyncNowAsync(
        IProgress<SaveSyncProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunPipelineAsync((service, provider, token) => service.SyncAsync(provider, progress, token), "Sync", cancellationToken);

    /// <summary>Forces every present unit in one direction (the manual overwrite), still backing up the loser.</summary>
    public Task<CloudSaveSyncOutcome> ForceAsync(
        SaveSyncDirection direction,
        IProgress<SaveSyncProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunPipelineAsync(
            (service, provider, token) => service.ForceAsync(provider, direction, progress, token),
            direction == SaveSyncDirection.Upload ? "Upload → cloud" : "Download → local",
            cancellationToken);

    private async Task<CloudSaveSyncOutcome> RunPipelineAsync(
        Func<SaveSyncService, ISaveLocationProvider, CancellationToken, Task<SaveSyncReport>> operation,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        var configurationDirectory = EffectivePcsx2Directory;
        if (!IsConfigured || string.IsNullOrWhiteSpace(configurationDirectory))
            return CloudSaveSyncOutcome.NotConfigured();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var provider = new Pcsx2SaveLocationProvider(configurationDirectory);
            var memoryCardsDirectory = await provider.GetMemoryCardsDirectoryAsync(cancellationToken);
            var endpoint = new FileSystemLocalSaveEndpoint(memoryCardsDirectory, _paths);
            var transport = new RcloneCloudSyncTransport(
                _paths,
                _settings.CloudSaveSync.RemoteName!,
                _settings.CloudSaveSync.CloudFolder!,
                _rclonePath);
            var service = new SaveSyncService(endpoint, transport, new JsonSaveSyncManifestStore(_paths));

            var report = await operation(service, provider, cancellationToken);
            await WriteSyncLogAsync(operationLabel, report, cancellationToken);
            return CloudSaveSyncOutcome.Completed(report);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidOperationException or ArgumentException or Pcsx2ConfigurationFormatException)
        {
            _logger.Error("Cloud save sync failed.", ex);
            return CloudSaveSyncOutcome.Failed(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
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
    string SyncLogPath,
    Func<CancellationToken, Task<string?>> GetDetectedMemoryCardsDirectoryAsync,
    Func<string, string, string, CancellationToken, Task<CloudSaveSyncConnectResult>> ConnectGoogleDriveAsync,
    Func<CancellationToken, Task> DisconnectAsync,
    Func<IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>> SyncNowAsync,
    Func<SaveSyncDirection, IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>> ForceAsync,
    Action<string?> UpdatePcsx2Directory,
    Func<CancellationToken, Task<bool>> DownloadRcloneAsync);
