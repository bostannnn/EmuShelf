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
    private readonly TimeSpan _operationTimeout;
    private string? _outbox;
    private string? _inbox;
    private Dictionary<string, SaveUnitSnapshot> _remoteIndex = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SaveUnitSnapshot> _pendingIndex = new(StringComparer.Ordinal);

    public RcloneCloudSyncTransport(
        IAppPaths appPaths,
        string remoteName,
        string cloudFolder,
        string? rclonePath = null,
        TimeSpan? operationTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(appPaths);
        ValidateRemoteName(remoteName);
        ValidateCloudFolder(cloudFolder);
        _rclonePath = RcloneExecutable.Resolve(appPaths, rclonePath);
        _configurationPath = Path.Combine(appPaths.SettingsDirectory, "rclone.conf");
        _remoteName = remoteName;
        _cloudFolder = cloudFolder.Trim().Trim('/').Replace('\\', '/');
        _transfersDirectory = Path.Combine(appPaths.SavesDirectory, "transfers");
        // A single rclone call (upload/download of one save) should be quick; a much longer wait
        // means a stalled network, so fail rather than appear to hang forever.
        _operationTimeout = operationTimeout ?? TimeSpan.FromMinutes(2);
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
            throw new IOException($"The cloud save payload for '{unitId}' was not found on the remote.");
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
            if (_outbox is not null && _pendingIndex.Count > 0)
            {
                var index = new Dictionary<string, SaveUnitSnapshot>(_remoteIndex, StringComparer.Ordinal);
                foreach (var (unitId, snapshot) in _pendingIndex)
                    index[unitId] = snapshot;

                var entries = index.Values
                    .Select(snapshot => new RemoteUnitMetadata(snapshot.UnitId, snapshot.ContentHash, snapshot.ModifiedUtc))
                    .ToList();
                await File.WriteAllTextAsync(
                    Path.Combine(_outbox, IndexFileName),
                    JsonSerializer.Serialize(entries, SerializerOptions),
                    cancellationToken);
                await RunAsync(["copy", _outbox, RemoteRoot], Stream.Null, cancellationToken);
                _remoteIndex = index;
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

    private async Task<string> EnsureInboxAsync(CancellationToken cancellationToken)
    {
        if (_inbox is not null)
            return _inbox;

        var inbox = CreateStagingDirectory("inbox");
        var exitCode = await RunAsync(["copy", RemoteRoot, inbox], Stream.Null, cancellationToken, throwOnNonZeroExit: false);
        if (exitCode != 0 && exitCode != RcloneDirectoryNotFoundExit)
            throw new IOException($"rclone could not download the cloud saves (exit code {exitCode}).");
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

    private string RemoteRoot =>
        string.IsNullOrEmpty(_cloudFolder) ? _remoteName + ":" : _remoteName + ":" + _cloudFolder;

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
        foreach (var argument in operationArguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("The operating system did not start rclone.");
        using var timeout = new CancellationTokenSource(_operationTimeout);
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
            throw new IOException($"rclone did not respond within {_operationTimeout.TotalSeconds:0} seconds.");
        }

        if (throwOnNonZeroExit && process.ExitCode != 0)
            throw new IOException($"rclone exited with code {process.ExitCode}: {(await readError).Trim()}");
        return process.ExitCode;
    }

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
}
