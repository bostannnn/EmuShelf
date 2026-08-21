using System.Diagnostics;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Infrastructure.SaveSync;
using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

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
/// Composes the save-sync pipeline (registry-provided provider + filesystem endpoint + Google Drive
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
    private readonly Func<string, SaveEmulatorInstallation?>? _emulatorInstallations;
    // Optional batched form of the resolver above: resolves every requested system's installation in
    // one database read. Used only for the one-time startup migration, which would otherwise open one
    // connection per system (15+) on the UI thread before the first frame. Runtime resolution keeps
    // using the per-system delegate so a config the user changes mid-session is always read fresh.
    private readonly Func<IReadOnlyList<string>, IReadOnlyDictionary<string, SaveEmulatorInstallation?>>?
        _emulatorInstallationsBatch;
    private readonly Func<string, IReadOnlyList<Game>>? _gamesForSystem;
    private readonly FileSaveSyncLog _syncLog;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _settings;

    // Built on first use and then kept, so the hour-long access token is minted once per run rather
    // than once per sync. Null until the first sync or connect, so a build that never touches cloud
    // sync constructs no OAuth client and never opens the token blob.
    private HttpClient? _googleHttpClient;
    private GoogleAccessTokenSource? _accessTokens;

    public CloudSaveSyncCoordinator(
        IAppPaths paths,
        ISettingsService settingsService,
        AppSettings settings,
        IAppLogger logger,
        Func<string, SaveEmulatorInstallation?>? emulatorInstallations = null,
        Func<string, IReadOnlyList<Game>>? gamesForSystem = null,
        Func<IReadOnlyList<string>, IReadOnlyDictionary<string, SaveEmulatorInstallation?>>?
            emulatorInstallationsBatch = null)
    {
        _gamesForSystem = gamesForSystem;
        _paths = paths;
        _settingsService = settingsService;
        _logger = logger;
        _emulatorInstallations = emulatorInstallations;
        _emulatorInstallationsBatch = emulatorInstallationsBatch;
        // Fold the legacy per-emulator fields into the per-system dictionary, then re-key each
        // per-system override to the system's active emulator — once, up front, so every read below
        // sees one (system, emulator) shape regardless of how old the settings file is. Ordered after
        // _emulatorInstallations so the migration can resolve each system's active emulator.
        _settings = settings with
        {
            CloudSaveSync = settings.CloudSaveSync
                .NormalizeSaveLocations()
                .MigrateOverridesToPerEmulator(ActiveEmulatorBySystem()),
        };
        _syncLog = new FileSaveSyncLog(paths);
    }

    /// <summary>The current cloud-sync settings snapshot.</summary>
    public CloudSaveSyncSettings Current => _settings.CloudSaveSync;

    /// <summary>
    /// Whether the account is connected and sync is enabled. The built-in Google Drive client
    /// authenticates as the connected account and needs only the folder. A connection left over from
    /// the retired rclone transport (<see cref="CloudTransportKind.Rclone"/>) counts as not configured,
    /// so the user reconnects through the built-in client rather than syncing against a dead transport.
    /// </summary>
    public bool IsConfigured => _settings.CloudSaveSync switch
    {
        { Enabled: false } => false,
        { TransportKind: CloudTransportKind.GoogleDrive } cloud => cloud.CloudFolder is { Length: > 0 },
        _ => false,
    };

    /// <summary>Whether this build ships an OAuth client, and so can offer the managed transport.</summary>
    public bool IsManagedTransportAvailable => GoogleOAuthClientSource.IsConfigured;

    private HttpClient GoogleHttpClient =>
        _googleHttpClient ??= new HttpClient(new SocketsHttpHandler
        {
            // Fail a dead connection quickly instead of letting it eat the whole request budget —
            // rclone's --contimeout did the same. The per-request stall timeout lives in the Drive
            // client; this is only the TCP/TLS handshake. HttpClient.Timeout stays as a coarse backstop.
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        })
        {
            Timeout = TimeSpan.FromMinutes(5),
        };

    private GoogleAccessTokenSource AccessTokens =>
        _accessTokens ??= new GoogleAccessTokenSource(
            new GoogleOAuthClient(
                GoogleHttpClient,
                GoogleOAuthClientSource.Resolve() ??
                    throw new InvalidOperationException(
                        "This build ships no Google OAuth client, so it cannot use the built-in Drive transport.")),
            GoogleDriveTokenStoreFactory.Create(_paths, _logger));

    /// <summary>
    /// Whether one system participates in sync. This asks the registry to build the provider, the
    /// same call the sync pipeline makes, so a "yes" here cannot become a silent no-op there.
    /// </summary>
    public bool CanSyncSystem(string systemId) => IsConfigured && CreateBaseProvider(systemId) is not null;

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
        // Everything below reads the emulator's own configuration, its version resources and binary
        // architecture, and the save/state folders — often on a slow external drive. The Saves
        // section resolves every platform at once when it opens, so this must stay off the UI thread;
        // running it inline froze the window for a few seconds each time. Provider construction reads
        // the RetroArch core info file, and building the context resolves emulator installations, so
        // both run off-thread. The active emulator profile is resolved here so DetectAsync and the
        // optional-content pass below both use the emulator that is actually configured.
        var resolved = await Task.Run(
            () => ResolveActiveProvider(systemId, _settings.CloudSaveSync),
            cancellationToken);
        if (resolved is not { } active)
            return null;
        var (context, descriptor, provider) = active;

        var detection = await descriptor.DetectAsync(provider, cancellationToken);
        var optionalSummary = (Summary: (string?)null, Locations: (IReadOnlyList<OptionalContentDetection>)[]);
        try
        {
            var optional = await Task.Run(
                () => SaveProviderRegistry.WithOptionalContent(
                    descriptor,
                    provider,
                    context,
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
        Persist(WithOverrideFor(_settings.CloudSaveSync, systemId, directory));

    /// <summary>Persists all edited platform overrides in one settings transaction.</summary>
    public void UpdateOverrides(IReadOnlyDictionary<string, string?> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        var configuration = _settings.CloudSaveSync;
        foreach (var (systemId, directory) in overrides)
            configuration = WithOverrideFor(configuration, systemId, directory);
        Persist(configuration);
    }

    /// <summary>Persists one platform's opt-in save-state choice.</summary>
    public void UpdateOptionalContent(string systemId, bool syncSaveStates) =>
        Persist(WithOptionalContentFor(_settings.CloudSaveSync, systemId, syncSaveStates));

    /// <summary>Persists one system's save-state folder override without changing the connection state.</summary>
    public void UpdateStateOverride(string systemId, string? directory) =>
        Persist(WithStateOverrideFor(_settings.CloudSaveSync, systemId, directory));

    /// <summary>
    /// Signs into Google Drive with EmuShelf's own built-in client and connects the transport. Opens
    /// the system browser, waits for the redirect, and stores only the refresh token in a protected
    /// blob; nothing secret reaches settings.json.
    /// </summary>
    /// <param name="openBrowser">
    /// How to show the consent page. Injected rather than called directly so this stays testable and
    /// so Android can hand it a custom tab instead of a desktop browser launch.
    /// </param>
    public async Task<CloudSaveSyncConnectResult> ConnectGoogleDriveManagedAsync(
        string cloudFolder,
        IReadOnlyDictionary<string, string?> overrides,
        Action<Uri> openBrowser,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        ArgumentNullException.ThrowIfNull(openBrowser);
        if (string.IsNullOrWhiteSpace(cloudFolder))
            return CloudSaveSyncConnectResult.InvalidInput;

        // At least one platform must produce a provider once the overrides are applied, otherwise
        // connecting leaves the user with a remote and nothing to sync into it. Checked before the
        // build capability below because this is the half the user can actually act on.
        var candidate = _settings.CloudSaveSync;
        foreach (var (systemId, directory) in overrides)
            candidate = WithOverrideFor(candidate, systemId, directory);
        if (!SaveProviderRegistry.SystemIds.Any(systemId => CreateBaseProvider(systemId, candidate) is not null))
            return CloudSaveSyncConnectResult.InvalidInput;

        if (!IsManagedTransportAvailable)
            return CloudSaveSyncConnectResult.ManagedTransportUnavailable;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var oauth = new GoogleOAuthClient(GoogleHttpClient, GoogleOAuthClientSource.Resolve()!);
            using var redirect = OAuthRedirectHandlerFactory.Create(_logger);
            var request = oauth.CreateAuthorizationRequest(redirect.RedirectUri);

            openBrowser(request.AuthorizationUri);
            var code = await redirect.WaitForCodeAsync(request.State, cancellationToken);
            AccessTokens.Adopt(await oauth.ExchangeCodeAsync(request, code, cancellationToken));

            var trimmedFolder = cloudFolder.Trim();
            // Create the folder now rather than on first sync, so a connect that appears to succeed
            // has actually proved it can write to the account.
            var transport = new GoogleDriveCloudSyncTransport(
                new GoogleDriveApiClient(GoogleHttpClient, AccessTokens, _logger),
                _paths,
                trimmedFolder,
                _logger);
            await transport.EnsureCloudFolderAsync(cancellationToken);

            Persist(candidate with
            {
                Enabled = true,
                TransportKind = CloudTransportKind.GoogleDrive,
                // The managed client authenticates as the account itself; there is no named remote.
                RemoteName = null,
                CloudFolder = trimmedFolder,
                CloudFolderId = transport.CloudFolderId,
            });
            return CloudSaveSyncConnectResult.Connected;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GoogleAuthorizationRequiredException ex)
        {
            _logger.Error("Cloud save connect failed: the Google sign-in did not complete.", ex);
            return CloudSaveSyncConnectResult.SignInDeclined;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException)
        {
            _logger.Error("Cloud save connect failed.", ex);
            return CloudSaveSyncConnectResult.Failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Turns sync off and forgets the connection. The cloud data is left intact; the stored account is
    /// dropped, because leaving a refresh token behind for a connection the user just ended is not what
    /// "disconnect" means.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_settings.CloudSaveSync.TransportKind == CloudTransportKind.GoogleDrive &&
                IsManagedTransportAvailable)
            {
                AccessTokens.Disconnect();
            }

            Persist(_settings.CloudSaveSync with
            {
                Enabled = false,
                RemoteName = null,
                CloudFolder = null,
                CloudFolderId = null,
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Bundles the coordinator's operations as a delegate context for the Settings view model.</summary>
    public CloudSaveSyncSettingsContext CreateSettingsContext() => new(
        _settings.CloudSaveSync,
        SyncLogPath,
        // A delegate, not a snapshot: Settings re-reads it after each operation so a row's
        // "last synced" / "last attempt failed" line cannot go stale within an open session.
        DescribePlatforms,
        GetDetectedPathAsync,
        DisconnectAsync,
        SyncNowAsync,
        ForceAsync,
        UpdateOverride,
        GetDetectionAsync,
        UpdateOptionalContent,
        UpdateOverrides,
        UpdateStateOverride,
        IsManagedTransportAvailable,
        ConnectGoogleDriveManagedAsync,
        DescribePlatformForEmulator,
        ExportSavesAsync);

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
            var syncStates = LocationFor(_settings.CloudSaveSync, systemId).SyncSaveStates;
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

    /// <summary>
    /// Exports every platform's saves — save states included — into a single portable <c>.zip</c> at
    /// <paramref name="destinationZipPath"/>. With <see cref="SaveExportScope.DeviceAndCloud"/> it also
    /// pulls down any save that lives only in the connected remote. Read-only over save data: it never
    /// writes to a save, game file, or emulator configuration. Shares the sync gate so an export and a
    /// sync can never run over each other.
    /// </summary>
    public async Task<SaveExportResult> ExportSavesAsync(
        string destinationZipPath,
        SaveExportScope scope,
        IProgress<SaveTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);
        var includeCloud = scope == SaveExportScope.DeviceAndCloud;
        if (includeCloud && !IsConfigured)
            return SaveExportResult.NotConfigured();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Provider construction reads emulator config, version resources and binary architecture,
            // and on a cold cache can shell out to flatpak — keep it off the caller's (UI) thread, as
            // the sync pipeline does.
            var targets = await Task.Run(BuildExportTargets, cancellationToken);
            if (targets.Count == 0 && !includeCloud)
                return SaveExportResult.NothingToExport();

            using var sink = new ZipSaveExportSink(destinationZipPath);
            IVerifiableCloudSyncTransport? transport =
                includeCloud ? await CreateTransportAsync(cancellationToken) : null;
            // A device+cloud export taken before the first sync after upgrade would otherwise miss every
            // cloud-only save still under an old emulator-scoped key (no provider owns it). Run the same
            // one-time, idempotent re-key here so those saves are present under their new system key and
            // get exported; the leftover old keys are then quietly ignored by the export's own guard.
            if (transport is not null)
                await EnsureBatteryNamespaceMigratedAsync(transport, cancellationToken);

            var result = await new SaveExportService().ExportAsync(
                targets, transport, sink, progress, cancellationToken);
            if (transport is not null)
                BankCloudFolderId(transport);

            if (result.Status != SaveExportStatus.Completed)
                return result; // The sink is disposed without Complete, so no archive is left behind.

            sink.Complete();
            return result with { DestinationPath = destinationZipPath };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or HttpRequestException or
                InvalidOperationException or InvalidDataException or ArgumentException or
                SaveProviderConfigurationException)
        {
            _logger.Error("Save export failed.", ex);
            return SaveExportResult.Failed(ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    // One export target per configured platform, each provider wrapped so it also enumerates, owns,
    // and resolves save states. The per-platform "Automatically sync save states" toggle is not
    // consulted: it governs automatic sync, whereas a one-off export always carries states (the user
    // opted in to including them).
    private IReadOnlyList<SaveExportTarget> BuildExportTargets()
    {
        var configuration = _settings.CloudSaveSync;
        var targets = new List<SaveExportTarget>();
        foreach (var systemId in SaveProviderRegistry.SystemIds)
        {
            if (ResolveActiveProvider(systemId, configuration) is not { } active)
                continue;
            var (context, descriptor, saves) = active;
            var provider = SaveProviderRegistry.WithOptionalContent(
                descriptor, saves, context, includeSaveStates: true);
            var platformName = SaveProviderRegistry.Find(systemId)?.DisplayName ?? systemId;
            targets.Add(new SaveExportTarget(
                provider, new FileSystemLocalSaveEndpoint(provider, _paths), platformName));
        }

        return targets;
    }

    private IReadOnlyList<CloudSaveSyncPlatformContext> DescribePlatforms() =>
        SaveProviderRegistry.All
            .Select(descriptor => DescribePlatform(descriptor, LocationFor(_settings.CloudSaveSync, descriptor.SystemId)))
            .ToArray();

    /// <summary>
    /// One system's platform context read against a specific emulator's saved override rather than
    /// its persisted active emulator. Settings uses this to refresh the Saves row when the user
    /// switches the emulator picker, before that switch is saved. Returns null when the system has no
    /// save-sync platform. The emulator id is resolved through the registry, so an unknown id falls
    /// back to the system's default profile — the same way a launch would resolve it.
    /// </summary>
    public CloudSaveSyncPlatformContext? DescribePlatformForEmulator(string systemId, string emulatorId)
    {
        var descriptor = SaveProviderRegistry.Find(systemId);
        if (descriptor is null)
            return null;
        var resolvedEmulatorId = SaveProviderRegistry.Resolve(systemId, emulatorId)?.EmulatorId;
        var location = resolvedEmulatorId is { } id
            ? _settings.CloudSaveSync.GetLocation(systemId, id)
            : _settings.CloudSaveSync.GetLocation(systemId);
        return DescribePlatform(descriptor, location);
    }

    // The display text (name, shape, placeholder, save-states label) is emulator-neutral — the row
    // reads the same whichever profile is active — so only the location varies between the active
    // emulator and a switched-to one.
    private static CloudSaveSyncPlatformContext DescribePlatform(
        SaveProviderDescriptor descriptor, SaveLocationSettings location) =>
        new(
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
            location.StateDirectoryOverride,
            descriptor.SaveStatesLabel);

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
            await EnsureBatteryNamespaceMigratedAsync(transport, cancellationToken);
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
            BankCloudFolderId(transport);
            RecordOutcome([systemId], error: null, report);
            return CloudSaveSyncOutcome.Completed(report);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Only the user pressing Stop rethrows as cancellation. A cancellation whose token is not
            // the caller's is a stalled request that slipped past the Drive client (e.g. a token mint
            // that hit HttpClient.Timeout); the next catch lists OperationCanceledException so it is
            // recorded as a per-platform failure rather than escaping uncaught or re-raised as a stop.
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or System.Text.Json.JsonException or
                InvalidOperationException or ArgumentException or SaveProviderConfigurationException or
                HttpRequestException or OperationCanceledException)
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
            await EnsureBatteryNamespaceMigratedAsync(transport, cancellationToken);
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
            BankCloudFolderId(transport);
            if (recordOutcome)
                RecordOutcome(synced, error: null, report);
            return CloudSaveSyncOutcome.Completed(report);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Only the user pressing Stop rethrows as cancellation. A cancellation whose token is not
            // the caller's is a stalled request that slipped past the Drive client (e.g. a token mint
            // that hit HttpClient.Timeout); the next catch lists OperationCanceledException so it is
            // recorded as a per-platform failure rather than escaping uncaught or re-raised as a stop.
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or InvalidDataException or System.Text.Json.JsonException or
                InvalidOperationException or ArgumentException or SaveProviderConfigurationException or
                HttpRequestException or OperationCanceledException)
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

    // Runs the one-time copy-only re-key of cloud battery saves from the old emulator-scoped namespace
    // to the new system-scoped one, once per machine, guarded by a persisted flag. Best-effort: a
    // failure leaves the flag unset so the next pass retries, and the idempotent migration skips
    // anything already copied. The old-key entries are never deleted (the transport has no delete), so
    // nothing is at risk if this is interrupted. See DECISIONS 2026-08-21.
    private async Task EnsureBatteryNamespaceMigratedAsync(
        ICloudSyncTransport transport,
        CancellationToken cancellationToken)
    {
        if (_settings.CloudSaveSync.BatteryNamespaceMigrated)
            return;

        try
        {
            var copied = await new BatterySaveNamespaceMigration(transport).RunAsync(cancellationToken);
            if (copied > 0)
            {
                _logger.Information(
                    $"Migrated {copied} cloud battery save(s) to the system-scoped namespace; the old " +
                    "emulator-scoped copies were left in the cloud untouched.");
            }

            // Re-key the local baseline manifest to match, so this machine's first post-migration sync
            // sees a baseline under each new key and reconciles cleanly instead of conflict-backing-up.
            var manifestStore = new JsonSaveSyncManifestStore(_paths);
            var manifest = await manifestStore.LoadAsync(cancellationToken);
            var rekeyed = BatterySaveNamespaceMigration.RekeyManifestBaselines(manifest);
            if (!ReferenceEquals(rekeyed, manifest))
                await manifestStore.SaveAsync(rekeyed, cancellationToken);

            Persist(_settings.CloudSaveSync with { BatteryNamespaceMigrated = true });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (
            ex is IOException or HttpRequestException or InvalidDataException or InvalidOperationException)
        {
            _logger.Warning(
                "Cloud battery-save namespace migration did not complete; it will retry on the next " +
                $"sync. The old saves remain in the cloud untouched: {ex.Message}");
        }
    }

    private GoogleDriveCloudSyncTransport CreateGoogleDriveTransport() => new(
        new GoogleDriveApiClient(GoogleHttpClient, AccessTokens, _logger),
        _paths,
        _settings.CloudSaveSync.CloudFolder ?? string.Empty,
        _logger,
        _settings.CloudSaveSync.CloudFolderId);

    /// <summary>
    /// Builds the cloud transport, ready to use. Google Drive is the only transport; the seam is kept
    /// so a future backend can be selected on <see cref="CloudSaveSyncSettings.TransportKind"/> here.
    /// </summary>
    private Task<IVerifiableCloudSyncTransport> CreateTransportAsync(CancellationToken cancellationToken) =>
        CreateGoogleDriveTransportAsync(cancellationToken);

    // The Google Drive transport resolves the folder id as part of its first call rather than needing a
    // probe of its own, so the only thing to do here is bank the id once it knows it.
    private async Task<IVerifiableCloudSyncTransport> CreateGoogleDriveTransportAsync(
        CancellationToken cancellationToken)
    {
        var transport = CreateGoogleDriveTransport();
        if (!string.IsNullOrWhiteSpace(_settings.CloudSaveSync.CloudFolderId))
            return transport;

        try
        {
            await transport.ListAsync(cancellationToken);
            BankCloudFolderId(transport);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            // Not fatal: the caller lists again anyway, and a transport without a cached folder id
            // simply resolves the path on its next call.
            _logger.Warning($"Could not resolve the cloud folder id; using the folder path instead: {ex.Message}");
        }

        return transport;
    }

    /// <summary>
    /// Persists the folder id a managed transport ended a pass holding, when it differs from what is
    /// stored. This is how a cached id that turned out to be wrong — and was re-resolved by path
    /// mid-pass — stops being wrong, instead of costing the same correction on every later sync.
    /// </summary>
    private void BankCloudFolderId(IVerifiableCloudSyncTransport transport)
    {
        if (transport is not GoogleDriveCloudSyncTransport { CloudFolderId: { } folderId })
            return;
        if (string.Equals(folderId, _settings.CloudSaveSync.CloudFolderId, StringComparison.Ordinal))
            return;

        try
        {
            Persist(_settings.CloudSaveSync with { CloudFolderId = folderId });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cache hint, not the transfer. A portable install on a removed or read-only drive
            // must not turn a completed sync into a reported failure over it.
            _logger.Warning($"Could not record the resolved cloud folder id: {ex.Message}");
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
        if (ResolveActiveProvider(systemId, configuration) is not { } active)
            return null;
        var (context, descriptor, saves) = active;
        return SaveProviderRegistry.WithOptionalContent(
            descriptor,
            saves,
            context,
            contentScope != SyncContentScope.SavesOnly &&
                LocationFor(configuration, systemId).SyncSaveStates,
            includeBaseSaves: contentScope != SyncContentScope.SaveStatesOnly,
            gameStateKeys: stateGameKeys);
    }

    private ISaveLocationProvider? CreateBaseProvider(string systemId) =>
        CreateBaseProvider(systemId, _settings.CloudSaveSync);

    private ISaveLocationProvider? CreateBaseProvider(string systemId, CloudSaveSyncSettings configuration) =>
        ResolveActiveProvider(systemId, configuration)?.Provider;

    // Resolves a system's active emulator profile, builds its provider context, and constructs the
    // base save provider — the one place all three provider-building callers share, so the active
    // (system, emulator) profile is chosen once and CreateProvider/DetectAsync never branch on the
    // emulator. Returns null when the platform has nothing to sync on this machine.
    private (SaveProviderContext Context, SaveProviderDescriptor Descriptor, ISaveLocationProvider Provider)?
        ResolveActiveProvider(string systemId, CloudSaveSyncSettings configuration)
    {
        var context = CreateProviderContext(systemId, configuration);
        if (SaveProviderRegistry.Resolve(systemId, context.ActiveEmulatorId) is not { } descriptor)
            return null;
        var provider = descriptor.CreateProvider(context);
        return provider is null ? null : (context, descriptor, provider);
    }

    private SaveProviderContext CreateProviderContext(string systemId, CloudSaveSyncSettings configuration)
    {
        var installation = _emulatorInstallations?.Invoke(systemId);
        var corePath = ResolvePortablePath(installation?.CorePath);
        return new SaveProviderContext(
            ResolvePortablePath(OverrideFor(configuration, systemId)),
            ResolvePortablePath(installation?.Directory),
            installation?.IsFlatpak == true,
            _paths,
            corePath,
            _gamesForSystem is null ? null : () => GameFileNames(systemId),
            installation?.LaunchArguments,
            ResolvePortablePath(installation?.ExecutablePath),
            installation?.FlatpakApplicationId,
            ResolvePortablePath(StateOverrideFor(configuration, systemId)),
            installation?.EmulatorId,
            CoreSharedAcrossSystems: IsCoreSharedAcrossSystems(systemId, corePath));
    }

    // The active emulator for a system — the installation's configured emulator, or the default
    // profile's emulator when nothing is installed yet. Save locations are keyed by (system, this),
    // so every read/write below routes through it rather than touching a bare system-id key directly.
    // Routed through Resolve (as provider resolution is) so the key always names the profile that
    // actually runs: an installation id with no matching profile falls back to the system's default,
    // keeping the override key and the running provider in lock-step.
    private string? ActiveEmulatorFor(string systemId) =>
        SaveProviderRegistry.Resolve(systemId, _emulatorInstallations?.Invoke(systemId)?.EmulatorId)?.EmulatorId;

    // Every system's active emulator, for the one-time legacy-override migration on load. Prefers the
    // batched resolver (one database read for all systems) and falls back to the per-system delegate
    // for callers/tests that supply only that one.
    private IReadOnlyDictionary<string, string> ActiveEmulatorBySystem()
    {
        var systemIds = SaveProviderRegistry.SystemIds;
        var installations = _emulatorInstallationsBatch?.Invoke(systemIds);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var systemId in systemIds)
        {
            var activeEmulatorId = installations is not null
                ? SaveProviderRegistry.Resolve(
                    systemId,
                    installations.GetValueOrDefault(systemId)?.EmulatorId)?.EmulatorId
                : ActiveEmulatorFor(systemId);
            if (activeEmulatorId is { } emulatorId)
                map[systemId] = emulatorId;
        }
        return map;
    }

    private SaveLocationSettings LocationFor(CloudSaveSyncSettings configuration, string systemId) =>
        ActiveEmulatorFor(systemId) is { } emulatorId
            ? configuration.GetLocation(systemId, emulatorId)
            : configuration.GetLocation(systemId);

    private string? OverrideFor(CloudSaveSyncSettings configuration, string systemId) =>
        ActiveEmulatorFor(systemId) is { } emulatorId
            ? configuration.GetOverride(systemId, emulatorId)
            : configuration.GetOverride(systemId);

    private string? StateOverrideFor(CloudSaveSyncSettings configuration, string systemId) =>
        ActiveEmulatorFor(systemId) is { } emulatorId
            ? configuration.GetStateOverride(systemId, emulatorId)
            : configuration.GetStateOverride(systemId);

    // Writes go to the active emulator's (system, emulator) entry and are mirrored onto the bare
    // system-id key (and, for the two legacy systems, their fields) so an older EmuShelf build can
    // still read the active emulator's choice after a downgrade.
    private CloudSaveSyncSettings WithOverrideFor(CloudSaveSyncSettings configuration, string systemId, string? directory) =>
        ActiveEmulatorFor(systemId) is { } emulatorId
            ? configuration.WithOverride(systemId, emulatorId, directory).WithOverride(systemId, directory)
            : configuration.WithOverride(systemId, directory);

    private CloudSaveSyncSettings WithStateOverrideFor(CloudSaveSyncSettings configuration, string systemId, string? directory) =>
        ActiveEmulatorFor(systemId) is { } emulatorId
            ? configuration.WithStateOverride(systemId, emulatorId, directory).WithStateOverride(systemId, directory)
            : configuration.WithStateOverride(systemId, directory);

    private CloudSaveSyncSettings WithOptionalContentFor(CloudSaveSyncSettings configuration, string systemId, bool syncSaveStates) =>
        ActiveEmulatorFor(systemId) is { } emulatorId
            ? configuration.WithOptionalContent(systemId, emulatorId, syncSaveStates).WithOptionalContent(systemId, syncSaveStates)
            : configuration.WithOptionalContent(systemId, syncSaveStates);

    private CloudSaveSyncSettings WithSyncSuccessFor(
        CloudSaveSyncSettings configuration, string systemId, DateTimeOffset completedUtc, string? notice) =>
        ActiveEmulatorFor(systemId) is { } emulatorId
            ? configuration.WithSyncSuccess(systemId, emulatorId, completedUtc, notice).WithSyncSuccess(systemId, completedUtc, notice)
            : configuration.WithSyncSuccess(systemId, completedUtc, notice);

    private CloudSaveSyncSettings WithSyncFailureFor(CloudSaveSyncSettings configuration, string systemId, string message) =>
        ActiveEmulatorFor(systemId) is { } emulatorId
            ? configuration.WithSyncFailure(systemId, emulatorId, message).WithSyncFailure(systemId, message)
            : configuration.WithSyncFailure(systemId, message);

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
                    ? WithSyncSuccessFor(configuration, systemId, completedUtc, DescribeSkipped(systemId, report))
                    : WithSyncFailureFor(configuration, systemId, error);
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
        // Battery saves are namespaced by system id and save states by the emulator, so this platform
        // owns two prefixes; match either. Build the provider once for the whole report rather than
        // once per skipped unit.
        if (report is null || CreateBaseProvider(systemId) is not { } provider)
            return null;

        var skipped = report.Skipped
            .Where(result =>
                result.UnitId.StartsWith(provider.UnitIdPrefix, StringComparison.Ordinal) ||
                result.UnitId.StartsWith(provider.StateNamespacePrefix, StringComparison.Ordinal))
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
    /// <summary>The account was connected (or reused) and the connection was saved.</summary>
    Connected,

    /// <summary>Required input (folder, or any usable save platform) was missing.</summary>
    InvalidInput,

    /// <summary>The connection attempt failed (e.g. the network or the Drive API was unreachable).</summary>
    Failed,

    /// <summary>
    /// The Google sign-in was declined, closed, or answered with a response that did not belong to
    /// this request. Distinct from <see cref="Failed"/> because nothing is wrong with the setup —
    /// the user simply did not finish, and the fix is to try again rather than to check anything.
    /// </summary>
    SignInDeclined,

    /// <summary>
    /// This build ships no Google OAuth client, so the built-in transport cannot be offered at all.
    /// Distinct from <see cref="Failed"/> because nothing the user does will change it — it is a
    /// property of how the binary was built, not of their setup or their network.
    /// </summary>
    ManagedTransportUnavailable,
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
    string? StateOverride = null,
    string? SaveStatesLabel = null);

/// <summary>
/// The cloud save-sync operations the Settings view model drives, wrapped as delegates so the view
/// model stays testable with a fake context. The delegate set is platform-agnostic: every operation
/// takes a system id, so adding a platform does not change this shape.
/// </summary>
public sealed record CloudSaveSyncSettingsContext(
    CloudSaveSyncSettings Current,
    string SyncLogPath,
    Func<IReadOnlyList<CloudSaveSyncPlatformContext>> GetPlatforms,
    Func<string, CancellationToken, Task<string?>> GetDetectedPathAsync,
    Func<CancellationToken, Task> DisconnectAsync,
    Func<IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>> SyncNowAsync,
    Func<string, SaveSyncDirection, IProgress<SaveSyncProgress>?, CancellationToken, Task<CloudSaveSyncOutcome>> ForceAsync,
    Action<string, string?> UpdateOverride,
    Func<string, CancellationToken, Task<SaveProviderDetection?>>? GetDetectionAsync = null,
    Action<string, bool>? UpdateOptionalContent = null,
    Action<IReadOnlyDictionary<string, string?>>? UpdateOverrides = null,
    Action<string, string?>? UpdateStateOverride = null,
    /// <summary>Whether this build ships an OAuth client, and so can offer cloud sync at all.</summary>
    bool IsManagedTransportAvailable = false,
    /// <summary>
    /// Connects Google Drive with the built-in client, taking the browser launcher as its third
    /// argument. Null in a test context that does not exercise it.
    /// </summary>
    Func<string, IReadOnlyDictionary<string, string?>, Action<Uri>, CancellationToken, Task<CloudSaveSyncConnectResult>>?
        ConnectGoogleDriveManagedAsync = null,
    /// <summary>
    /// Reads one system's platform context against a specific emulator's saved override, so the
    /// Saves row can follow the emulator picker before the switch is saved. Null in a test context
    /// that does not exercise it.
    /// </summary>
    Func<string, string, CloudSaveSyncPlatformContext?>? DescribePlatformForEmulator = null,
    /// <summary>
    /// Exports every platform's saves into a portable <c>.zip</c> at the given path, optionally
    /// including cloud-only saves. Null in a test context that does not exercise it.
    /// </summary>
    Func<string, SaveExportScope, IProgress<SaveTransferProgress>?, CancellationToken, Task<SaveExportResult>>?
        ExportSavesAsync = null);
