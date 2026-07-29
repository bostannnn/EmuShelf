using System.Security.Cryptography;
using EmuShelf.Core.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

/// <summary>An in-memory local save store: live units plus a record of conflict backups.</summary>
internal sealed class InMemoryLocalSaveEndpoint : ILocalSaveEndpoint
{
    private readonly Dictionary<string, LiveUnit> _units = new(StringComparer.Ordinal);

    public List<BackupEntry> Backups { get; } = new();

    public void Seed(string unitId, byte[] content, DateTimeOffset modifiedUtc) =>
        _units[unitId] = new LiveUnit(content, modifiedUtc);

    public bool Has(string unitId) => _units.ContainsKey(unitId);

    public byte[] Content(string unitId) => _units[unitId].Content;

    public Task<SaveUnitSnapshot?> SnapshotAsync(string unitId, CancellationToken cancellationToken = default)
    {
        if (!_units.TryGetValue(unitId, out var unit))
            return Task.FromResult<SaveUnitSnapshot?>(null);
        return Task.FromResult<SaveUnitSnapshot?>(
            new SaveUnitSnapshot(unitId, Hash(unit.Content), unit.ModifiedUtc));
    }

    public Task<Stream> ReadAsync(string unitId, CancellationToken cancellationToken = default)
    {
        Stream stream = new MemoryStream(_units[unitId].Content, writable: false);
        return Task.FromResult(stream);
    }

    public async Task WriteAsync(
        string unitId,
        Stream content,
        string expectedContentHash,
        DateTimeOffset modifiedUtc,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        if (!string.Equals(expectedContentHash, Hash(bytes), StringComparison.Ordinal))
            throw new InvalidDataException("The downloaded save did not match the cloud index and was not installed.");
        _units[unitId] = new LiveUnit(bytes, modifiedUtc);
    }

    public Task BackupLocalAsync(string unitId, string reason, CancellationToken cancellationToken = default)
    {
        Backups.Add(new BackupEntry(unitId, _units[unitId].Content, reason, FromLocal: true));
        return Task.CompletedTask;
    }

    public async Task BackupIncomingAsync(
        string unitId,
        Stream content,
        string reason,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Backups.Add(new BackupEntry(unitId, buffer.ToArray(), reason, FromLocal: false));
    }

    internal static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    private readonly record struct LiveUnit(byte[] Content, DateTimeOffset ModifiedUtc);

    internal sealed record BackupEntry(string UnitId, byte[] Content, string Reason, bool FromLocal);
}
