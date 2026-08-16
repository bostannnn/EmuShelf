using System.Text;
using EmuShelf.Core.Diagnostics;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>
/// Copy-only Google Drive transport, speaking the Drive API directly. Writes exactly the layout
/// <see cref="CloudSaveIndex"/> defines — one <c>index.json</c> beside nested
/// <c>&lt;unitId&gt;.payload</c> blobs — which is the on-remote wire format save sync depends on.
/// </summary>
/// <remarks>
/// The commit discipline is the part that was hard to get right: payloads are uploaded first and the
/// index second, in batches, so an interrupted pass keeps the batches it landed and can never leave
/// the index promising a payload that is not there. See DECISIONS 2026-07-24.
/// </remarks>
public sealed class GoogleDriveCloudSyncTransport : IVerifiableCloudSyncTransport
{
    private const int MaxUnitsPerBatch = 64;
    private const long MaxBytesPerBatch = 32L * 1024 * 1024;

    private readonly List<string> _timings = [];

    private readonly GoogleDriveApiClient _api;
    private readonly IAppLogger _logger;
    private readonly string _cloudFolder;
    private readonly string _transfersDirectory;

    private string? _rootFolderId;
    private string? _outbox;
    private bool _remoteIndexExists;
    private bool _treeLoaded;

    // False only while the root id came from the caller's cache and has not yet been shown to be the
    // folder the configured path names. An id this transport resolved itself is verified by
    // construction. See RecoverStaleFolderIdAsync.
    private bool _cachedFolderIdVerified;

    /// <summary>Remote-relative folder path to Drive folder id, for every folder found or created.</summary>
    private readonly Dictionary<string, string> _folderIds = new(StringComparer.Ordinal);

    /// <summary>Remote-relative file path to Drive file id, so a replace updates in place.</summary>
    private readonly Dictionary<string, string> _fileIds = new(StringComparer.Ordinal);

    private readonly HashSet<string> _missingPayloads = new(StringComparer.Ordinal);
    private Dictionary<string, SaveUnitSnapshot> _remoteIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SaveUnitSnapshot> _pendingIndex = new(StringComparer.Ordinal);

    public GoogleDriveCloudSyncTransport(
        GoogleDriveApiClient api,
        IAppPaths appPaths,
        string cloudFolder,
        IAppLogger? logger = null,
        string? cloudFolderId = null)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        ArgumentNullException.ThrowIfNull(appPaths);
        ValidateCloudFolder(cloudFolder);
        _logger = logger ?? NullAppLogger.Instance;
        _cloudFolder = (cloudFolder ?? string.Empty).Trim().Trim('/').Replace('\\', '/');
        _transfersDirectory = Path.Combine(appPaths.SavesDirectory, "transfers");
        _rootFolderId = string.IsNullOrWhiteSpace(cloudFolderId) ? null : cloudFolderId.Trim();
        // Nothing was handed in, so nothing is unverified.
        _cachedFolderIdVerified = _rootFolderId is null;
    }

    /// <summary>
    /// The saves folder's Drive id once resolved. Cached in settings by the caller so later syncs do
    /// not walk the account root one segment at a time on every pass.
    /// </summary>
    public string? CloudFolderId => _rootFolderId;

    /// <inheritdoc />
    public IReadOnlyList<string> Timings => _timings;

    /// <summary>
    /// Times one phase into <see cref="Timings"/>. Recorded per phase rather than per HTTP call: a
    /// single sync makes hundreds of Drive requests, and a log line for each would bury the one
    /// number a slow pass is actually explained by.
    /// </summary>
    private async Task<T> TimedAsync<T>(string label, Func<Task<T>> operation)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            return await operation();
        }
        finally
        {
            elapsed.Stop();
            _timings.Add($"drive {label} — {elapsed.ElapsedMilliseconds} ms");
        }
    }

    private async Task TimedAsync(string label, Func<Task> operation) =>
        await TimedAsync<object?>(label, async () =>
        {
            await operation();
            return null;
        });

    public Task<IReadOnlyList<SaveUnitSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
        TimedAsync("list", () => ListCoreAsync(cancellationToken));

    private async Task<IReadOnlyList<SaveUnitSnapshot>> ListCoreAsync(CancellationToken cancellationToken)
    {
        var rootId = await ResolveRootAsync(create: false, cancellationToken);
        if (rootId is null)
        {
            // The folder does not exist yet. That is the state before the first upload, not a
            // failure — but it must not look that way once an index has been read this session.
            if (_remoteIndexExists)
                throw new IOException("The cloud folder disappeared during this save-sync session.");
            _remoteIndex = new Dictionary<string, SaveUnitSnapshot>(StringComparer.Ordinal);
            return [];
        }

        var index = await _api.FindChildAsync(rootId, CloudSaveIndex.FileName, isFolder: false, cancellationToken);
        if (index is null && await RecoverStaleFolderIdAsync(cancellationToken) is { } recoveredId)
        {
            rootId = recoveredId;
            index = await _api.FindChildAsync(rootId, CloudSaveIndex.FileName, isFolder: false, cancellationToken);
        }

        if (index is null)
        {
            if (_remoteIndexExists)
                throw new IOException("The cloud index disappeared during this save-sync session.");
            _remoteIndex = new Dictionary<string, SaveUnitSnapshot>(StringComparer.Ordinal);
            return [];
        }

        // Reaching an index under this id is the proof the id is the right folder.
        _cachedFolderIdVerified = true;
        _fileIds[CloudSaveIndex.FileName] = index.Id;
        await using var content = await _api.DownloadAsync(index.Id, cancellationToken) ??
            throw new IOException("Google Drive listed the cloud index but would not return it.");

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);

        _remoteIndex = CloudSaveIndex.Parse(buffer.ToArray());
        _remoteIndexExists = true;
        return _remoteIndex.Values.ToList();
    }

    /// <summary>
    /// Creates the saves folder if it is not already there, and adopts its id. Called at connect time
    /// so a connection that reports success has actually proved it can write to the account, rather
    /// than deferring the first real failure to whenever the user next launches a game.
    /// </summary>
    public Task EnsureCloudFolderAsync(CancellationToken cancellationToken = default) =>
        TimedAsync("ensure folder", async () =>
        {
            _ = await ResolveRootAsync(create: true, cancellationToken) ??
                throw new IOException("EmuShelf could not create the cloud saves folder.");
        });

    /// <summary>
    /// Warms the folder tree before the first download. Drive charges per request and resolves no
    /// paths of its own, so walking the tree once beats resolving each unit's folder chain on demand.
    /// </summary>
    public void ExpectDownloads(IEnumerable<string> unitIds)
    {
        ArgumentNullException.ThrowIfNull(unitIds);
        // Nothing per-unit is recorded: this transport scopes a session by walking the saves folder
        // once and caching every id, which serves any announced unit and any unannounced one equally.
        // The interface permits ignoring the hint, and there is nothing cheaper to do with it here.
    }

    public async Task<Stream> DownloadAsync(string unitId, CancellationToken cancellationToken = default)
    {
        CloudSaveIndex.ValidateUnitId(unitId);

        var payloadPath = CloudSaveIndex.PayloadName(unitId);
        await EnsureTreeLoadedAsync(cancellationToken);

        if (!_fileIds.TryGetValue(payloadPath, out var fileId))
        {
            _missingPayloads.Add(unitId);
            throw new CloudPayloadMissingException(unitId);
        }

        var content = await _api.DownloadAsync(fileId, cancellationToken);
        if (content is null)
        {
            // Listed a moment ago and gone now: the index entry outlived its blob. Recorded so the
            // next flush prunes it and the machine that still holds the save uploads it again.
            _missingPayloads.Add(unitId);
            throw new CloudPayloadMissingException(unitId);
        }

        return content;
    }

    public async Task UploadAsync(
        string unitId,
        Stream content,
        string contentHash,
        DateTimeOffset modifiedUtc,
        CancellationToken cancellationToken = default,
        string? compatibility = null)
    {
        CloudSaveIndex.ValidateUnitId(unitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentNullException.ThrowIfNull(content);

        // Staged to disk rather than held in memory: a single unit has measured at 179 MB, and the
        // upload has to be re-readable anyway for a retried request to re-send it from the start.
        _outbox ??= CreateStagingDirectory("outbox");
        var stagedPath = Path.Combine(_outbox, StageRelativePath(CloudSaveIndex.PayloadName(unitId)));
        Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);

        await using (var staged = new FileStream(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(staged, 81920, cancellationToken);
        }

        _pendingIndex[unitId] = new SaveUnitSnapshot(unitId, contentHash, modifiedUtc, compatibility);
    }

    public Task FlushAsync(
        IProgress<SaveTransferProgress>? transferProgress = null,
        CancellationToken cancellationToken = default) =>
        TimedAsync("flush", () => FlushCoreAsync(transferProgress, cancellationToken));

    private async Task FlushCoreAsync(
        IProgress<SaveTransferProgress>? transferProgress,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_outbox is null && _missingPayloads.Count == 0)
                return;

            var index = new Dictionary<string, SaveUnitSnapshot>(_remoteIndex, StringComparer.Ordinal);
            foreach (var unitId in _missingPayloads)
            {
                if (!_pendingIndex.ContainsKey(unitId))
                    index.Remove(unitId);
            }

            if (_pendingIndex.Count == 0)
            {
                if (_missingPayloads.Count > 0)
                {
                    await CommitIndexAsync(index, cancellationToken);
                    _remoteIndex = index;
                    _missingPayloads.Clear();
                }

                return;
            }

            // Learn what the remote already holds before writing anything. Drive addresses files by
            // id and happily accepts a second file with the same name in the same folder, so an
            // upload that cannot see the existing blob creates a duplicate rather than replacing it —
            // and which of the two a later sync reads then depends on listing order. A pass that only
            // uploads (the common repeat sync, where local is simply newer) never downloads, so
            // nothing else would have loaded the tree.
            await EnsureTreeLoadedAsync(cancellationToken);

            var totalUnits = _pendingIndex.Count;
            var unitsCommitted = 0;
            foreach (var batch in PlanUploadBatches())
            {
                cancellationToken.ThrowIfCancellationRequested();
                transferProgress?.Report(new SaveTransferProgress(
                    unitsCommitted, totalUnits, OverallPercent(unitsCommitted, totalUnits)));

                var landed = unitsCommitted;
                foreach (var unitId in batch)
                {
                    await UploadStagedUnitAsync(unitId, cancellationToken);
                    index[unitId] = _pendingIndex[unitId];

                    // Counted as each unit lands rather than once per batch, so the number moves
                    // during a batch instead of parking on a multiple of the batch size.
                    landed++;
                    transferProgress?.Report(new SaveTransferProgress(
                        Math.Min(landed, totalUnits), totalUnits, OverallPercent(landed, totalUnits)));
                }

                // The index is the commit, so it always follows the payloads it describes.
                await CommitIndexAsync(index, cancellationToken);
                unitsCommitted += batch.Count;
                _remoteIndex = new Dictionary<string, SaveUnitSnapshot>(index, StringComparer.Ordinal);
                _missingPayloads.Clear();
                transferProgress?.Report(new SaveTransferProgress(
                    unitsCommitted, totalUnits, OverallPercent(unitsCommitted, totalUnits)));
            }
        }
        finally
        {
            _pendingIndex.Clear();
            if (_outbox is not null)
            {
                TryDeleteDirectory(_outbox);
                _outbox = null;
            }
        }
    }

    /// <summary>
    /// Lists what the remote actually holds and reports the indexed units whose payload is not there,
    /// so the next flush rewrites the index without them.
    /// </summary>
    public Task<IReadOnlyList<string>> FindMissingPayloadsAsync(CancellationToken cancellationToken = default) =>
        TimedAsync("verify", () => FindMissingPayloadsCoreAsync(cancellationToken));

    private async Task<IReadOnlyList<string>> FindMissingPayloadsCoreAsync(CancellationToken cancellationToken)
    {
        var rootId = await ResolveRootAsync(create: false, cancellationToken);
        if (rootId is null)
        {
            if (_remoteIndexExists)
                throw new IOException("The cloud folder disappeared during save verification.");
            return [];
        }

        await EnsureTreeLoadedAsync(cancellationToken);
        if (_remoteIndexExists && !_fileIds.ContainsKey(CloudSaveIndex.FileName))
        {
            // An index was read this session but the listing cannot see it, so the listing is not
            // authoritative evidence that anything is missing.
            throw new IOException("Google Drive could not verify the cloud index while listing save payloads.");
        }

        var missing = _remoteIndex.Keys
            .Where(unitId => !_fileIds.ContainsKey(CloudSaveIndex.PayloadName(unitId)))
            .OrderBy(unitId => unitId, StringComparer.Ordinal)
            .ToList();
        foreach (var unitId in missing)
            _missingPayloads.Add(unitId);
        return missing;
    }

    private async Task UploadStagedUnitAsync(string unitId, CancellationToken cancellationToken)
    {
        var payloadPath = CloudSaveIndex.PayloadName(unitId);
        var stagedPath = Path.Combine(_outbox!, StageRelativePath(payloadPath));
        var parentPath = ParentPath(payloadPath);
        var parentId = await EnsureFolderAsync(parentPath, cancellationToken);

        await using var staged = new FileStream(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        _fileIds.TryGetValue(payloadPath, out var existingId);
        var fileId = await _api.UploadAsync(
            parentId,
            LeafName(payloadPath),
            existingId,
            staged,
            null,
            cancellationToken);
        _fileIds[payloadPath] = fileId;
    }

    private async Task CommitIndexAsync(
        IReadOnlyDictionary<string, SaveUnitSnapshot> index,
        CancellationToken cancellationToken)
    {
        var rootId = await ResolveRootAsync(create: true, cancellationToken) ??
            throw new IOException("EmuShelf could not create the cloud saves folder.");

        var payload = Encoding.UTF8.GetBytes(CloudSaveIndex.Serialize(index.Values));
        _fileIds.TryGetValue(CloudSaveIndex.FileName, out var existingId);

        using var content = new MemoryStream(payload, writable: false);
        var fileId = await _api.UploadAsync(
            rootId,
            CloudSaveIndex.FileName,
            existingId,
            content,
            null,
            cancellationToken);

        _fileIds[CloudSaveIndex.FileName] = fileId;
        _remoteIndexExists = true;
    }

    /// <summary>
    /// Walks the saves folder once and caches every folder and file id under it. Drive has no paths,
    /// so without this each unit would cost one listing per path segment on every call.
    /// </summary>
    private Task EnsureTreeLoadedAsync(CancellationToken cancellationToken) =>
        _treeLoaded ? Task.CompletedTask : TimedAsync("walk", () => LoadTreeAsync(cancellationToken));

    private async Task LoadTreeAsync(CancellationToken cancellationToken)
    {
        if (_treeLoaded)
            return;

        var rootId = await ResolveRootAsync(create: false, cancellationToken);
        if (rootId is null)
        {
            _treeLoaded = true;
            return;
        }

        var pending = new Queue<(string Path, string Id)>();
        pending.Enqueue((string.Empty, rootId));
        while (pending.Count > 0)
        {
            var (path, id) = pending.Dequeue();
            // Order children the way FindChildAsync resolves a duplicate name — oldest first, then by
            // id — so the walk's "first writer wins" is genuinely oldest-wins rather than whatever
            // order Drive listed them in. Without this, two machines could cache different ids for the
            // same duplicated name and never converge, since the transport never deletes the extra.
            var children = (await _api.ListChildrenAsync(id, cancellationToken))
                .OrderBy(child => child.ModifiedTime ?? DateTimeOffset.MaxValue)
                .ThenBy(child => child.Id, StringComparer.Ordinal);
            foreach (var child in children)
            {
                var childPath = path.Length == 0 ? child.Name : path + "/" + child.Name;
                if (child.IsFolder)
                {
                    // The oldest same-named folder is the deterministic *write* target (path
                    // resolution reads _folderIds), but ALL same-named folders are descended: the
                    // transport never deletes, so two machines' concurrent first-writes can leave two
                    // provider folders each holding *different* units, and a unit that only ever landed
                    // in the newer folder must still be discoverable. Merging their contents here (with
                    // the oldest-wins leaf tiebreak below) is what keeps a blob from being orphaned and
                    // then pruned from the index.
                    _folderIds.TryAdd(childPath, child.Id);
                    pending.Enqueue((childPath, child.Id));
                }
                else
                {
                    // Oldest blob wins on a duplicate name, matching FindChildAsync, so the two never
                    // disagree about which blob a unit means.
                    _fileIds.TryAdd(childPath, child.Id);
                }
            }
        }

        _treeLoaded = true;
    }

    /// <summary>
    /// Re-resolves the saves folder by path when a cached id turned up no index, returning the
    /// corrected id, or null when there was nothing to correct.
    /// </summary>
    /// <remarks>
    /// The failure this exists for is silent. A folder id cached in settings can stop being valid —
    /// the folder was deleted, or it belongs to a folder created under some other app's full-Drive
    /// access and is therefore invisible to this client's per-file access — and Drive answers a listing
    /// for a parent that is not there with an <em>empty list</em>, not an error. Believing that would report
    /// an empty cloud, re-upload everything, and never reconcile with the machine whose saves are
    /// sitting in the real folder. Nothing would be destroyed and nothing would look wrong.
    ///
    /// So a cached id that yields no index is not trusted until it has been checked against the path.
    /// Costs one extra listing, only when there is no index to find, and at most once per session.
    /// </remarks>
    private async Task<string?> RecoverStaleFolderIdAsync(CancellationToken cancellationToken)
    {
        if (_cachedFolderIdVerified || _rootFolderId is null || string.IsNullOrEmpty(_cloudFolder))
            return null;

        // Whatever the path resolves to now is the truth; mark it checked either way so this runs once.
        _cachedFolderIdVerified = true;
        var staleId = _rootFolderId;
        _rootFolderId = null;

        var resolved = await ResolveRootAsync(create: false, cancellationToken);
        if (resolved is null)
        {
            // The folder genuinely is not there. Restore the id rather than inventing a new state:
            // the caller correctly reports an empty remote, and a later upload creates the folder.
            _rootFolderId = staleId;
            return null;
        }

        if (string.Equals(resolved, staleId, StringComparison.Ordinal))
            return null;

        _logger.Warning(
            "The cached cloud folder id did not match the saves folder and has been re-resolved by path.");
        // Anything cached against the wrong folder is meaningless now.
        _folderIds.Clear();
        _fileIds.Clear();
        _treeLoaded = false;
        return resolved;
    }

    private async Task<string?> ResolveRootAsync(bool create, CancellationToken cancellationToken)
    {
        if (_rootFolderId is not null)
            return _rootFolderId;

        if (string.IsNullOrEmpty(_cloudFolder))
        {
            _rootFolderId = GoogleDriveApiClient.RootFolderAlias;
            return _rootFolderId;
        }

        var resolved = await _api.ResolveFolderPathAsync(
            GoogleDriveApiClient.RootFolderAlias,
            _cloudFolder,
            create,
            cancellationToken);
        if (resolved is not null)
        {
            _rootFolderId = resolved;
            _folderIds[string.Empty] = resolved;
        }

        return resolved;
    }

    private async Task<string> EnsureFolderAsync(string relativePath, CancellationToken cancellationToken)
    {
        var rootId = await ResolveRootAsync(create: true, cancellationToken) ??
            throw new IOException("EmuShelf could not create the cloud saves folder.");
        if (relativePath.Length == 0)
            return rootId;

        if (_folderIds.TryGetValue(relativePath, out var cached))
            return cached;

        var folderId = await _api.ResolveFolderPathAsync(rootId, relativePath, create: true, cancellationToken) ??
            throw new IOException($"EmuShelf could not create the cloud folder '{relativePath}'.");
        _folderIds[relativePath] = folderId;
        return folderId;
    }

    /// <summary>
    /// Groups staged units into commit batches, bounded by both count and size, because either bound
    /// alone leaves a bad case: a count bound lets one batch carry hundreds of megabytes, a size
    /// bound lets one carry thousands of tiny files.
    /// </summary>
    private List<List<string>> PlanUploadBatches()
    {
        var batches = new List<List<string>>();
        var current = new List<string>();
        var currentBytes = 0L;
        foreach (var unitId in _pendingIndex.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (current.Count >= MaxUnitsPerBatch || (current.Count > 0 && currentBytes >= MaxBytesPerBatch))
            {
                batches.Add(current);
                current = [];
                currentBytes = 0;
            }

            current.Add(unitId);
            currentBytes += StagedSize(unitId);
        }

        if (current.Count > 0)
            batches.Add(current);
        return batches;
    }

    private long StagedSize(string unitId)
    {
        try
        {
            var path = Path.Combine(_outbox!, StageRelativePath(CloudSaveIndex.PayloadName(unitId)));
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static int OverallPercent(double completedUnits, int totalUnits) =>
        totalUnits <= 0 ? 100 : Math.Clamp((int)(100 * completedUnits / totalUnits), 0, 100);

    private string CreateStagingDirectory(string prefix)
    {
        Directory.CreateDirectory(_transfersDirectory);
        var directory = Path.Combine(_transfersDirectory, prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string StageRelativePath(string remoteRelativePath) =>
        remoteRelativePath.Replace('/', Path.DirectorySeparatorChar);

    internal static string ParentPath(string remoteRelativePath)
    {
        var separator = remoteRelativePath.LastIndexOf('/');
        return separator < 0 ? string.Empty : remoteRelativePath[..separator];
    }

    internal static string LeafName(string remoteRelativePath)
    {
        var separator = remoteRelativePath.LastIndexOf('/');
        return separator < 0 ? remoteRelativePath : remoteRelativePath[(separator + 1)..];
    }

    private static void ValidateCloudFolder(string? cloudFolder)
    {
        if ((cloudFolder ?? string.Empty).Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("The cloud folder cannot contain traversal segments.", nameof(cloudFolder));
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Could not clean up the save-sync staging directory: {ex.Message}");
        }
    }
}
