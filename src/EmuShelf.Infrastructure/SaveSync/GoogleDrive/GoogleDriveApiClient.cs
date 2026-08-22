using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EmuShelf.Core.Diagnostics;

namespace EmuShelf.Infrastructure.SaveSync.GoogleDrive;

/// <summary>
/// The Drive v3 calls EmuShelf's save sync needs, and nothing else: resolve and create folders, list
/// a folder, download a file, and create or replace one. There is deliberately no delete — the
/// transport above is copy-only, and an API that cannot delete cannot be made to.
/// </summary>
/// <remarks>
/// Hand-written rather than taken from Google's client library: the surface used here is five calls,
/// and the library brings a dependency graph plus a credential model of its own that would have to be
/// bent back onto <see cref="IGoogleAccessTokenSource"/> and the portable token store anyway.
/// </remarks>
public sealed class GoogleDriveApiClient
{
    public const string DefaultApiBaseAddress = "https://www.googleapis.com/drive/v3/";
    public const string DefaultUploadBaseAddress = "https://www.googleapis.com/upload/drive/v3/";

    /// <summary>The alias Drive accepts in place of the account root folder's id.</summary>
    public const string RootFolderAlias = "root";

    /// <summary>
    /// Above this, upload resumably instead of in one request. A single 179 MB PCSX2/RPCS3 unit
    /// (DECISIONS 2026-07-24) sent as one body has to restart from zero on any mid-flight failure.
    /// </summary>
    internal const long ResumableThresholdBytes = 5L * 1024 * 1024;

    /// <summary>Drive requires resumable chunks to be a multiple of 256 KiB.</summary>
    internal const int ResumableChunkBytes = 8 * 1024 * 1024;

    private const int MaxAttempts = 5;

    private readonly HttpClient _httpClient;
    private readonly IGoogleAccessTokenSource _tokens;
    private readonly IAppLogger _logger;
    private readonly Uri _apiBase;
    private readonly Uri _uploadBase;

    /// <summary>Injected so tests can drive the backoff without actually sleeping.</summary>
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>
    /// How long a metadata request — a listing, a folder create, a download's headers, the token mint —
    /// may run before it is treated as stalled and retried, and the idle budget each read of a streamed
    /// download is given to make progress. Kept short so a stalled listing in a Sync-all tree walk gives
    /// up quickly; uploads get their own, longer budget (<see cref="_uploadTimeout"/>) because they are
    /// the one request that is legitimately slow rather than stalled. The old client relied only on the
    /// 5-minute <see cref="HttpClient.Timeout"/>, so a silent connection hung for the full five minutes;
    /// the rclone transport this replaced got that protection from rclone's own <c>--timeout</c> plus
    /// low-level retries. Injected small in tests so a stall need not be waited out.
    /// </summary>
    private readonly TimeSpan _networkTimeout;

    /// <summary>
    /// The per-attempt budget for an upload request — a simple upload or one resumable chunk. Larger
    /// than <see cref="_networkTimeout"/> because a chunk (capped at <see cref="ResumableChunkBytes"/> =
    /// 8 MiB) is slow-but-progressing on weak wifi, not stalled: a metadata-length timeout would fail a
    /// perfectly good upload every attempt. One value covers even a 179 MB save, since it is sent one
    /// bounded chunk at a time.
    /// </summary>
    private readonly TimeSpan _uploadTimeout;

    public GoogleDriveApiClient(
        HttpClient httpClient,
        IGoogleAccessTokenSource tokens,
        IAppLogger? logger = null,
        string apiBaseAddress = DefaultApiBaseAddress,
        string uploadBaseAddress = DefaultUploadBaseAddress,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? networkTimeout = null,
        TimeSpan? uploadTimeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _logger = logger ?? NullAppLogger.Instance;
        _apiBase = new Uri(apiBaseAddress, UriKind.Absolute);
        _uploadBase = new Uri(uploadBaseAddress, UriKind.Absolute);
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        _networkTimeout = networkTimeout ?? TimeSpan.FromSeconds(45);
        _uploadTimeout = uploadTimeout ?? TimeSpan.FromSeconds(120);
    }

    /// <summary>Lists one folder's immediate children, following Drive's paging to the end.</summary>
    public async Task<IReadOnlyList<DriveFile>> ListChildrenAsync(
        string folderId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderId);

        var files = new List<DriveFile>();
        string? pageToken = null;
        do
        {
            var query = new StringBuilder("files?spaces=drive&pageSize=1000")
                .Append("&fields=").Append(Uri.EscapeDataString("nextPageToken,files(id,name,mimeType,size,modifiedTime)"))
                .Append("&q=").Append(Uri.EscapeDataString($"'{EscapeQueryLiteral(folderId)}' in parents and trashed=false"));
            if (pageToken is not null)
                query.Append("&pageToken=").Append(Uri.EscapeDataString(pageToken));

            using var response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, new Uri(_apiBase, query.ToString())),
                cancellationToken);
            await ThrowIfFailedAsync(response, "list a cloud folder", cancellationToken);

            var page = await ReadJsonAsync<DriveFileList>(response, cancellationToken);
            if (page?.Files is { } pageFiles)
                files.AddRange(pageFiles);
            pageToken = string.IsNullOrEmpty(page?.NextPageToken) ? null : page.NextPageToken;
        }
        while (pageToken is not null);

        return files;
    }

    /// <summary>
    /// Lists every file the app can see in one paginated pass, each carrying its parents. Under the
    /// <c>drive.file</c> scope this is exactly EmuShelf's own files, so a caller can rebuild the whole
    /// saves folder tree from it without a listing per folder — one round-trip instead of one per
    /// folder, which on a phone's link is the difference between a fast sync and a ~20-second one.
    /// </summary>
    public async Task<IReadOnlyList<DriveFile>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var files = new List<DriveFile>();
        string? pageToken = null;
        do
        {
            var query = new StringBuilder("files?spaces=drive&pageSize=1000")
                .Append("&fields=").Append(Uri.EscapeDataString("nextPageToken,files(id,name,mimeType,modifiedTime,parents)"))
                .Append("&q=").Append(Uri.EscapeDataString("trashed=false"));
            if (pageToken is not null)
                query.Append("&pageToken=").Append(Uri.EscapeDataString(pageToken));

            using var response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, new Uri(_apiBase, query.ToString())),
                cancellationToken);
            await ThrowIfFailedAsync(response, "list cloud files", cancellationToken);

            var page = await ReadJsonAsync<DriveFileList>(response, cancellationToken);
            if (page?.Files is { } pageFiles)
                files.AddRange(pageFiles);
            pageToken = string.IsNullOrEmpty(page?.NextPageToken) ? null : page.NextPageToken;
        }
        while (pageToken is not null);

        return files;
    }

    /// <summary>
    /// The named child of a folder, or null when it does not exist. Drive permits duplicate names in
    /// one folder, so the oldest match wins — deterministically, and in favour of the copy that has
    /// been there longest rather than whichever the listing happened to return first.
    /// </summary>
    public async Task<DriveFile?> FindChildAsync(
        string folderId,
        string name,
        bool? isFolder = null,
        CancellationToken cancellationToken = default)
    {
        var children = await ListChildrenAsync(folderId, cancellationToken);
        return children
            .Where(file => string.Equals(file.Name, name, StringComparison.Ordinal))
            .Where(file => isFolder is null || file.IsFolder == isFolder)
            .OrderBy(file => file.ModifiedTime ?? DateTimeOffset.MaxValue)
            .ThenBy(file => file.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Resolves a <c>/</c>-separated folder path beneath <paramref name="rootId"/>, creating the
    /// segments that do not exist when <paramref name="create"/> is set. Returns null when a segment
    /// is missing and creation was not asked for.
    /// </summary>
    public async Task<string?> ResolveFolderPathAsync(
        string rootId,
        string path,
        bool create,
        CancellationToken cancellationToken = default)
    {
        var current = string.IsNullOrWhiteSpace(rootId) ? RootFolderAlias : rootId;
        foreach (var segment in SplitPath(path))
        {
            var existing = await FindChildAsync(current, segment, isFolder: true, cancellationToken);
            if (existing is not null)
            {
                current = existing.Id;
                continue;
            }

            if (!create)
                return null;

            current = await CreateFolderAsync(current, segment, cancellationToken);
        }

        return current;
    }

    /// <summary>Creates one folder and returns its id.</summary>
    public async Task<string> CreateFolderAsync(
        string parentId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var metadata = JsonSerializer.Serialize(new
        {
            name,
            mimeType = DriveFile.FolderMimeType,
            parents = new[] { string.IsNullOrWhiteSpace(parentId) ? RootFolderAlias : parentId },
        });

        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, new Uri(_apiBase, "files?fields=id"))
            {
                Content = new StringContent(metadata, Encoding.UTF8, "application/json"),
            },
            cancellationToken);
        await ThrowIfFailedAsync(response, $"create the cloud folder '{name}'", cancellationToken);

        var created = await ReadJsonAsync<DriveFile>(response, cancellationToken);
        return string.IsNullOrEmpty(created?.Id)
            ? throw new IOException($"Google Drive created the folder '{name}' but returned no id.")
            : created.Id;
    }

    /// <summary>
    /// Opens a file's content. Returns null when Drive reports it is gone, which the transport treats
    /// as a missing payload rather than a failure — an index entry can outlive its blob.
    /// </summary>
    public async Task<Stream?> DownloadAsync(string fileId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        var response = await SendAsync(
            () => new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(_apiBase, $"files/{Uri.EscapeDataString(fileId)}?alt=media")),
            cancellationToken,
            HttpCompletionOption.ResponseHeadersRead);
        try
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                response.Dispose();
                return null;
            }

            await ThrowIfFailedAsync(response, "download a cloud save", cancellationToken);
            // The stream owns the response: disposing it releases the connection. The body is read
            // after the headers under ResponseHeadersRead, so neither the per-attempt timeout above
            // nor HttpClient.Timeout bounds it — the idle wrapper is what stops a download that goes
            // silent mid-body from hanging forever, the way rclone's --timeout used to.
            return new HttpResponseStream(
                response,
                new IdleTimeoutStream(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    _networkTimeout,
                    cancellationToken));
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates <paramref name="name"/> in <paramref name="folderId"/>, or replaces the content of
    /// <paramref name="existingFileId"/> when one is supplied. Returns the file's id.
    /// </summary>
    /// <param name="content">
    /// Must be seekable: a retried request has to re-send the body from the start, and a payload that
    /// cannot rewind would upload a truncated save on the second attempt.
    /// </param>
    public async Task<string> UploadAsync(
        string folderId,
        string name,
        string? existingFileId,
        Stream content,
        IProgress<long>? bytesUploaded = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!content.CanSeek)
            throw new ArgumentException("Cloud uploads require a seekable stream.", nameof(content));

        var length = content.Length - content.Position;
        return length > ResumableThresholdBytes
            ? await UploadResumableAsync(folderId, name, existingFileId, content, length, bytesUploaded, cancellationToken)
            : await UploadSimpleAsync(folderId, name, existingFileId, content, length, bytesUploaded, cancellationToken);
    }

    private async Task<string> UploadSimpleAsync(
        string folderId,
        string name,
        string? existingFileId,
        Stream content,
        long length,
        IProgress<long>? bytesUploaded,
        CancellationToken cancellationToken)
    {
        var origin = content.Position;
        using var response = await SendAsync(
            () =>
            {
                content.Position = origin;
                if (existingFileId is not null)
                {
                    return new HttpRequestMessage(
                        HttpMethod.Patch,
                        new Uri(_uploadBase, $"files/{Uri.EscapeDataString(existingFileId)}?uploadType=media&fields=id"))
                    {
                        Content = OctetStream(content),
                    };
                }

                var metadata = JsonSerializer.Serialize(new { name, parents = new[] { folderId } });
                var multipart = new MultipartContent("related")
                {
                    new StringContent(metadata, Encoding.UTF8, "application/json"),
                    OctetStream(content),
                };
                return new HttpRequestMessage(HttpMethod.Post, new Uri(_uploadBase, "files?uploadType=multipart&fields=id"))
                {
                    Content = multipart,
                };
            },
            cancellationToken,
            attemptTimeout: _uploadTimeout);
        await ThrowIfFailedAsync(response, $"upload '{name}'", cancellationToken);

        bytesUploaded?.Report(length);
        var uploaded = await ReadJsonAsync<DriveFile>(response, cancellationToken);
        return string.IsNullOrEmpty(uploaded?.Id)
            ? existingFileId ?? throw new IOException($"Google Drive accepted '{name}' but returned no id.")
            : uploaded.Id;
    }

    /// <summary>
    /// Starts a resumable session and sends the body in chunks. Chunked rather than one long PUT so a
    /// large save reports progress as it moves and a failure costs one chunk, not the whole payload.
    /// </summary>
    private async Task<string> UploadResumableAsync(
        string folderId,
        string name,
        string? existingFileId,
        Stream content,
        long length,
        IProgress<long>? bytesUploaded,
        CancellationToken cancellationToken)
    {
        var sessionUri = await StartResumableSessionAsync(folderId, name, existingFileId, cancellationToken);
        var origin = content.Position;
        var buffer = new byte[ResumableChunkBytes];
        long sent = 0;

        while (sent < length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            content.Position = origin + sent;
            var chunkLength = (int)Math.Min(ResumableChunkBytes, length - sent);
            await content.ReadExactlyAsync(buffer.AsMemory(0, chunkLength), cancellationToken);

            var chunkStart = sent;
            using var response = await SendAsync(
                () =>
                {
                    var chunk = new ByteArrayContent(buffer, 0, chunkLength);
                    chunk.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    chunk.Headers.ContentRange = new ContentRangeHeaderValue(
                        chunkStart,
                        chunkStart + chunkLength - 1,
                        length);
                    return new HttpRequestMessage(HttpMethod.Put, sessionUri) { Content = chunk };
                },
                cancellationToken,
                // 308 "Resume Incomplete" is Drive saying the chunk landed and it wants the next one.
                // It is a success here, so it must not be retried as a redirect or an error.
                isSuccess: response => response.IsSuccessStatusCode || (int)response.StatusCode == 308,
                attemptTimeout: _uploadTimeout);

            if ((int)response.StatusCode != 308)
            {
                await ThrowIfFailedAsync(response, $"upload '{name}'", cancellationToken);
                bytesUploaded?.Report(length);
                var uploaded = await ReadJsonAsync<DriveFile>(response, cancellationToken);
                return string.IsNullOrEmpty(uploaded?.Id)
                    ? existingFileId ?? throw new IOException($"Google Drive accepted '{name}' but returned no id.")
                    : uploaded.Id;
            }

            // Drive states how much it actually persisted in the 308's Range header. Trusting the
            // chunk length instead would skip whatever it dropped and write a corrupt save that
            // still looks successful, so the server's count wins whenever it sends one.
            var advanced = PersistedBytes(response) ?? sent + chunkLength;
            // Forward-progress guard: a 308 whose Range does not move past the chunk we just sent
            // (stuck, or reported backwards) would otherwise re-send the same chunk forever, with no
            // attempt cap and no backoff. Fail the upload instead of looping.
            if (advanced <= chunkStart)
            {
                throw new IOException(
                    $"Google Drive stopped accepting '{name}' at {chunkStart} of {length} bytes.");
            }

            sent = advanced;
            bytesUploaded?.Report(sent);
        }

        throw new IOException($"Google Drive never completed the resumable upload of '{name}'.");
    }

    /// <summary>
    /// How many bytes Drive says it has stored, read from a 308's <c>Range: bytes=0-N</c>. Null when
    /// it sent no range, which means nothing has been persisted yet.
    /// </summary>
    internal static long? PersistedBytes(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Range", out var values))
            return null;

        var range = values.FirstOrDefault();
        var separator = range?.LastIndexOf('-') ?? -1;
        return separator > 0 && long.TryParse(range![(separator + 1)..], out var lastByte) && lastByte >= 0
            ? lastByte + 1
            : null;
    }

    private async Task<Uri> StartResumableSessionAsync(
        string folderId,
        string name,
        string? existingFileId,
        CancellationToken cancellationToken)
    {
        var metadata = existingFileId is not null
            ? JsonSerializer.Serialize(new { name })
            : JsonSerializer.Serialize(new { name, parents = new[] { folderId } });
        var uri = existingFileId is not null
            ? new Uri(_uploadBase, $"files/{Uri.EscapeDataString(existingFileId)}?uploadType=resumable&fields=id")
            : new Uri(_uploadBase, "files?uploadType=resumable&fields=id");
        var method = existingFileId is not null ? HttpMethod.Patch : HttpMethod.Post;

        using var response = await SendAsync(
            () => new HttpRequestMessage(method, uri)
            {
                Content = new StringContent(metadata, Encoding.UTF8, "application/json"),
            },
            cancellationToken);
        await ThrowIfFailedAsync(response, $"start the upload of '{name}'", cancellationToken);

        return response.Headers.Location ??
            throw new IOException($"Google Drive did not return an upload session for '{name}'.");
    }

    /// <summary>
    /// Sends a request, refreshing the token once on a 401 and backing off on the conditions Drive
    /// uses to mean "later": 429, and the 5xx family. The request is rebuilt per attempt because an
    /// <see cref="HttpRequestMessage"/> cannot be sent twice.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead,
        Func<HttpResponseMessage, bool>? isSuccess = null,
        TimeSpan? attemptTimeout = null)
    {
        var budget = attemptTimeout ?? _networkTimeout;
        var refreshed = false;
        HttpResponseMessage? response = null;

        for (var attempt = 1; ; attempt++)
        {
            response?.Dispose();

            // Each attempt gets its own budget layered on the caller's cancellation, so a request — or
            // the access-token mint that precedes it, which shares this HttpClient and was historically
            // the one call with no stall bound of its own — is abandoned and retried rather than parking
            // on the 5-minute HttpClient ceiling. The linked token trips for either reason; the catch
            // below tells them apart, since only the caller's own token is a real cancel.
            using var attemptCts = new CancellationTokenSource(budget);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, attemptCts.Token);

            var request = createRequest();
            try
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Bearer",
                    await _tokens.GetAccessTokenAsync(forceRefresh: false, linked.Token));
                response = await _httpClient.SendAsync(request, completionOption, linked.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A stalled request, not a user stop: the caller's token has not fired, so the trip
                // was our own per-attempt timeout. Retry it exactly as a transport failure, and once
                // the retries are spent surface an IOException — never a bare cancellation, which the
                // pipeline would mistake for the user pressing Stop and skip its failure handling.
                if (attempt < MaxAttempts)
                {
                    _logger.Warning(
                        $"Google Drive did not respond within {budget.TotalSeconds:0}s; " +
                        $"retrying (attempt {attempt} of {MaxAttempts}).");
                    await _delay(BackoffFor(attempt, null), cancellationToken);
                    continue;
                }

                throw new IOException(
                    $"Google Drive stopped responding (no reply within {budget.TotalSeconds:0} seconds).");
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await _delay(BackoffFor(attempt, null), cancellationToken);
                continue;
            }
            finally
            {
                request.Dispose();
            }

            if (isSuccess?.Invoke(response) ?? response.IsSuccessStatusCode)
                return response;

            // One forced refresh, then treat a further 401 as a real authorization failure rather
            // than looping on a token the account will never honour again.
            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
            {
                refreshed = true;
                await _tokens.GetAccessTokenAsync(forceRefresh: true, cancellationToken);
                continue;
            }

            // Drive answers 403 for rate limiting as well as for real authorization failures, and the
            // two are told apart only by the error reason in the body. Buffer that body — so the
            // eventual message can still read it — and back off on the rate-limit reasons exactly as
            // on a 429, rather than returning a "reconnect the account" failure the user cannot act on.
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var reason = ParseError(await BufferErrorBodyAsync(response, cancellationToken)).Reason;
                if (IsRateLimitReason(reason) && attempt < MaxAttempts)
                {
                    _logger.Warning(
                        $"Google Drive rate-limited the request (403 {reason}); retrying (attempt {attempt} of {MaxAttempts}).");
                    await _delay(BackoffFor(attempt, RetryAfterDelay(response)), cancellationToken);
                    continue;
                }

                return response;
            }

            if (!IsTransient(response.StatusCode) || attempt >= MaxAttempts)
                return response;

            _logger.Warning(
                $"Google Drive returned {(int)response.StatusCode}; retrying (attempt {attempt} of {MaxAttempts}).");
            await _delay(BackoffFor(attempt, RetryAfterDelay(response)), cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    /// <summary>
    /// Drive's own guidance is exponential backoff with jitter; the server's Retry-After wins when it
    /// sends one. Jitter matters because several units failing at once would otherwise all come back
    /// at the same instant and trip the same limit again.
    /// </summary>
    internal static TimeSpan BackoffFor(int attempt, TimeSpan? retryAfter)
    {
        if (retryAfter is { } delta && delta > TimeSpan.Zero)
            return delta;

        var seconds = Math.Min(Math.Pow(2, attempt - 1), 16);
        var jitter = Random.Shared.NextDouble() * 0.5;
        return TimeSpan.FromSeconds(seconds + jitter);
    }

    private static async Task ThrowIfFailedAsync(
        HttpResponseMessage response,
        string action,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        // Read the body for the message and reason but never log it wholesale: a Drive error can echo
        // file names. The reason is a fixed token, safe to branch on without matching a localized message.
        var (detail, reason) = await ReadErrorAsync(response, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new GoogleAuthorizationRequiredException(
                $"Google Drive refused to {action}. Reconnect the account in Settings. {detail}".TrimEnd());
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            // A rate-limit 403 that reached here has exhausted its retries — a transient failure, not
            // a reason to reconnect. A storage-quota 403 means the Drive is full. Only a genuine
            // permission/authorization 403 asks the user to reconnect.
            if (IsRateLimitReason(reason))
            {
                throw new IOException(
                    $"Google Drive is rate-limiting EmuShelf and could not {action} right now; try again shortly. {detail}".TrimEnd());
            }

            if (string.Equals(reason, "storageQuotaExceeded", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(
                    $"Google Drive is out of space, so EmuShelf could not {action}. {detail}".TrimEnd());
            }

            throw new GoogleAuthorizationRequiredException(
                $"Google Drive refused to {action}. Reconnect the account in Settings. {detail}".TrimEnd());
        }

        throw new IOException($"Google Drive could not {action} ({(int)response.StatusCode}). {detail}".TrimEnd());
    }

    private static async Task<(string Message, string? Reason)> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return ParseError(await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            return (string.Empty, null);
        }
    }

    // Pulls the human message and the machine reason out of a Drive error body. The reason lives at
    // error.errors[0].reason on classic responses and error.status on newer ones; either identifies a
    // rate limit or a full Drive without EmuShelf having to match on the (localized) message text.
    internal static (string Message, string? Reason) ParseError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (string.Empty, null);

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.Object)
            {
                return (string.Empty, null);
            }

            var message = error.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString() ?? string.Empty
                : string.Empty;

            string? reason = null;
            if (error.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0 &&
                errors[0].TryGetProperty("reason", out var reasonElement))
            {
                reason = reasonElement.GetString();
            }

            if (string.IsNullOrEmpty(reason) && error.TryGetProperty("status", out var statusElement))
                reason = statusElement.GetString();

            return (message, reason);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // JsonException: not JSON at all (an HTML/plain-text proxy body). InvalidOperationException:
            // valid JSON of the wrong shape — a bare scalar/array root, or a field whose type is not
            // what TryGetProperty/GetString expects (e.g. a numeric "message"). Either way there is no
            // classifiable detail, which is the safe answer: the 403 retry path then treats it as
            // non-rate-limit and the caller maps it to a reconnect, rather than the parse escaping.
            return (string.Empty, null);
        }
    }

    private static readonly HashSet<string> RateLimitReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "rateLimitExceeded",
        "userRateLimitExceeded",
        "dailyLimitExceeded",
        "sharingRateLimitExceeded",
    };

    // The 403 reasons that mean "back off and retry" — the same posture as a 429 — rather than "this
    // account cannot do this". Everything else on a 403 is treated as a real authorization failure.
    internal static bool IsRateLimitReason(string? reason) =>
        reason is not null && RateLimitReasons.Contains(reason);

    // The server's requested wait, whether given as delta-seconds or an absolute HTTP-date. Reading
    // only Delta would silently drop a date-form Retry-After and retry before the server is ready.
    private static TimeSpan? RetryAfterDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta;
        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    // Buffers a failed response's body and puts it back as fresh content, so classifying a 403 here
    // does not consume the body the eventual error message still needs to read.
    private static async Task<string> BufferErrorBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            return string.Empty;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
        response.Content.Dispose();
        response.Content = new StringContent(body, Encoding.UTF8, mediaType);
        return body;
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new IOException("Google Drive returned a response EmuShelf could not read.", ex);
        }
    }

    /// <summary>
    /// Wraps the caller's payload for one request without handing over ownership of it.
    /// </summary>
    /// <remarks>
    /// <see cref="StreamContent"/> disposes the stream it wraps, and this client disposes the request
    /// after every attempt — so without the shim, the first 429 would close the caller's payload and
    /// the retry would fault on a spent stream instead of re-sending. Retrying a rate-limited upload
    /// is most of the reason this client is hand-written, so it has to survive its own retry.
    /// </remarks>
    private static StreamContent OctetStream(Stream content)
    {
        var payload = new StreamContent(new NonClosingStream(content));
        payload.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return payload;
    }

    /// <summary>Forwards everything to the inner stream except disposal, which it swallows.</summary>
    private sealed class NonClosingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void Flush() => inner.Flush();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // Deliberately does not dispose the inner stream: the caller owns it.
            base.Dispose(disposing);
        }
    }

    internal static IEnumerable<string> SplitPath(string? path) =>
        (path ?? string.Empty)
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment is not "." and not "..");

    /// <summary>
    /// Escapes a value for Drive's <c>q</c> parameter, where a bare apostrophe in a file name would
    /// otherwise close the literal and change the query's meaning.
    /// </summary>
    internal static string EscapeQueryLiteral(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    /// <summary>
    /// A read-side idle timeout for a streamed body: each read is given <paramref name="idleTimeout"/>
    /// to return, the clock restarting on every read so a slow-but-moving download is never punished. A
    /// download whose connection falls silent mid-body is abandoned as an <see cref="IOException"/>
    /// instead of blocking indefinitely — the protection a
    /// <see cref="HttpCompletionOption.ResponseHeadersRead"/> body read otherwise has none of.
    /// </summary>
    /// <remarks>
    /// The guard is applied to the synchronous <see cref="Read(byte[],int,int)"/> overloads too, not
    /// just <see cref="ReadAsync(Memory{byte},CancellationToken)"/>: the save-restore path reads the
    /// body synchronously (<c>FileSystemLocalSaveEndpoint</c> copies and unzips on a
    /// <see cref="Task.Run(Action)"/> thread), so guarding only the async path would leave the very
    /// large-restore hang this class exists to close still unbounded. <paramref name="streamCancellation"/>
    /// is the download's own token, linked into every read so a user Stop can break a blocked read that
    /// the synchronous <see cref="Stream.Read(byte[],int,int)"/> API has no token of its own to carry.
    /// </remarks>
    private sealed class IdleTimeoutStream(Stream inner, TimeSpan idleTimeout, CancellationToken streamCancellation) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            using var idle = new CancellationTokenSource(idleTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                streamCancellation, cancellationToken, idle.Token);
            try
            {
                return await inner.ReadAsync(buffer, linked.Token);
            }
            catch (OperationCanceledException) when (IsIdleTrip(idle, cancellationToken))
            {
                throw StalledDownload();
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => ReadSync(buffer.AsMemory(offset, count));

        public override int Read(Span<byte> buffer)
        {
            // Span cannot cross the await the timeout needs, so bounce through a pooled array.
            var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
            try
            {
                var read = ReadSync(rented.AsMemory(0, buffer.Length));
                rented.AsSpan(0, read).CopyTo(buffer);
                return read;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        // The synchronous read path, run through the async read so the same idle timeout applies. This
        // blocks the calling thread, which is a Task.Run thread-pool thread here (never the UI thread),
        // so there is no synchronization context to dead-lock against.
        private int ReadSync(Memory<byte> buffer)
        {
            using var idle = new CancellationTokenSource(idleTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(streamCancellation, idle.Token);
            try
            {
                return inner.ReadAsync(buffer, linked.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (IsIdleTrip(idle, CancellationToken.None))
            {
                throw StalledDownload();
            }
        }

        // True when the trip was our idle timer and not either token the caller supplied — the only
        // case that is a stall rather than a real cancellation to propagate untouched.
        private bool IsIdleTrip(CancellationTokenSource idle, CancellationToken readCancellation) =>
            idle.IsCancellationRequested &&
            !streamCancellation.IsCancellationRequested &&
            !readCancellation.IsCancellationRequested;

        private IOException StalledDownload() => new(
            $"Google Drive stopped sending the save (no data for {idleTimeout.TotalSeconds:0} seconds).");

        public override void Flush() => inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Keeps the HTTP response alive for exactly as long as the caller reads its body.</summary>
    private sealed class HttpResponseStream(HttpResponseMessage response, Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush() => inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                response.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
