using System.Collections.Concurrent;

namespace EmuShelf.Core.TexturePacks;

/// <summary>An inventory presented with the current availability of its external root.</summary>
public sealed record TexturePackInventoryState(
    TexturePackInventorySnapshot Snapshot,
    bool IsStale,
    TexturePackRootStatus ObservedRootStatus,
    string? AvailabilityDiagnostic = null);

/// <summary>
/// Separates cheap startup cache reads from explicit background refreshes and retains the last
/// good inventory when a removable or network-backed texture root is unavailable.
/// </summary>
public sealed class TexturePackInventoryService
{
    private readonly ITexturePackInventoryStore _store;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshGates = new(StringComparer.Ordinal);

    public TexturePackInventoryService(ITexturePackInventoryStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<TexturePackInventorySnapshot?> LoadCachedAsync(
        string installationId,
        CancellationToken cancellationToken = default) =>
        _store.LoadAsync(installationId, cancellationToken);

    public async Task<TexturePackInventoryState> RefreshAsync(
        ITexturePackSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var gate = _refreshGates.GetOrAdd(source.InstallationId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var fresh = await source.ScanAsync(cancellationToken);
            if (!string.Equals(fresh.InstallationId, source.InstallationId, StringComparison.Ordinal) ||
                !string.Equals(fresh.EmulatorId, source.EmulatorId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The texture source returned an inventory for a different emulator installation.");
            }

            if (fresh.RootStatus == TexturePackRootStatus.Ready)
            {
                await _store.SaveAsync(fresh, cancellationToken);
                return new TexturePackInventoryState(fresh, false, fresh.RootStatus);
            }

            var cached = await _store.LoadAsync(source.InstallationId, cancellationToken);
            return cached is null
                ? new TexturePackInventoryState(fresh, false, fresh.RootStatus, fresh.Diagnostic)
                : new TexturePackInventoryState(cached, true, fresh.RootStatus, fresh.Diagnostic);
        }
        finally
        {
            gate.Release();
        }
    }
}
