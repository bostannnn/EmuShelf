using System.Collections.Generic;
using System.Linq;
using EmuShelf.Core.Handheld;
using Xunit;

namespace EmuShelf.App.Tests.Handheld;

public class HandheldHomeArrangingTests
{
    private static HandheldHomeLayout Home(params HandheldSlot[] slots) => HandheldHomeLayout.FromSlots(slots);

    // ---- The model ----

    [Fact]
    public void FromSlots_PadsToWholePages_AtLeastTwo_AndTrimsEmptyPagesPastThat()
    {
        Assert.Equal(2, Home(HandheldSlot.Recent).Pages.Count);

        var slots = Enumerable.Repeat(HandheldSlot.Empty, 70).ToList();
        slots[41] = HandheldSlot.Game(7);
        Assert.Equal(3, HandheldHomeLayout.FromSlots(slots).Pages.Count);

        slots[41] = HandheldSlot.Empty;
        Assert.Equal(2, HandheldHomeLayout.FromSlots(slots).Pages.Count);
    }

    [Fact]
    public void AGame_CanBePinnedAndInSeveralFoldersAtOnce()
    {
        var layout = Home(HandheldSlot.PlatformFolder("ps2"))
            .WithNewFolder(1, "a", "RPGs", "#EF79AC", [5])
            .WithNewFolder(2, "b", "Couch", "#FF9A44", [5, 6])
            .WithPin(5, out var pinnedAt);

        Assert.Equal(3, pinnedAt);
        Assert.True(layout.IsPinned(5));
        Assert.True(layout.FindFolder("a")!.Contains(5));
        Assert.True(layout.FindFolder("b")!.Contains(5));
    }

    [Fact]
    public void WithPin_TakesTheFirstEmptySlot_AndRefusesAPinTwiceOrAFullHome()
    {
        var layout = Home(HandheldSlot.Recent, HandheldSlot.Empty, HandheldSlot.PlatformFolder("gc"));

        var pinned = layout.WithPin(9, out var index);
        Assert.Equal(1, index);
        Assert.Same(pinned, pinned.WithPin(9, out var again));
        Assert.Equal(1, again);

        var full = HandheldHomeLayout.FromSlots(Enumerable.Range(1, 40).Select(id => HandheldSlot.Game(id)));
        Assert.Same(full, full.WithPin(99, out var none));
        Assert.Equal(-1, none);
    }

    [Fact]
    public void FolderEdits_AddOnceInOrder_Remove_AndDeleteLeavesPinsAndOtherFolders()
    {
        var layout = Home(HandheldSlot.Game(5))
            .WithNewFolder(1, "a", "RPGs", "#EF79AC", [3])
            .WithGameAdded("a", 4)
            .WithGameAdded("a", 3);
        Assert.Equal([3, 4], layout.FindFolder("a")!.GameIds);

        layout = layout.WithGameRemoved("a", 3);
        Assert.Equal([4], layout.FindFolder("a")!.GameIds);

        var deleted = layout.WithoutFolder("a");
        Assert.Null(deleted.FindFolder("a"));
        Assert.True(deleted.SlotAt(1).IsEmpty);
        // Deleting a folder touches nothing else: the pin is still there.
        Assert.Equal(HandheldSlot.Game(5), deleted.SlotAt(0));
    }

    [Fact]
    public void RibbonClears_AndSameTileIgnoresIt()
    {
        var layout = Home(HandheldSlot.PlatformFolder("gba", isNew: true));

        Assert.True(layout.SlotAt(0).SameTileAs(HandheldSlot.PlatformFolder("gba")));
        Assert.False(layout.WithRibbonCleared(0).SlotAt(0).IsNew);
    }

    // ---- Move mode ----

    [Fact]
    public void Classify_GameOnGameMakesAFolder_GameOnYourFolderAddsIt_EverythingElsePlaces()
    {
        Assert.Equal(HandheldDropKind.MakeFolder, HandheldHomeArranging.Classify(HandheldSlot.Game(1), HandheldSlot.Game(2)));
        Assert.Equal(HandheldDropKind.AddToFolder, HandheldHomeArranging.Classify(HandheldSlot.Game(1), HandheldSlot.UserFolder("a")));
        Assert.Equal(HandheldDropKind.Place, HandheldHomeArranging.Classify(HandheldSlot.Game(1), HandheldSlot.PlatformFolder("ps2")));
        Assert.Equal(HandheldDropKind.Place, HandheldHomeArranging.Classify(HandheldSlot.UserFolder("a"), HandheldSlot.Game(2)));
        Assert.Equal(HandheldDropKind.Place, HandheldHomeArranging.Classify(HandheldSlot.Game(1), HandheldSlot.Empty));
    }

    [Fact]
    public void InsertAt_SlidesTheTilesInTheWayTowardTheNearestEmptySlot()
    {
        HandheldSlot A = HandheldSlot.PlatformFolder("a"), B = HandheldSlot.PlatformFolder("b"),
            C = HandheldSlot.PlatformFolder("c"), X = HandheldSlot.Game(9), E = HandheldSlot.Empty;

        // Into an empty slot: straight in.
        Assert.Equal([A, X, B], HandheldHomeArranging.InsertAt([A, E, B], 1, X)!.Take(3));
        // Onto a tile: it and the ones after it shift right into the next empty slot.
        Assert.Equal([X, A, B, C], HandheldHomeArranging.InsertAt([A, B, E, C], 0, X)!.Take(4));
        // Nothing empty after the target: the tiles before it shift left instead.
        Assert.Equal([A, B, X], HandheldHomeArranging.InsertAt([E, A, B], 2, X));
        // Nowhere to go.
        Assert.Null(HandheldHomeArranging.InsertAt([A, B], 0, X));
    }

    [Fact]
    public void Lift_LeavesTheOriginEmpty()
    {
        var lifted = HandheldHomeArranging.Lift([HandheldSlot.Game(1), HandheldSlot.Game(2)], 0);
        Assert.True(lifted[0].IsEmpty);
        Assert.Equal(HandheldSlot.Game(2), lifted[1]);
    }

    // ---- Reconciliation ----

    private static HandheldHomeLayout Reconcile(HandheldHomeLayout layout, string[] platforms, long[] games, out bool changed) =>
        HandheldHomeReconciler.Reconcile(layout, platforms, new HashSet<long>(games), out changed);

    [Fact]
    public void Reconcile_NothingToDo_ChangesNothing()
    {
        var layout = HandheldHomeLayout.CreateDefault(["ps2", "gc"]);

        var result = Reconcile(layout, ["ps2", "gc"], [], out var changed);

        Assert.False(changed);
        Assert.Equal(layout.AllSlots(), result.AllSlots());
    }

    [Fact]
    public void Reconcile_ANewPlatform_IsAppendedAfterTheLastTile_WithTheNewRibbon()
    {
        // You moved GC to slot 6; the gap before it is yours and is not filled in.
        var layout = Home(HandheldSlot.Recent, HandheldSlot.PlatformFolder("ps2"), HandheldSlot.Empty, HandheldSlot.Empty,
            HandheldSlot.Empty, HandheldSlot.Empty, HandheldSlot.PlatformFolder("gc"));

        var result = Reconcile(layout, ["ps2", "gba", "gc"], [], out var changed);

        Assert.True(changed);
        Assert.True(result.SlotAt(2).IsEmpty);
        Assert.Equal(HandheldSlot.PlatformFolder("gba", isNew: true), result.SlotAt(7));
    }

    [Fact]
    public void Reconcile_APlatformWithoutGames_LeavesItsSlotEmpty_WithoutShiftingOthers()
    {
        var layout = HandheldHomeLayout.CreateDefault(["ps2", "gc", "wii"]);

        var result = Reconcile(layout, ["ps2", "wii"], [], out var changed);

        Assert.True(changed);
        Assert.True(result.SlotAt(2).IsEmpty);
        Assert.Equal("wii", result.SlotAt(3).SystemId);
    }

    [Fact]
    public void Reconcile_RemovedGames_LeaveEveryFolderAndPin()
    {
        var layout = Home(HandheldSlot.Recent, HandheldSlot.Game(1), HandheldSlot.Game(2))
            .WithNewFolder(3, "a", "RPGs", "#EF79AC", [1, 2, 3]);

        var result = Reconcile(layout, ["ps2"], [2, 3], out var changed);

        Assert.True(changed);
        Assert.True(result.SlotAt(1).IsEmpty);
        Assert.Equal(HandheldSlot.Game(2), result.SlotAt(2));
        Assert.Equal([2, 3], result.FindFolder("a")!.GameIds);
        // An emptied folder of yours stays: it is still yours to fill.
        Assert.NotNull(Reconcile(result, ["ps2"], [], out _).FindFolder("a"));
    }

    [Fact]
    public void Reconcile_PutsRecentBack_AndDropsDuplicatesAndFoldersWithoutATile()
    {
        var layout = HandheldHomeLayout.FromSlots(
            [HandheldSlot.PlatformFolder("ps2"), HandheldSlot.PlatformFolder("ps2"), HandheldSlot.UserFolder("a")],
            [new HandheldUserFolder("a", "Mine", "#EF79AC", []), new HandheldUserFolder("orphan", "Lost", "#EF79AC", [])]);

        var result = Reconcile(layout, ["ps2"], [], out _);

        Assert.True(result.SlotAt(1).IsEmpty);
        Assert.Equal(HandheldSlot.Recent, result.SlotAt(3));
        Assert.Equal(["a"], result.Folders.Select(folder => folder.Id));
    }
}
