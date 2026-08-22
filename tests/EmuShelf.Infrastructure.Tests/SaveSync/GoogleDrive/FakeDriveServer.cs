using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using EmuShelf.Infrastructure.SaveSync.GoogleDrive;

namespace EmuShelf.Infrastructure.Tests.SaveSync.GoogleDrive;

/// <summary>
/// An in-memory stand-in for the slice of Drive v3 the transport uses: list a folder, create a
/// folder, create or replace a file, download one.
/// </summary>
/// <remarks>
/// A fake server rather than a script of canned responses, because what these tests need to assert is
/// the <em>resulting layout</em> on the remote — that a sync produces the expected folder tree and
/// file names. A response script can only assert the calls that were made, which is the thing least
/// worth pinning down.
/// </remarks>
internal class FakeDriveServer : HttpMessageHandler
{
    public const string RootId = "root";

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private int _nextId;

    public int ListCalls { get; private set; }

    public int UploadCalls { get; private set; }

    /// <summary>Every stored file keyed by its full path beneath the Drive root.</summary>
    public IReadOnlyDictionary<string, byte[]> Files =>
        _entries.Values
            .Where(entry => !entry.IsFolder)
            .ToDictionary(PathOf, entry => entry.Content, StringComparer.Ordinal);

    /// <summary>Every folder path beneath the Drive root.</summary>
    public IReadOnlyCollection<string> Folders =>
        _entries.Values.Where(entry => entry.IsFolder).Select(PathOf).ToHashSet(StringComparer.Ordinal);

    public string AddFolder(string path)
    {
        var parentId = RootId;
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var existing = _entries.Values.FirstOrDefault(entry =>
                entry.IsFolder && entry.ParentId == parentId && entry.Name == segment);
            parentId = existing?.Id ?? Create(segment, parentId, DriveFile.FolderMimeType, []).Id;
        }

        return parentId;
    }

    public void AddFile(string path, byte[] content)
    {
        var separator = path.LastIndexOf('/');
        var parentId = separator < 0 ? RootId : AddFolder(path[..separator]);
        Create(separator < 0 ? path : path[(separator + 1)..], parentId, "application/octet-stream", content);
    }

    public void AddFile(string path, string content) => AddFile(path, Encoding.UTF8.GetBytes(content));

    /// <summary>
    /// Adds a file at <paramref name="path"/> with an explicit modified time, without collapsing a
    /// duplicate name — so a test can stage two blobs of the same name and control which is older.
    /// Drive itself permits duplicate names in a folder, which is the situation this models.
    /// </summary>
    public void AddDuplicateFile(string path, string content, DateTimeOffset modifiedTime)
    {
        var separator = path.LastIndexOf('/');
        var parentId = separator < 0 ? RootId : AddFolder(path[..separator]);
        var entry = Create(
            separator < 0 ? path : path[(separator + 1)..],
            parentId,
            "application/octet-stream",
            Encoding.UTF8.GetBytes(content));
        entry.ModifiedTime = modifiedTime;
    }

    /// <summary>
    /// Creates a folder at <paramref name="path"/> even if one with that name already exists (Drive
    /// permits duplicate folder names), with an explicit modified time, and returns its id. Ancestors
    /// are resolved idempotently; only the leaf is forced to be a new entry. Lets a test stage two
    /// same-named provider folders holding different units.
    /// </summary>
    public string AddDuplicateFolder(string path, DateTimeOffset modifiedTime)
    {
        var separator = path.LastIndexOf('/');
        var parentId = separator < 0 ? RootId : AddFolder(path[..separator]);
        var entry = Create(separator < 0 ? path : path[(separator + 1)..], parentId, DriveFile.FolderMimeType, []);
        entry.ModifiedTime = modifiedTime;
        return entry.Id;
    }

    /// <summary>Adds a file directly under a known folder id, with an explicit modified time.</summary>
    public void AddFileUnder(string parentId, string name, string content, DateTimeOffset modifiedTime)
    {
        var entry = Create(name, parentId, "application/octet-stream", Encoding.UTF8.GetBytes(content));
        entry.ModifiedTime = modifiedTime;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);

        if (request.Method == HttpMethod.Get && query["alt"] == "media")
            return Download(LastSegment(path));

        if (request.Method == HttpMethod.Get)
            return List(query["q"] ?? string.Empty);

        if (request.Method == HttpMethod.Post && path.Contains("/upload/", StringComparison.Ordinal))
            return await CreateFromMultipartAsync(request, cancellationToken);

        if (request.Method == HttpMethod.Post)
            return await CreateFolderAsync(request, cancellationToken);

        if (request.Method == HttpMethod.Patch)
            return await ReplaceAsync(LastSegment(path), request, cancellationToken);

        return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
    }

    private HttpResponseMessage List(string query)
    {
        ListCalls++;
        // A "'<id>' in parents" query lists one folder; a flat query (no parents clause) lists every
        // file, as the real drive.file scope does, so the transport can rebuild the tree in one call.
        var flat = !query.Contains("in parents", StringComparison.Ordinal);
        var matches = flat
            ? _entries.Values.AsEnumerable()
            : _entries.Values.Where(entry =>
                string.Equals(entry.ParentId, ParentFromQuery(query), StringComparison.Ordinal));
        var files = matches.Select(entry => new
        {
            id = entry.Id,
            name = entry.Name,
            mimeType = entry.MimeType,
            size = entry.IsFolder ? (long?)null : entry.Content.Length,
            modifiedTime = entry.ModifiedTime,
            parents = new[] { entry.ParentId },
        });
        return Json(new { files });
    }

    private HttpResponseMessage Download(string id) =>
        _entries.TryGetValue(id, out var entry)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(entry.Content) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);

    private async Task<HttpResponseMessage> CreateFolderAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var metadata = ParseMetadata(await request.Content!.ReadAsStringAsync(cancellationToken));
        var created = Create(metadata.Name, metadata.ParentId ?? RootId, DriveFile.FolderMimeType, []);
        return Json(new { id = created.Id });
    }

    private async Task<HttpResponseMessage> CreateFromMultipartAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        UploadCalls++;
        var boundary = request.Content!.Headers.ContentType!.Parameters
            .First(parameter => parameter.Name == "boundary").Value!.Trim('"');
        var body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var (json, content) = SplitMultipart(body, boundary);

        var metadata = ParseMetadata(json);
        var created = Create(metadata.Name, metadata.ParentId ?? RootId, "application/octet-stream", content);
        return Json(new { id = created.Id });
    }

    private async Task<HttpResponseMessage> ReplaceAsync(
        string id,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        UploadCalls++;
        if (!_entries.TryGetValue(id, out var entry))
            return new HttpResponseMessage(HttpStatusCode.NotFound);

        entry.Content = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
        entry.ModifiedTime = DateTimeOffset.UtcNow;
        return Json(new { id });
    }

    private Entry Create(string name, string parentId, string mimeType, byte[] content)
    {
        var entry = new Entry
        {
            Id = "id-" + Interlocked.Increment(ref _nextId),
            Name = name,
            ParentId = parentId,
            MimeType = mimeType,
            Content = content,
            ModifiedTime = DateTimeOffset.UtcNow,
        };
        _entries[entry.Id] = entry;
        return entry;
    }

    private string PathOf(Entry entry)
    {
        var segments = new List<string>();
        var current = entry;
        while (current is not null)
        {
            segments.Insert(0, current.Name);
            current = _entries.TryGetValue(current.ParentId, out var parent) ? parent : null;
        }

        return string.Join('/', segments);
    }

    private static string ParentFromQuery(string query)
    {
        var start = query.IndexOf('\'');
        var end = query.IndexOf('\'', start + 1);
        return start < 0 || end < 0 ? RootId : query[(start + 1)..end];
    }

    private static (string Name, string? ParentId) ParseMetadata(string json)
    {
        using var document = JsonDocument.Parse(json);
        var name = document.RootElement.GetProperty("name").GetString()!;
        string? parentId = null;
        if (document.RootElement.TryGetProperty("parents", out var parents) && parents.GetArrayLength() > 0)
            parentId = parents[0].GetString();
        return (name, parentId);
    }

    /// <summary>Splits a <c>multipart/related</c> body into its JSON metadata part and its binary part.</summary>
    private static (string Json, byte[] Content) SplitMultipart(byte[] body, string boundary)
    {
        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        var separator = "\r\n\r\n"u8.ToArray();

        var parts = new List<byte[]>();
        var cursor = 0;
        while (true)
        {
            var start = IndexOf(body, delimiter, cursor);
            if (start < 0)
                break;

            var next = IndexOf(body, delimiter, start + delimiter.Length);
            if (next < 0)
                break;

            var headerEnd = IndexOf(body, separator, start);
            if (headerEnd < 0 || headerEnd > next)
                break;

            var contentStart = headerEnd + separator.Length;
            // Trim the CRLF that precedes the next boundary.
            var contentEnd = Math.Max(contentStart, next - 2);
            parts.Add(body[contentStart..contentEnd]);
            cursor = next;
        }

        return parts.Count < 2
            ? throw new InvalidOperationException("Malformed multipart upload body.")
            : (Encoding.UTF8.GetString(parts[0]), parts[1]);
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        for (var i = from; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return i;
        }

        return -1;
    }

    private static string LastSegment(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static HttpResponseMessage Json(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

    private sealed class Entry
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string ParentId { get; init; }
        public required string MimeType { get; init; }
        public required byte[] Content { get; set; }
        public required DateTimeOffset ModifiedTime { get; set; }

        public bool IsFolder => MimeType == DriveFile.FolderMimeType;
    }
}
