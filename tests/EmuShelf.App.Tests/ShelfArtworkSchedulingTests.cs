using EmuShelf.App.Controls;
using EmuShelf.App.Rendering;
using EmuShelf.Rendering;

namespace EmuShelf.App.Tests;

public sealed class ShelfArtworkSchedulingTests
{
    [Fact]
    public void SevenVisibleFourFaceGamesRemainPinned_WhenOverTextureBudget()
    {
        var lru = new LinkedList<long>(Enumerable.Range(1, 7).Select(i => (long)i));
        var visible = lru.ToHashSet();
        Assert.Null(MediaShelf3DControl.FindEvictableCover(lru, visible));
        lru.AddFirst(8); // Even a more recently used off-screen game is evicted before visible ones.
        Assert.Equal(8L, MediaShelf3DControl.FindEvictableCover(lru, visible)!.Value);
        visible.Remove(7);
        Assert.Equal(7L, MediaShelf3DControl.FindEvictableCover(lru, visible)!.Value);
    }

    [Fact]
    public void UploadsPrioritizeSelectionThenNearestNeighbours_DuringGlide()
    {
        MediaShelfRenderItem[] items =
        [
            new() { Key = 1, CentreX = -3 },
            new() { Key = 2, CentreX = -1 },
            new() { Key = 3, CentreX = 0.2f },
            new() { Key = 4, CentreX = 2 },
        ];
        Assert.Equal(new long[] { 4, 3, 2, 1 },
            MediaShelf3DControl.OrderArtworkItems(items, 4).Select(item => item.Key));
    }

    [Fact]
    public void NeighbourFrontDecodePrecedesFocusedHiddenFaces()
    {
        var queued = new[]
        {
            (ShelfArtworkFace.DiscLabel, 0), (ShelfArtworkFace.Back, 0),
            (ShelfArtworkFace.Front, 2), (ShelfArtworkFace.Front, 0),
        };
        var sorted = queued.OrderBy(item => MediaShelf3DControl.ArtworkDecodePriority(item.Item1))
            .ThenBy(item => item.Item2).ToArray();
        Assert.Equal((ShelfArtworkFace.Front, 0), sorted[0]);
        Assert.Equal((ShelfArtworkFace.Front, 2), sorted[1]);
    }
}
