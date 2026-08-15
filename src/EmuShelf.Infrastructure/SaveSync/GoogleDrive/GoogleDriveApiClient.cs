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

    public GoogleDriveApiClient(
        HttpClient httpClient,
        IGoogleAccessTokenSource tokens,
        IAppLogger? logger = null,
        string apiBaseAddress = DefaultApiBaseAddress,
        string uploadBaseAddress = DefaultUploadBaseAddress,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _logger = logger ?? NullAppLogger.Instance;
        _apiBase = new Uri(apiBaseAddress, UriKind.Absolute);
        _uploadBase = new Uri(uploadBaseAddress, UriKind.Absolute);
        _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
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
            // The stream owns the response: disposing it releases the connection.
            return new HttpResponseStream(
                response,
                await response.Content.ReadAsStreamAsync(cancellationToken));
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
            cancellationToken);
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
                isSuccess: response => response.IsSuccessStatusCode || (int)response.StatusCode == 308);

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
            sent = PersistedBytes(response) ?? sent + chunkLength;
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
        Func<HttpResponseMessage, bool>? isSuccess = null)
    {
        var refreshed = false;
        HttpResponseMessage? response = null;

        for (var attempt = 1; ; attempt++)
        {
            response?.Dispose();

            var request = createRequest();
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await _tokens.GetAccessTokenAsync(forceRefresh: false, cancellationToken));

            try
            {
                response = await _httpClient.SendAsync(request, completionOption, cancellationToken);
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

            if (!IsTransient(response.StatusCode) || attempt >= MaxAttempts)
                return response;

            var retryAfter = response.Headers.RetryAfter?.Delta;
            _logger.Warning(
                $"Google Drive returned {(int)response.StatusCode}; retrying (attempt {attempt} of {MaxAttempts}).");
            await _delay(BackoffFor(attempt, retryAfter), cancellationToken);
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

        // Read the body for the message but never log it wholesale: a Drive error can echo file names.
        var detail = await SafeReadErrorAsync(response, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new GoogleAuthorizationRequiredException(
                $"Google Drive refused to {action}. Reconnect the account in Settings. {detail}".TrimEnd());
        }

        throw new IOException($"Google Drive could not {action} ({(int)response.StatusCode}). {detail}".TrimEnd());
    }

    private static async Task<string> SafeReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;

            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error) &&
                   error.TryGetProperty("message", out var message)
                ? message.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or HttpRequestException)
        {
            return string.Empty;
        }
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
