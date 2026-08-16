using System.Diagnostics;
using System.Text.Json;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>
/// Copy-only rclone transport for save units. Payloads and their EmuShelf hashes are stored as
/// separate remote files so rclone's provider-specific hashes never participate in reconciliation.
/// </summary>
public sealed class RcloneCloudSyncTransport : IVerifiableCloudSyncTransport
{
    // rclone exit code 3 ("directory not found") / 4 ("file not found") are the expected states
    // before the first upload to a fresh remote, and must read as an empty listing, not a failure.
    private const int RcloneDirectoryNotFoundExit = 3;
    private const int RcloneFileNotFoundExit = 4;

    // One index file describes every unit on the remote so listing is a single request. The format
    // itself belongs to CloudSaveIndex, shared with every other transport.
    private const string IndexFileName = CloudSaveIndex.FileName;

    // How much one commit batch may carry. Each batch costs one extra index write, so these trade
    // that fixed overhead against how much work an interrupted pass loses.
    private const int MaxUnitsPerBatch = 64;
    private const long MaxBytesPerBatch = 32L * 1024 * 1024;

    private readonly string _rclonePath;
    private readonly string _configurationPath;
    private readonly string _remoteName;
    private readonly string _cloudFolder;
    private readonly string _transfersDirectory;
    private readonly TimeSpan _metadataTimeout;
    private readonly TimeSpan _transferTimeout;
    private string? _cloudFolderId;
    private readonly HashSet<string> _expectedDownloads = new(StringComparer.Ordinal);
    private readonly HashSet<string> _missingPayloads = new(StringComparer.Ordinal);
    private readonly List<string> _timings = [];
    private string? _outbox;
    private string? _inbox;
    private bool _remoteIndexExists;
    private Dictionary<string, SaveUnitSnapshot> _remoteIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SaveUnitSnapshot> _pendingIndex = new(StringComparer.Ordinal);

    public RcloneCloudSyncTransport(
        IAppPaths appPaths,
        string remoteName,
        string cloudFolder,
        string? rclonePath = null,
        TimeSpan? operationTimeout = null,
        string? cloudFolderId = null)
    {
        _cloudFolderId = string.IsNullOrWhiteSpace(cloudFolderId) || !IsSafeFolderId(cloudFolderId)
            ? null
            : cloudFolderId.Trim();
        ArgumentNullException.ThrowIfNull(appPaths);
        ValidateRemoteName(remoteName);
        ValidateCloudFolder(cloudFolder);
        _rclonePath = RcloneExecutable.Resolve(appPaths, rclonePath);
        _configurationPath = Path.Combine(appPaths.SettingsDirectory, "rclone.conf");
        _remoteName = remoteName;
        _cloudFolder = cloudFolder.Trim().Trim('/').Replace('\\', '/');
        _transfersDirectory = Path.Combine(appPaths.SavesDirectory, "transfers");
        // Two budgets, because one number cannot serve both kinds of call. A metadata call — read
        // the index, list a folder — is a round trip and a long one means a stalled network. A
        // transfer is bounded by how much data there is: this library's first RPCS3 upload was
        // 179 MB, and a two-minute cap on it guaranteed a killed session on any ordinary uplink.
        _metadataTimeout = operationTimeout ?? TimeSpan.FromMinutes(2);
        _transferTimeout = operationTimeout ?? TimeSpan.FromMinutes(30);
    }

    public async Task<IReadOnlyList<SaveUnitSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Read the single index file (one request) rather than a metadata file per save.
        _remoteIndex = await ReadRemoteIndexAsync(cancellationToken);
        return _remoteIndex.Values.ToList();
    }

    private async Task<Dictionary<string, SaveUnitSnapshot>> ReadRemoteIndexAsync(CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var exitCode = await RunAsync(
            ["cat", RemoteRoot.TrimEnd('/') + "/" + IndexFileName],
            buffer,
            cancellationToken,
            throwOnNonZeroExit: false);
        if (exitCode is RcloneDirectoryNotFoundExit or RcloneFileNotFoundExit)
        {
            if (_remoteIndexExists)
                throw new IOException("The cloud index disappeared during this save-sync session.");
            return new Dictionary<string, SaveUnitSnapshot>(StringComparer.Ordinal);
        }
        if (exitCode != 0)
            throw new IOException($"rclone could not read the cloud index (exit code {exitCode}).");

        var index = CloudSaveIndex.Parse(buffer.ToArray());
        _remoteIndexExists = true;
        return index;
    }

    public async Task<Stream> DownloadAsync(string unitId, CancellationToken cancellationToken = default)
    {
        ValidateUnitId(unitId);
        // Downloads for one sync are served from a single rclone-populated cache (one session for
        // the whole remote) rather than a `cat` per unit.
        var inbox = await EnsureInboxAsync(cancellationToken);
        var payloadPath = Path.Combine(inbox, StageRelativePath(CloudSaveIndex.PayloadName(unitId)));
        if (!File.Exists(payloadPath))
        {
            // The session was scoped to the announced units. A download outside that set is still
            // served — one extra call for one payload — so scoping can never lose a save.
            Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
            var exitCode = await RunAsync(
                ["copyto", RemoteRoot.TrimEnd('/') + "/" + CloudSaveIndex.PayloadName(unitId), payloadPath, "--no-traverse"],
                Stream.Null,
                cancellationToken,
                throwOnNonZeroExit: false);
            if (exitCode is RcloneDirectoryNotFoundExit or RcloneFileNotFoundExit)
            {
                // Recorded so the next flush prunes the index entry that promised it.
                _missingPayloads.Add(unitId);
                throw new CloudPayloadMissingException(unitId);
            }
            if (exitCode != 0)
                throw new IOException($"rclone could not download save unit '{unitId}' (exit code {exitCode}).");
            if (!File.Exists(payloadPath))
                throw new IOException($"rclone reported success but did not download save unit '{unitId}'.");
        }

        return new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public async Task UploadAsync(
        string unitId,
        Stream content,
        string contentHash,
        DateTimeOffset modifiedUtc,
        CancellationToken cancellationToken = default,
        string? compatibility = null)
    {
        ValidateUnitId(unitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        // Stage the payload under the outbox and record its index entry; the actual upload — the
        // changed payloads plus a rebuilt index.json — happens once, in FlushAsync, as a single
        // rclone session so the provider's rate limiter is respected.
        _outbox ??= CreateStagingDirectory("outbox");
        var payloadPath = Path.Combine(_outbox, StageRelativePath(CloudSaveIndex.PayloadName(unitId)));
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);

        await using (var payload = new FileStream(payloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(payload, 81920, cancellationToken);
        }

        _pendingIndex[unitId] = new SaveUnitSnapshot(unitId, contentHash, modifiedUtc, compatibility);
    }

    public async Task FlushAsync(
        IProgress<SaveTransferProgress>? transferProgress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_outbox is null && _missingPayloads.Count == 0)
                return;

            var index = new Dictionary<string, SaveUnitSnapshot>(_remoteIndex, StringComparer.Ordinal);

            // Entries whose payload was found to be missing are dropped, so the machine that still
            // has the save stops seeing "already on the remote" and uploads it on its next pass.
            // Applied to the first commit rather than the last: a later batch that fails must not
            // take the pruning down with it.
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
                    // Adopted, not left behind: the pruned entries are gone from the remote, and a
                    // caller that reuses this transport without listing again must not still be
                    // told they are there.
                    _remoteIndex = index;
                    _missingPayloads.Clear();
                }
                return;
            }

            // Payloads first, index second, and never in one rclone session. rclone transfers
            // concurrently, so a failure could otherwise leave index.json describing a payload that
            // never arrived — and because the index carries the content hash, the machine that owns
            // that save then sees "unchanged" and never re-uploads it, while every other machine
            // fails trying to download it. The index is the commit, so it always follows the
            // payloads it describes.
            //
            // Committed in batches rather than once at the end. A single terminal commit makes the
            // whole pass all-or-nothing: on a provider that bills per file, a few hundred saves take
            // long enough that an interrupted run was the normal case, and every interrupted run
            // threw away all of its uploads and re-staged them from scratch on the next attempt. A
            // batch that lands stays landed.
            var batches = PlanUploadBatches();
            var totalUnits = _pendingIndex.Count;
            var unitsCommitted = 0;
            foreach (var batch in batches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Progress is anchored to units, not bytes. Bytes are what rclone reports, but they
                // describe this transfer badly: on a per-file-metered provider a thousand small
                // saves cost far more time than one large one worth the same bytes, so a byte
                // percentage races ahead and then appears to freeze for the whole small-file tail.
                // The batch's own byte percentage is folded in only to keep the bar moving within
                // a batch.
                var completedBefore = unitsCommitted;
                var batchProgress = transferProgress is null
                    ? null
                    : new Progress<int>(percent =>
                    {
                        // The batch's own saves are counted as they upload rather than all at once
                        // when it commits. Reporting only committed saves would park the count on a
                        // multiple of the batch size for the whole batch — the same "nothing is
                        // happening" that the byte percentage already produces.
                        var within = completedBefore + batch.Count * percent / 100.0;
                        transferProgress.Report(new SaveTransferProgress(
                            Math.Min((int)within, totalUnits),
                            totalUnits,
                            OverallPercent(within, totalUnits)));
                    });
                transferProgress?.Report(new SaveTransferProgress(
                    completedBefore, totalUnits, OverallPercent(completedBefore, totalUnits)));

                await UploadBatchAsync(batch, batchProgress, cancellationToken);
                foreach (var unitId in batch)
                    index[unitId] = _pendingIndex[unitId];

                await CommitIndexAsync(index, cancellationToken);
                unitsCommitted += batch.Count;
                // Adopted as each batch commits, so an exception from a later batch leaves this
                // session's view of the remote matching what the remote actually holds.
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

            if (_inbox is not null)
            {
                TryDeleteDirectory(_inbox);
                _inbox = null;
            }
        }
    }

    private static int OverallPercent(double completedUnits, int totalUnits) =>
        totalUnits <= 0 ? 100 : Math.Clamp((int)(100 * completedUnits / totalUnits), 0, 100);

    /// <summary>
    /// Groups the staged units into commit batches, bounded by both count and size. Two bounds
    /// because either alone leaves a bad case: a count bound lets one batch carry several hundred
    /// megabytes of save states, and a size bound lets one carry thousands of tiny files.
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

    // --files-from: only this batch's payloads, named explicitly.
    // --no-traverse: the destination holds every save ever synced, and rclone would otherwise list
    // all of it to decide whether to copy a handful of staged files.
    // --ignore-times: EmuShelf's own content hash decides what changed, and the provider's
    // modification times are not a sound basis for skipping a save it asked to upload.
    private async Task UploadBatchAsync(
        IReadOnlyList<string> unitIds,
        IProgress<int>? transferProgress,
        CancellationToken cancellationToken)
    {
        var fileList = Path.Combine(_transfersDirectory, "upload-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await File.WriteAllLinesAsync(
                fileList,
                unitIds.Select(unitId => CloudSaveIndex.PayloadName(unitId)),
                cancellationToken);
            await RunAsync(
                ["copy", _outbox!, RemoteRoot, "--files-from", fileList, "--no-traverse", "--ignore-times"],
                Stream.Null,
                cancellationToken,
                transferProgress: transferProgress);
        }
        finally
        {
            TryDeleteFile(fileList);
        }
    }

    private async Task CommitIndexAsync(
        IReadOnlyDictionary<string, SaveUnitSnapshot> index,
        CancellationToken cancellationToken)
    {
        var indexDirectory = CreateStagingDirectory("index");
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(indexDirectory, IndexFileName),
                CloudSaveIndex.Serialize(index.Values),
                cancellationToken);
            await RunAsync(
                ["copy", indexDirectory, RemoteRoot, "--no-traverse", "--ignore-times"],
                Stream.Null,
                cancellationToken);
        }
        finally
        {
            TryDeleteDirectory(indexDirectory);
        }

        _remoteIndexExists = true;
    }

    /// <summary>
    /// Declares the units this session may download, so the one rclone session fetches those
    /// payloads instead of the whole remote. Without this the transport still works — it falls back
    /// to copying everything — but a one-save download would pull every platform's saves.
    /// </summary>
    public void ExpectDownloads(IEnumerable<string> unitIds)
    {
        ArgumentNullException.ThrowIfNull(unitIds);
        foreach (var unitId in unitIds)
        {
            if (IsSafeUnitId(unitId))
                _expectedDownloads.Add(unitId);
        }
    }

    private async Task<string> EnsureInboxAsync(CancellationToken cancellationToken)
    {
        if (_inbox is not null)
            return _inbox;

        var inbox = CreateStagingDirectory("inbox");
        var arguments = new List<string> { "copy", RemoteRoot, inbox };
        string? fileList = null;
        if (_expectedDownloads.Count > 0)
        {
            // One listing of just these paths beats walking the whole remote: on a provider like
            // Drive the traversal, not the transfer, is what a small download waits on.
            fileList = Path.Combine(Path.GetDirectoryName(inbox)!, Path.GetFileName(inbox) + "-files.txt");
            await File.WriteAllLinesAsync(
                fileList,
                _expectedDownloads.Select(unitId => CloudSaveIndex.PayloadName(unitId)),
                cancellationToken);
            arguments.Add("--files-from");
            arguments.Add(fileList);
            arguments.Add("--no-traverse");
        }

        try
        {
            var exitCode = await RunAsync(arguments, Stream.Null, cancellationToken, throwOnNonZeroExit: false);
            // A scoped session names payloads the index promised, and the index can be wrong: rclone
            // fails the whole session over one absent file. Failing here would put every unit's sync
            // behind one broken entry again — exactly the fault this scoping was added alongside. Any
            // payload that did not arrive is caught per unit below, where it is a recoverable
            // condition, so a scoped session reports rather than throws.
            if (exitCode != 0 && exitCode != RcloneDirectoryNotFoundExit && fileList is null)
                throw new IOException($"rclone could not download the cloud saves (exit code {exitCode}).");
        }
        finally
        {
            TryDeleteFile(fileList);
        }

        _inbox = inbox;
        return inbox;
    }

    private string CreateStagingDirectory(string prefix)
    {
        Directory.CreateDirectory(_transfersDirectory);
        var directory = Path.Combine(_transfersDirectory, prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string StageRelativePath(string remoteRelativePath) =>
        remoteRelativePath.Replace('/', Path.DirectorySeparatorChar);

    // With the folder's own id known, the remote root is that folder directly: the provider no
    // longer resolves one path segment at a time from the account root on every call.
    private string RemoteRoot =>
        _cloudFolderId is not null || string.IsNullOrEmpty(_cloudFolder)
            ? _remoteName + ":"
            : _remoteName + ":" + _cloudFolder;

    /// <summary>Looks up the provider's id for the configured folder, or null when it cannot.</summary>
    /// <remarks>
    /// Google Drive only. Addressing a folder by id means dropping it from the remote path, which is
    /// correct exactly because <c>--drive-root-folder-id</c> re-roots the remote there. On any other
    /// backend that flag is ignored, so the same substitution would silently address the remote's
    /// root instead of the saves folder.
    /// </remarks>
    public async Task<string?> ResolveCloudFolderIdAsync(CancellationToken cancellationToken = default)
    {
        if (_cloudFolderId is not null || string.IsNullOrEmpty(_cloudFolder))
            return _cloudFolderId;
        if (!await IsGoogleDriveRemoteAsync(cancellationToken))
            return null;

        // Deliberately a listing of the parent rather than `lsjson --stat` on the folder itself:
        // stat describes the queried path as its own root and reports no id at all, which is how
        // this silently resolved to nothing the first time.
        var separator = _cloudFolder.LastIndexOf('/');
        var parent = separator < 0 ? string.Empty : _cloudFolder[..separator];
        var name = separator < 0 ? _cloudFolder : _cloudFolder[(separator + 1)..];

        await using var buffer = new MemoryStream();
        var exitCode = await RunAsync(
            ["lsjson", "--dirs-only", _remoteName + ":" + parent],
            buffer,
            cancellationToken,
            throwOnNonZeroExit: false);
        if (exitCode != 0 || buffer.Length == 0)
            return null;

        try
        {
            var entries = JsonSerializer.Deserialize<List<RemoteStatEntry>>(buffer.ToArray()) ?? [];
            var match = entries.FirstOrDefault(entry =>
                entry is { IsDir: true } &&
                string.Equals(entry.Name, name, StringComparison.Ordinal) &&
                IsSafeFolderId(entry.ID));
            return match?.ID;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Lists what the remote actually stores and returns the indexed units whose payload is not
    /// there. Those entries are marked for removal, so the next <see cref="FlushAsync"/> rewrites
    /// the index without them and the machines still holding those saves upload them again.
    /// </summary>
    /// <remarks>
    /// One listing for the whole remote, rather than discovering each break by failing a download:
    /// the machine that owns a save never downloads it, so it would otherwise never learn that its
    /// upload is missing.
    /// </remarks>
    public async Task<IReadOnlyList<string>> FindMissingPayloadsAsync(CancellationToken cancellationToken = default)
    {
        await using var buffer = new MemoryStream();
        var exitCode = await RunAsync(
            ["lsf", "-R", "--files-only", RemoteRoot],
            buffer,
            cancellationToken,
            throwOnNonZeroExit: false);
        if (exitCode is RcloneDirectoryNotFoundExit or RcloneFileNotFoundExit)
        {
            if (_remoteIndexExists)
                throw new IOException("The cloud folder disappeared during save verification.");
            return [];
        }
        if (exitCode != 0)
            throw new IOException($"rclone could not list the cloud folder (exit code {exitCode}).");

        var present = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = new StreamReader(new MemoryStream(buffer.ToArray())))
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                var name = line.Trim();
                if (name.Length > 0)
                    present.Add(name);
            }
        }

        // lsf includes index.json itself. If an index was read successfully but this listing cannot
        // see it, the listing is not authoritative evidence that any payload is missing.
        if (_remoteIndexExists && !present.Contains(IndexFileName))
            throw new IOException("rclone could not verify the cloud index while listing save payloads.");

        var missing = _remoteIndex.Keys
            .Where(unitId => !present.Contains(CloudSaveIndex.PayloadName(unitId)))
            .OrderBy(unitId => unitId, StringComparer.Ordinal)
            .ToList();
        foreach (var unitId in missing)
            _missingPayloads.Add(unitId);
        return missing;
    }

    /// <summary>
    /// Adopts a folder id resolved by <see cref="ResolveCloudFolderIdAsync"/> for the rest of this
    /// transport's life, so one session's timings and staging state stay in one instance.
    /// </summary>
    public void UseCloudFolderId(string folderId)
    {
        if (IsSafeFolderId(folderId))
            _cloudFolderId = folderId.Trim();
    }

    private async Task<bool> IsGoogleDriveRemoteAsync(CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var exitCode = await RunAsync(
            ["config", "show", _remoteName],
            buffer,
            cancellationToken,
            throwOnNonZeroExit: false);
        if (exitCode != 0)
            return false;

        using var reader = new StreamReader(new MemoryStream(buffer.ToArray()));
        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("type", StringComparison.OrdinalIgnoreCase))
                continue;

            var separator = line.IndexOf('=');
            if (separator > 0)
                return line[(separator + 1)..].Trim().Equals("drive", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    // Provider ids are opaque tokens; accept only what can be passed as one argument safely.
    private static bool IsSafeFolderId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private async Task<int> RunAsync(
        IReadOnlyList<string> operationArguments,
        Stream standardOutput,
        CancellationToken cancellationToken,
        bool throwOnNonZeroExit = true,
        IProgress<int>? transferProgress = null)
    {
        if (!File.Exists(_rclonePath))
            throw new IOException($"rclone was not found at {_rclonePath}. Place rclone beside EmuShelf.");
        if (!File.Exists(_configurationPath))
            throw new IOException("The rclone configuration file was not found in Settings. Reconnect the cloud remote.");

        var startInfo = new ProcessStartInfo
        {
            FileName = _rclonePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(_configurationPath);
        if (_cloudFolderId is not null)
        {
            startInfo.ArgumentList.Add("--drive-root-folder-id");
            startInfo.ArgumentList.Add(_cloudFolderId);
        }

        foreach (var argument in operationArguments)
            startInfo.ArgumentList.Add(argument);

        // rclone reports its own transfer stats on stderr, which is the only true measure of a
        // large upload's progress — EmuShelf cannot infer it, because everything is staged before
        // a byte moves.
        if (transferProgress is not null)
        {
            startInfo.ArgumentList.Add("--stats");
            startInfo.ArgumentList.Add("1s");
            startInfo.ArgumentList.Add("--stats-one-line");
            startInfo.ArgumentList.Add("-v");
        }

        var elapsed = Stopwatch.StartNew();
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The operating system did not start rclone.");
        var operationTimeout = operationArguments[0] is "copy" or "copyto" ? _transferTimeout : _metadataTimeout;
        using var timeout = new CancellationTokenSource(operationTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked.Token;
        var copyOutput = process.StandardOutput.BaseStream.CopyToAsync(standardOutput, 81920, token);
        var readError = ReadErrorAsync(process, transferProgress, token);
        try
        {
            await Task.WhenAll(copyOutput, readError, process.WaitForExitAsync(token));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await ObserveProcessExitAsync(process, copyOutput, readError);
            if (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new IOException($"rclone did not respond within {operationTimeout.TotalSeconds:0} seconds.");
            throw;
        }

        elapsed.Stop();
        // Recorded per invocation because the cloud provider's latency, not EmuShelf's work, is what
        // a user waits on before a launch: the log has to say which call spent the time.
        _timings.Add($"rclone {operationArguments[0]} — {elapsed.ElapsedMilliseconds} ms");

        if (throwOnNonZeroExit && process.ExitCode != 0)
            throw new IOException($"rclone exited with code {process.ExitCode}: {(await readError).Trim()}");
        return process.ExitCode;
    }

    // Read line by line rather than to the end, so the stats lines can drive progress while the
    // transfer runs. Only the tail is kept: a verbose session is long, and all an error needs is
    // what rclone said last.
    private static async Task<string> ReadErrorAsync(
        Process process,
        IProgress<int>? transferProgress,
        CancellationToken cancellationToken)
    {
        var tail = new Queue<string>();
        var lastPercent = -1;
        while (await process.StandardError.ReadLineAsync(cancellationToken) is { } line)
        {
            tail.Enqueue(line);
            if (tail.Count > 20)
                tail.Dequeue();

            if (transferProgress is null || !TryReadPercent(line, out var percent) || percent == lastPercent)
                continue;

            lastPercent = percent;
            transferProgress.Report(percent);
        }

        return string.Join(Environment.NewLine, tail);
    }

    // A one-line stats report looks like "12.345 MiB / 179.000 MiB, 7%, 5.012 MiB/s, ETA 26s".
    private static bool TryReadPercent(string line, out int percent)
    {
        percent = 0;
        var marker = line.IndexOf("%,", StringComparison.Ordinal);
        if (marker <= 0)
            return false;

        var start = marker;
        while (start > 0 && char.IsAsciiDigit(line[start - 1]))
            start--;
        return start != marker &&
            int.TryParse(line[start..marker], out percent) &&
            percent is >= 0 and <= 100;
    }

    /// <summary>
    /// How long each rclone call in this session took, oldest first, for the activity log.
    /// </summary>
    public IReadOnlyList<string> Timings => _timings;

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }

    private static async Task ObserveProcessExitAsync(
        Process process,
        Task copyOutput,
        Task<string> readError)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }

        try
        {
            await Task.WhenAll(copyOutput, readError);
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static bool IsSafeUnitId(string unitId) => CloudSaveIndex.IsSafeUnitId(unitId);

    private static void ValidateUnitId(string unitId) => CloudSaveIndex.ValidateUnitId(unitId);

    private static void ValidateRemoteName(string remoteName)
    {
        if (string.IsNullOrWhiteSpace(remoteName) ||
            remoteName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new ArgumentException("The rclone remote name contains unsupported characters.", nameof(remoteName));
        }
    }

    private static void ValidateCloudFolder(string cloudFolder)
    {
        if (cloudFolder.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("The cloud folder cannot contain traversal segments.", nameof(cloudFolder));
        }
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record RemoteStatEntry(string? ID, string? Name, bool IsDir);
}
