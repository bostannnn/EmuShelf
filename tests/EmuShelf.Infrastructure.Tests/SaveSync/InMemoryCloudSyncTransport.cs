using System.Security.Cryptography;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

/// <summary>
/// An in-memory stand-in for the rclone-backed cloud transport, used to drive save-sync tests
/// without a real remote. It can simulate an offline/failing remote via <see cref="ThrowOnAccess"/>.
/// </summary>
internal sealed class InMemoryCloudSyncTransport : ICloudSyncTransport
{
    private readonly Dictionary<string, StoredUnit> _units = new(StringComparer.Ordinal);

    public bool Connected { get; set; } = true;

    public bool ThrowOnAccess { get; set; }

    public int Uploads { get; private set; }

    public int Downloads { get; private set; }

    public int ListCalls { get; private set; }

    public int FlushCalls { get; private set; }

    public void Seed(string unitId, byte[] content, DateTimeOffset modifiedUtc) =>
        _units[unitId] = new StoredUnit(content, Hash(content), modifiedUtc);

    public void ReplacePayloadWithoutUpdatingIndex(string unitId, byte[] content)
    {
        var stored = _units[unitId];
        _units[unitId] = stored with { Content = content };
    }

    public bool Has(string unitId) => _units.ContainsKey(unitId);

    /// <summary>The unit ids the service announced before transferring anything, in order.</summary>
    public List<string> AnnouncedDownloads { get; } = [];

    public void ExpectDownloads(IEnumerable<string> unitIds) => AnnouncedDownloads.AddRange(unitIds);

    public byte[] Content(string unitId) => _units[unitId].Content;

    public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Connected);

    public Task<IReadOnlyList<SaveUnitSnapshot>> ListAsync(CancellationToken cancellationToken = default)
    {
        Guard();
        ListCalls++;
        IReadOnlyList<SaveUnitSnapshot> snapshots = _units
            .Select(pair => new SaveUnitSnapshot(pair.Key, pair.Value.Hash, pair.Value.ModifiedUtc))
            .ToList();
        return Task.FromResult(snapshots);
    }

    /// <summary>Unit ids the index advertises but whose payload the remote cannot produce.</summary>
    public HashSet<string> MissingPayloads { get; } = new(StringComparer.Ordinal);

    public Task<Stream> DownloadAsync(string unitId, CancellationToken cancellationToken = default)
    {
        Guard();
        if (MissingPayloads.Contains(unitId))
            throw new CloudPayloadMissingException(unitId);

        Downloads++;
        Stream stream = new MemoryStream(_units[unitId].Content, writable: false);
        return Task.FromResult(stream);
    }

    public async Task UploadAsync(
        string unitId,
        Stream content,
        string contentHash,
        DateTimeOffset modifiedUtc,
        CancellationToken cancellationToken = default)
    {
        Guard();
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        _units[unitId] = new StoredUnit(buffer.ToArray(), contentHash, modifiedUtc);
        Uploads++;
    }

    public Task FlushAsync(
        IProgress<int>? transferProgress = null,
        CancellationToken cancellationToken = default)
    {
        FlushCalls++;
        return Task.CompletedTask;
    }

    private void Guard()
    {
        if (ThrowOnAccess)
            throw new IOException("Simulated cloud transport failure.");
    }

    internal static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    private readonly record struct StoredUnit(byte[] Content, string Hash, DateTimeOffset ModifiedUtc);
}
