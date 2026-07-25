using EmuShelf.Core.SaveSync;

namespace EmuShelf.Infrastructure.Tests.SaveSync;

public sealed class SaveSyncPlannerTests
{
    private const string UnitId = "pcsx2/Mcd001.ps2";
    private static readonly DateTimeOffset Earlier = new(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NothingOnEitherSide_IsNone()
    {
        var decision = SaveSyncPlanner.Decide(local: null, remote: null, baseline: null);

        Assert.Equal(SaveSyncAction.None, decision.Action);
    }

    [Fact]
    public void LocalOnlyWithNoBaseline_Uploads()
    {
        var decision = SaveSyncPlanner.Decide(Snapshot("a", Later), remote: null, baseline: null);

        Assert.Equal(SaveSyncAction.Upload, decision.Action);
    }

    [Fact]
    public void RemoteOnlyWithNoBaseline_Downloads()
    {
        var decision = SaveSyncPlanner.Decide(local: null, Snapshot("a", Later), baseline: null);

        Assert.Equal(SaveSyncAction.Download, decision.Action);
    }

    [Fact]
    public void LocalOnlyWithBaseline_RestoresRemoteByUploading()
    {
        // Remote copy vanished; sync never deletes, so it restores from local rather than
        // propagating the disappearance.
        var decision = SaveSyncPlanner.Decide(Snapshot("a", Later), remote: null, Baseline("a"));

        Assert.Equal(SaveSyncAction.Upload, decision.Action);
    }

    [Fact]
    public void RemoteOnlyWithBaseline_RestoresLocalByDownloading()
    {
        var decision = SaveSyncPlanner.Decide(local: null, Snapshot("a", Later), Baseline("a"));

        Assert.Equal(SaveSyncAction.Download, decision.Action);
    }

    [Fact]
    public void IdenticalContent_IsNone()
    {
        var decision = SaveSyncPlanner.Decide(Snapshot("a", Earlier), Snapshot("a", Later), baseline: null);

        Assert.Equal(SaveSyncAction.None, decision.Action);
    }

    [Fact]
    public void LocalChangedRemoteUnchanged_Uploads()
    {
        var decision = SaveSyncPlanner.Decide(Snapshot("a2", Later), Snapshot("a", Earlier), Baseline("a"));

        Assert.Equal(SaveSyncAction.Upload, decision.Action);
    }

    [Fact]
    public void RemoteChangedLocalUnchanged_Downloads()
    {
        var decision = SaveSyncPlanner.Decide(Snapshot("a", Earlier), Snapshot("a2", Later), Baseline("a"));

        Assert.Equal(SaveSyncAction.Download, decision.Action);
    }

    [Fact]
    public void BothChangedLocalNewer_ConflictLocalWins()
    {
        var decision = SaveSyncPlanner.Decide(Snapshot("local", Later), Snapshot("remote", Earlier), Baseline("base"));

        Assert.Equal(SaveSyncAction.ConflictLocalWins, decision.Action);
        Assert.True(decision.IsConflict);
    }

    [Fact]
    public void BothChangedRemoteNewer_ConflictRemoteWins()
    {
        var decision = SaveSyncPlanner.Decide(Snapshot("local", Earlier), Snapshot("remote", Later), Baseline("base"));

        Assert.Equal(SaveSyncAction.ConflictRemoteWins, decision.Action);
    }

    [Fact]
    public void NoBaselineButBothPresentAndDifferent_IsAConflictByModifiedTime()
    {
        // A brand-new second machine that already has its own save must not silently overwrite
        // either side: with no shared history, differing content is a conflict.
        var decision = SaveSyncPlanner.Decide(Snapshot("local", Later), Snapshot("remote", Earlier), baseline: null);

        Assert.Equal(SaveSyncAction.ConflictLocalWins, decision.Action);
    }

    [Fact]
    public void EqualModifiedTimesButDifferentContent_ResolvesToLocalWithoutLosingRemote()
    {
        // The tie-break favours local, but the losing side is always backed up by the service,
        // so an unresolvable timestamp tie still cannot lose data.
        var decision = SaveSyncPlanner.Decide(Snapshot("local", Later), Snapshot("remote", Later), Baseline("base"));

        Assert.Equal(SaveSyncAction.ConflictLocalWins, decision.Action);
    }

    private static SaveUnitSnapshot Snapshot(string hash, DateTimeOffset modified) =>
        new(UnitId, hash, modified);

    private static SaveUnitBaseline Baseline(string hash) =>
        new(UnitId, hash, Earlier, Revision: 1);
}
