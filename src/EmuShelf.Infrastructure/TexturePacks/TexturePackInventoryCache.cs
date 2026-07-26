using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EmuShelf.Core.Storage;
using EmuShelf.Core.TexturePacks;
using EmuShelf.Infrastructure.Storage;

namespace EmuShelf.Infrastructure.TexturePacks;

/// <summary>
/// Stores one atomic JSON snapshot per emulator installation under portable Cache/TexturePacks.
/// A corrupt or mismatched cache is ignored so it can never fabricate a library mark.
/// </summary>
public sealed class TexturePackInventoryCache : ITexturePackInventoryStore
{
    private const int SchemaVersion = 1;
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TexturePackInventoryCache(IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _directory = Path.Combine(paths.CacheDirectory, "TexturePacks");
    }

    public async Task<TexturePackInventorySnapshot?> LoadAsync(
        string installationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = GetPath(installationId);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var document = await JsonSerializer.DeserializeAsync<CacheDocument>(
                stream,
                cancellationToken: cancellationToken);
            return document is { SchemaVersion: SchemaVersion } &&
                IsValid(document.Snapshot, installationId)
                    ? document.Snapshot
                    : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        TexturePackInventorySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.InstallationId);
        if (!IsValid(snapshot, snapshot.InstallationId))
            throw new ArgumentException("The texture inventory snapshot is incomplete.", nameof(snapshot));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directory);
            var document = new CacheDocument(SchemaVersion, snapshot);
            await AtomicFile.WriteAsync(
                GetPath(snapshot.InstallationId),
                (stream, token) => JsonSerializer.SerializeAsync(stream, document, cancellationToken: token),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string GetPath(string installationId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(installationId));
        return Path.Combine(_directory, $"installation-{Convert.ToHexStringLower(hash)}.json");
    }

    private static bool IsValid(TexturePackInventorySnapshot? snapshot, string installationId) =>
        snapshot is not null &&
        !string.IsNullOrWhiteSpace(snapshot.EmulatorId) &&
        !string.IsNullOrWhiteSpace(snapshot.InstallationId) &&
        string.Equals(snapshot.InstallationId, installationId, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(snapshot.RootDirectory) &&
        Path.IsPathFullyQualified(snapshot.RootDirectory) &&
        snapshot.ScannedAt != default &&
        Enum.IsDefined(snapshot.RootStatus) &&
        snapshot.RootStatus != TexturePackRootStatus.Unknown &&
        snapshot.Entries is not null &&
        snapshot.Entries.All(entry =>
            entry is not null &&
            !string.IsNullOrWhiteSpace(entry.PackKey) &&
            !string.IsNullOrWhiteSpace(entry.SourcePath) &&
            Path.IsPathFullyQualified(entry.SourcePath) &&
            Enum.IsDefined(entry.ContentStatus) &&
            entry.ContentStatus != TexturePackContentStatus.Unknown &&
            entry.MatchKeys is not null &&
            entry.MatchKeys.All(key =>
                key is not null &&
                Enum.IsDefined(key.Rule) &&
                key.Rule != TexturePackMatchRule.Unknown &&
                !string.IsNullOrWhiteSpace(key.Value)));

    private sealed record CacheDocument(int SchemaVersion, TexturePackInventorySnapshot Snapshot);
}
