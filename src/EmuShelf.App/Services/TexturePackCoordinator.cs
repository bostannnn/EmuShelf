using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.Library;
using EmuShelf.Core.Metadata;
using EmuShelf.Core.Settings;
using EmuShelf.Core.Storage;
using EmuShelf.Core.TexturePacks;

namespace EmuShelf.App.Services;

/// <summary>One platform's inventory as Settings presents it.</summary>
/// <param name="SystemId">The library system id this row covers.</param>
/// <param name="DisplayName">The emulator name.</param>
/// <param name="DetectedRoot">The resolved texture folder, or null when none was resolved.</param>
/// <param name="IsOverridden">Whether <paramref name="DetectedRoot"/> came from the user.</param>
/// <param name="RootStatus">Whether that folder was readable during the last scan.</param>
/// <param name="IsStale">Whether the shown entries come from cache because the root was unavailable.</param>
/// <param name="Loading">Whether this installation would load replacement textures at all.</param>
/// <param name="Diagnostic">Why a root or a scan did not succeed, when it did not.</param>
public sealed record TexturePackPlatformState(
    string SystemId,
    string DisplayName,
    string? DetectedRoot,
    bool IsOverridden,
    TexturePackRootStatus RootStatus,
    bool IsStale,
    TexturePackLoadingStatus Loading,
    string? Diagnostic);

/// <summary>The result of one complete inventory pass.</summary>
/// <param name="Map">Classified packs plus the per-game matches driving the library marks.</param>
/// <param name="Platforms">Per-platform state for the Settings section.</param>
public sealed record TexturePackInventoryResult(
    TexturePackLibraryMap Map,
    IReadOnlyList<TexturePackPlatformState> Platforms)
{
    public static TexturePackInventoryResult Empty { get; } = new(TexturePackLibraryMap.Empty, []);
}

/// <summary>
/// The texture-pack operations the Settings view model drives, wrapped as delegates so the view
/// model stays testable with a fake context. Note what is absent: there is no install, repair,
/// move, rename, or delete operation, because EmuShelf never performs one.
/// </summary>
public sealed record TexturePackSettingsContext(
    Func<TexturePackInventoryResult> GetInventory,
    Func<bool> HasScanned,
    Func<CancellationToken, Task<TexturePackInventoryResult>> RescanAsync,
    Action<string, string?> UpdateOverride,
    IReadOnlyDictionary<string, string> OverridePlaceholders,
    Func<IReadOnlyDictionary<long, string>> GetGameTitles);

/// <summary>
/// Composes the texture-pack pipeline (registry-provided resolvers + sources + portable cache +
/// pure matcher) and runs it under a single-flight gate so a startup load and an explicit Rescan
/// can never overlap.
///
/// Platform knowledge lives in <see cref="TexturePackProviderRegistry"/>, not here: this type never
/// names an emulator. Everything it does is read-only — it opens directories and configuration
/// files, and writes only to EmuShelf's own portable inventory cache.
/// </summary>
public sealed class TexturePackCoordinator
{
    private readonly IAppPaths _paths;
    private readonly IGameMetadataStore _metadataStore;
    private readonly TexturePackInventoryService _inventory;
    private readonly IAppLogger _logger;
    private readonly Func<string, SaveEmulatorInstallation?>? _emulatorInstallations;
    private readonly ISettingsService? _settingsService;
    private readonly Func<string, IReadOnlyList<Game>>? _gamesForSystem;
    private readonly IReadOnlyDictionary<string, IGameIdentifierExtractor> _identifierExtractors;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _settings;

    public TexturePackCoordinator(
        IAppPaths paths,
        IGameMetadataStore metadataStore,
        AppSettings settings,
        IAppLogger logger,
        ITexturePackInventoryStore? store = null,
        Func<string, SaveEmulatorInstallation?>? emulatorInstallations = null,
        ISettingsService? settingsService = null,
        Func<string, IReadOnlyList<Game>>? gamesForSystem = null,
        IReadOnlyList<MetadataSystemProfile>? metadataProfiles = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(metadataStore);
        _paths = paths;
        _metadataStore = metadataStore;
        _settings = settings;
        _logger = logger;
        _inventory = new TexturePackInventoryService(store ?? new Infrastructure.TexturePacks.TexturePackInventoryCache(paths));
        _emulatorInstallations = emulatorInstallations;
        _settingsService = settingsService;
        _gamesForSystem = gamesForSystem;
        _identifierExtractors = (metadataProfiles ?? [])
            .Where(profile => TexturePackProviderRegistry.Find(profile.SystemId) is not null)
            .ToDictionary(
                profile => profile.SystemId,
                profile => profile.IdentifierExtractor,
                StringComparer.Ordinal);
    }

    /// <summary>The most recent completed pass, or an empty result before the first one.</summary>
    public TexturePackInventoryResult Current { get; private set; } = TexturePackInventoryResult.Empty;

    /// <summary>
    /// Whether a real inventory has been produced, so the views can tell "no packs" from "not
    /// scanned yet". A cache-only load that found nothing cached does not count: nothing was
    /// examined, so claiming a game has no pack would be a statement we cannot support.
    /// </summary>
    public bool HasScanned { get; private set; }

    public TexturePackSettings Settings => _settings.TexturePacks;

    /// <summary>Applies a changed settings snapshot; the next pass picks up new overrides.</summary>
    public void UpdateSettings(AppSettings settings) => _settings = settings;

    /// <summary>
    /// Replaces one platform's texture-root override and persists it, so a folder the user chose
    /// explicitly survives a restart rather than silently reverting to auto-detection.
    /// </summary>
    public void UpdateOverride(string systemId, string? directory)
    {
        if (_settingsService is null)
        {
            _settings = _settings with { TexturePacks = _settings.TexturePacks.WithOverride(systemId, directory) };
            return;
        }

        _settings = _settingsService.Update(latest => latest with
        {
            TexturePacks = latest.TexturePacks.WithOverride(systemId, directory),
        });
    }

    /// <summary>Bundles the coordinator's operations as a delegate context for the Settings view model.</summary>
    /// <param name="gameTitles">Titles for every library game, not just the visible collection.</param>
    /// <param name="rescanAsync">
    /// Overrides the rescan delegate so the library view can refresh its marks in the same pass.
    /// Defaults to a bare refresh, which updates Settings but leaves already-rendered rows stale.
    /// </param>
    public TexturePackSettingsContext CreateSettingsContext(
        Func<IReadOnlyDictionary<long, string>> gameTitles,
        Func<CancellationToken, Task<TexturePackInventoryResult>>? rescanAsync = null) => new(
        // Delegates, not snapshots: Settings re-reads them after a Rescan so the totals, the
        // per-platform rows, and the pack list cannot disagree within an open session.
        () => Current,
        () => HasScanned,
        rescanAsync ?? RefreshAsync,
        UpdateOverride,
        TexturePackProviderRegistry.All
            .Select(descriptor => (descriptor.SystemId, descriptor.OverridePlaceholder))
            .ToDictionary(pair => pair.SystemId, pair => pair.OverridePlaceholder, StringComparer.Ordinal),
        gameTitles);

    /// <summary>
    /// Reads the cached inventory without touching any texture directory. This is what startup uses:
    /// it is a handful of small JSON reads, so it never turns launching EmuShelf into a recursive
    /// walk of every installed pack.
    /// </summary>
    public Task<TexturePackInventoryResult> LoadCachedAsync(CancellationToken cancellationToken = default) =>
        RunAsync(refresh: false, cancellationToken);

    /// <summary>Rescans every configured installation. Cancellable and off the UI thread.</summary>
    public Task<TexturePackInventoryResult> RefreshAsync(CancellationToken cancellationToken = default) =>
        RunAsync(refresh: true, cancellationToken);

    private async Task<TexturePackInventoryResult> RunAsync(bool refresh, CancellationToken cancellationToken)
    {
        if (!_settings.TexturePacks.Enabled)
        {
            Current = TexturePackInventoryResult.Empty;
            return Current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var snapshots = new List<TexturePackInventorySnapshot>();
            var platforms = new List<TexturePackPlatformState>();
            // One Dolphin installation backs both GameCube and Wii. Scanning it once and reusing the
            // snapshot keeps the pack from being counted twice in the Settings totals.
            var scanned = new Dictionary<string, TexturePackInventoryState>(StringComparer.Ordinal);

            foreach (var descriptor in TexturePackProviderRegistry.All)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = await ScanPlatformAsync(descriptor, refresh, scanned, cancellationToken);
                platforms.Add(state.Platform);
                if (state.Snapshot is { } snapshot && state.IsFirstForInstallation)
                    snapshots.Add(snapshot);
            }

            // An explicit rescan may extract the identifiers it needs. Without this, texture
            // matching would silently depend on the opt-in network-metadata pass having run:
            // GameCube, Wii, PlayStation, and PS2 write no identifiers at import, so every pack
            // for them would sit at "identification pending" forever. The extraction itself is a
            // local header/descriptor read and needs no consent — only the network stage does.
            if (refresh)
                await BackfillIdentifiersAsync(snapshots, cancellationToken);

            var identifiers = await Task.Run(_metadataStore.GetAllIdentifiers, cancellationToken);
            Current = new TexturePackInventoryResult(
                TexturePackLibraryMap.Build(snapshots, identifiers),
                platforms);
            // A cache-only load that found no cached snapshot examined nothing, so it must not
            // flip the views from "not scanned yet" to "no pack for this game".
            HasScanned |= refresh || snapshots.Count > 0;
            return Current;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Extracts identifiers for games whose system has packs installed but that carry none yet.
    /// Scoped deliberately narrowly: only systems that actually produced a snapshot, only games
    /// with no identifiers at all, and only on an explicit rescan — so this never becomes a
    /// startup cost and never re-reads a disc whose evidence is already stored.
    /// </summary>
    private async Task BackfillIdentifiersAsync(
        IReadOnlyList<TexturePackInventorySnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        if (_gamesForSystem is null || _identifierExtractors.Count == 0 || snapshots.Count == 0)
            return;

        // Only bother with systems whose installation actually holds a usable pack; reading discs
        // for a platform with an empty texture folder would be work with no possible payoff.
        var systemIds = TexturePackProviderRegistry.All
            .Where(descriptor => _identifierExtractors.ContainsKey(descriptor.SystemId))
            .Where(descriptor => snapshots.Any(snapshot =>
                string.Equals(snapshot.EmulatorId, descriptor.EmulatorId, StringComparison.Ordinal) &&
                snapshot.Entries.Any(entry => entry.IsUsable)))
            .Select(descriptor => descriptor.SystemId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (systemIds.Length == 0)
            return;

        try
        {
            var existing = await Task.Run(_metadataStore.GetAllIdentifiers, cancellationToken);
            await Task.Run(
                () =>
                {
                    foreach (var systemId in systemIds)
                    {
                        var extractor = _identifierExtractors[systemId];
                        foreach (var game in _gamesForSystem(systemId))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (existing.TryGetValue(game.Id, out var stored) && stored.Count > 0)
                                continue;

                            try
                            {
                                var identifiers = extractor.Extract(game);
                                if (identifiers.Count > 0)
                                    _metadataStore.ReplaceIdentifiers(game.Id, identifiers);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                // One unreadable game must not abort the whole pass.
                                _logger?.Warning(
                                    $"Could not read texture-match evidence from '{game.Path}': {ex.Message}");
                            }
                        }
                    }
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Matching simply stays pending if this fails; it must never break the scan.
            _logger?.Warning($"Texture-match identifier backfill failed: {ex.Message}");
        }
    }

    private async Task<(TexturePackPlatformState Platform, TexturePackInventorySnapshot? Snapshot, bool IsFirstForInstallation)>
        ScanPlatformAsync(
            TextureProviderDescriptor descriptor,
            bool refresh,
            Dictionary<string, TexturePackInventoryState> scanned,
            CancellationToken cancellationToken)
    {
        var overridePath = _settings.TexturePacks.GetOverride(descriptor.SystemId);
        var installation = _emulatorInstallations?.Invoke(descriptor.SystemId);
        var provider = descriptor.CreateProvider(new TextureProviderContext(
            overridePath,
            installation?.Directory,
            installation?.IsFlatpak == true,
            _paths));

        if (provider is null)
        {
            return (Unconfigured(descriptor, "No texture-pack-capable emulator is configured for this system."), null, false);
        }

        if (scanned.TryGetValue(provider.InstallationId, out var existing))
        {
            // A second system sharing one installation reports the same state but contributes no
            // second snapshot, so its packs are neither rescanned nor double-counted.
            return (
                Describe(descriptor, existing, overridePath, await ResolveLoadingAsync(provider, cancellationToken)),
                existing.Snapshot,
                false);
        }

        TexturePackRootResolution resolution;
        try
        {
            resolution = await provider.RootResolver.ResolveAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Texture root resolution failed for {descriptor.DisplayName}: {ex.Message}");
            return (Unconfigured(descriptor, ex.Message), null, false);
        }

        if (!resolution.IsResolved || resolution.RootDirectory is null)
            return (Unconfigured(descriptor, resolution.Diagnostic), null, false);

        TexturePackInventoryState state;
        if (refresh)
        {
            state = await _inventory.RefreshAsync(provider.CreateSource(resolution.RootDirectory), cancellationToken);
        }
        else
        {
            var cached = await _inventory.LoadCachedAsync(provider.InstallationId, cancellationToken);
            if (cached is null)
            {
                return (
                    new TexturePackPlatformState(
                        descriptor.SystemId,
                        descriptor.DisplayName,
                        resolution.RootDirectory,
                        overridePath is not null,
                        TexturePackRootStatus.Unknown,
                        IsStale: false,
                        await ResolveLoadingAsync(provider, cancellationToken),
                        "Not scanned yet."),
                    null,
                    false);
            }

            // This IS the last completed scan, not a fallback after a failed one, so it reports
            // that scan's own root status. "Stale" is reserved for a refresh that could not read
            // the folder and fell back to cache — conflating the two made a perfectly good cached
            // inventory read as "Not scanned yet".
            state = new TexturePackInventoryState(cached, IsStale: false, cached.RootStatus);
        }

        scanned[provider.InstallationId] = state;
        return (
            Describe(descriptor, state, overridePath, await ResolveLoadingAsync(provider, cancellationToken)),
            state.Snapshot,
            true);
    }

    private async Task<TexturePackLoadingStatus> ResolveLoadingAsync(
        TexturePackProvider provider,
        CancellationToken cancellationToken)
    {
        if (provider.LoadingResolver is null)
            return TexturePackLoadingStatus.Unknown;

        try
        {
            var resolution = await provider.LoadingResolver.ResolveAsync(null, cancellationToken);
            return resolution.Status;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Texture loading resolution failed for {provider.InstallationId}: {ex.Message}");
            return TexturePackLoadingStatus.Unknown;
        }
    }

    private static TexturePackPlatformState Describe(
        TextureProviderDescriptor descriptor,
        TexturePackInventoryState state,
        string? overridePath,
        TexturePackLoadingStatus loading) =>
        new(
            descriptor.SystemId,
            descriptor.DisplayName,
            state.Snapshot.RootDirectory,
            overridePath is not null,
            state.IsStale ? state.ObservedRootStatus : state.Snapshot.RootStatus,
            state.IsStale,
            loading,
            state.AvailabilityDiagnostic ?? state.Snapshot.Diagnostic);

    private static TexturePackPlatformState Unconfigured(
        TextureProviderDescriptor descriptor,
        string? diagnostic) =>
        new(
            descriptor.SystemId,
            descriptor.DisplayName,
            null,
            IsOverridden: false,
            TexturePackRootStatus.Unknown,
            IsStale: false,
            TexturePackLoadingStatus.Unknown,
            diagnostic);
}
