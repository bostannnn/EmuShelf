using System.Diagnostics;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.SaveSync;

namespace EmuShelf.App.Services;

/// <summary>Where a system's emulator lives on this machine, as resolved from EmuShelf's own config.</summary>
/// <param name="Directory">The emulator-derived directory, or null when nothing is configured.</param>
/// <param name="IsFlatpak">Whether the configured installation is a Flatpak target.</param>
/// <param name="CorePath">The configured libretro core, when applicable.</param>
/// <param name="LaunchArguments">The configured launch template, used to honor emulator data-directory switches.</param>
public sealed record SaveEmulatorInstallation(
    string? Directory,
    bool IsFlatpak,
    string? CorePath = null,
    string? LaunchArguments = null,
    string? ExecutablePath = null,
    string? FlatpakApplicationId = null,
    string? EmulatorId = null);

/// <summary>
/// Composes the save-sync pipeline (registry-provided provider + filesystem endpoint + rclone
/// transport + manifest store) from portable settings and runs it under a single-flight gate so a
/// manual sync and a launch-triggered sync can never overlap.
///
/// Platform knowledge lives in <see cref="SaveProviderRegistry"/>, not here: this type never names
/// an emulator, so adding one does not touch it.
/// </summary>
public sealed class CloudSaveSyncCoordinator : IGameSaveSyncService
{
    private readonly IAppPaths _paths;
    private readonly ISettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly string? _rclonePath;
    private readonly Func<string, SaveEmulatorInstallation?>? _emulatorInstallations;
    private readonly Func<string, IReadOnlyList<Game>>? _gamesForSystem;
    private readonly FileSaveSyncLog _syncLog;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _settings;

    public CloudSaveSyncCoordinator(
        IAppPaths paths,
        ISettingsService settingsService,
        AppSettings settings,
        IAppLogger logger,
        string? rclonePath = null,
        Func<string, SaveEmulatorInstallation?>? emulatorInstallations = null,
        Func<string, IReadOnlyList<Game>>? gamesForSystem = null)
    {
        _gamesForSystem = gamesForSystem;
        _paths = paths;
        _settingsService = settingsService;
        // Fold the legacy per-emulator fields into the per-system dictionary once, up front, so
        // every read below sees one shape regardless of how old the settings file is.
        _settings = settings with { CloudSaveSync = settings.CloudSaveSync.NormalizeSaveLocations() };
        _logger = logger;
        _rclonePath = rclonePath;
        _emulatorInstallations = emulatorInstallations;
        _syncLog = new FileSaveSyncLog(paths);
    }

    /// <summary>The current cloud-sync settings snapshot.</summary>
    public CloudSaveSyncSettings Current => _settings.CloudSaveSync;

    /// <summary>Whether a remote is connected and sync is enabled.</summary>
    public bool IsConfigured =>
        _settings.CloudSaveSync is { Enabled: true, RemoteName.Length: > 0, CloudFolder.Length: > 0 };

    /// <summary>
    /// Whether one system participates in sync. This asks the registry to build the provider, the
    /// same call the sync pipeline makes, so a "yes" here cannot become a silent no-op there.
    /// </summary>
    public bool CanSyncSystem(string systemId) => IsConfigured && CreateBaseProvider(systemId) is not null;

    /// <summary>Whether the rclone executable EmuShelf will invoke actually exists.</summary>
    public bool IsRcloneAvailable => File.Exists(RcloneExecutable.Resolve(_paths, _rclonePath));

    /// <summary>The path EmuShelf will invoke for rclone — where it is, or where to place it.</summary>
    public string RcloneExpectedPath => RcloneExecutable.Resolve(_paths, _rclonePath);

    /// <summary>Where the human-readable sync activity log is written.</summary>
    public string SyncLogPath => _syncLog.LogPath;

    /// <summary>The concrete save directory one system will use, or null when it cannot be resolved.</summary>
    public async Task<string?> GetDetectedPathAsync(string systemId, CancellationToken cancellationToken = default)
        => (await GetDetectionAsync(systemId, cancellationToken))?.Directory;

    /// <summary>The concrete save directory and any non-blocking compatibility warning.</summary>
    public async Task<SaveProviderDetection?> GetDetectionAsync(
        string systemId,
        CancellationToken cancellationToken = default)
    {
        var descriptor = SaveProviderRegistry.Find(systemId);
        if (descriptor is null)
            return null;

        // Everything below reads the emulator's own configuration, its version resources and binary
        // architecture, and the save/state folders — often on a slow external drive. The Saves
        // section resolves every platform at once when it opens, so this must stay off the UI thread;
        // running it inline froze the window for a few seconds each time. Provider construction reads
        // the RetroArch core info file, so it is off-thread too.
        var provider = await Task.Run(() => CreateBaseProvider(systemId), cancellationToken);
        if (provider is null)
            return null;

        var detection = await descriptor.DetectAsync(provider, cancellationToken);
        var optionalSummary = (Summary: (string?)null, Locations: (IReadOnlyList<OptionalContentDetection>)[]);
        try
        {
            var optional = await Task.Run(
                () => SaveProviderRegistry.WithOptionalContent(
                    descriptor,
                    provider,
                    CreateProviderContext(systemId, _settings.CloudSaveSync),
                    includeSaveStates: true),
                cancellationToken);
            optionalSummary = await DescribeOptionalContentAsync(optional, provider, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            // Optional discovery is diagnostic only. It must not turn a valid memory-card/save
            // location into a disabled platform row.
            optionalSummary = (
                "Save states could not be inspected.",
                [new OptionalContentDetection("Save states", null, 0, 0, 0, Warning: ex.Message)]);
        }
        return detection with
        {
            Warning = WithMissingDirectoryNotice(detection),
            OptionalContentSummary = optionalSummary.Summary,
            OptionalContent = optionalSummary.Locations,
        };
    }

    // A resolved folder that does not exist is the quietest possible failure: the platform reports a
    // path, syncs zero units, and reports success — which reads as "my saves did not sync" with
    // nothing to go on. Say it in the row instead. An existing but empty folder is normal (the
    // emulator has simply not written a save yet) and is not flagged.
    private static string? WithMissingDirectoryNotice(SaveProviderDetection detection)
    {
        if (string.IsNullOrWhiteSpace(detection.Directory) || DirectoryExists(detection.Directory))
            return detection.Warning;

        const string notice =
            "This folder does not exist on this machine, so nothing is being synced from it. " +
            "Check that the emulator is the one you actually play with, or set the save location here.";
        return string.IsNullOrWhiteSpace(detection.Warning) ? notice : detection.Warning + " " + notice;
    }

    private static bool DirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Persists an updated cloud-sync configuration to the portable settings file.</summary>
    public void UpdateConfiguration(CloudSaveSyncSettings configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Persist(configuration);
    }

    /// <summary>Persists one system's save-location override without changing the connection state.</summary>
    public void UpdateOverride(string systemId, string? directory) =>
        Persist(_settings.CloudSaveSync.WithOverride(systemId, directory));

    /// <summary>Persists all edited platform overrides in one settings transaction.</summary>
    public void UpdateOverrides(IReadOnlyDictionary<string, string?> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var configuration = _settings.CloudSaveSync;
        foreach (var (systemId, directory) in overrides)
            configuration = configuration.WithOverride(systemId, directory);
        Persist(configuration);
    }

    /// <summary>Persists one platform's opt-in save-state choice.</summary>
    public void UpdateOptionalContent(string systemId, bool syncSaveStates) =>
        Persist(_settings.CloudSaveSync.WithOptionalContent(systemId, syncSaveStates));

    /// <summary>Persists one system's save-state folder override without changing the connection state.</summary>
    public void UpdateStateOverride(string systemId, string? directory) =>
        Persist(_settings.CloudSaveSync.WithStateOverride(systemId, directory));

    /// <summary>
    /// Runs rclone's Google Drive OAuth (opening the browser), ensures the cloud folder exists, and
    /// persists the connection. Only the non-secret remote name and folder are stored — the OAuth
    /// token stays in rclone's own config, never in EmuShelf's settings.
    /// </summary>
    /// <param name="overrides">
    /// Save-location overrides by system id. Keyed rather than positional so adding a platform
    /// cannot silently shift one emulator's path onto another.
    /// </param>
    /// <param name="clientId">An optional Google OAuth client id; null uses rclone's shared client.</param>
    /// <param name="clientSecret">
    /// The matching secret. It is passed to rclone and never stored by EmuShelf.
    /// </param>
    public async Task<CloudSaveSyncConnectResult> ConnectGoogleDriveAsync(
        string remoteName,
        string cloudFolder,
        IReadOnlyDictionary<string, string?> overrides,
        CancellationToken cancellationToken = default,
        string? clientId = null,
        string? clientSecret = null)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        if (string.IsNullOrWhiteSpace(remoteName) || string.IsNullOrWhiteSpace(cloudFolder))
            return CloudSaveSyncConnectResult.InvalidInput;

        // At least one platform must be able to produce a provider once the overrides are applied,
        // otherwise connecting would leave the user with a remote and nothing to sync into it.
        var candidate = _settings.CloudSaveSync;
        foreach (var (systemId, directory) in overrides)
            candidate = candidate.WithOverride(systemId, directory);
        if (!SaveProviderRegistry.SystemIds.Any(systemId => CreateBaseProvider(systemId, candidate) is not null))
            return CloudSaveSyncConnectResult.InvalidInput;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var configurator = new RcloneConfigurator(_paths, _rclonePath);
            if (!configurator.IsRcloneAvailable)
                return CloudSaveSyncConnectResult.RcloneMissing;

            var trimmedRemote = remoteName.Trim();
            var trimmedFolder = cloudFolder.Trim();
            await configurator.CreateGoogleDriveRemoteAsync(
                trimmedRemote,
                cancellationToken,
                clientId,
                clientSecret);
            await configurator.EnsureFolderAsync(trimmedRemote, trimmedFolder, cancellationToken);

            Persist(candidate with
            {
                Enabled = true,
                RemoteName = trimmedRemote,
                CloudFolder = trimmedFolder,
                // The id is recorded so Settings can show which client the remote uses and prefill
                // it next time; the secret is not, and only rclone's config holds it.
                GoogleClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId.Trim(),
                // A different folder has a different id; carrying the old one over would address
                // the previous connection's folder.
                CloudFolderId = null,
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
        SyncLogPath,
        // A delegate, not a snapshot: Settings re-reads it after each operation so a row's
        // "last synced" / "last attempt failed" line cannot go stale within an open session.
        DescribePlatforms,
        GetDetectedPathAsync,
        ConnectGoogleDriveAsync,
        DisconnectAsync,
        SyncNowAsync,
        ForceAsync,
        UpdateOverride,
        DownloadRcloneAsync,
        GetDetectionAsync,
        UpdateOptionalContent,
        UpdateOverrides,
        UpdateStateOverride);

    /// <summary>Reconciles every participating platform against the cloud in one pass.</summary>
    public Task<CloudSaveSyncOutcome> SyncNowAsync(
        IProgress<SaveSyncProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunSyncPipelineAsync(
            SaveProviderRegistry.SystemIds,
            progress,
            "Sync all",
            cancellationToken,
            // A full manual sync is where the cloud is checked against itself. It is one extra
            // listing on an operation the user is already waiting on, and it is the only way the
            // machine that owns a save learns that its upload never arrived — it never downloads
            // its own save, so a broken entry would otherwise stay broken until another machine
            // tripped over it.
            verifyRemote: true,
            contentScope: SyncContentScope.All);

    /// <summary>Reconciles only the save provider associated with one launched system.</summary>
    /// <remarks>
    /// Declines rather than queues when another pass holds the gate. A launch-triggered sync is
    /// work the user did not ask for, and a manual sync can legitimately run for minutes. Ordinary
    /// saves commit before the optional state phase, so a state failure cannot strand a reconciled
    /// memory-card save in staging. The launch lifecycle itself supplies cancellation; there is no
    /// shorter application-level pre-launch budget.
    ///
    /// <paramref name="launchStateKeys"/> scopes the state phase to the launched game (its file
    /// stem, serials, and disc ids) so launching one game no longer hashes and syncs every game's
    /// states in a shared folder. Regular saves are never scoped, and a manual Sync all passes no
    /// keys so it still covers every state.
    /// </remarks>
    public async Task<CloudSaveSyncOutcome> SyncSystemAsync(
        string systemId,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<string>? launchStateKeys = null)
    {
        if (!IsConfigured)
            return CloudSaveSyncOutcome.NotConfigured();
        if (!await _gate.WaitAsync(0, cancellationToken))
            return CloudSaveSyncOutcome.AlreadyRunning();

        try
        {
            var saves = await RunSyncPipelineAsync(
                [systemId],
                progress: null,
                $"Automatic saves ({systemId})",
                cancellationToken,
                gateAlreadyHeld: true,
                contentScope: SyncContentScope.SavesOnly,
                recordOutcome: false);
            if (saves.Status != CloudSaveSyncStatus.Completed ||
                !_settings.CloudSaveSync.GetLocation(systemId).SyncSaveStates)
            {
                if (saves.Status == CloudSaveSyncStatus.Completed &&
                    !_settings.CloudSaveSync.GetLocation(systemId).SyncSaveStates)
                {
                    _logger.Information(
                        $"Save-state sync skipped for '{systemId}': the platform's " +
                        "'Automatically sync save states' toggle is off on this machine.");
                }
                RecordAutomaticOutcome(systemId, saves);
                return saves;
            }

            await LogStatePhaseDiagnosticsAsync(systemId, launchStateKeys);
            var states = await RunSyncPipelineAsync(
                [systemId],
                progress: null,
                $"Automatic save states ({systemId})",
                cancellationToken,
                gateAlreadyHeld: true,
                contentScope: SyncContentScope.SaveStatesOnly,
                recordOutcome: false,
                stateGameKeys: launchStateKeys);
            LogStatePhaseResult(systemId, states);
            if (states.Status != CloudSaveSyncStatus.Completed)
            {
                RecordAutomaticOutcome(
                    systemId,
                    states.Status == CloudSaveSyncStatus.Failed ? states : saves);
                return states;
            }

            var combined = CloudSaveSyncOutcome.Completed(new SaveSyncReport(
                saves.Report!.Results.Concat(states.Report!.Results).ToArray()));
            RecordAutomaticOutcome(systemId, combined);
            return combined;
        }
        finally
        {
            _gate.Release();
        }
    }

    // Names the failing gate when launch-time save-state sync appears to do nothing. Writes to the
    // portable app log (Logs/EmuShelf-*.log). Best-effort: diagnostics must never fail a sync.
    private async Task LogStatePhaseDiagnosticsAsync(string systemId, IReadOnlyCollection<string>? keys)
    {
        try
        {
            var keyText = keys is { Count: > 0 }
                ? string.Join(", ", keys)
                : "(none — this pass reconciles every state for the platform)";
            var provider = CreateProvider(systemId, SyncContentScope.SaveStatesOnly, keys);
            var auxiliary = provider as AuxiliarySyncProvider;
            var compatible = auxiliary?.HasStateCompatibility;
            // The exact key, logged on both machines: two machines must print the SAME key for a state
            // to restore. A different key here is the direct signature of the cross-machine mismatch
            // that leaves states uploaded-but-not-restored.
            var compatibilityKey = auxiliary?.StateCompatibilityKey ?? "(none)";
            var localCount = provider is null ? 0 : (await provider.GetSaveUnitsAsync()).Count;
            _logger.Information(
                $"Save-state sync for '{systemId}': keys=[{keyText}]; " +
                $"compatibilityDetected={compatible?.ToString() ?? "n/a"}; compatibilityKey={compatibilityKey}; " +
                $"localStatesMatched={localCount}. " +
                (compatible == false
                    ? "compatibilityDetected=false means the emulator/core version or CPU architecture " +
                      "could not be read, so states are neither uploaded nor restored — check the emulator " +
                      "and core are configured. "
                    : string.Empty) +
                (localCount == 0 && compatible != false
                    ? "localStatesMatched=0 while the folder has states means the launched game's name/serial " +
                      "did not match the state file names (or the state folder is wrong). "
                    : string.Empty));
        }
        catch (Exception ex) when (
            ex is IOException or InvalidOperationException or ArgumentException or SaveProviderConfigurationException)
        {
            _logger.Information($"Save-state sync diagnostics for '{systemId}' were unavailable: {ex.Message}");
        }
    }

    private void LogStatePhaseResult(string systemId, CloudSaveSyncOutcome outcome)
    {
        if (outcome.Report is not { } report)
        {
            _logger.Information($"Save-state sync for '{systemId}' did not complete: {outcome.Status}.");
            return;
        }

        var skipped = report.Skipped;
        // Enumerate every distinct skip reason (capped), not just the first: on a launch pass several
        // states can skip for different reasons (a version mismatch and a name-scope miss at once), and
        // seeing only the first hides the others.
        var skippedDetail = string.Empty;
        if (skipped.Count > 0)
        {
            var reasons = skipped
                .GroupBy(result => result.Reason, StringComparer.Ordinal)
                .Select(group => $"{group.Count()}× {group.Key}")
                .Take(5);
            skippedDetail = " Skip reasons: " + string.Join(" | ", reasons);
        }
        _logger.Information(
            $"Save-state sync for '{systemId}' result: {report.Uploaded} uploaded, {report.Downloaded} downloaded, " +
            $"{report.Unchanged} already in sync, {skipped.Count} skipped.{skippedDetail}");
    }

    /// <summary>
    /// Checks every indexed save against what the remote actually stores, drops the entries whose
    /// payload is missing, and reports them. The saves themselves are re-uploaded by whichever
    /// machine still has them on its next sync.
    /// </summary>
    public async Task<IReadOnlyList<string>> VerifyCloudDataAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            return [];

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var transport = await CreateTransportAsync(cancellationToken);
            await transport.ListAsync(cancellationToken);
            var missing = await transport.FindMissingPayloadsAsync(cancellationToken);
            if (missing.Count > 0)
                await transport.FlushAsync(cancellationToken: cancellationToken);
            return missing;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forces one platform's present units in one direction, still backing up the loser.</summary>
    public Task<CloudSaveSyncOutcome> ForceAsync(
        string systemId,
        SaveSyncDirection direction,
        IProgress<SaveSyncProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunForcePipelineAsync(systemId, direction, progress, cancellationToken);

    private IReadOnlyList<CloudSaveSyncPlatformContext> DescribePlatforms() =>
        SaveProviderRegistry.All.Select(descriptor =>
        {
            var location = _settings.CloudSaveSync.GetLocation(descriptor.SystemId);
            return new CloudSaveSyncPlatformContext(
                descriptor.SystemId,
                descriptor.DisplayName,
                descriptor.SaveShapeDescription,
                descriptor.OverridePlaceholder,
                location.DirectoryOverride,
                location.LastSuccessUtc,
                location.LastError,
                location.LastNotice,
                descriptor.SupportsSaveStates,
                location.SyncSaveStates,
                location.StateDirectoryOverride);
        }).ToArray();

    private async Task<CloudSaveSyncOutcome> RunForcePipelineAsync(
        string systemId,
        SaveSyncDirection direction,
        IProgress<SaveSyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var target = CreateTarget(systemId, SyncContentScope.All);
        if (!IsConfigured || target is null)
            return CloudSaveSyncOutcome.NotConfigured();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var elapsed = Stopwatch.StartNew();
            var transport = await CreateTransportAsync(cancellationToken);
            var service = new SaveSyncService(
                target.LocalEndpoint,
                transport,
                new JsonSaveSyncManifestStore(_paths));

            var report = await service.ForceAsync(target.Provider, direction, progress, cancellationToken);
            elapsed.Stop();
            var platformName = SaveProviderRegistry.Find(systemId)?.DisplayName ?? systemId;
            var operationLabel = direction == SaveSyncDirection.Upload
                ? $"Upload {platformName} → cloud"
                : $"Download {platformName} → local";
            await WriteSyncLogAsync(operationLabel, report, elapsed.Elapsed, transport.Timings, cancellationToken);
            RecordOutcome([systemId], error: null, report);
            return CloudSaveSyncOutcome.Completed(report);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or System.Text.Json.JsonException or
                InvalidOperationException or ArgumentException or SaveProviderConfigurationException)
        {
            _logger.Error("Cloud save sync failed.", ex);
            ForgetCloudFolderIdAfterOperationalFailure(ex);
            RecordOutcome([systemId], ex.Message);
            return CloudSaveSyncOutcome.Failed(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CloudSaveSyncOutcome> RunSyncPipelineAsync(
        IReadOnlyList<string> systemIds,
        IProgress<SaveSyncProgress>? progress,
        string operationLabel,
        CancellationToken cancellationToken,
        bool verifyRemote = false,
        bool waitForGate = true,
        bool gateAlreadyHeld = false,
        SyncContentScope contentScope = SyncContentScope.SavesOnly,
        bool recordOutcome = true,
        IReadOnlyCollection<string>? stateGameKeys = null)
    {
        if (!IsConfigured)
            return CloudSaveSyncOutcome.NotConfigured();

        var requestedSystemIds = systemIds.Distinct(StringComparer.Ordinal).ToArray();
        var ownsGate = false;
        if (!gateAlreadyHeld)
        {
            if (waitForGate)
                await _gate.WaitAsync(cancellationToken);
            else if (!await _gate.WaitAsync(0, cancellationToken))
                return CloudSaveSyncOutcome.AlreadyRunning();
            ownsGate = true;
        }

        var synced = new List<string>();
        var targets = new List<SaveSyncTarget>();
        string? constructingSystemId = null;
        try
        {
            foreach (var systemId in requestedSystemIds)
            {
                constructingSystemId = systemId;
                if (CreateTarget(systemId, contentScope, stateGameKeys) is { } target)
                {
                    targets.Add(target);
                    synced.Add(systemId);
                }
                constructingSystemId = null;
            }

            if (targets.Count == 0)
                return CloudSaveSyncOutcome.NotConfigured();

            var elapsed = Stopwatch.StartNew();
            var transport = await CreateTransportAsync(cancellationToken);
            if (verifyRemote)
            {
                await transport.ListAsync(cancellationToken);
                var missing = await transport.FindMissingPayloadsAsync(cancellationToken);
                if (missing.Count > 0)
                {
                    // Committed before the reconciliation reads the index, not with the pass's own
                    // flush at the end. Otherwise this pass still plans against the entries it is
                    // about to remove — every one of them looks "unchanged" — and the saves are only
                    // re-uploaded by a second sync the user has no reason to know they need.
                    await transport.FlushAsync(cancellationToken: cancellationToken);
                    _logger.Warning(
                        $"{missing.Count} cloud save entries had no payload on the remote and were removed " +
                        "from the index; the saves still held on this machine are uploaded by this pass.");
                }
            }

            var service = new SaveSyncService(
                targets[0].LocalEndpoint,
                transport,
                new JsonSaveSyncManifestStore(_paths));
            var report = await service.SyncAllAsync(targets, progress, cancellationToken);
            elapsed.Stop();
            await WriteSyncLogAsync(operationLabel, report, elapsed.Elapsed, transport.Timings, cancellationToken);
            if (recordOutcome)
                RecordOutcome(synced, error: null, report);
            return CloudSaveSyncOutcome.Completed(report);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or System.Text.Json.JsonException or
                InvalidOperationException or ArgumentException or SaveProviderConfigurationException)
        {
            _logger.Error("Cloud save sync failed.", ex);
            ForgetCloudFolderIdAfterOperationalFailure(ex);
            // Construction failures identify the provider being built, and a runtime failure with
            // one target can only belong to that target. A runtime failure after several targets
            // were staged is ambiguous and remains solely in the global outcome.
            var failedSystemId = constructingSystemId ??
                (targets.Count == 1 ? targets[0].Provider.SystemId : null);
            if (recordOutcome && failedSystemId is not null)
                RecordOutcome([failedSystemId], ex.Message);
            return CloudSaveSyncOutcome.Failed(ex.Message);
        }
        finally
        {
            if (ownsGate)
                _gate.Release();
        }
    }

    private void RecordAutomaticOutcome(string systemId, CloudSaveSyncOutcome outcome)
    {
        if (outcome.Status == CloudSaveSyncStatus.Completed)
            RecordOutcome([systemId], error: null, report: outcome.Report);
        else if (outcome.Status == CloudSaveSyncStatus.Failed)
            RecordOutcome([systemId], outcome.Message);
    }

    private RcloneCloudSyncTransport CreateTransport() => new(
        _paths,
        _settings.CloudSaveSync.RemoteName!,
        _settings.CloudSaveSync.CloudFolder!,
        _rclonePath,
        cloudFolderId: _settings.CloudSaveSync.CloudFolderId);

    // One extra call, once: from then on every pass addresses the saves folder by its provider id
    // instead of walking the account root to it. A failed lookup is not an error — the transport
    // keeps using the path — and a stale id is repaired by clearing it on the next failed pass.
    private async Task<RcloneCloudSyncTransport> CreateTransportAsync(CancellationToken cancellationToken)
    {
        var transport = CreateTransport();
        if (!string.IsNullOrWhiteSpace(_settings.CloudSaveSync.CloudFolderId))
            return transport;

        try
        {
            if (await transport.ResolveCloudFolderIdAsync(cancellationToken) is not { } folderId)
                return transport;

            // Adopted rather than rebuilt, so this pass already benefits and every rclone call it
            // makes is accounted for in one place.
            transport.UseCloudFolderId(folderId);
            Persist(_settings.CloudSaveSync with { CloudFolderId = folderId });
            return transport;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            _logger.Warning($"Could not resolve the cloud folder id; using the folder path instead: {ex.Message}");
            return transport;
        }
    }

    private ISaveLocationProvider? CreateProvider(
        string systemId,
        SyncContentScope contentScope = SyncContentScope.SavesOnly,
        IReadOnlyCollection<string>? stateGameKeys = null) =>
        CreateProvider(systemId, _settings.CloudSaveSync, contentScope, stateGameKeys);

    private ISaveLocationProvider? CreateProvider(
        string systemId,
        CloudSaveSyncSettings configuration,
        SyncContentScope contentScope,
        IReadOnlyCollection<string>? stateGameKeys = null)
    {
        if (SaveProviderRegistry.Find(systemId) is not { } descriptor)
            return null;

        var context = CreateProviderContext(systemId, configuration);
        var saves = descriptor.CreateProvider(context);
        if (saves is null)
            return null;
        return SaveProviderRegistry.WithOptionalContent(
            descriptor,
            saves,
            context,
            contentScope != SyncContentScope.SavesOnly &&
                configuration.GetLocation(systemId).SyncSaveStates,
            includeBaseSaves: contentScope != SyncContentScope.SaveStatesOnly,
            gameStateKeys: stateGameKeys);
    }

    private ISaveLocationProvider? CreateBaseProvider(string systemId) =>
        CreateBaseProvider(systemId, _settings.CloudSaveSync);

    private ISaveLocationProvider? CreateBaseProvider(string systemId, CloudSaveSyncSettings configuration)
    {
        if (SaveProviderRegistry.Find(systemId) is not { } descriptor)
            return null;
        return descriptor.CreateProvider(CreateProviderContext(systemId, configuration));
    }

    private SaveProviderContext CreateProviderContext(string systemId, CloudSaveSyncSettings configuration)
    {
        var installation = _emulatorInstallations?.Invoke(systemId);
        return new SaveProviderContext(
            ResolvePortablePath(configuration.GetOverride(systemId)),
            ResolvePortablePath(installation?.Directory),
            installation?.IsFlatpak == true,
            _paths,
            ResolvePortablePath(installation?.CorePath),
            _gamesForSystem is null ? null : () => GameFileNames(systemId),
            installation?.LaunchArguments,
            ResolvePortablePath(installation?.ExecutablePath),
            installation?.FlatpakApplicationId,
            ResolvePortablePath(configuration.GetStateOverride(systemId)),
            installation?.EmulatorId);
    }

    private static async Task<(string? Summary, IReadOnlyList<OptionalContentDetection> Locations)> DescribeOptionalContentAsync(
        ISaveLocationProvider optional,
        ISaveLocationProvider saves,
        CancellationToken cancellationToken)
    {
        if (ReferenceEquals(optional, saves))
            return (null, []);
        if (optional is not AuxiliarySyncProvider auxiliary)
            return (null, []);

        var inspected = await auxiliary.GetContentLocationsAsync(cancellationToken);
        var locations = inspected.Select(location => new OptionalContentDetection(
            "Save states",
            location.Directory,
            location.EligibleFileCount,
            location.TotalFileCount,
            location.EligibleBytes,
            location.Compatibility,
            location.Warning)).ToArray();
        var states = locations.Sum(location => location.EligibleFileCount);
        var stateBytes = locations.Sum(location => location.EligibleBytes);
        return ($"Found {states} eligible state(s) ({FormatBytes(stateBytes)}).", locations);

        static string FormatBytes(long bytes)
        {
            string[] suffixes = ["B", "KB", "MB", "GB"];
            var value = (double)bytes;
            var suffix = 0;
            while (value >= 1024 && suffix < suffixes.Length - 1)
            {
                value /= 1024;
                suffix++;
            }
            return $"{value:0.#} {suffixes[suffix]}";
        }
    }

    // File names, not titles: an emulator that names a save after the game file can only be matched
    // on what is actually on disk. Extensions are stripped because that is how every such emulator
    // derives the save name.
    private IReadOnlyCollection<string> GameFileNames(string systemId) =>
        _gamesForSystem!(systemId)
            .Select(game => Path.GetFileNameWithoutExtension(game.Path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private SaveSyncTarget? CreateTarget(
        string systemId,
        SyncContentScope contentScope = SyncContentScope.SavesOnly,
        IReadOnlyCollection<string>? stateGameKeys = null) =>
        CreateProvider(systemId, contentScope, stateGameKeys) is not { } provider
            ? null
            : new SaveSyncTarget(provider, new FileSystemLocalSaveEndpoint(provider, _paths));

    private enum SyncContentScope
    {
        SavesOnly,
        SaveStatesOnly,
        All,
    }

    // Per-system outcomes let Settings show each platform's own state rather than one shared
    // message. A failure keeps the previous success time so "last synced" does not vanish.
    //
    // Recording the result is metadata about a transfer that has already happened, so it is
    // best-effort exactly like the activity log: a settings-write failure must never turn a
    // completed sync into a reported failure. It also must not throw from inside the caller's
    // catch block, where a second failing write would escape the pipeline uncaught.
    private void RecordOutcome(IReadOnlyList<string> systemIds, string? error, SaveSyncReport? report = null)
    {
        if (systemIds.Count == 0)
            return;

        try
        {
            var configuration = _settings.CloudSaveSync;
            var completedUtc = DateTimeOffset.UtcNow;
            foreach (var systemId in systemIds)
            {
                configuration = error is null
                    ? configuration.WithSyncSuccess(systemId, completedUtc, DescribeSkipped(systemId, report))
                    : configuration.WithSyncFailure(systemId, error);
            }

            Persist(configuration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Error("Could not record the per-platform cloud sync result.", ex);
        }
    }

    // A cached folder id can go stale — the folder was moved, renamed, or recreated elsewhere. An
    // operational failure drops it so the next attempt resolves the folder by path again. Catalog
    // integrity failures deliberately retain it: switching from a stable id to an ambiguous Drive
    // name after detecting damage could silently select a different folder and bypass the guard.
    private void ForgetCloudFolderIdAfterOperationalFailure(Exception failure)
    {
        if (ShouldForgetCloudFolderIdAfter(failure) &&
            !string.IsNullOrWhiteSpace(_settings.CloudSaveSync.CloudFolderId))
        {
            Persist(_settings.CloudSaveSync with { CloudFolderId = null });
        }
    }

    internal static bool ShouldForgetCloudFolderIdAfter(Exception failure) =>
        failure is not InvalidDataException;

    // A pass can succeed and still not have moved a save the user expected. The reasons are already
    // written per unit; the row needs the one line that says how many and why, scoped to the
    // platform whose row will show it.
    private string? DescribeSkipped(string systemId, SaveSyncReport? report)
    {
        var prefix = SaveProviderRegistry.Find(systemId) is null ? null : systemId;
        if (prefix is null || report is null)
            return null;

        var skipped = report.Skipped
            .Where(result => BelongsToSystem(result.UnitId, systemId))
            .ToList();
        if (skipped.Count == 0)
            return null;

        var reason = skipped[0].Reason;
        return skipped.Count == 1
            ? $"1 save was not synced. {reason}"
            : $"{skipped.Count} saves were not synced. {reason}";
    }

    // Unit ids are namespaced by provider, not by system id, so ask the registry's own provider
    // which prefix belongs to this platform rather than pattern-matching the id here.
    private bool BelongsToSystem(string unitId, string systemId) =>
        CreateBaseProvider(systemId) is { } provider &&
        unitId.StartsWith(provider.UnitIdPrefix, StringComparison.Ordinal);

    private void Persist(CloudSaveSyncSettings configuration)
    {
        _settings = _settingsService.Update(latest => latest with { CloudSaveSync = configuration });
    }

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
    private async Task WriteSyncLogAsync(
        string operation,
        SaveSyncReport report,
        TimeSpan elapsed,
        IReadOnlyList<string> transportTimings,
        CancellationToken cancellationToken)
    {
        try
        {
            await _syncLog.AppendAsync(operation, report, elapsed, transportTimings, cancellationToken);
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

    public static CloudSaveSyncOutcome AlreadyRunning() => new(
        CloudSaveSyncStatus.AlreadyRunning,
        null,
        "Another cloud sync is already running.");
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

    /// <summary>
    /// Another pass held the sync gate, so this one was declined rather than queued behind it.
    /// Nothing was attempted and local saves are untouched.
    /// </summary>
    AlreadyRunning,
}

/// <summary>The outcome of a cloud save-sync connect attempt.</summary>
public enum CloudSaveSyncConnectResult
{
    /// <summary>The remote was created (or reused) and the connection was saved.</summary>
    Connected,

    /// <summary>Required input (remote name, folder, or any usable save platform) was missing.</summary>
    InvalidInput,

    /// <summary>The portable rclone executable is not present beside EmuShelf.</summary>
    RcloneMissing,

    /// <summary>rclone reported a failure (e.g. the OAuth flow was declined or the remote is unreachable).</summary>
    Failed,
}

/// <summary>One supported save platform as Settings needs to present it.</summary>
public sealed record CloudSaveSyncPlatformContext(
    string SystemId,
    string DisplayName,
    string SaveShapeDescription,
    string OverridePlaceholder,
    string? Override,
    DateTimeOffset? LastSuccessUtc,
    string? LastError,
    string? LastNotice = null,
    bool SupportsSaveStates = false,
    bool SyncSaveStates = false,
    string? StateOverride = null);

/// <summary>
/// The cloud save-sync operations the Settings view model drives, wrapped as delegates so the view
/// model stays testable with a fake context. The delegate set is platform-agnostic: every operation
/// takes a system id, so adding a platform does not change this shape.
/// </summary>
public sealed record CloudSaveSyncSettingsContext(
    CloudSaveSyncSettings Current,
    bool IsRcloneAvailable,
    string RcloneExpectedPath,
    string SyncLogPath,
    Func<IReadOnlyList<CloudSaveSyncPlatformContext>> GetPlatforms,
    Func<string, CancellationToken, Task<string?>> GetDetectedPathAsync,
    Func<string, string, IReadOnlyDictionary<string, string?>, CancellationToken, string?, string?, Task<CloudSaveSyncConnectResult>> ConnectGoogleDriveAsync,
    Func<CancellationToken, Task> DisconnectAsync,
    Func<IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>> SyncNowAsync,
    Func<string, SaveSyncDirection, IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>> ForceAsync,
    Action<string, string?> UpdateOverride,
    Func<CancellationToken, Task<bool>> DownloadRcloneAsync,
    Func<string, CancellationToken, Task<SaveProviderDetection?>>? GetDetectionAsync = null,
    Action<string, bool>? UpdateOptionalContent = null,
    Action<IReadOnlyDictionary<string, string?>>? UpdateOverrides = null,
    Action<string, string?>? UpdateStateOverride = null);
