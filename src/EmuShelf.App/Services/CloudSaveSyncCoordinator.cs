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
    public async Task<CloudSaveSyncConnectResult> ConnectGoogleDriveAsync(
        string remoteName,
        string cloudFolder,
        IReadOnlyDictionary<string, string?> overrides,
        CancellationToken cancellationToken = default)
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
            await configurator.CreateGoogleDriveRemoteAsync(trimmedRemote, cancellationToken);
            await configurator.EnsureFolderAsync(trimmedRemote, trimmedFolder, cancellationToken);

            Persist(candidate with
            {
                Enabled = true,
                RemoteName = trimmedRemote,
                CloudFolder = trimmedFolder,
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
        catch (RcloneSignInServerBusyException ex)
        {
            // A leftover sign-in is holding rclone's loopback port. Surface it distinctly so the user
            // is told to close it or restart, not that they declined the sign-in.
            _logger.Error("Cloud save connect failed: the sign-in port is still in use.", ex);
            return CloudSaveSyncConnectResult.SignInServerBusy;
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
            var syncStates = _settings.CloudSaveSync.GetLocation(systemId).SyncSaveStates;
            var supportsStates = SaveProviderRegistry.Find(systemId)?.SupportsSaveStates == true;
            // Name the resolved state folder before the pass so a later "nothing matched" is readable.
            if (syncStates && supportsStates)
                await LogStatePhaseDiagnosticsAsync(systemId, launchStateKeys);

            // One combined pass — base saves plus save states (when the toggle is on) — so a launch or
            // exit makes a single cloud index round-trip for the platform instead of two. All includes
            // states only when the toggle is on, so the toggle-off case reconciles just the base saves.
            var outcome = await RunSyncPipelineAsync(
                [systemId],
                progress: null,
                $"Automatic sync ({systemId})",
                cancellationToken,
                gateAlreadyHeld: true,
                contentScope: SyncContentScope.All,
                recordOutcome: false,
                stateGameKeys: launchStateKeys);
            RecordAutomaticOutcome(systemId, outcome);
            LogAutomaticSyncResult(systemId, outcome);

            // Tell the launch/exit summary the states were skipped, but only where the platform
            // actually offers save-state sync — otherwise there is no toggle for the user to turn on.
            if (outcome.Status == CloudSaveSyncStatus.Completed && supportsStates && !syncStates)
            {
                _logger.Information(
                    $"Save-state sync skipped for '{systemId}': the platform's " +
                    "'Automatically sync save states' toggle is off on this machine.");
                return outcome with { SaveStatesSkipped = true };
            }

            return outcome;
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
            // localStatesMatched is scoped to the launched game; the folder figures below are the
            // whole folder, unscoped. Reporting the resolved folder and its actual manual-state count
            // is what lets a zero match be read correctly — an empty or missing folder is a different
            // problem from a folder full of states whose names did not match the launched game.
            var localCount = provider is null ? 0 : (await provider.GetSaveUnitsAsync()).Count;
            IReadOnlyList<AuxiliaryContentLocation> locations =
                auxiliary is null ? [] : await auxiliary.GetContentLocationsAsync();
            var folder = locations
                .Select(location => location.Directory)
                .FirstOrDefault(directory => !string.IsNullOrWhiteSpace(directory));
            var folderStates = locations.Sum(location => location.EligibleFileCount);
            var folderWarning = locations
                .Select(location => location.Warning)
                .FirstOrDefault(warning => !string.IsNullOrWhiteSpace(warning));

            _logger.Information(
                $"Save-state sync for '{systemId}': keys=[{keyText}]; " +
                $"compatibilityDetected={compatible?.ToString() ?? "n/a"}; compatibilityKey={compatibilityKey}; " +
                $"stateFolder={folder ?? "(unresolved)"}; folderStates={folderStates}; " +
                $"localStatesMatched={localCount}. " +
                StatePhaseHint(compatible, localCount, folder, folderStates, folderWarning));
        }
        catch (Exception ex) when (
            ex is IOException or InvalidOperationException or ArgumentException or SaveProviderConfigurationException)
        {
            _logger.Information($"Save-state sync diagnostics for '{systemId}' were unavailable: {ex.Message}");
        }
    }

    // The plain-language reason a state phase found nothing to do, drawn from what was actually
    // observed — the resolved folder and its real manual-state count — rather than assumed. Returns
    // an empty string when there is nothing noteworthy to explain (states were matched normally).
    internal static string StatePhaseHint(
        bool? compatible,
        int localStatesMatched,
        string? folder,
        int folderStates,
        string? folderWarning)
    {
        if (compatible == false)
        {
            return "compatibilityDetected=false means the emulator/core version or CPU architecture " +
                "could not be read, so states are neither uploaded nor restored — check the emulator " +
                "and core are configured.";
        }
        if (localStatesMatched > 0)
            return string.Empty;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return "The state folder could not be resolved" +
                (string.IsNullOrWhiteSpace(folderWarning) ? "." : $": {folderWarning}");
        }
        if (folderStates == 0)
        {
            return $"The state folder ('{folder}') holds no manual save states yet" +
                (string.IsNullOrWhiteSpace(folderWarning) ? string.Empty : $" ({folderWarning})") +
                " — nothing was created for this game, or only auto-save states (which are not synced) exist.";
        }

        return $"The state folder ('{folder}') holds {folderStates} manual save state(s), but none matched the " +
            "launched game's name or serial — check the state file names against the game, or the resolved folder.";
    }

    // A concise result line in the portable app log (Logs/EmuShelf-*.log) for an automatic launch or
    // exit sync — base saves and states together — so uploads, downloads, and skips (with reasons)
    // are visible there and not only in sync-log.txt. Failures are already logged by the pipeline.
    private void LogAutomaticSyncResult(string systemId, CloudSaveSyncOutcome outcome)
    {
        if (outcome.Report is not { } report)
            return;

        var skipped = report.Skipped;
        // Enumerate every distinct skip reason (capped), not just the first: a pass can skip several
        // units for different reasons (a version mismatch and a name-scope miss at once), and seeing
        // only the first hides the others.
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
            $"Automatic sync for '{systemId}' result: {report.Uploaded} uploaded, {report.Downloaded} downloaded, " +
            $"{report.Unchanged} already in sync, {skipped.Count} skipped.{skippedDetail}");
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
        if (!IsConfigured)
            return CloudSaveSyncOutcome.NotConfigured();

        // Provider construction reads the emulator's config, version resources and binary
        // architecture — and on a cold cache can shell out to `flatpak info` with a multi-second
        // wait. Keep it off the caller's (UI) thread, exactly as GetDetectionAsync does.
        var target = await Task.Run(() => CreateTarget(systemId, SyncContentScope.All), cancellationToken);
        if (target is null)
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
            // Building every system's provider inline on an uncontended gate runs on the caller's
            // (UI) thread and froze the window; offload it as detection does. constructingSystemId is
            // a captured local, so it still names the provider being built if construction throws.
            await Task.Run(() =>
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
            }, cancellationToken);

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
        var corePath = ResolvePortablePath(installation?.CorePath);
        return new SaveProviderContext(
            ResolvePortablePath(configuration.GetOverride(systemId)),
            ResolvePortablePath(installation?.Directory),
            installation?.IsFlatpak == true,
            _paths,
            corePath,
            _gamesForSystem is null ? null : () => GameFileNames(systemId),
            installation?.LaunchArguments,
            ResolvePortablePath(installation?.ExecutablePath),
            installation?.FlatpakApplicationId,
            ResolvePortablePath(configuration.GetStateOverride(systemId)),
            installation?.EmulatorId,
            CoreSharedAcrossSystems: IsCoreSharedAcrossSystems(systemId, corePath));
    }

    // True when another EmuShelf system is configured with the same libretro core file, so both
    // resolve to the same per-core save/state folder (e.g. mGBA set for both Game Boy Advance and
    // Game Boy Color). Such a system must claim only its own library's files, otherwise each sharing
    // system claims — and double-uploads — the whole folder.
    private bool IsCoreSharedAcrossSystems(string systemId, string? corePath)
    {
        if (string.IsNullOrWhiteSpace(corePath) || _emulatorInstallations is null)
            return false;
        foreach (var otherSystemId in SaveProviderRegistry.SystemIds)
        {
            if (string.Equals(otherSystemId, systemId, StringComparison.Ordinal))
                continue;
            var otherCore = ResolvePortablePath(_emulatorInstallations(otherSystemId)?.CorePath);
            if (!string.IsNullOrWhiteSpace(otherCore) &&
                FilePathComparison.Comparer.Equals(otherCore, corePath))
            {
                return true;
            }
        }

        return false;
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
        // Unit ids are namespaced by provider, not by system id, so ask the registry's own provider
        // for the prefix that belongs to this platform. Build the provider once for the whole report
        // rather than once per skipped unit.
        if (report is null || CreateBaseProvider(systemId) is not { } provider)
            return null;

        var skipped = report.Skipped
            .Where(result => result.UnitId.StartsWith(provider.UnitIdPrefix, StringComparison.Ordinal))
            .ToList();
        if (skipped.Count == 0)
            return null;

        var reason = skipped[0].Reason;
        return skipped.Count == 1
            ? $"1 save was not synced. {reason}"
            : $"{skipped.Count} saves were not synced. {reason}";
    }

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
    /// <summary>
    /// The save pass completed but the save-state pass was skipped because the platform's
    /// "Automatically sync save states" toggle is off (and the platform actually supports states).
    /// Surfaced so a launch/exit summary can say so, instead of a bare "nothing to sync" that hides
    /// why a save state the player just made did not sync.
    /// </summary>
    public bool SaveStatesSkipped { get; init; }

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

    /// <summary>
    /// rclone could not bind its loopback OAuth port because a previous, unfinished sign-in is still
    /// holding it. Distinct from <see cref="Failed"/> so the user is told to close it or restart.
    /// </summary>
    SignInServerBusy,
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
    Func<string, string, IReadOnlyDictionary<string, string?>, CancellationToken, Task<CloudSaveSyncConnectResult>> ConnectGoogleDriveAsync,
    Func<CancellationToken, Task> DisconnectAsync,
    Func<IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>> SyncNowAsync,
    Func<string, SaveSyncDirection, IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>> ForceAsync,
    Action<string, string?> UpdateOverride,
    Func<CancellationToken, Task<bool>> DownloadRcloneAsync,
    Func<string, CancellationToken, Task<SaveProviderDetection?>>? GetDetectionAsync = null,
    Action<string, bool>? UpdateOptionalContent = null,
    Action<IReadOnlyDictionary<string, string?>>? UpdateOverrides = null,
    Action<string, string?>? UpdateStateOverride = null);
