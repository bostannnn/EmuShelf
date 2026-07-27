using System.Diagnostics;
using System.Text.Json;
using EmuShelf.Core.SaveSync;
using EmuShelf.Core.Storage;

namespace EmuShelf.Infrastructure.SaveSync;

/// <summary>
/// Copy-only rclone transport for save units. Payloads and their EmuShelf hashes are stored as
/// separate remote files so rclone's provider-specific hashes never participate in reconciliation.
/// </summary>
public sealed class RcloneCloudSyncTransport : ICloudSyncTransport
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    // rclone exit code 3 ("directory not found") / 4 ("file not found") are the expected states
    // before the first upload to a fresh remote, and must read as an empty listing, not a failure.
    private const int RcloneDirectoryNotFoundExit = 3;
    private const int RcloneFileNotFoundExit = 4;

    // One index file describes every unit on the remote so listing is a single request.
    private const string IndexFileName = "index.json";

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

    public async Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RunAsync(["lsjson", _remoteName + ":"], Stream.Null, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return false;
        }
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
        if (exitCode is RcloneDirectoryNotFoundExit or RcloneFileNotFoundExit || buffer.Length == 0)
            return new Dictionary<string, SaveUnitSnapshot>(StringComparer.Ordinal);
        if (exitCode != 0)
            throw new IOException($"rclone could not read the cloud index (exit code {exitCode}).");

        var entries = JsonSerializer.Deserialize<List<RemoteUnitMetadata>>(buffer.ToArray()) ?? [];
        var index = new Dictionary<string, SaveUnitSnapshot>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is null || !IsSafeUnitId(entry.UnitId) ||
                string.IsNullOrWhiteSpace(entry.ContentHash) || entry.ModifiedUtc == default)
            {
                throw new InvalidDataException("The cloud index is not valid EmuShelf metadata.");
            }

            index[entry.UnitId] = new SaveUnitSnapshot(entry.UnitId, entry.ContentHash, entry.ModifiedUtc);
        }

        return index;
    }

    public async Task<Stream> DownloadAsync(string unitId, CancellationToken cancellationToken = default)
    {
        ValidateUnitId(unitId);
        // Downloads for one sync are served from a single rclone-populated cache (one session for
        // the whole remote) rather than a `cat` per unit.
        var inbox = await EnsureInboxAsync(cancellationToken);
        var payloadPath = Path.Combine(inbox, StageRelativePath(unitId + ".payload"));
        if (!File.Exists(payloadPath))
        {
            // The session was scoped to the announced units. A download outside that set is still
            // served — one extra call for one payload — so scoping can never lose a save.
            Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);
            var exitCode = await RunAsync(
                ["copyto", RemoteRoot.TrimEnd('/') + "/" + unitId + ".payload", payloadPath, "--no-traverse"],
                Stream.Null,
                cancellationToken,
                throwOnNonZeroExit: false);
            if (exitCode != 0 || !File.Exists(payloadPath))
            {
                // Recorded so the next flush prunes the index entry that promised it.
                _missingPayloads.Add(unitId);
                throw new CloudPayloadMissingException(unitId);
            }
        }

        return new FileStream(payloadPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public async Task UploadAsync(
        string unitId,
        Stream content,
        string contentHash,
        DateTimeOffset modifiedUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateUnitId(unitId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        // Stage the payload under the outbox and record its index entry; the actual upload — the
        // changed payloads plus a rebuilt index.json — happens once, in FlushAsync, as a single
        // rclone session so the provider's rate limiter is respected.
        _outbox ??= CreateStagingDirectory("outbox");
        var payloadPath = Path.Combine(_outbox, StageRelativePath(unitId + ".payload"));
        Directory.CreateDirectory(Path.GetDirectoryName(payloadPath)!);

        await using (var payload = new FileStream(payloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(payload, 81920, cancellationToken);
        }

        _pendingIndex[unitId] = new SaveUnitSnapshot(unitId, contentHash, modifiedUtc);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_outbox is null && _missingPayloads.Count == 0)
                return;

            // Payloads first, index second, in two separate rclone sessions. In one session rclone
            // transfers concurrently, so a failure could leave index.json describing a payload that
            // never arrived — and because the index carries the content hash, the machine that owns
            // that save then sees "unchanged" and never re-uploads it, while every other machine
            // fails trying to download it. The index is a commit, so it goes last and only after
            // the payloads it describes are on the remote.
            // --no-traverse: the destination holds every save ever synced, and rclone would
            // otherwise list all of it to decide whether to copy a handful of staged files.
            if (_outbox is not null && _pendingIndex.Count > 0)
            {
                await RunAsync(
                    ["copy", _outbox, RemoteRoot, "--no-traverse"],
                    Stream.Null,
                    cancellationToken);
            }

            var index = new Dictionary<string, SaveUnitSnapshot>(_remoteIndex, StringComparer.Ordinal);
            foreach (var (unitId, snapshot) in _pendingIndex)
                index[unitId] = snapshot;

            // Entries whose payload was found to be missing are dropped, so the machine that still
            // has the save stops seeing "already on the remote" and uploads it on its next pass.
            foreach (var unitId in _missingPayloads)
            {
                if (!_pendingIndex.ContainsKey(unitId))
                    index.Remove(unitId);
            }

            if (_pendingIndex.Count == 0 && _missingPayloads.Count == 0)
                return;

            var entries = index.Values
                .Select(snapshot => new RemoteUnitMetadata(snapshot.UnitId, snapshot.ContentHash, snapshot.ModifiedUtc))
                .ToList();
            var indexDirectory = CreateStagingDirectory("index");
            try
            {
                await File.WriteAllTextAsync(
                    Path.Combine(indexDirectory, IndexFileName),
                    JsonSerializer.Serialize(entries, SerializerOptions),
                    cancellationToken);
                await RunAsync(
                    ["copy", indexDirectory, RemoteRoot, "--no-traverse"],
                    Stream.Null,
                    cancellationToken);
            }
            finally
            {
                TryDeleteDirectory(indexDirectory);
            }

            _remoteIndex = index;
            _missingPayloads.Clear();
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
                _expectedDownloads.Select(unitId => unitId + ".payload"),
                cancellationToken);
            arguments.Add("--files-from");
            arguments.Add(fileList);
            arguments.Add("--no-traverse");
        }

        try
        {
            var exitCode = await RunAsync(arguments, Stream.Null, cancellationToken, throwOnNonZeroExit: false);
            if (exitCode != 0 && exitCode != RcloneDirectoryNotFoundExit)
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
        if (exitCode == RcloneDirectoryNotFoundExit)
            return [];
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

        var missing = _remoteIndex.Keys
            .Where(unitId => !present.Contains(unitId + ".payload"))
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
        bool throwOnNonZeroExit = true)
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

        var elapsed = Stopwatch.StartNew();
        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The operating system did not start rclone.");
        var operationTimeout = operationArguments[0] is "copy" or "copyto" ? _transferTimeout : _metadataTimeout;
        using var timeout = new CancellationTokenSource(operationTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var token = linked.Token;
        var copyOutput = process.StandardOutput.BaseStream.CopyToAsync(standardOutput, 81920, token);
        var readError = process.StandardError.ReadToEndAsync(token);
        try
        {
            await Task.WhenAll(copyOutput, readError, process.WaitForExitAsync(token));
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new IOException($"rclone did not respond within {operationTimeout.TotalSeconds:0} seconds.");
        }

        elapsed.Stop();
        // Recorded per invocation because the cloud provider's latency, not EmuShelf's work, is what
        // a user waits on before a launch: the log has to say which call spent the time.
        _timings.Add($"rclone {operationArguments[0]} — {elapsed.ElapsedMilliseconds} ms");

        if (throwOnNonZeroExit && process.ExitCode != 0)
            throw new IOException($"rclone exited with code {process.ExitCode}: {(await readError).Trim()}");
        return process.ExitCode;
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

    private static bool IsSafeUnitId(string unitId) =>
        !string.IsNullOrWhiteSpace(unitId) &&
        unitId.Split('/', StringSplitOptions.None).All(segment =>
            segment.Length > 0 && segment is not "." and not ".." &&
            !segment.Contains('\\') && !segment.Contains(':'));

    private static void ValidateUnitId(string unitId)
    {
        if (!IsSafeUnitId(unitId))
            throw new ArgumentException("The cloud save unit id is not a safe remote-relative path.", nameof(unitId));
    }

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

    private sealed record RemoteUnitMetadata(string UnitId, string ContentHash, DateTimeOffset ModifiedUtc);

    private sealed record RemoteStatEntry(string? ID, string? Name, bool IsDir);
}
