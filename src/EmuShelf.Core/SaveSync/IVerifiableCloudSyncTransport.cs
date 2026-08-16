namespace EmuShelf.Core.SaveSync;

/// <summary>
/// A cloud transport that can additionally audit the remote and account for its own time.
/// </summary>
/// <remarks>
/// Split from <see cref="ICloudSyncTransport"/> rather than folded into it because the sync engine
/// needs none of this: reconciliation only ever lists, downloads, uploads, and flushes. These two are
/// what the *caller around* a sync needs — one to repair an index whose payload vanished, one to say
/// where a slow pass spent its time — and keeping them separate means a future transport can be
/// useful without implementing either.
/// </remarks>
public interface IVerifiableCloudSyncTransport : ICloudSyncTransport
{
    /// <summary>
    /// Human-readable per-call durations for this session, for the sync activity log. The cloud
    /// provider's latency, not EmuShelf's work, is what a user waits on before a launch, so the log
    /// has to be able to say which call spent the time.
    /// </summary>
    IReadOnlyList<string> Timings { get; }

    /// <summary>
    /// Lists what the remote actually holds and reports indexed units whose payload is not there, so
    /// the next flush can rewrite the index without them and the machines still holding those saves
    /// upload them again.
    /// </summary>
    Task<IReadOnlyList<string>> FindMissingPayloadsAsync(CancellationToken cancellationToken = default);
}
